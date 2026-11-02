using Saas.Application.School;
using Saas.Infrastructure.Property;
using SkiaSharp;

namespace Saas.Infrastructure.Schools;

/// <summary>Decodes an uploaded photo (applying the camera's orientation), shrinks it to fit a square and re-encodes it as WebP. The re-encode also drops EXIF data such as GPS.</summary>
public sealed class StudentPhotoProcessor : IStudentPhotoProcessor
{
    public byte[] ToWebp(Stream image, int maxWidth, int maxHeight)
    {
        SKBitmap? bitmap;
        try
        {
            bitmap = PhotoProcessor.DecodeUpright(image);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("The photo could not be read.", ex);
        }
        using (bitmap)
        {
            if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0) throw new InvalidDataException("The photo could not be read.");
            var scale = Math.Min(1.0, Math.Min(maxWidth / (double)bitmap.Width, maxHeight / (double)bitmap.Height));
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            using var resized = scale < 1.0
                ? bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
                : bitmap.Copy();
            if (resized is null) throw new InvalidDataException("The photo could not be read.");
            using var encoded = SKImage.FromBitmap(resized).Encode(SKEncodedImageFormat.Webp, 85);
            return encoded.ToArray();
        }
    }
}
