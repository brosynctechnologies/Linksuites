namespace Saas.Application.Common;

public sealed record Upload(Stream Content, string FileName, string ContentType, long Length);

public static class ImageUploads
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Copies an uploaded image into memory after checking its size and real type (magic bytes, not the
    /// client's Content-Type). Returns the buffer, the extension and the mime type.
    /// </summary>
    public static async Task<(MemoryStream Buffer, string Extension, string MimeType)> ReadAsync(Upload upload, CancellationToken ct)
    {
        if (upload.Length <= 0) throw BadRequestException.Field("file", "Choose an image to upload.");
        if (upload.Length > MaxBytes) throw BadRequestException.Field("file", "Images must be 5 MB or smaller.");

        var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxBytes) throw BadRequestException.Field("file", "Images must be 5 MB or smaller.");

        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(12, buffer.Length));
        var (ext, mime) = header switch
        {
            [0xFF, 0xD8, 0xFF, ..] => (".jpg", "image/jpeg"),
            [0x89, 0x50, 0x4E, 0x47, ..] => (".png", "image/png"),
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50] => (".webp", "image/webp"),
            _ => throw BadRequestException.Field("file", "Upload a JPEG, PNG or WebP image."),
        };
        buffer.Position = 0;
        return (buffer, ext, mime);
    }

    /// <summary>The largest side, in pixels, of a logo, signature or stamp: decoding is bounded however the file is compressed.</summary>
    public const int MaxDocumentImageSide = 4000;

    public static readonly string TooLargeMessage = $"Images must be {MaxDocumentImageSide} × {MaxDocumentImageSide} pixels or smaller.";

    /// <summary>Refuses an image whose header declares more than <paramref name="maxSide"/> pixels a side, or no readable size.</summary>
    public static void EnsureDimensions(MemoryStream buffer, int maxSide = MaxDocumentImageSide)
    {
        var size = Dimensions(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (size is null) throw BadRequestException.Field("file", "This image could not be read. Upload a JPEG, PNG or WebP image.");
        // A 4-byte PNG size over 2^31 reads as negative: that is too large as well.
        if (size.Value.Width is <= 0 || size.Value.Height is <= 0 || size.Value.Width > maxSide || size.Value.Height > maxSide)
            throw BadRequestException.Field("file", TooLargeMessage);
    }

    /// <summary>
    /// The width and height a PNG, JPEG or WebP file declares in its header, without decoding it; null when the header can't be read.
    /// </summary>
    public static (int Width, int Height)? Dimensions(ReadOnlySpan<byte> data)
    {
        // PNG: the IHDR chunk comes first; width and height are big-endian at bytes 16 and 20.
        if (data is [0x89, 0x50, 0x4E, 0x47, ..])
            return data.Length >= 24 ? (BigEndian(data, 16, 4), BigEndian(data, 20, 4)) : null;

        // JPEG: walk the segments to the first start-of-frame marker (C0-CF, except C4 DHT, C8 JPG and CC DAC).
        if (data is [0xFF, 0xD8, ..])
        {
            var pos = 2;
            while (pos + 3 < data.Length)
            {
                if (data[pos] != 0xFF) return null;
                var marker = data[pos + 1];
                if (marker == 0xFF) { pos++; continue; } // fill byte
                if (marker is 0x01 or (>= 0xD0 and <= 0xD9)) { pos += 2; continue; } // markers without a length
                var length = BigEndian(data, pos + 2, 2);
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return pos + 8 < data.Length ? (BigEndian(data, pos + 7, 2), BigEndian(data, pos + 5, 2)) : null;
                if (length < 2) return null;
                pos += 2 + length;
            }
            return null;
        }

        // WebP: RIFF....WEBP, then a VP8 (lossy), VP8L (lossless) or VP8X (extended) chunk at byte 12.
        if (data.Length >= 30 && data is [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..])
        {
            var chunk = System.Text.Encoding.ASCII.GetString(data.Slice(12, 4));
            return chunk switch
            {
                "VP8X" => (1 + LittleEndian(data, 24, 3), 1 + LittleEndian(data, 27, 3)),
                "VP8L" => (1 + (((data[22] & 0x3F) << 8) | data[21]), 1 + (((data[24] & 0x0F) << 10) | (data[23] << 2) | ((data[22] & 0xC0) >> 6))),
                "VP8 " => (LittleEndian(data, 26, 2) & 0x3FFF, LittleEndian(data, 28, 2) & 0x3FFF),
                _ => null,
            };
        }
        return null;
    }

    private static int BigEndian(ReadOnlySpan<byte> data, int start, int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++) value = (value << 8) | data[start + i];
        return value;
    }

    private static int LittleEndian(ReadOnlySpan<byte> data, int start, int count)
    {
        var value = 0;
        for (var i = count - 1; i >= 0; i--) value = (value << 8) | data[start + i];
        return value;
    }
}
