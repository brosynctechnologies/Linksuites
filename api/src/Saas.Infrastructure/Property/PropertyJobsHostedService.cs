using Saas.Infrastructure.Jobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saas.Application.Property;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Property;

/// <summary>Renew links signed with ASP.NET Data Protection (keys in Postgres), valid for 14 days.</summary>
public sealed class ListingTokens(IDataProtectionProvider provider) : IListingTokens
{
    private readonly ITimeLimitedDataProtector _renew = provider.CreateProtector("property.renew").ToTimeLimitedDataProtector();

    public string Renew(Guid listingId) => _renew.Protect(listingId.ToString("N"), TimeSpan.FromDays(14));

    public Guid? ReadRenew(string token)
    {
        try
        {
            return Guid.TryParseExact(_renew.Unprotect(token), "N", out var id) ? id : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Runs <see cref="PropertyJobs"/> every 2 minutes. A PostgreSQL advisory lock keeps it to one API instance at a time.</summary>
public sealed class PropertyJobsHostedService(IServiceScopeFactory scopes, ILogger<PropertyJobsHostedService> logger) : BackgroundService
{
    private const long LockKey = 0x1_1A5_0004;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        do
        {
            try
            {
                await Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "property", () => RunOnceAsync(stoppingToken), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Property jobs failed");
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
            await jobScope.ServiceProvider.GetRequiredService<PropertyJobs>().RunAllAsync(DateTimeOffset.UtcNow, ct);
        }
        finally
        {
            await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({LockKey}) AS \"Value\"").ToListAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
