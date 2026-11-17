using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saas.Application.Auth;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Platform;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Platform;

/// <summary>
/// Stores errors in <c>error_events</c>, one row per kind of error, in its own database scope (the request's own context may be
/// broken by the very error being recorded). The first time a new error appears, and when an API error marked fixed comes back,
/// super and support admins get an email, at most
/// <see cref="MaxAlertsPerHour"/> an hour on this server so a crash loop can't flood their inboxes. Browser reports never email.
/// </summary>
public sealed class ErrorLogRecorder(IServiceScopeFactory scopes, TimeProvider clock, PlatformOptions platform, AppUrls urls,
    ILogger<ErrorLogRecorder> logger) : IErrorLog
{
    public const int MaxAlertsPerHour = 10;
    /// <summary>New kinds of browser error one app may add in a day; past that, only existing rows are counted.</summary>
    public const int MaxNewBrowserErrorsPerAppPerDay = 200;
    private readonly Lock _gate = new();
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private int _alerts;
    /// <summary>New browser error kinds added per reporter (IP and app) in the current hour.</summary>
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> _reporters = [];

    public async Task RecordAsync(string source, string kind, string message, string? path, string? stack, CancellationToken ct = default,
        string? reporter = null)
    {
        try
        {
            source = ErrorRules.Cap(source, ErrorRules.MaxSource);
            kind = ErrorRules.Cap(kind, ErrorRules.MaxKind);
            // Secrets (bearer tokens, secret keys, password= and key=/token= values) never reach the table or the alert email.
            message = ErrorRules.Cap(ErrorRules.Redact(message), ErrorRules.MaxMessage);
            var cleanPath = ErrorRules.StripQuery(path) is { Length: > 0 } p ? ErrorRules.Cap(ErrorRules.Redact(p), ErrorRules.MaxPath) : null;
            var cleanStack = string.IsNullOrEmpty(stack) ? null : ErrorRules.Cap(ErrorRules.Redact(stack), ErrorRules.MaxStack);
            var fingerprint = ErrorRules.Fingerprint(source, kind, message, cleanPath);
            var now = clock.GetUtcNow();

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fromBrowser = source.StartsWith("web:", StringComparison.Ordinal);
            if (fromBrowser)
            {
                // Browser reports are anonymous, so one reporter can add only a few new kinds an hour and each app only so many a day.
                // Past either limit, a report only counts against an error that is already listed.
                var since = now.AddDays(-1);
                if (!ReporterMayAdd(reporter, now)
                    || await db.ErrorEvents.CountAsync(e => e.Source == source && e.FirstSeenAt > since, ct) >= MaxNewBrowserErrorsPerAppPerDay)
                {
                    await db.ErrorEvents.Where(e => e.Fingerprint == fingerprint)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.Count, e => e.Count + 1).SetProperty(e => e.LastSeenAt, now), ct);
                    return;
                }
            }
            // An API error marked fixed that comes back is news, like a new one. Reopening is a conditional update (row-locked), so of
            // two servers seeing it at once only one counts it as reopened. A browser report never reopens (anyone can send one).
            var reopened = !fromBrowser && await db.ErrorEvents
                .Where(e => e.Fingerprint == fingerprint && e.ResolvedAt != null)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ResolvedAt, (DateTimeOffset?)null), ct) > 0;
            // One statement, so two servers seeing the same error at once still make one row. xmax = 0 only for a fresh insert.
            var isNew = await db.Database.SqlQuery<bool>($"""
                INSERT INTO error_events (id, fingerprint, source, kind, message, path, stack, count, first_seen_at, last_seen_at)
                VALUES ({Guid.NewGuid()}, {fingerprint}, {source}, {kind}, {message}, {cleanPath}, {cleanStack}, 1, {now}, {now})
                ON CONFLICT (fingerprint) DO UPDATE SET count = error_events.count + 1, last_seen_at = EXCLUDED.last_seen_at,
                    resolved_at = CASE WHEN EXCLUDED.source LIKE 'web:%' THEN error_events.resolved_at ELSE NULL END,
                    stack = COALESCE(error_events.stack, EXCLUDED.stack)
                RETURNING (xmax = 0) AS "Value"
                """).AsAsyncEnumerable().SingleAsync(ct);
            // A browser report can't reopen an error marked fixed (anyone can send one); an API error that comes back does.
            if (isNew && fromBrowser) CountReporterAdd(reporter, now);

            if (!(isNew || reopened) || fromBrowser || !TakeAlert(now)) return; // reopened errors share the hourly cap
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationQueue>();
            var payload = new Dictionary<string, string>
            {
                ["source"] = source, ["kind"] = kind, ["message"] = message, ["path"] = cleanPath ?? "",
                ["link"] = $"{urls.Web("admin")}/errors", ["reopened"] = reopened && !isNew ? "true" : "false",
            };
            foreach (var email in await RecipientsAsync(db, ct)) notifications.Email(email, "platform.error_new", new(payload));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record an error from {Source}", source);
        }
    }

    private bool ReporterMayAdd(string? reporter, DateTimeOffset now)
    {
        if (reporter is null) return true;
        lock (_gate)
            return !_reporters.TryGetValue(reporter, out var r) || now - r.Start >= TimeSpan.FromHours(1)
                || r.Count < ErrorRules.NewBrowserKindsPerReporterPerHour;
    }

    private void CountReporterAdd(string? reporter, DateTimeOffset now)
    {
        if (reporter is null) return;
        lock (_gate)
        {
            if (_reporters.Count > 10_000) // forget stale reporters so the table stays small
                foreach (var key in _reporters.Where(x => now - x.Value.Start >= TimeSpan.FromHours(1)).Select(x => x.Key).ToList())
                    _reporters.Remove(key);
            _reporters[reporter] = _reporters.TryGetValue(reporter, out var r) && now - r.Start < TimeSpan.FromHours(1)
                ? (r.Start, r.Count + 1)
                : (now, 1);
        }
    }

    private bool TakeAlert(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (now - _windowStart >= TimeSpan.FromHours(1)) (_windowStart, _alerts) = (now, 0);
            if (_alerts >= MaxAlertsPerHour) return false;
            _alerts++;
            return true;
        }
    }

    private async Task<List<string>> RecipientsAsync(AppDbContext db, CancellationToken ct)
    {
        var team = await (from a in db.PlatformAdmins join u in db.Users on a.UserId equals u.Id
                          where a.RemovedAt == null && (a.Role == PlatformRole.SuperAdmin || a.Role == PlatformRole.Support) && u.Email != null
                          select u.Email!).Distinct().ToListAsync(ct);
        return team.Count > 0
            ? team
            : platform.AdminEmails.Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
