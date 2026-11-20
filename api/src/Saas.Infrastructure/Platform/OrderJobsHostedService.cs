using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saas.Application.Orders;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Platform;

/// <summary>Runs <see cref="OrderJobs"/> (OrderLink's stranded online payment re-check) every 5 minutes. An advisory lock keeps it to one API instance.</summary>
public sealed class OrderJobsHostedService(IServiceScopeFactory scopes, ILogger<OrderJobsHostedService> logger) : BackgroundService
{
    private const long LockKey = 0x1_1A5_00C2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(55), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                await Jobs.JobRuns.TrackAsync(scopes, "orders", () => RunOnceAsync(stoppingToken), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Order jobs failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.OpenConnectionAsync(ct);
        var locked = (await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({LockKey}) AS \"Value\"").ToListAsync(ct)).Single();
        if (!locked) return;
        try
        {
            await scope.ServiceProvider.GetRequiredService<OrderJobs>().RunOnceAsync(DateTimeOffset.UtcNow, ct);
        }
        finally
        {
            await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({LockKey}) AS \"Value\"").ToListAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
