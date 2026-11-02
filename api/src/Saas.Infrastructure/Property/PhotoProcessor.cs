using Saas.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saas.Application.Common;
using Saas.Application.Property;
using Saas.Domain.Property;
using Saas.Infrastructure.Persistence;
using SkiaSharp;

namespace Saas.Infrastructure.Property;

/// <summary>
/// Turns uploaded listing photos into three watermarked WebP sizes, hashes them and flags photos that match another
/// agency's listing. The original (which may carry the camera's GPS location) is deleted once processed.
/// </summary>
public sealed class PhotoProcessor(IServiceScopeFactory scopes, ILogger<PhotoProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(10);
    /// <summary>50 megapixels: bigger than any phone photo, small enough to decode safely.</summary>
    public const long MaxPixels = 50_000_000;

    private sealed class PhotoTooLargeException() : Exception("The photo is too large to process.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            try
            {
                await Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "photos", async () => { while (await ProcessNextAsync(stoppingToken)) { } }, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Photo processing failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static async Task<bool> ExistsAsync(IFileStorage storage, string key, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(key, ct);
        return stream is not null;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var staleBefore = now - Stale;
        var candidate = await db.ListingMedia.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.Status == MediaStatus.Processing && (m.ClaimedAt == null || m.ClaimedAt < staleBefore))
            .OrderBy(m => m.CreatedAt).Select(m => new { m.Id, m.TenantId }).FirstOrDefaultAsync(ct);
        if (candidate is null) return false;
        var claimed = await db.ListingMedia.IgnoreQueryFilters()
            .Where(m => m.Id == candidate.Id && m.Status == MediaStatus.Processing && (m.ClaimedAt == null || m.ClaimedAt < staleBefore))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ClaimedAt, now), ct);
        if (claimed == 0) return true; // another instance took it

        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(candidate.TenantId);
        var media = await db.ListingMedia.FirstAsync(m => m.Id == candidate.Id, ct);
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var processed = false;
        if (media.FullUrl is not null && media.CardUrl is not null && media.ThumbUrl is not null && !await ExistsAsync(storage, media.OriginalKey, ct))
            processed = true; // an earlier run made the sizes and deleted the original, but didn't get to mark it ready
        else
        try
        {
            var agencyName = await db.AgencyProfiles.Select(a => a.Name).FirstAsync(ct);
            await using (var original = await storage.OpenReadAsync(media.OriginalKey, ct) ?? throw new InvalidOperationException("The upload is missing."))
            {
                using var bitmap = DecodeUpright(original) ?? throw new InvalidOperationException("Not a readable image.");
                media.Width = bitmap.Width;
                media.Height = bitmap.Height;
                foreach (var width in PhotoSizes.Widths)
                {
                    using var resized = Resize(bitmap, width);
                    if (width != PhotoSizes.Thumb) Watermark(resized, agencyName);
                    using var image = SKImage.FromBitmap(resized);
                    using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
                    await using var stream = data.AsStream();
                    var url = await storage.SaveAsync(ListingMediaService.SizeKey(media, width), stream, "image/webp", ct);
                    if (width == PhotoSizes.Thumb) media.ThumbUrl = url;
                    else if (width == PhotoSizes.Card) media.CardUrl = url;
                    else media.FullUrl = url;
                }
                media.Phash = unchecked((long)Hash(bitmap));
            }
            processed = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Photo {Id} failed to process", media.Id);
            media.Status = MediaStatus.Failed;
            media.Error = ex is PhotoTooLargeException
                ? "This photo is too large (over 50 megapixels). Export it smaller and upload it again."
                : "We couldn't read this photo. Upload it again as a JPEG or PNG.";
            // The original can carry the uploader's GPS location; don't keep it once it's no use.
            try { await storage.DeleteAsync(media.OriginalKey, ct); }
            catch (Exception deleteError) when (deleteError is not OperationCanceledException)
            {
                logger.LogWarning(deleteError, "Photo {Id}: couldn't delete the original after a failure", media.Id);
            }
        }
        if (processed)
        {
            try
            {
                // Ready only once everything is done: the original (which can carry GPS) is gone and duplicates are checked.
                await storage.DeleteAsync(media.OriginalKey, ct);
                media.Status = MediaStatus.Ready;
                media.Error = null;
                await FlagDuplicateAsync(db, media, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The photo itself is fine. It stays Processing (claimed) and is tried again when the claim goes stale.
                logger.LogWarning(ex, "Photo {Id}: couldn't finish (deleting the original); will retry", media.Id);
                media.Status = MediaStatus.Processing;
            }
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Another agency's ready photo within the duplicate threshold marks this one, and holds a live listing for review.</summary>
    private static async Task FlagDuplicateAsync(AppDbContext db, ListingMedia media, CancellationToken ct)
    {
        var hash = media.Phash!.Value;
        if (!ImageHash.IsDistinctive(unchecked((ulong)hash))) return; // plain walls, sky, floor plans: too little detail to compare
        var threshold = ImageHash.DuplicateThreshold;
        var match = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM listing_media
            WHERE status = 'Ready' AND tenant_id <> {media.TenantId} AND phash IS NOT NULL
              AND bit_count((phash # {hash})::bit(64)) <= {threshold}
            ORDER BY created_at LIMIT 1
            """).ToListAsync(ct);
        if (match.Count == 0) return;
        media.DuplicateOfMediaId = match[0];
        var listing = await db.Listings.FirstAsync(l => l.Id == media.ListingId, ct);
        if (listing.Status == ListingStatus.Active)
        {
            listing.Status = ListingStatus.Pending;
            listing.ModerationNote = "A photo matches another agency's listing. A moderator will check it.";
        }
    }

    /// <summary>Decodes and applies the camera's orientation, so phone photos aren't sideways.</summary>
    internal static SKBitmap? DecodeUpright(Stream stream)
    {
        using var managed = new SKManagedStream(stream);
        using var codec = SKCodec.Create(managed);
        if (codec is null) return null;
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels) throw new PhotoTooLargeException();
        var decoded = SKBitmap.Decode(codec);
        if (decoded is null) return null;
        var origin = codec.EncodedOrigin;
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return decoded;

        var swap = origin is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var upright = new SKBitmap(swap ? decoded.Height : decoded.Width, swap ? decoded.Width : decoded.Height);
        using (var canvas = new SKCanvas(upright))
        {
            switch (origin)
            {
                case SKEncodedOrigin.BottomRight:
                    canvas.RotateDegrees(180, decoded.Width / 2f, decoded.Height / 2f);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(upright.Width, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, upright.Height);
                    canvas.RotateDegrees(270);
                    break;
            }
            canvas.DrawBitmap(decoded, 0, 0);
        }
        decoded.Dispose();
        return upright;
    }

    private static SKBitmap Resize(SKBitmap source, int maxWidth)
    {
        var width = Math.Min(maxWidth, source.Width);
        var height = (int)Math.Round(source.Height * (double)width / source.Width);
        return source.Resize(new SKImageInfo(width, Math.Max(1, height)), new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    private static void Watermark(SKBitmap bitmap, string text)
    {
        using var canvas = new SKCanvas(bitmap);
        using var font = new SKFont(SKTypeface.Default, Math.Max(12, bitmap.Width * 0.04f));
        var margin = bitmap.Width * 0.025f;
        var x = bitmap.Width - margin;
        var y = bitmap.Height - margin;
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 70), IsAntialias = true };
        using var fill = new SKPaint { Color = new SKColor(255, 255, 255, 102), IsAntialias = true };
        canvas.DrawText(text, x + 1.5f, y + 1.5f, SKTextAlign.Right, font, shadow);
        canvas.DrawText(text, x, y, SKTextAlign.Right, font, fill);
    }

    /// <summary>Difference hash of the unwatermarked image: shrink to 9x8 greyscale, then compare neighbours.</summary>
    public static ulong Hash(SKBitmap bitmap)
    {
        using var small = bitmap.Resize(new SKImageInfo(ImageHash.Width, ImageHash.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        var gray = new byte[ImageHash.Width * ImageHash.Height];
        for (var y = 0; y < ImageHash.Height; y++)
        for (var x = 0; x < ImageHash.Width; x++)
        {
            var c = small.GetPixel(x, y);
            gray[y * ImageHash.Width + x] = (byte)Math.Round(0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue);
        }
        return ImageHash.DHash(gray);
    }
}
