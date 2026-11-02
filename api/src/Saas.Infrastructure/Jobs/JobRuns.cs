using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Jobs;

/// <summary>Records each background job's last start, success and error in <c>job_runs</c> for the admin health page.</summary>
public static class JobRuns
{
    public static async Task TrackAsync(IServiceScopeFactory scopes, string name, Func<Task> run, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            await run();
            await SaveAsync(scopes, name, started, DateTimeOffset.UtcNow, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await SaveAsync(scopes, name, started, null, ex.Message.Length > 500 ? ex.Message[..500] : ex.Message, CancellationToken.None);
            throw;
        }
    }

    private static async Task SaveAsync(IServiceScopeFactory scopes, string name, DateTimeOffset started, DateTimeOffset? succeeded, string? error, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO job_runs (name, last_started_at, last_succeeded_at, last_error) VALUES ({name}, {started}, {succeeded}, {error})
                ON CONFLICT (name) DO UPDATE SET last_started_at = EXCLUDED.last_started_at,
                  last_succeeded_at = COALESCE(EXCLUDED.last_succeeded_at, job_runs.last_succeeded_at), last_error = EXCLUDED.last_error
                """, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Recording a run must never break the job itself.
            System.Diagnostics.Debug.WriteLine($"Could not record job run {name}: {ex.Message}");
        }
    }
}
