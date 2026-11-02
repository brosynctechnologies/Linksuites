using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>
/// CareLink's scheduled work, run every minute by the care job (Task 13). Each step runs for every Active clinic in its own scope with
/// that clinic's tenant set, and a failure in one clinic or step is logged and skipped so the rest still run. Suspended and closed clinics
/// are left alone. Every step is idempotent, so running often is safe. The steps live in <see cref="CareJobSteps"/>:
/// appointment reminders, no-shows, missed doses, bed charges and low deposits; then the online-payment upkeep
/// (<see cref="OnlinePaymentService.RecheckPendingAsync"/>, <see cref="OnlinePaymentService.RetirePreviousKeyAsync"/>).
/// </summary>
public sealed class CareJobs(IServiceScopeFactory scopes, ILogger<CareJobs> logger)
{
    /// <summary>
    /// Every active clinic's care steps first, then (ruling M5) a second pass of payment upkeep for every active clinic, so a slow
    /// Paystack never holds up another clinic's reminders, doses or bed charges.
    /// </summary>
    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var clinics = await ActiveClinicsAsync(ct);
        foreach (var tenantId in clinics)
        {
            ct.ThrowIfCancellationRequested();
            await RunForClinicAsync(tenantId, now, ct);
        }
        foreach (var tenantId in clinics)
        {
            ct.ThrowIfCancellationRequested();
            await RunPaymentUpkeepAsync(tenantId, now, ct);
        }
    }

    /// <summary>The care steps for one clinic, each in its own scope and try/catch. Callers check the clinic is active (tests call it directly).</summary>
    public async Task RunForClinicAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        await StepAsync(tenantId, "appointment reminders", ct, sp => sp.GetRequiredService<CareJobSteps>().SendRemindersAsync(now, ct));
        await StepAsync(tenantId, "no-shows", ct, sp => sp.GetRequiredService<CareJobSteps>().MarkNoShowsAsync(now, ct));
        await StepAsync(tenantId, "missed doses", ct, sp => sp.GetRequiredService<CareJobSteps>().FlagMissedDosesAsync(now, ct));
        await StepAsync(tenantId, "bed charges", ct, sp => sp.GetRequiredService<CareJobSteps>().ChargeBedDaysAsync(now, ct));
        await StepAsync(tenantId, "low deposits", ct, sp => sp.GetRequiredService<CareJobSteps>().CheckDepositsAsync(now, ct));
    }

    /// <summary>Online-payment upkeep for one clinic: the throttled stranded-checkout recheck and retiring the previous Paystack key.</summary>
    public async Task RunPaymentUpkeepAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        await StepAsync(tenantId, "stranded payments", ct, sp => sp.GetRequiredService<OnlinePaymentService>().RecheckPendingAsync(now, ct));
        await StepAsync(tenantId, "previous Paystack key", ct, sp => sp.GetRequiredService<OnlinePaymentService>().RetirePreviousKeyAsync(now, ct));
    }

    private async Task<List<Guid>> ActiveClinicsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Tenants.AsNoTracking().Where(t => t.Product == Products.Care && t.Status == TenantStatus.Active)
            .OrderBy(t => t.CreatedAt).Select(t => t.Id).ToListAsync(ct);
    }

    /// <summary>Runs one step for one clinic in a fresh scope scoped to that clinic. A failure is logged, never thrown.</summary>
    private async Task StepAsync(Guid tenantId, string step, CancellationToken ct, Func<IServiceProvider, Task> run)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            await run(scope.ServiceProvider);
        }
        // Only the job's own shutdown stops the run. A timeout inside a step is that clinic's failure.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Care job step {Step} failed for clinic {TenantId}", step, tenantId);
        }
    }
}
