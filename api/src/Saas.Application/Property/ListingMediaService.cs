using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Application.Property;

public sealed record ReorderRequest(IReadOnlyList<Guid> Ids);

/// <summary>Listing photos: upload checks, order and removal. Resizing and duplicate checks run in the photo processor.</summary>
public sealed class ListingMediaService(IAppDbContext db, ITenantContext tenant, IFileStorage storage)
{
    public const long MaxBytes = 15 * 1024 * 1024;
    public const int MaxPhotos = 30;

    public async Task<ListingMediaDto> UploadAsync(Guid listingId, Upload file, CancellationToken ct)
    {
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, ct) ?? throw new NotFoundException("Listing");
        if (file.Length == 0) throw BadRequestException.Field("file", "That file is empty.");
        if (file.Length > MaxBytes) throw BadRequestException.Field("file", "Photos can be up to 15 MB.");
        var head = new byte[12];
        var read = await file.Content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        var ext = Sniff(head.AsSpan(0, read)) ?? throw BadRequestException.Field("file",
            "Upload a JPEG, PNG or WebP photo. On iPhone, set Camera > Formats to Most Compatible, or share the photo as JPEG.");
        var count = await db.ListingMedia.CountAsync(m => m.ListingId == listingId, ct);
        if (count >= MaxPhotos) throw BadRequestException.Field("file", $"A listing can have up to {MaxPhotos} photos.");

        var media = new ListingMedia
        {
            ListingId = listing.Id, FileName = Path.GetFileName(file.FileName), Position = count,
            OriginalKey = "",
        };
        media.OriginalKey = $"property/{tenant.TenantId}/{listing.Id}/{media.Id:N}-original.{ext}";
        using var content = new ConcatStream(head.AsMemory(0, read), file.Content);
        await storage.SaveAsync(media.OriginalKey, content, ContentTypeFor(ext), ct);
        db.ListingMedia.Add(media);
        await db.SaveChangesAsync(ct);
        return (await ListingStudioService.MediaDtosAsync(db, listingId, ct)).First(m => m.Id == media.Id);
    }

    public async Task ReorderAsync(Guid listingId, ReorderRequest r, CancellationToken ct)
    {
        var media = await db.ListingMedia.Where(m => m.ListingId == listingId).ToListAsync(ct);
        if (media.Count == 0 && !await db.Listings.AnyAsync(l => l.Id == listingId, ct)) throw new NotFoundException("Listing");
        if (r.Ids.Count != media.Count || r.Ids.Distinct().Count() != media.Count || media.Any(m => !r.Ids.Contains(m.Id)))
            throw BadRequestException.Field("ids", "Send every photo of this listing once.");
        for (var i = 0; i < r.Ids.Count; i++) media.First(m => m.Id == r.Ids[i]).Position = i;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid listingId, Guid mediaId, CancellationToken ct)
    {
        var media = await db.ListingMedia.FirstOrDefaultAsync(m => m.Id == mediaId && m.ListingId == listingId, ct) ?? throw new NotFoundException("Photo");
        db.ListingMedia.Remove(media);
        await db.SaveChangesAsync(ct);
        foreach (var key in Keys(media)) await storage.DeleteAsync(key, ct);
    }

    /// <summary>Storage keys a photo may occupy: the original (until processed) and the three sizes.</summary>
    public static IEnumerable<string> Keys(ListingMedia m)
    {
        yield return m.OriginalKey;
        foreach (var width in PhotoSizes.Widths) yield return SizeKey(m, width);
    }

    public static string SizeKey(ListingMedia m, int width) => $"property/{m.TenantId}/{m.ListingId}/{m.Id:N}-{width}.webp";

    /// <summary>The content type for a sniffed extension, so stored originals never carry the uploader's claimed type.</summary>
    public static string ContentTypeFor(string ext) => ext switch { "jpg" => "image/jpeg", "png" => "image/png", "webp" => "image/webp", _ => "application/octet-stream" };

    /// <summary>JPEG, PNG and WebP by their magic bytes; null for anything else (HEIC, PDF, renamed files).</summary>
    public static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return "webp";
        return null;
    }

    /// <summary>Replays the sniffed header before the rest of the upload stream.</summary>
    private sealed class ConcatStream(ReadOnlyMemory<byte> head, Stream rest) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (_position < head.Length)
            {
                var n = Math.Min(buffer.Length, head.Length - _position);
                head.Span.Slice(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }
            return rest.Read(buffer);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_position < head.Length) return Read(buffer.Span);
            return await rest.ReadAsync(buffer, ct);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public static class PhotoSizes
{
    public const int Thumb = 400;
    public const int Card = 960;
    public const int Full = 1920;
    public static readonly int[] Widths = [Thumb, Card, Full];
}
