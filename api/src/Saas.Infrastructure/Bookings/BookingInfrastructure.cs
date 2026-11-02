using Saas.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Saas.Application.Billing;
using Saas.Application.Bookings;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Bookings;

public sealed class PostgresDbErrors : IDbErrors
{
    /// <summary>23P01 = exclusion_violation.</summary>
    public bool IsExclusionViolation(Exception ex) => Has(ex, PostgresErrorCodes.ExclusionViolation);

    /// <summary>23505 = unique_violation.</summary>
    public bool IsUniqueViolation(Exception ex) => Has(ex, PostgresErrorCodes.UniqueViolation);

    /// <summary>40P01 = deadlock_detected.</summary>
    public bool IsConcurrencyConflict(Exception ex) => Has(ex, PostgresErrorCodes.DeadlockDetected);

    /// <summary>EF can wrap the driver's error more than once (e.g. its "transient failure" exception around a DbUpdateException).</summary>
    private static bool Has(Exception? ex, string sqlState)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is PostgresException p && p.SqlState == sqlState) return true;
        return false;
    }
}

/// <summary>
/// Every 2 minutes: release unpaid deposit holds and send due booking reminders for each bookings tenant.
/// A PostgreSQL advisory lock keeps it to one API instance at a time.
/// </summary>
public sealed class BookingJobsHostedService(IServiceScopeFactory scopes, ILogger<BookingJobsHostedService> logger) : BackgroundService
{
    private const long LockKey = 0x1_1A5_0002;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        do
        {
            try
            {
                await Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "bookings", () => RunOnceAsync(stoppingToken), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Booking jobs failed");
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
            var tenantIds = await db.Tenants.Where(t => t.Product == Products.Bookings && t.Status == TenantStatus.Active)
                .Select(t => t.Id).ToListAsync(ct);
            foreach (var tenantId in tenantIds)
            {
                try
                {
                    using var tenantScope = scopes.CreateScope();
                    tenantScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
                    var (expired, reminded) = await tenantScope.ServiceProvider.GetRequiredService<BookingJobs>().RunAsync(ct);
                    if (expired + reminded > 0)
                        logger.LogInformation("Tenant {Tenant}: {Expired} holds released, {Reminded} reminders sent", tenantId, expired, reminded);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Booking jobs failed for tenant {Tenant}", tenantId);
                }
            }
        }
        finally
        {
            await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({LockKey}) AS \"Value\"").ToListAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
