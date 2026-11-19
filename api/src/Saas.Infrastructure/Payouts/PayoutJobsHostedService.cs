using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saas.Application.Payouts;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Payouts;

/// <summary>
/// Re-checks Paystack transfers still in flight every 5 minutes (GET /transfer/verify). It runs whether automatic transfers are on or
/// off, so transfers still in flight when they are switched off are settled (it only re-sends while they are on). A PostgreSQL advisory
/// lock keeps it to one API instance at a time.
/// </summary>
public sealed class PayoutJobsHostedService(IServiceScopeFactory scopes, ILogger<PayoutJobsHostedService> logger) : BackgroundService
{
    private const long LockKey = 0x1_1A5_0009;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                await Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "payouts", () => RunOnceAsync(stoppingToken), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Payout transfer re-check failed");
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
            using var jobScope = scopes.CreateScope();
            await jobScope.ServiceProvider.GetRequiredService<PayoutAdminService>().RecheckTransfersAsync(DateTimeOffset.UtcNow, ct);
        }
        finally
        {
            await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({LockKey}) AS \"Value\"").ToListAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
