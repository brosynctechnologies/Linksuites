using SkiaSharp;

namespace Saas.Infrastructure.Invoices;

/// <summary>
/// The web preview draws the uploaded stamp at 90% opacity with multiply blending, so the paper shows through its white
/// background. QuestPDF has no image opacity or blend modes, so the PDF gets a pre-processed copy instead: white is turned
/// into transparency ("unmultiplying" it, which looks the same as multiply on a white page) and the ink is kept at 90%.
/// </summary>
public static class StampInk
{
    public const float Opacity = 0.9f;

    /// <summary>The stamp as a PNG with its white made transparent, or the original bytes when it can't be decoded.</summary>
    public static byte[] Apply(byte[] bytes)
    {
        using var decoded = SKBitmap.Decode(bytes);
        if (decoded is null) return bytes;
        var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (!decoded.CopyTo(bitmap, SKColorType.Rgba8888)) return bytes;

        var pixels = bitmap.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            // How far the darkest channel is from white is how much ink there is.
            var ink = 1f - Math.Min(p.Red, Math.Min(p.Green, p.Blue)) / 255f;
            var alpha = ink * (p.Alpha / 255f);
            if (alpha <= 0.004f)
            {
                pixels[i] = SKColors.Transparent;
                continue;
            }
            // The colour that, laid over white at this alpha, gives the original colour.
            byte Unwhite(byte c) => (byte)Math.Clamp(MathF.Round(255f - (255f - c) / ink), 0, 255);
            pixels[i] = new SKColor(Unwhite(p.Red), Unwhite(p.Green), Unwhite(p.Blue), (byte)MathF.Round(alpha * Opacity * 255f));
        }
        bitmap.Pixels = pixels;
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
