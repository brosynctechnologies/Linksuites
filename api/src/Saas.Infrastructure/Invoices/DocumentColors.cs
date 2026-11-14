using System.Globalization;
using System.Text.RegularExpressions;

namespace Saas.Infrastructure.Invoices;

/// <summary>
/// Document colours, computed exactly as the web preview does (invoice-web/src/lib/templates.ts), so a design's PDF and its
/// web preview use the same shades.
/// </summary>
internal static partial class DocumentColors
{
    public const string DefaultAccent = "#2563eb";
    /// <summary>Ink for text on light accent fills.</summary>
    public const string Ink = "#111827";
    public const string Graphite = "#1e2430";
    public const string Muted = "#5b6474";
    public const string Slate = "#4a5262";
    public const string Hairline = "#e8ebf0";
    public const string Rule = "#d9dee6";
    public const string Fill = "#f6f7f9";
    public const string LedgerRule = "#c3cad5";
    public const string White = "#ffffff";

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Long();

    [GeneratedRegex("^#([0-9a-fA-F])([0-9a-fA-F])([0-9a-fA-F])$")]
    private static partial Regex Short();

    /// <summary>#rgb or #rrggbb as lower-case #rrggbb, or null.</summary>
    public static string? Parse(string? color)
    {
        var s = color?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (Long().IsMatch(s)) return s.ToLowerInvariant();
        var m = Short().Match(s);
        return m.Success ? $"#{m.Groups[1].Value}{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[2].Value}{m.Groups[3].Value}{m.Groups[3].Value}".ToLowerInvariant() : null;
    }

    public static string Normalize(string? color) => Parse(color) ?? DefaultAccent;

    /// <summary>The business's second accent, or the accent 35% darker.</summary>
    public static string Second(string accent, string? secondary) => Parse(secondary) ?? Darken(accent, 0.35);

    /// <summary>Each channel multiplied by (1 - amount).</summary>
    public static string Darken(string color, double amount) => Hex(Channels(color).Select(v => v * (1 - amount)));

    /// <summary>The colour at <paramref name="alpha"/> over white (the web uses an alpha channel on the white sheet).</summary>
    public static string Tint(string color, double alpha)
    {
        var a = Math.Round(Math.Clamp(alpha, 0, 1) * 255) / 255;
        return Hex(Channels(color).Select(v => v * a + 255 * (1 - a)));
    }

    /// <summary><paramref name="color"/> at <paramref name="alpha"/> over <paramref name="background"/> (the web's opacity on a fill).</summary>
    public static string Mix(string color, string background, double alpha)
    {
        var fg = Channels(color);
        var bg = Channels(background);
        return Hex(fg.Select((v, i) => v * alpha + bg[i] * (1 - alpha)));
    }

    public static double Luminance(string color)
    {
        var c = Channels(color).Select(v =>
        {
            var s = v / 255;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }).ToArray();
        return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    }

    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Text on a fill: white or ink, whichever contrasts more.</summary>
    public static string On(string fill) => Contrast(fill, White) >= Contrast(fill, Ink) ? White : Ink;

    /// <summary>The colour as text on white: darkened in 5% steps until it reaches 4.5:1 (ink if it never does).</summary>
    public static string TextOnWhite(string color)
    {
        for (var i = 0; i <= 19; i++)
        {
            var c = Darken(color, i * 0.05);
            if (Contrast(c, White) >= 4.5) return c;
        }
        return Ink;
    }

    private static double[] Channels(string color)
    {
        var c = Normalize(color);
        return
        [
            int.Parse(c.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(c.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(c.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        ];
    }

    private static string Hex(IEnumerable<double> rgb) =>
        "#" + string.Concat(rgb.Select(v => ((int)Math.Round(Math.Clamp(v, 0, 255), MidpointRounding.AwayFromZero)).ToString("x2", CultureInfo.InvariantCulture)));
}
