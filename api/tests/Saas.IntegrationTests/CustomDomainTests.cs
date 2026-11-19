using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Orders;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Wave 3 item 2: custom domains for OrderLink stores on a plan that includes them.</summary>
[Collection(nameof(ApiCollection))]
public sealed class CustomDomainTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private FakeDomainResolver Dns => api.Dns;

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {text}");
        return string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    private async Task<(HttpClient Client, Guid TenantId, string Slug)> ProStoreAsync(string name)
    {
        var (client, auth) = await api.SignupAsync(name);
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.MakeProAsync(tenantId, Products.WhatsAppOrders);
        return (client, tenantId, auth.GetProperty("tenant").GetProperty("slug").GetString()!);
    }

    private static Task<HttpResponseMessage> SetDomain(HttpClient c, string domain) =>
        c.PutAsJsonAsync("/api/store/domain", new { domain }, Ct);

    private HttpClient Host(string? secret = ApiFixture.CustomDomainsHostSecret)
    {
        var c = api.CreateClient();
        if (secret is not null) c.DefaultRequestHeaders.Add("X-Host-Secret", secret);
        return c;
    }

    private async Task<List<string>> HostListAsync() =>
        (await Json(await Host().GetAsync("/api/internal/custom-domains", Ct))).GetProperty("domains").EnumerateArray()
            .Select(d => d.GetProperty("domain").GetString()!).ToList();

    private Task<HttpResponseMessage> Report(string domain, bool ok, string? error = null) =>
        Host().PostAsJsonAsync("/api/internal/custom-domains/report", new { domain, ok, error }, Ct);

    private void DropCache() => api.Services.GetRequiredService<CustomDomainCache>().Invalidate();

    /// <summary>A Pro store whose domain points here, verified and reported live by the host script.</summary>
    private async Task<(HttpClient Client, Guid TenantId, string Slug, string Domain)> LiveStoreAsync(string name)
    {
        var (client, tenantId, slug) = await ProStoreAsync(name);
        var domain = $"{Unique("shop")}.example.com";
        Dns.Point(domain, ApiFixture.CustomDomainsIp);
        await Json(await SetDomain(client, domain));
        (await Report(domain, ok: true)).EnsureSuccessStatusCode();
        return (client, tenantId, slug, domain);
    }

    [Fact]
    public async Task A_plan_without_custom_domains_cannot_set_one()
    {
        var (free, _) = await api.SignupAsync("DomainFree");
        var r = await SetDomain(free, $"{Unique("free")}.example.com");
        Assert.Equal(HttpStatusCode.PaymentRequired, r.StatusCode);

        var dto = await Json(await free.GetAsync("/api/store/domain", Ct));
        Assert.False(dto.GetProperty("included").GetBoolean());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("domain").ValueKind);
    }

    [Theory]
    [InlineData("not a domain")]
    [InlineData("localhost")]
    [InlineData("192.168.1.10")]
    [InlineData("-bad.example.com")]
    [InlineData("shop..example.com")]
    [InlineData("shop.example.c")]
    [InlineData("shop_1.example.com")]
    [InlineData("https://")]
    [InlineData("*.example.com")]
    public async Task Malformed_domains_are_refused(string domain)
    {
        var (store, _, _) = await ProStoreAsync("DomainFormat");
        var r = await SetDomain(store, domain);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Theory]
    [InlineData("linksuite.brosync.ng")]
    [InlineData("orders.linksuite.brosync.ng")]
    [InlineData("evil.brosync.ng")]
    [InlineData("stores.linksuite.test")]
    [InlineData("x.stores.linksuite.test")]
    public async Task LinkSuites_own_domains_are_refused(string domain)
    {
        var (store, _, _) = await ProStoreAsync("DomainOwn");
        var r = await SetDomain(store, domain);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("LinkSuite", await r.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_domain_is_normalised_and_shows_cname_instructions()
    {
        var (store, _, _) = await ProStoreAsync("DomainCname");
        var name = Unique("Shop");
        var dto = await Json(await SetDomain(store, $"  https://{name}.Example.COM./  "));

        Assert.Equal($"{name.ToLowerInvariant()}.example.com", dto.GetProperty("domain").GetString());
        Assert.Equal("PendingDns", dto.GetProperty("status").GetString());
        var dns = dto.GetProperty("dns");
        Assert.Equal("CNAME", dns.GetProperty("type").GetString());
        Assert.Equal(name.ToLowerInvariant(), dns.GetProperty("name").GetString());
        Assert.Equal(ApiFixture.CustomDomainsTarget, dns.GetProperty("value").GetString());
        Assert.NotEqual(JsonValueKind.Null, dto.GetProperty("checkedAt").ValueKind); // checked straight away
        Assert.Contains("DNS", dto.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("example.com.ng")]
    [InlineData("example.co.uk")]
    public async Task An_apex_domain_gets_an_a_record(string suffix)
    {
        var (store, _, _) = await ProStoreAsync("DomainApex");
        var apex = $"{Unique("apex")}{suffix}".Replace("-", "");
        var dto = await Json(await SetDomain(store, apex));
        var dns = dto.GetProperty("dns");
        Assert.Equal("A", dns.GetProperty("type").GetString());
        Assert.Equal("@", dns.GetProperty("name").GetString());
        Assert.Equal(ApiFixture.CustomDomainsIp, dns.GetProperty("value").GetString());
    }

    [Fact]
    public async Task A_domain_held_by_another_store_is_refused()
    {
        var (first, _, _) = await ProStoreAsync("DomainDupOne");
        var (second, _, _) = await ProStoreAsync("DomainDupTwo");
        var domain = $"{Unique("dup")}.example.com";
        await Json(await SetDomain(first, domain));

        var r = await SetDomain(second, domain.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("already", await r.Content.ReadAsStringAsync(Ct));
        // Setting your own domain again is fine.
        await Json(await SetDomain(first, domain));
    }

    [Fact]
    public async Task Dns_check_moves_a_domain_from_pending_to_verified_or_failed()
    {
        var (store, _, _) = await ProStoreAsync("DomainDns");
        var domain = $"{Unique("dns")}.example.com";
        var pending = await Json(await SetDomain(store, domain));
        Assert.Equal("PendingDns", pending.GetProperty("status").GetString());

        Dns.Point(domain, "198.51.100.7");
        var failed = await Json(await store.PostAsync("/api/store/domain/check", null, Ct));
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.Contains("198.51.100.7", failed.GetProperty("error").GetString());

        Dns.Point(domain, ApiFixture.CustomDomainsIp);
        var verified = await Json(await store.PostAsync("/api/store/domain/check", null, Ct));
        Assert.Equal("Verified", verified.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, verified.GetProperty("error").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, verified.GetProperty("verifiedAt").ValueKind);
    }

    [Fact]
    public async Task A_lookup_error_leaves_the_domain_pending()
    {
        var (store, _, _) = await ProStoreAsync("DomainDnsDown");
        var domain = $"{Unique("down")}.example.com";
        Dns.Broken.Add(domain);
        var dto = await Json(await SetDomain(store, domain));
        Assert.Equal("PendingDns", dto.GetProperty("status").GetString());
        Assert.Contains("try again", dto.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_background_job_verifies_pending_domains()
    {
        var (store, tenantId, _) = await ProStoreAsync("DomainJob");
        var domain = $"{Unique("job")}.example.com";
        await Json(await SetDomain(store, domain));
        Dns.Point(domain, ApiFixture.CustomDomainsIp);

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CustomDomainJobs>().RunOnceAsync(DateTimeOffset.UtcNow, Ct);

        var status = await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == tenantId).Select(t => t.CustomDomainStatus).SingleAsync(Ct));
        Assert.Equal(CustomDomainStatus.Verified, status);
    }

    [Fact]
    public async Task Host_endpoints_need_the_shared_secret()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Host(null).GetAsync("/api/internal/custom-domains", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Host("wrong-secret").GetAsync("/api/internal/custom-domains", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Host("wrong-secret").PostAsJsonAsync("/api/internal/custom-domains/report", new { domain = "a.example.com", ok = true }, Ct)).StatusCode);

        // A host without a secret configured refuses every call, even with an empty header.
        var unset = api.WithWebHostBuilder(b => b.UseSetting("CustomDomains:HostSecret", "")).CreateClient();
        unset.DefaultRequestHeaders.Add("X-Host-Secret", "");
        Assert.Equal(HttpStatusCode.Unauthorized, (await unset.GetAsync("/api/internal/custom-domains", Ct)).StatusCode);
    }

    [Fact]
    public async Task Host_lists_verified_and_live_domains_and_reports_make_them_live_or_failed()
    {
        var (store, _, _) = await ProStoreAsync("DomainHost");
        var pendingStore = await ProStoreAsync("DomainHostPending");
        var domain = $"{Unique("host")}.example.com";
        var pendingDomain = $"{Unique("hostp")}.example.com";
        Dns.Point(domain, ApiFixture.CustomDomainsIp);
        await Json(await SetDomain(store, domain));
        await Json(await SetDomain(pendingStore.Client, pendingDomain));

        var listed = await HostListAsync();
        Assert.Contains(domain, listed);
        Assert.DoesNotContain(pendingDomain, listed);

        (await Report(domain, ok: true)).EnsureSuccessStatusCode();
        var live = await Json(await store.GetAsync("/api/store/domain", Ct));
        Assert.Equal("Live", live.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, live.GetProperty("liveAt").ValueKind);
        Assert.True(live.GetProperty("served").GetBoolean());
        Assert.Contains(domain, await HostListAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await Report("nobody.example.com", ok: true)).StatusCode);

        // The host script reads the plain-text form, closed by "# end".
        var text = await (await Host().GetAsync("/api/internal/custom-domains?format=text", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains($"{domain} Live\n", text);
        Assert.EndsWith("# end\n", text);
    }

    [Fact]
    public async Task A_certificate_failure_is_shown_and_not_retried_for_an_hour()
    {
        var (store, tenantId, _) = await ProStoreAsync("DomainCertFail");
        var domain = $"{Unique("cert")}.example.com";
        Dns.Point(domain, ApiFixture.CustomDomainsIp);
        await Json(await SetDomain(store, domain));

        (await Report(domain, ok: false, error: "certbot: too many failed authorizations")).EnsureSuccessStatusCode();
        var failed = await Json(await store.GetAsync("/api/store/domain", Ct));
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.Contains("certificate", failed.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

        // Checking again finds the DNS fine, but the host waits an hour before asking for a certificate again.
        var rechecked = await Json(await store.PostAsync("/api/store/domain/check", null, Ct));
        Assert.Equal("Verified", rechecked.GetProperty("status").GetString());
        Assert.DoesNotContain(domain, await HostListAsync());

        await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CustomDomainCertFailedAt, DateTimeOffset.UtcNow.AddHours(-2)), Ct));
        Assert.Contains(domain, await HostListAsync());
    }

    [Fact]
    public async Task Public_lookup_answers_only_live_domains()
    {
        var (store, _, slug) = await ProStoreAsync("DomainLookup");
        var domain = $"{Unique("look")}.example.com";
        Dns.Point(domain, ApiFixture.CustomDomainsIp);
        await Json(await SetDomain(store, domain));
        var anon = api.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/domains/{domain}", Ct)).StatusCode); // verified only
        (await Report(domain, ok: true)).EnsureSuccessStatusCode();

        var found = await Json(await anon.GetAsync($"/api/public/domains/{domain.ToUpperInvariant()}", Ct));
        Assert.Equal(slug, found.GetProperty("slug").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/public/domains/unknown.example.com", Ct)).StatusCode);
    }

    [Fact]
    public async Task Cors_accepts_a_live_custom_domain_only()
    {
        var (_, _, slug, domain) = await LiveStoreAsync("DomainCors");
        var other = $"{Unique("nocors")}.example.com";

        async Task<string?> AllowedOrigin(string origin)
        {
            using var preflight = new HttpRequestMessage(HttpMethod.Options, $"/api/public/stores/{slug}/quote");
            preflight.Headers.Add("Origin", origin);
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
            var r = await api.CreateClient().SendAsync(preflight, Ct);
            return r.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) ? v.Single() : null;
        }

        Assert.Equal($"https://{domain}", await AllowedOrigin($"https://{domain}"));
        Assert.Null(await AllowedOrigin($"https://{other}"));
        Assert.Null(await AllowedOrigin($"http://{domain}.evil.test"));
        Assert.Equal("http://localhost:3700", await AllowedOrigin("http://localhost:3700")); // configured origins still work
    }

    [Fact]
    public async Task A_lapsed_plan_stops_serving_the_domain()
    {
        var (store, tenantId, _, domain) = await LiveStoreAsync("DomainLapse");
        Assert.Contains(domain, await HostListAsync());

        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentPeriodEnd, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        DropCache();

        Assert.DoesNotContain(domain, await HostListAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/api/public/domains/{domain}", Ct)).StatusCode);
        var dto = await Json(await store.GetAsync("/api/store/domain", Ct));
        Assert.False(dto.GetProperty("included").GetBoolean());
        Assert.False(dto.GetProperty("served").GetBoolean());
        Assert.Equal(domain, dto.GetProperty("domain").GetString()); // kept, so it comes back when the plan does

        await api.MakeProAsync(tenantId, Products.WhatsAppOrders);
        DropCache();
        Assert.Contains(domain, await HostListAsync());
    }

    [Fact]
    public async Task A_suspended_business_is_not_served()
    {
        var (_, tenantId, _, domain) = await LiveStoreAsync("DomainSuspended");
        await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenantStatus.Suspended), Ct));
        DropCache();
        Assert.DoesNotContain(domain, await HostListAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/api/public/domains/{domain}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Removing_or_changing_the_domain_takes_the_old_one_off_the_host_list()
    {
        var (store, _, _, domain) = await LiveStoreAsync("DomainRemove");
        var next = $"{Unique("next")}.example.com";

        var changed = await Json(await SetDomain(store, next));
        Assert.Equal("PendingDns", changed.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("liveAt").ValueKind);
        Assert.DoesNotContain(domain, await HostListAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/api/public/domains/{domain}", Ct)).StatusCode);

        Dns.Point(next, ApiFixture.CustomDomainsIp);
        await Json(await store.PostAsync("/api/store/domain/check", null, Ct));
        Assert.Contains(next, await HostListAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await store.DeleteAsync("/api/store/domain", Ct)).StatusCode);
        Assert.DoesNotContain(next, await HostListAsync());
        var cleared = await Json(await store.GetAsync("/api/store/domain", Ct));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("domain").ValueKind);
    }

    [Fact]
    public async Task Migration_puts_custom_domain_back_on_plans_and_snapshots_that_include_it()
    {
        var (_, tenantId, _) = await ProStoreAsync("DomainShow");
        const string Feature = "Custom domain";
        await api.WithDbAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            // As HideCustomDomain left things: off the plan and off a paid snapshot (which includes the limit).
            await db.Database.ExecuteSqlRawAsync(Saas.Infrastructure.Persistence.Migrations.HideCustomDomain.Sql, Ct);
            await db.Database.ExecuteSqlAsync($$"""
                UPDATE subscriptions SET features = ARRAY['Unlimited products'], limits = '{"custom_domain": 1}'::jsonb WHERE tenant_id = {{tenantId}}
                """, Ct);

            await db.Database.ExecuteSqlRawAsync(Saas.Infrastructure.Persistence.Migrations.Wave3CustomDomains.ShowCustomDomainSql, Ct);
            await db.Database.ExecuteSqlRawAsync(Saas.Infrastructure.Persistence.Migrations.Wave3CustomDomains.ShowCustomDomainSql, Ct); // twice

            var plans = await db.Plans.AsNoTracking().Where(p => p.Product == Products.WhatsAppOrders).ToListAsync(Ct);
            Assert.Single(plans.Single(p => p.Code == "pro").Features, f => f == Feature);
            Assert.DoesNotContain(Feature, plans.Single(p => p.Code == "free").Features);
            var sub = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().Where(s => s.TenantId == tenantId).SingleAsync(Ct);
            Assert.Single(sub.Features!, f => f == Feature);
            await tx.RollbackAsync(Ct);
            return true;
        });
    }

    [Fact]
    public async Task Staff_cannot_change_the_domain()
    {
        var (_, auth) = await api.SignupAsync("DomainStaff");
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.MakeProAsync(tenantId, Products.WhatsAppOrders);
        await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, MemberRole.Admin), Ct));
        var refreshed = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = auth.GetProperty("refreshToken").GetString() }, Ct));
        var admin = api.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", refreshed.GetProperty("accessToken").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/store/domain", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetDomain(admin, $"{Unique("staff")}.example.com")).StatusCode);
    }
}
