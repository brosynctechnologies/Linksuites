using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Orders;

/// <summary>
/// Custom domains for OrderLink stores (Wave 3). <c>CustomDomains</c> in configuration. The API only checks DNS and keeps the list; a
/// script on the host (deploy/custom-domains.sh) issues the certificates and writes the nginx sites, asking the internal endpoints
/// with <see cref="HostSecret"/>.
/// </summary>
public sealed class CustomDomainOptions
{
    public const string Section = "CustomDomains";
    /// <summary>The host a subdomain's CNAME points at, e.g. stores.linksuite.brosync.ng (it resolves to the server).</summary>
    public string Target { get; set; } = "";
    /// <summary>The server's public IPv4 address, for an apex domain's A record.</summary>
    public string? ApexIp { get; set; }
    /// <summary>The shared secret the host script sends in <c>X-Host-Secret</c> (env CUSTOM_DOMAINS_HOST_SECRET). Empty: the host endpoints refuse every call.</summary>
    public string HostSecret { get; set; } = "";
    /// <summary>Domains (and everything under them) a store may never claim, on top of the target and the apps' own hosts.</summary>
    public List<string> Reserved { get; set; } = ["brosync.ng"];
    /// <summary>How often the background job checks pending domains (minutes).</summary>
    public int CheckMinutes { get; set; } = 10;
}

/// <summary>Looks a host name up in DNS. Answers no addresses for a name that doesn't exist; throws <see cref="DomainLookupException"/> when the lookup itself fails.</summary>
public interface IDomainResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct);
}

/// <summary>A DNS lookup that failed (server error, timeout), as opposed to a name with no records.</summary>
public sealed class DomainLookupException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record CustomDomainDnsRecord(string Type, string Name, string Value);

/// <summary>
/// The store's custom domain as its settings page shows it. <c>Included</c>: the plan includes custom domains. <c>Served</c>: customers
/// reach the store on it now (live, included, business active).
/// </summary>
public sealed record CustomDomainDto(
    string? Domain, CustomDomainStatus? Status, string? Error, DateTimeOffset? CheckedAt, DateTimeOffset? VerifiedAt, DateTimeOffset? LiveAt,
    bool Included, bool Served, CustomDomainDnsRecord? Dns, string Target, string? ApexIp);

public sealed record SetCustomDomainRequest(string? Domain);
public sealed record HostDomainDto(string Domain, CustomDomainStatus Status);
public sealed record HostDomainsDto(IReadOnlyList<HostDomainDto> Domains);
public sealed record HostDomainReport(string? Domain, bool Ok, string? Error);
public sealed record PublicDomainDto(string Slug);

public static partial class CustomDomainRules
{
    /// <summary>Two-label public suffixes common for our markets, so example.com.ng counts as an apex domain.</summary>
    private static readonly HashSet<string> SecondLevelSuffixes =
    [
        "com.ng", "org.ng", "net.ng", "edu.ng", "gov.ng", "name.ng", "sch.ng", "mobi.ng",
        "com.gh", "org.gh", "edu.gh", "co.ke", "or.ke", "ac.ke", "co.za", "org.za", "web.za",
        "co.uk", "org.uk", "me.uk", "ltd.uk", "plc.uk", "com.au", "net.au", "org.au", "co.in", "co.nz", "com.br",
    ];

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex Label();

    [GeneratedRegex("^(?:[a-z]{2,63}|xn--[a-z0-9-]{1,59})$")]
    private static partial Regex TopLevel();

    /// <summary>What the owner typed, as a bare lower-case host: scheme, path, port and a trailing dot removed. Null when nothing is left.</summary>
    public static string? Normalize(string? input)
    {
        var d = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(d)) return null;
        foreach (var scheme in new[] { "https://", "http://" })
            if (d.StartsWith(scheme, StringComparison.Ordinal)) d = d[scheme.Length..];
        var cut = d.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) d = d[..cut];
        var colon = d.IndexOf(':');
        if (colon >= 0) d = d[..colon];
        d = d.TrimEnd('.');
        return d.Length == 0 ? null : d;
    }

    public static bool IsWellFormed(string domain)
    {
        if (domain.Length > 253) return false;
        var labels = domain.Split('.');
        return labels.Length >= 2 && labels.All(l => Label().IsMatch(l)) && TopLevel().IsMatch(labels[^1]);
    }

    private static int RegistrableLabels(string domain)
    {
        var labels = domain.Split('.');
        return labels.Length >= 3 && SecondLevelSuffixes.Contains($"{labels[^2]}.{labels[^1]}") ? 3 : 2;
    }

    /// <summary>A domain with no subdomain (example.com, example.com.ng): it takes an A record, since the root can't hold a CNAME.</summary>
    public static bool IsApex(string domain) => domain.Split('.').Length <= RegistrableLabels(domain);

    /// <summary>The record's name as DNS panels ask for it: "@" for the apex, else the part before the registrable domain ("shop").</summary>
    public static string RecordName(string domain)
    {
        if (IsApex(domain)) return "@";
        var labels = domain.Split('.');
        return string.Join('.', labels[..^RegistrableLabels(domain)]);
    }

    public static bool IsUnder(string domain, string reserved)
    {
        var r = reserved.Trim().TrimEnd('.').ToLowerInvariant();
        return r.Length > 0 && (domain == r || domain.EndsWith("." + r, StringComparison.Ordinal));
    }

    public static string? HostOf(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : null;
}

/// <summary>
/// The domains served right now (live, plan includes them, business active), domain → store slug. Read by the public lookup and by
/// CORS on every storefront call, so it is kept for a minute; any change through <see cref="CustomDomainService"/> drops it at once.
/// </summary>
public sealed class CustomDomainCache(IServiceScopeFactory scopes, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile Snapshot? current;

    private sealed record Snapshot(IReadOnlyDictionary<string, string> Domains, DateTimeOffset LoadedAt);

    public void Invalidate() => current = null;

    public async Task<IReadOnlyDictionary<string, string>> ServedAsync(CancellationToken ct)
    {
        var snap = current;
        if (snap is not null && clock.GetUtcNow() - snap.LoadedAt < Lifetime) return snap.Domains;
        await gate.WaitAsync(ct);
        try
        {
            snap = current;
            if (snap is not null && clock.GetUtcNow() - snap.LoadedAt < Lifetime) return snap.Domains;
            using var scope = scopes.CreateScope();
            var domains = await scope.ServiceProvider.GetRequiredService<CustomDomainService>().LoadServedAsync(ct);
            current = new Snapshot(domains, clock.GetUtcNow());
            return domains;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The store slug served on <paramref name="host"/>, or null.</summary>
    public async Task<string?> SlugForAsync(string? host, CancellationToken ct)
    {
        var d = CustomDomainRules.Normalize(host);
        return d is not null && (await ServedAsync(ct)).TryGetValue(d, out var slug) ? slug : null;
    }
}

public sealed class CustomDomainService(
    IAppDbContext db, ITenantContext tenant, PlanService plans, IDomainResolver resolver, CustomDomainOptions options, AppUrls urls,
    CustomDomainCache cache, IDbErrors dbErrors, TimeProvider clock)
{
    /// <summary>A certificate failure isn't retried for this long (Let's Encrypt allows 5 failed validations per hour per name).</summary>
    public static readonly TimeSpan CertificateRetryAfter = TimeSpan.FromHours(1);
    public const int MaxErrorLength = 500;

    private async Task<Tenant> CurrentAsync(CancellationToken ct)
    {
        var id = tenant.TenantId ?? throw new ForbiddenException();
        return await db.Tenants.FirstAsync(t => t.Id == id, ct);
    }

    public async Task<CustomDomainDto> GetAsync(CancellationToken ct)
    {
        var t = await CurrentAsync(ct);
        return ToDto(t, await plans.HasFeatureAsync(LimitKeys.CustomDomain, ct));
    }

    public async Task<CustomDomainDto> SetAsync(SetCustomDomainRequest r, CancellationToken ct)
    {
        await EnsureIncludedAsync(ct);
        var domain = CustomDomainRules.Normalize(r.Domain);
        if (domain is null || !CustomDomainRules.IsWellFormed(domain))
            throw BadRequestException.Field("domain", "Enter a domain such as shop.yourbusiness.com or yourbusiness.com.");
        if (Reserved().Any(x => CustomDomainRules.IsUnder(domain, x)))
            throw BadRequestException.Field("domain", "That is a LinkSuite address. Enter a domain your business owns, such as shop.yourbusiness.com.");
        var t = await CurrentAsync(ct);
        if (await db.Tenants.AnyAsync(x => x.CustomDomain == domain && x.Id != t.Id, ct)) throw Taken();

        if (t.CustomDomain != domain)
        {
            t.CustomDomain = domain;
            t.CustomDomainStatus = CustomDomainStatus.PendingDns;
            t.CustomDomainError = null;
            t.CustomDomainVerifiedAt = null;
            t.CustomDomainLiveAt = null;
            t.CustomDomainCertFailedAt = null;
            t.CustomDomainCheckedAt = null;
        }
        await CheckAsync(t, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex))
        {
            throw Taken(); // another store saved it between the check and this save
        }
        cache.Invalidate();
        return ToDto(t, included: true);
    }

    public async Task RemoveAsync(CancellationToken ct)
    {
        var t = await CurrentAsync(ct);
        t.CustomDomain = null;
        t.CustomDomainStatus = null;
        t.CustomDomainError = null;
        t.CustomDomainCheckedAt = null;
        t.CustomDomainVerifiedAt = null;
        t.CustomDomainLiveAt = null;
        t.CustomDomainCertFailedAt = null;
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
    }

    /// <summary>The owner's "Check again": looks the DNS up now.</summary>
    public async Task<CustomDomainDto> CheckNowAsync(CancellationToken ct)
    {
        await EnsureIncludedAsync(ct);
        var t = await CurrentAsync(ct);
        if (t.CustomDomain is null) throw BadRequestException.Field("domain", "Add your domain first.");
        await CheckAsync(t, ct);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return ToDto(t, included: true);
    }

    private async Task EnsureIncludedAsync(CancellationToken ct)
    {
        if (await plans.HasFeatureAsync(LimitKeys.CustomDomain, ct)) return;
        var plan = await plans.GetCurrentPlanAsync(ct);
        throw new PlanLimitException($"Custom domains are not included in the {plan.Name} plan. Upgrade to use your own domain.");
    }

    private static BadRequestException Taken() => BadRequestException.Field("domain", "That domain is already used by another store.");

    private IEnumerable<string> Reserved()
    {
        foreach (var r in options.Reserved) yield return r;
        if (CustomDomainRules.Normalize(options.Target) is { } target) yield return target;
        foreach (var url in new[] { urls.WebBaseUrl, urls.ApiBaseUrl }.Concat(urls.ProductWebUrls?.Values ?? []))
            if (CustomDomainRules.HostOf(url) is { } host) yield return host;
    }

    /// <summary>
    /// Looks the domain up and moves it on: pointing at LinkSuite (the target's addresses or the apex IP) verifies it; pointing elsewhere
    /// fails it; no records yet leaves it pending. A failed lookup changes nothing but the note. A live domain is only failed when it
    /// clearly points elsewhere, never for a missing answer, so a DNS hiccup can't take a store offline.
    /// </summary>
    public async Task CheckAsync(Tenant t, CancellationToken ct)
    {
        if (t.CustomDomain is not { } domain) return;
        var now = clock.GetUtcNow();
        t.CustomDomainCheckedAt = now;
        IReadOnlyList<IPAddress> found;
        try
        {
            found = await resolver.ResolveAsync(domain, ct);
        }
        catch (DomainLookupException)
        {
            t.CustomDomainError = $"We couldn't look up {domain} just now. We'll try again shortly.";
            return;
        }
        var expected = await ExpectedAsync(ct);
        if (expected.Count == 0)
        {
            t.CustomDomainError = "Custom domains aren't switched on yet. We'll check again shortly.";
            return;
        }
        if (found.Count == 0)
        {
            if (t.CustomDomainStatus is CustomDomainStatus.Live or CustomDomainStatus.Verified) return;
            t.CustomDomainStatus = CustomDomainStatus.PendingDns;
            t.CustomDomainError = $"No DNS record found for {domain} yet. Add the record below; DNS changes can take a few hours to show.";
            return;
        }
        if (found.Any(expected.Contains))
        {
            if (t.CustomDomainStatus is not (CustomDomainStatus.Live or CustomDomainStatus.Verified))
            {
                t.CustomDomainStatus = CustomDomainStatus.Verified;
                t.CustomDomainVerifiedAt = now;
            }
            t.CustomDomainError = null;
            return;
        }
        t.CustomDomainStatus = CustomDomainStatus.Failed;
        t.CustomDomainLiveAt = null;
        t.CustomDomainError = Cap($"{domain} points to {string.Join(", ", found.Take(4))}, not to LinkSuite. Change the record to the one below.");
    }

    private async Task<HashSet<IPAddress>> ExpectedAsync(CancellationToken ct)
    {
        var set = new HashSet<IPAddress>();
        if (IPAddress.TryParse(options.ApexIp?.Trim(), out var apex)) set.Add(apex);
        if (CustomDomainRules.Normalize(options.Target) is { } target)
        {
            try { set.UnionWith(await resolver.ResolveAsync(target, ct)); }
            catch (DomainLookupException) { }
        }
        return set;
    }

    private CustomDomainDto ToDto(Tenant t, bool included)
    {
        CustomDomainDnsRecord? dns = null;
        var target = CustomDomainRules.Normalize(options.Target) ?? "";
        var apexIp = string.IsNullOrWhiteSpace(options.ApexIp) ? null : options.ApexIp.Trim();
        if (t.CustomDomain is { } d)
            dns = CustomDomainRules.IsApex(d)
                ? apexIp is not null ? new("A", "@", apexIp) : new("ALIAS", "@", target)
                : new("CNAME", CustomDomainRules.RecordName(d), target);
        var served = included && t.Status == TenantStatus.Active && t.CustomDomainStatus == CustomDomainStatus.Live;
        return new CustomDomainDto(t.CustomDomain, t.CustomDomainStatus, t.CustomDomainError, t.CustomDomainCheckedAt, t.CustomDomainVerifiedAt,
            t.CustomDomainLiveAt, included, served, dns, target, apexIp);
    }

    private static string Cap(string s) => s.Length <= MaxErrorLength ? s : s[..MaxErrorLength];

    // Plan check for many businesses at once (no tenant in scope): the same rule as PlanService.GetCurrentPlanAsync.

    /// <summary>Of <paramref name="tenantIds"/>, the OrderLink businesses whose current plan includes custom domains.</summary>
    public async Task<HashSet<Guid>> EntitledAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken ct)
    {
        if (tenantIds.Count == 0) return [];
        var now = clock.GetUtcNow();
        var subs = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => tenantIds.Contains(s.TenantId) && s.Plan!.Product == Products.WhatsAppOrders)
            .Current(now)
            .Select(s => new { s.TenantId, s.Limits, PlanLimits = s.Plan!.Limits })
            .ToListAsync(ct);
        var current = subs.GroupBy(s => s.TenantId).ToDictionary(g => g.Key, g => g.First().Limits ?? g.First().PlanLimits);
        var free = await db.Plans.AsNoTracking().Where(p => p.Product == Products.WhatsAppOrders && p.IsDefault)
            .Select(p => p.Limits).FirstOrDefaultAsync(ct) ?? [];
        return tenantIds.Where(id => (current.GetValueOrDefault(id) ?? free).GetValueOrDefault(LimitKeys.CustomDomain) != 0).ToHashSet();
    }

    private sealed record Row(Guid Id, string Domain, CustomDomainStatus? Status, DateTimeOffset? CertFailedAt);

    private async Task<List<Row>> ActiveRowsAsync(CancellationToken ct) => await db.Tenants.AsNoTracking()
        .Where(t => t.CustomDomain != null && t.Status == TenantStatus.Active && t.Product == Products.WhatsAppOrders)
        .Select(t => new Row(t.Id, t.CustomDomain!, t.CustomDomainStatus, t.CustomDomainCertFailedAt))
        .ToListAsync(ct);

    /// <summary>Live domains to serve now (plan includes them, business active), domain → store slug. Loaded through <see cref="CustomDomainCache"/>.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadServedAsync(CancellationToken ct)
    {
        var rows = (await ActiveRowsAsync(ct)).Where(r => r.Status == CustomDomainStatus.Live).ToList();
        var entitled = await EntitledAsync(rows.Select(r => r.Id).ToList(), ct);
        var ids = rows.Where(r => entitled.Contains(r.Id)).Select(r => r.Id).ToList();
        var slugs = await db.Stores.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.TenantId))
            .Select(s => new { s.TenantId, s.Slug }).ToListAsync(ct);
        var byTenant = slugs.GroupBy(s => s.TenantId).ToDictionary(g => g.Key, g => g.First().Slug);
        return rows.Where(r => byTenant.ContainsKey(r.Id)).ToDictionary(r => r.Domain, r => byTenant[r.Id], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For the host script: the domains it should serve. Verified ones need a certificate (unless one just failed), live ones keep
    /// their site. Anything not listed (removed, changed, plan lapsed, business suspended) has its site removed by the script.
    /// </summary>
    public async Task<HostDomainsDto> HostListAsync(CancellationToken ct)
    {
        var retryFrom = clock.GetUtcNow() - CertificateRetryAfter;
        var rows = (await ActiveRowsAsync(ct)).Where(r => r.Status == CustomDomainStatus.Live
            || (r.Status == CustomDomainStatus.Verified && (r.CertFailedAt == null || r.CertFailedAt < retryFrom))).ToList();
        var entitled = await EntitledAsync(rows.Select(r => r.Id).ToList(), ct);
        return new HostDomainsDto(rows.Where(r => entitled.Contains(r.Id)).OrderBy(r => r.Domain)
            .Select(r => new HostDomainDto(r.Domain, r.Status!.Value)).ToList());
    }

    /// <summary>The host script's result for one domain: a certificate and site in place (live), or why it failed.</summary>
    public async Task ReportAsync(HostDomainReport r, CancellationToken ct)
    {
        var domain = CustomDomainRules.Normalize(r.Domain) ?? throw new NotFoundException("Domain");
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.CustomDomain == domain, ct) ?? throw new NotFoundException("Domain");
        var now = clock.GetUtcNow();
        if (r.Ok)
        {
            if (t.CustomDomainStatus is not (CustomDomainStatus.Verified or CustomDomainStatus.Live)) return; // changed since the script asked
            if (t.CustomDomainStatus != CustomDomainStatus.Live) t.CustomDomainLiveAt = now;
            t.CustomDomainStatus = CustomDomainStatus.Live;
            t.CustomDomainError = null;
            t.CustomDomainCertFailedAt = null;
        }
        else
        {
            var why = string.IsNullOrWhiteSpace(r.Error) ? "the server reported an error" : r.Error.Trim();
            t.CustomDomainStatus = CustomDomainStatus.Failed;
            t.CustomDomainLiveAt = null;
            t.CustomDomainCertFailedAt = now;
            t.CustomDomainError = Cap($"We couldn't set up the security certificate for {domain} ({why}). We'll try again within a few hours; check that the DNS record below is the only one for this name.");
        }
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
    }

    /// <summary>
    /// The background check: pending domains every run, failed ones hourly (a failed certificate is retried once the DNS checks out
    /// again), verified and live ones daily. Only businesses whose plan includes custom domains are looked up.
    /// </summary>
    public async Task<int> RunChecksAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.Tenants
            .Where(t => t.CustomDomain != null && t.Status == TenantStatus.Active && t.Product == Products.WhatsAppOrders
                && (t.CustomDomainStatus == CustomDomainStatus.PendingDns
                    || (t.CustomDomainStatus == CustomDomainStatus.Failed && (t.CustomDomainCheckedAt == null || t.CustomDomainCheckedAt < now.AddHours(-1)))
                    || t.CustomDomainCheckedAt == null || t.CustomDomainCheckedAt < now.AddDays(-1)))
            .OrderBy(t => t.CustomDomainCheckedAt).Take(200).ToListAsync(ct);
        var entitled = await EntitledAsync(due.Select(t => t.Id).ToList(), ct);
        var checkedCount = 0;
        foreach (var t in due.Where(t => entitled.Contains(t.Id)))
        {
            await CheckAsync(t, ct);
            checkedCount++;
        }
        if (checkedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            cache.Invalidate();
        }
        return checkedCount;
    }
}

/// <summary>Runs <see cref="CustomDomainService.RunChecksAsync"/> for the hosted job (and for tests by hand).</summary>
public sealed class CustomDomainJobs(CustomDomainService domains)
{
    public Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct) => domains.RunChecksAsync(now, ct);
}
