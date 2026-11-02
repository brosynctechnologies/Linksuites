using Saas.Infrastructure.Jobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Invoices;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Invoices;

/// <summary>ASP.NET Data Protection, with keys stored in PostgreSQL so every API instance can decrypt.</summary>
public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Saas.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}

/// <summary>
/// Every 15 minutes, runs recurring invoices and reminders for each invoices tenant. A PostgreSQL advisory
/// lock makes sure only one API instance does this at a time.
/// </summary>
public sealed class InvoiceJobsHostedService(IServiceScopeFactory scopes, ILogger<InvoiceJobsHostedService> logger) : BackgroundService
{
    private const long LockKey = 0x1_1A5_0001;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "invoices", () => RunOnceAsync(stoppingToken), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Invoice jobs failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> tenantIds;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.OpenConnectionAsync(ct);
            var locked = (await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({LockKey}) AS \"Value\"").ToListAsync(ct)).Single();
            if (!locked) return;
            try
            {
                tenantIds = await db.Tenants.Where(t => t.Product == Products.Invoices && t.Status == Domain.Core.TenantStatus.Active)
                    .Select(t => t.Id).ToListAsync(ct);
                foreach (var tenantId in tenantIds)
                {
                    try
                    {
                        using var tenantScope = scopes.CreateScope();
                        tenantScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
                        var (created, reminded) = await tenantScope.ServiceProvider.GetRequiredService<InvoiceJobs>().RunAsync(ct);
                        if (created + reminded > 0)
                            logger.LogInformation("Tenant {Tenant}: {Created} recurring invoices, {Reminded} reminders", tenantId, created, reminded);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Invoice jobs failed for tenant {Tenant}", tenantId);
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
}
