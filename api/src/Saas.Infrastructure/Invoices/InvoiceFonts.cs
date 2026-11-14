using QuestPDF.Drawing;

namespace Saas.Infrastructure.Invoices;

/// <summary>
/// The three document font families, embedded (Fonts/*.ttf, SIL OFL; every file maps U+20A6, the naira sign) and registered
/// with QuestPDF once per process. The web preview loads the same families.
/// </summary>
public static class InvoiceFonts
{
    public const string Modern = "Inter";
    public const string Serif = "Noto Serif";
    public const string Sans = "Noto Sans";
    private const string ResourcePrefix = "InvoiceFonts.";

    /// <summary>Tried in order when a character is missing from the chosen family.</summary>
    public static readonly string[] Fallbacks = [Sans, Serif];

    private static readonly Lazy<int> Registered = new(() =>
    {
        var assembly = typeof(InvoiceFonts).Assembly;
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".ttf", StringComparison.Ordinal)).ToList();
        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            FontManager.RegisterFontFromBinaryData(copy.ToArray());
        }
        return names.Count;
    });

    /// <summary>Registers the embedded fonts (first call only); returns how many files were registered.</summary>
    public static int EnsureRegistered() => Registered.Value;

    /// <summary>The family for a business's font style: modern = Inter, serif = Noto Serif, sans = Noto Sans.</summary>
    public static string For(string? fontStyle) => fontStyle switch
    {
        "serif" => Serif,
        "sans" => Sans,
        _ => Modern,
    };
}
