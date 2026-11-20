using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saas.Application.Orders;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Platform;

/// <summary>
/// Addresses through the operating system's resolver; TXT records through DnsClient (the OS resolver can't ask for TXT), against the
/// system's name servers without a cache, so a just-added record is seen. 5-second limit each. A name with no records answers nothing.
/// </summary>
public sealed class SystemDomainResolver : IDomainResolver
{
    private static readonly DnsClient.LookupClient Txt = new(new DnsClient.LookupClientOptions
    {
        Timeout = TimeSpan.FromSeconds(5),
        Retries = 1,
        UseCache = false,
        ThrowDnsErrors = false,
        ContinueOnDnsError = true,
    });

    public async Task<IReadOnlyList<string>> ResolveTxtAsync(string name, CancellationToken ct)
    {
        DnsClient.IDnsQueryResponse response;
        try
        {
            response = await Txt.QueryAsync(name, DnsClient.QueryType.TXT, DnsClient.QueryClass.IN, ct);
        }
        catch (DnsClient.DnsResponseException ex)
        {
            throw new DomainLookupException($"TXT lookup of {name} failed: {ex.Code}", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new DomainLookupException($"TXT lookup of {name} timed out", ex);
        }
        if (response.HasError)
        {
            if (response.Header.ResponseCode == DnsClient.DnsHeaderResponseCode.NotExistentDomain) return [];
            throw new DomainLookupException($"TXT lookup of {name} failed: {response.ErrorMessage}");
        }
        // A long value comes as several character-strings of one record: joined, as the owner typed it.
        return response.Answers.TxtRecords().Select(r => string.Concat(r.Text)).ToList();
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await Dns.GetHostAddressesAsync(host, timeout.Token);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
        {
            return [];
        }
        catch (SocketException ex)
        {
            throw new DomainLookupException($"Lookup of {host} failed: {ex.SocketErrorCode}", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new DomainLookupException($"Lookup of {host} timed out", ex);
        }
    }
}

/// <summary>Checks custom domains' DNS every <c>CustomDomains:CheckMinutes</c> (default 10). An advisory lock keeps it to one API instance.</summary>
public sealed class CustomDomainJobsHostedService(IServiceScopeFactory scopes, CustomDomainOptions options, ILogger<CustomDomainJobsHostedService> logger)
    : BackgroundService
{
    private const long LockKey = 0x1_1A5_00C1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(50), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.CheckMinutes)));
        do
        {
            try
            {
                await Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "custom-domains", () => RunOnceAsync(stoppingToken), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Custom domain checks failed");
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
            await scope.ServiceProvider.GetRequiredService<CustomDomainJobs>().RunOnceAsync(DateTimeOffset.UtcNow, ct);
        }
        finally
        {
            await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({LockKey}) AS \"Value\"").ToListAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
