using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saas.Application.School;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Schools;

/// <summary>Runs <see cref="SchoolJobs"/> every minute. A PostgreSQL advisory lock keeps it to one API instance at a time.</summary>
public sealed class SchoolJobsHostedService(IServiceScopeFactory scopes, ILogger<SchoolJobsHostedService> logger) : BackgroundService
{
    private const long LockKey = 0x1_1A5_0005;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "School jobs failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One run, recorded in <c>job_runs</c> as "school" for the admin health page.</summary>
    public Task TickAsync(CancellationToken ct) =>
        Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "school", () => RunOnceAsync(ct), ct);

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.OpenConnectionAsync(ct);
        var locked = (await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({LockKey}) AS \"Value\"").ToListAsync(ct)).Single();
        if (!locked) return;
        try
        {
            await scope.ServiceProvider.GetRequiredService<SchoolJobs>().RunOnceAsync(DateTimeOffset.UtcNow, ct);
        }
        finally
        {
            await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({LockKey}) AS \"Value\"").ToListAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
