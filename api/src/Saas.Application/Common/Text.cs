using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Saas.Application.Common;

public static partial class Slugs
{
    /// <summary>
    /// Paths the web apps use, so they can never be business slugs. school-web serves each school's sign-in page at /{slug}, so its
    /// own top-level pages are here too (SchoolPlatformTests checks the school-web app folder against this list).
    /// </summary>
    public static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "o", "login", "signup", "dashboard", "pricing", "admin", "app", "www", "static",
        "_next", "uploads", "about", "terms", "privacy", "help", "support", "b", "i", "q", "invite", "create",
        "parent", "student", "demo", "forgot-password", "reset-password", "account",
    };

    public static string From(string text)
    {
        var normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        var slug = NonSlugChars().Replace(sb.ToString(), "-").Trim('-');
        slug = MultiDash().Replace(slug, "-");
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        return slug.Length == 0 ? "item" : slug;
    }

    public const string Rule = "Use 3-60 lowercase letters, numbers and dashes (some words are reserved).";

    /// <summary>A usable new slug: well formed and not reserved.</summary>
    public static bool IsValid(string slug) => IsWellFormed(slug) && !Reserved.Contains(slug);

    /// <summary>The format alone. Settings saves check this, then <see cref="EnsureNotNewlyReserved"/>, so a business that had a word
    /// before it was reserved can still save its settings.</summary>
    public static bool IsWellFormed(string slug) => slug.Length is >= 3 and <= 60 && ValidSlug().IsMatch(slug);

    /// <summary>A reserved word is refused only when the slug is changing to it; an existing slug is grandfathered.</summary>
    public static void EnsureNotNewlyReserved(string slug, string? current)
    {
        if (slug != current && Reserved.Contains(slug)) throw BadRequestException.Field("slug", Rule);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ValidSlug();
}

public static class Codes
{
    // No 0/O/1/I/L so codes read clearly over the phone.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Random(int length = 6) =>
        string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });

    /// <summary>A code of <paramref name="length"/> characters from the same alphabet, taken from the first bytes of <paramref name="bytes"/>.</summary>
    public static string FromBytes(ReadOnlySpan<byte> bytes, int length)
    {
        if (bytes.Length < length) throw new ArgumentException("Not enough bytes for the code.", nameof(bytes));
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++) chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    public static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public static partial class PhoneNumbers
{
    /// <summary>
    /// Turns local or international input ("0803 123 4567", "+234 803...") into wa.me digits ("2348031234567").
    /// Returns null when the result is not a plausible phone number.
    /// </summary>
    public static string? Normalize(string? input, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var trimmed = input.Trim();
        var digits = NonDigits().Replace(trimmed, "");
        if (trimmed.StartsWith("00")) digits = digits[2..];
        else if (!trimmed.StartsWith('+'))
        {
            var dial = Countries.DialCode(countryCode);
            if (digits.StartsWith('0')) digits = dial + digits.TrimStart('0');
            else if (!digits.StartsWith(dial) && digits.Length <= 10) digits = dial + digits;
        }
        return digits.Length is >= 8 and <= 15 ? digits : null;
    }

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigits();
}

public static class Countries
{
    private static readonly Dictionary<string, (string Dial, string Currency, string Timezone)> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["NG"] = ("234", "NGN", "Africa/Lagos"),
            ["GH"] = ("233", "GHS", "Africa/Accra"),
            ["KE"] = ("254", "KES", "Africa/Nairobi"),
            ["ZA"] = ("27", "ZAR", "Africa/Johannesburg"),
            ["GB"] = ("44", "GBP", "Europe/London"),
            ["US"] = ("1", "USD", "America/New_York"),
        };

    public static bool IsSupported(string code) => Known.ContainsKey(code);
    public static string DialCode(string code) => Known.TryGetValue(code, out var c) ? c.Dial : "234";
    public static string Currency(string code) => Known.TryGetValue(code, out var c) ? c.Currency : "NGN";
    public static string Timezone(string code) => Known.TryGetValue(code, out var c) ? c.Timezone : "UTC";
}

public static class Money
{
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NGN"] = "₦", ["GHS"] = "GH₵", ["KES"] = "KSh ", ["ZAR"] = "R", ["GBP"] = "£", ["USD"] = "$", ["EUR"] = "€",
    };

    public static readonly HashSet<string> SupportedCurrencies = new(Symbols.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>Formats minor units for display, e.g. 600000 NGN → "₦6,000" and 150050 → "₦1,500.50".</summary>
    public static string Format(long minor, string currency)
    {
        var symbol = Symbols.TryGetValue(currency, out var s) ? s : currency + " ";
        var sign = minor < 0 ? "-" : "";
        var abs = Math.Abs(minor);
        var amount = abs % 100 == 0
            ? (abs / 100).ToString("N0", CultureInfo.InvariantCulture)
            : (abs / 100m).ToString("N2", CultureInfo.InvariantCulture);
        return $"{sign}{symbol}{amount}";
    }
}
