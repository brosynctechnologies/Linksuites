using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.School;

/// <summary>
/// SchoolLink's scheduled work, run every minute by the school job. Each step runs for every Active school in its own scope with that
/// school's tenant set, and a failure in one school or step is logged and skipped so the rest still run. Suspended and closed schools are
/// left alone (nothing is sent to their parents). Every step is idempotent, so running it often is safe.
/// </summary>
public sealed class SchoolJobs(IServiceScopeFactory scopes, ILogger<SchoolJobs> logger)
{
    /// <summary>Fee reminders go out from 08:00 Africa/Lagos.</summary>
    public static readonly TimeSpan ReminderTime = TimeSpan.FromHours(8);
    public const int BroadcastBatchSize = 100;
    public const int MaxBroadcastBatches = 10;

    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var schools = await ActiveSchoolsAsync(ct);
        foreach (var tenantId in schools)
        {
            ct.ThrowIfCancellationRequested();
            await ForSchoolAsync(tenantId, "fee reminders", ct, sp => RemindersAsync(sp, now, ct));
            await ForSchoolAsync(tenantId, "report card bundles", ct, sp => BundlesAsync(sp, now, ct));
            await ForSchoolAsync(tenantId, "announcement broadcasts", ct, sp => BroadcastsAsync(sp, ct));
            await ForSchoolAsync(tenantId, "stranded payments", ct, sp => sp.GetRequiredService<FeePaymentService>().RecheckPendingAsync(now, ct));
            await ForSchoolAsync(tenantId, "previous Paystack key", ct, sp => sp.GetRequiredService<FeePaymentService>().RetirePreviousKeyAsync(now, ct));
        }
        await Globally("expired imports", ct, async sp =>
            await sp.GetRequiredService<IAppDbContext>().ImportBatches.IgnoreQueryFilters().Where(b => b.ExpiresAt < now).ExecuteDeleteAsync(ct));
        await Globally("notification cleanup", ct, sp => sp.GetRequiredService<InAppNotifications>().CleanupAsync(now, ct));
    }

    private async Task<List<Guid>> ActiveSchoolsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Tenants.AsNoTracking().Where(t => t.Product == Products.School && t.Status == TenantStatus.Active).OrderBy(t => t.CreatedAt).Select(t => t.Id).ToListAsync(ct);
    }

    /// <summary>Runs one step for one school in a fresh scope scoped to that school. A failure is logged, never thrown.</summary>
    private async Task ForSchoolAsync(Guid tenantId, string step, CancellationToken ct, Func<IServiceProvider, Task> run)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            await run(scope.ServiceProvider);
        }
        // Only the job's own shutdown stops the run. A timeout inside a step (an HttpClient TaskCanceledException) is that school's failure.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "School job step {Step} failed for school {TenantId}", step, tenantId);
        }
    }

    private async Task Globally(string step, CancellationToken ct, Func<IServiceProvider, Task> run)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await run(scope.ServiceProvider);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "School job step {Step} failed", step);
        }
    }

    /// <summary>Once per school per Lagos day, at or after 08:00 there. The day is recorded after the run, so a restart at 09:00 doesn't send again.</summary>
    private static async Task RemindersAsync(IServiceProvider sp, DateTimeOffset now, CancellationToken ct)
    {
        if (now.ToOffset(SchoolCalendar.LagosOffset).TimeOfDay < ReminderTime) return;
        var today = SchoolCalendar.Today(now);
        var db = sp.GetRequiredService<IAppDbContext>();
        var profile = await db.SchoolProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (profile is null || profile.LastRemindersOn >= today) return;
        await sp.GetRequiredService<FinanceService>().SendDueRemindersAsync(today, ct);
        await db.SchoolProfiles.Where(p => p.Id == profile.Id && (p.LastRemindersOn == null || p.LastRemindersOn < today))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastRemindersOn, today), ct);
    }

    private async Task BundlesAsync(IServiceProvider sp, DateTimeOffset now, CancellationToken ct)
    {
        var cards = sp.GetRequiredService<ReportCardService>();
        foreach (var id in await cards.QueuedBundleIdsAsync(ct))
        {
            try
            {
                await cards.BuildBundleAsync(id, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Report card bundle {BundleId} failed", id);
            }
        }
        await cards.DeleteOldBundlesAsync(now, ct);
    }

    /// <summary>Up to 10 batches of 100 emails a run; the rest wait for the next minute, so one big school can't hold up the others.</summary>
    private static async Task BroadcastsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var announcements = sp.GetRequiredService<AnnouncementService>();
        for (var batch = 0; batch < MaxBroadcastBatches; batch++)
            if (await announcements.SendPendingBroadcastsAsync(BroadcastBatchSize, ct) < BroadcastBatchSize) break;
    }
}
