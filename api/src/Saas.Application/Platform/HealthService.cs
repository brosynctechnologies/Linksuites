using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Platform;

public sealed record JobRunDto(string Name, DateTimeOffset? LastStartedAt, DateTimeOffset? LastSucceededAt, string? LastError);
public sealed record OutboxDto(int Pending, int Sent, int Failed);
public sealed record FailedNotificationDto(Guid Id, string TemplateKey, string Recipient, string? Error, int Attempts, DateTimeOffset CreatedAt);
public sealed record HealthDto(IReadOnlyList<JobRunDto> Jobs, OutboxDto Outbox, IReadOnlyList<FailedNotificationDto> Failures);

/// <summary>Background jobs' last runs and the email outbox, for the admin health page.</summary>
public sealed partial class HealthService(IAppDbContext db, PlatformViewer viewer)
{
    public async Task<HealthDto> GetAsync(CancellationToken ct)
    {
        var jobs = await db.JobRuns.AsNoTracking().OrderBy(j => j.Name)
            .Select(j => new JobRunDto(j.Name, j.LastStartedAt, j.LastSucceededAt, j.LastError)).ToListAsync(ct);
        var counts = await db.Notifications.GroupBy(n => n.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(NotificationStatus s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;
        var failures = await db.Notifications.AsNoTracking().Where(n => n.Status == NotificationStatus.Failed).OrderByDescending(n => n.CreatedAt).Take(50)
            .Select(n => new FailedNotificationDto(n.Id, n.TemplateKey, n.Recipient, n.Error, n.Attempts, n.CreatedAt)).ToListAsync(ct);
        if (!await viewer.SeesContactDetailsAsync(ct)) failures = failures.Select(f => f with
        {
            Recipient = PlatformViewer.Mask(f.Recipient),
            Error = f.Error is null ? null : EmailPattern().Replace(f.Error, m => PlatformViewer.Mask(m.Value)),
        }).ToList();
        return new HealthDto(jobs, new OutboxDto(Count(NotificationStatus.Pending), Count(NotificationStatus.Sent), Count(NotificationStatus.Failed)), failures);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[^\s<>""',;:]+@[^\s<>""',;:]+")]
    private static partial System.Text.RegularExpressions.Regex EmailPattern();
}
