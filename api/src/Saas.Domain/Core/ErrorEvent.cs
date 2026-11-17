namespace Saas.Domain.Core;

/// <summary>
/// One kind of error seen in production: an unhandled API exception or a crash reported by one of the web apps. Repeats of the
/// same error (same source, type, message shape and route) add to <see cref="Count"/> instead of making new rows.
/// </summary>
public class ErrorEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>SHA-256 (hex) of the source, kind, message and route with ids and numbers taken out.</summary>
    public string Fingerprint { get; set; } = "";
    /// <summary>"api", or "web:{app}" for a browser crash (for example "web:school").</summary>
    public string Source { get; set; } = "";
    /// <summary>The exception type, or "client" for a browser error.</summary>
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Path { get; set; }
    public string? Stack { get; set; }
    public int Count { get; set; } = 1;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    /// <summary>Set when an admin marks it fixed; cleared again if the error comes back.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }
}
