using Saas.Infrastructure.Jobs;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Saas.Application.Common;
using Saas.Application.Courses;
using Saas.Domain.Courses;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Courses;

public sealed class VideoOptions
{
    public const string Section = "Video";
    /// <summary>Path to ffmpeg; ffprobe is expected next to it. Without FFmpeg, uploads are served as MP4.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";
    public bool ProcessorEnabled { get; set; } = true;
}

public sealed record Rendition(int Height, int VideoKbps)
{
    public string Name => $"{Height}p";
    public int Width => (int)Math.Round(Height * 16 / 9.0 / 2) * 2;
}

/// <summary>FFmpeg arguments for adaptive HLS, kept separate so they're easy to test and tune.</summary>
public static class FfmpegCommands
{
    public static readonly Rendition[] Ladder = [new(240, 400), new(480, 900), new(720, 2000), new(1080, 4000)];

    /// <summary>Renditions up to the source height (always at least the lowest one), so we never upscale.</summary>
    public static IReadOnlyList<Rendition> For(int sourceHeight) =>
        Ladder.Where(r => r.Height <= Math.Max(sourceHeight, Ladder[0].Height)).ToList();

    public static string[] Hls(string input, string outputDir, Rendition r) =>
    [
        "-y", "-i", input,
        "-vf", $"scale=-2:{r.Height}",
        "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "main", "-crf", "23",
        "-maxrate", $"{r.VideoKbps}k", "-bufsize", $"{r.VideoKbps * 2}k", "-g", "48", "-sc_threshold", "0",
        "-c:a", "aac", "-b:a", "96k", "-ac", "2",
        "-hls_time", "6", "-hls_playlist_type", "vod",
        "-hls_segment_filename", Path.Combine(outputDir, r.Name, "seg_%04d.ts"),
        Path.Combine(outputDir, r.Name, "index.m3u8"),
    ];

    public static string[] Thumbnail(string input, string output) => ["-y", "-ss", "2", "-i", input, "-frames:v", "1", "-vf", "scale=640:-2", output];

    public static string[] Probe(string input) =>
        ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=height:format=duration", "-of", "json", input];

    public static string Master(IEnumerable<Rendition> renditions)
    {
        var sb = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n");
        foreach (var r in renditions)
            sb.Append(CultureInfo.InvariantCulture, $"#EXT-X-STREAM-INF:BANDWIDTH={(r.VideoKbps + 96) * 1000},RESOLUTION={r.Width}x{r.Height}\n{r.Name}/index.m3u8\n");
        return sb.ToString();
    }
}

/// <summary>
/// Turns uploaded videos into adaptive HLS (240p-1080p) with FFmpeg, or, if FFmpeg isn't installed, publishes the upload
/// as an MP4. Claims one video at a time with an atomic update, so several API instances can run this safely.
/// </summary>
public sealed class VideoProcessor(IServiceScopeFactory scopes, IOptions<VideoOptions> options, ILogger<VideoProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan Stale = TimeSpan.FromHours(2);
    private bool? _hasFfmpeg;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "videos", async () => { while (await ProcessNextAsync(stoppingToken)) { } }, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Video processing failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var staleBefore = now - Stale;
        var candidate = await db.Videos.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Status == VideoStatus.Processing && (v.ProcessingStartedAt == null || v.ProcessingStartedAt < staleBefore))
            .OrderBy(v => v.CreatedAt).Select(v => new { v.Id, v.TenantId }).FirstOrDefaultAsync(ct);
        if (candidate is null) return false;
        var claimed = await db.Videos.IgnoreQueryFilters()
            .Where(v => v.Id == candidate.Id && v.Status == VideoStatus.Processing && (v.ProcessingStartedAt == null || v.ProcessingStartedAt < staleBefore))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ProcessingStartedAt, now), ct);
        if (claimed == 0) return true; // another instance took it

        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(candidate.TenantId);
        var video = await db.Videos.FirstAsync(v => v.Id == candidate.Id, ct);
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        try
        {
            if (await HasFfmpegAsync(ct)) await TranscodeAsync(video, storage, ct);
            else
            {
                video.Format = "mp4";
                video.PlaybackKey = video.SourceKey;
                video.Status = VideoStatus.Ready;
            }
            var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == video.LessonId, ct);
            if (lesson is not null && video.DurationSec > 0) lesson.DurationSec = video.DurationSec;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Video {Id} failed to process", video.Id);
            video.Status = VideoStatus.Failed;
            video.Error = "We couldn't process this video. Try exporting it as MP4 (H.264) and upload again.";
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task TranscodeAsync(Video video, IFileStorage storage, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("saas-video-");
        try
        {
            var input = Path.Combine(work.FullName, "source" + Path.GetExtension(video.SourceKey));
            await using (var source = await storage.OpenReadAsync(video.SourceKey, ct) ?? throw new FileNotFoundException(video.SourceKey))
            await using (var file = File.Create(input))
                await source.CopyToAsync(file, ct);

            var (height, duration) = await ProbeAsync(input, ct);
            var renditions = FfmpegCommands.For(height);
            var output = Path.Combine(work.FullName, "hls");
            foreach (var r in renditions)
            {
                Directory.CreateDirectory(Path.Combine(output, r.Name));
                await RunAsync(options.Value.FfmpegPath, FfmpegCommands.Hls(input, output, r), ct);
            }
            await File.WriteAllTextAsync(Path.Combine(output, "master.m3u8"), FfmpegCommands.Master(renditions), ct);

            var prefix = $"{video.TenantId}/videos/{video.Id:N}/hls";
            foreach (var path in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(output, path).Replace('\\', '/');
                await using var stream = File.OpenRead(path);
                await storage.SaveAsync($"{prefix}/{relative}", stream, relative.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "video/mp2t", ct);
            }

            var thumb = Path.Combine(work.FullName, "thumb.jpg");
            await RunAsync(options.Value.FfmpegPath, FfmpegCommands.Thumbnail(input, thumb), ct);
            if (File.Exists(thumb))
            {
                await using var stream = File.OpenRead(thumb);
                video.ThumbnailUrl = await storage.SaveAsync($"{video.TenantId}/videos/{video.Id:N}/thumb.jpg", stream, "image/jpeg", ct);
            }

            video.Format = "hls";
            video.PlaybackKey = prefix;
            video.Renditions = renditions.Select(r => r.Name).ToList();
            video.DurationSec = duration;
            video.Status = VideoStatus.Ready;
        }
        finally
        {
            try { work.Delete(recursive: true); } catch (IOException) { }
        }
    }

    private async Task<(int Height, int DurationSec)> ProbeAsync(string input, CancellationToken ct)
    {
        var ffprobe = Path.Combine(Path.GetDirectoryName(options.Value.FfmpegPath) ?? "", OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!Path.IsPathRooted(options.Value.FfmpegPath)) ffprobe = "ffprobe";
        var json = await RunAsync(ffprobe, FfmpegCommands.Probe(input), ct);
        using var doc = JsonDocument.Parse(json);
        var height = doc.RootElement.TryGetProperty("streams", out var streams) && streams.GetArrayLength() > 0
            && streams[0].TryGetProperty("height", out var h) ? h.GetInt32() : 480;
        var duration = doc.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var d)
            && double.TryParse(d.GetString(), CultureInfo.InvariantCulture, out var seconds) ? (int)Math.Round(seconds) : 0;
        return (height, duration);
    }

    private async Task<bool> HasFfmpegAsync(CancellationToken ct)
    {
        if (_hasFfmpeg is { } known) return known;
        try
        {
            await RunAsync(options.Value.FfmpegPath, ["-version"], ct);
            _hasFfmpeg = true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning("FFmpeg not found at {Path}; videos will be served as uploaded MP4s without adaptive streaming", options.Value.FfmpegPath);
            _hasFfmpeg = false;
        }
        return _hasFfmpeg.Value;
    }

    private static async Task<string> RunAsync(string file, IEnumerable<string> args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {file}");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(file)} exited with {process.ExitCode}: {(await stderr)[^Math.Min(500, (await stderr).Length)..]}");
        return await stdout;
    }
}

/// <summary>A landscape certificate of completion, in the academy's colour.</summary>
public sealed class CertificateRenderer : ICertificateRenderer
{
    static CertificateRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public Task<byte[]> RenderAsync(CertificateDto c, string brandColor, CancellationToken ct)
    {
        var brand = Color.FromHex(brandColor);
        var pdf = Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(36);
            page.Content().Border(2).BorderColor(brand).Padding(8).Border(0.75f).BorderColor(brand).Padding(40).Column(col =>
            {
                col.Spacing(10);
                col.Item().AlignCenter().Text(c.AcademyName).FontSize(16).FontColor(brand).Bold();
                col.Item().PaddingTop(16).AlignCenter().Text("Certificate of completion").FontSize(30).Bold();
                col.Item().PaddingTop(20).AlignCenter().Text("This certifies that").FontSize(12).FontColor(Colors.Grey.Darken1);
                col.Item().AlignCenter().Text(c.StudentName).FontSize(34).Bold().FontColor(brand);
                col.Item().AlignCenter().Text("has completed").FontSize(12).FontColor(Colors.Grey.Darken1);
                col.Item().AlignCenter().Text(c.CourseTitle).FontSize(22).SemiBold();
                col.Item().PaddingTop(30).Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text(c.TutorName).FontSize(12).SemiBold();
                        left.Item().Text("Tutor").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                    row.RelativeItem().AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Text(c.IssuedAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)).FontSize(12).SemiBold();
                        right.Item().AlignRight().Text($"Certificate {c.Code}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        })).GeneratePdf();
        return Task.FromResult(pdf);
    }
}
