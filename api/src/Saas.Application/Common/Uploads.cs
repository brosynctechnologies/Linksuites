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
}
