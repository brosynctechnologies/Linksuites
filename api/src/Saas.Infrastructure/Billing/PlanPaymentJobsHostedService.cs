using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Billing;

/// <summary>
/// Runs <see cref="PlanPaymentJobs"/> every minute (each payment is still asked about at most every 10 minutes). A PostgreSQL advisory
/// lock keeps it to one API instance at a time. Off with <c>Jobs:BillingEnabled=false</c> (tests run it by hand).
/// </summary>
public sealed class PlanPaymentJobsHostedService(IServiceScopeFactory scopes, ILogger<PlanPaymentJobsHostedService> logger) : BackgroundService
{
    private const long LockKey = 0x1_1A5_0008;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "billing", () => RunOnceAsync(stoppingToken), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Plan payment re-check failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.OpenConnectionAsync(ct);
        var locked = (await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({LockKey}) AS \"Value\"").ToListAsync(ct)).Single();
        if (!locked) return;
        try
        {
            await scope.ServiceProvider.GetRequiredService<PlanPaymentJobs>().RunOnceAsync(DateTimeOffset.UtcNow, ct);
        }
        finally
        {
            await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({LockKey}) AS \"Value\"").ToListAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
