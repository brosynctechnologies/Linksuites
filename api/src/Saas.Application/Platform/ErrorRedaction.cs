using System.Text.RegularExpressions;

namespace Saas.Application.Platform;

/// <summary>Takes obvious secrets out of error text before it is stored or emailed.</summary>
public static partial class ErrorRules
{
    public const string Redacted = "[redacted]";

    // "Bearer eyJ…" (an Authorization header or a token printed in a message).
    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokens();

    // Paystack-style secret and restricted keys: sk_live_…, sk_test_…, rk_live_…
    [GeneratedRegex(@"\b(sk|rk)_(live|test)_[A-Za-z0-9]+")]
    private static partial Regex SecretKeys();

    // key=value pairs whose name says secret: query strings (?key=…&token=…), connection strings (Password=…;), form bodies.
    [GeneratedRegex(@"\b(password|passwd|pwd|secret|client_secret|token|access_token|refresh_token|id_token|api_key|apikey|key|signature|sig)=([^&\s;,'""]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretPairs();

    // The same names as JSON: "password":"…"
    [GeneratedRegex(@"(""(?:password|passwd|secret|client_secret|token|access_token|refresh_token|api_key|apiKey|key)""\s*:\s*"")[^""]*("")",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretJson();

    /// <summary>
    /// The text with bearer tokens, secret keys and secret-named values replaced by <see cref="Redacted"/>. Ordinary words that only
    /// end in a secret name ("monkey=") are left alone, and so are other query values.
    /// </summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        text = BearerTokens().Replace(text, "Bearer " + Redacted);
        text = SecretKeys().Replace(text, m => $"{m.Groups[1].Value}_{m.Groups[2].Value}_{Redacted}");
        text = SecretPairs().Replace(text, m => $"{m.Groups[1].Value}={Redacted}");
        return SecretJson().Replace(text, m => $"{m.Groups[1].Value}{Redacted}{m.Groups[2].Value}");
    }
}
