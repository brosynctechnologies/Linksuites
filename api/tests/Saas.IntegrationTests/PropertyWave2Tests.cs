using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Common;
using Saas.Application.Property;
using Saas.Domain.Property;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

/// <summary>Wave 2 (roadmap 2026-11-17): the PropertyLink minors left from the recheck (M-7, M-11, M-13) and the boost alert race.</summary>
[Collection(nameof(ApiCollection))]
public sealed class PropertyWave2Tests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<T> AtAsync<TService, T>(Guid? tenantId, DateTimeOffset now, Func<TService, Task<T>> action) where TService : notnull
    {
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        if (tenantId is { } t) sp.GetRequiredService<ITenantContext>().SetTenant(t);
        TimeProvider clock = new FixedClock(now);
        return await action(ActivatorUtilities.CreateInstance<TService>(sp, clock));
    }

    // M-7

    [Fact]
    public async Task M7_A_monthly_rent_is_filtered_on_its_yearly_amount_and_pins_carry_the_period()
    {
        var agency = await kit.AgencyAsync("Monthly Rent Agency", verified: true);
        var monthlyBody = await kit.ListingBodyAsync(title: "Monthly flat in Lekki", price: 1_500_000_00);
        monthlyBody["pricePeriod"] = "Month"; // ₦1.5m a month is ₦18m a year
        var monthly = await kit.LiveListingAsync(agency.Client, monthlyBody);
        var yearly = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Yearly flat in Lekki", price: 1_800_000_00));

        static async Task<JsonElement> Search(HttpClient c, string q) =>
            await Json(await c.GetAsync($"/api/listings?purpose=Rent&{q}&pageSize=48", Ct));
        static IEnumerable<Guid> Pins(JsonElement r) => r.GetProperty("pins").EnumerateArray().Select(p => p.GetProperty("id").GetGuid());

        var anon = api.CreateClient();
        var cheap = await Search(anon, "maxPrice=200000000"); // up to ₦2m a year
        Assert.DoesNotContain(monthly, Pins(cheap));
        Assert.Contains(yearly, Pins(cheap));

        var dear = await Search(anon, "minPrice=1700000000&maxPrice=1900000000"); // ₦17m to ₦19m a year
        Assert.Contains(monthly, Pins(dear));
        Assert.DoesNotContain(yearly, Pins(dear));

        var pin = dear.GetProperty("pins").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == monthly);
        Assert.Equal("Month", pin.GetProperty("pricePeriod").GetString());
        Assert.Equal(1_500_000_00, pin.GetProperty("priceMinor").GetInt64());
    }

    // M-11

    [Fact]
    public async Task M11_Stats_and_enquiries_count_on_the_Lagos_day()
    {
        var agency = await kit.AgencyAsync("Lagos Day Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Lagos day flat"));
        await api.MakeProAsync(agency.TenantId, "property");

        // 23:30 UTC on 20 Nov is 00:30 on 21 Nov in Lagos.
        var lateNight = new DateTimeOffset(2026, 11, 20, 23, 30, 0, TimeSpan.Zero);
        await AtAsync<ListingEventsService, bool>(null, lateNight, async s => { await s.RecordAsync(id, ListingEventKind.Call, Ct); return true; });
        await AtAsync<LeadService, bool>(null, lateNight, async s =>
        {
            await s.SendAsync(id, new EnquiryRequest("Night Owl", "+2348011112222", null, "Still free?"), Ct);
            return true;
        });

        var rows = await api.WithDbAsync(db => db.ListingStats.IgnoreQueryFilters().Where(s => s.ListingId == id).ToListAsync(Ct));
        var row = Assert.Single(rows);
        Assert.Equal(new DateOnly(2026, 11, 21), row.Day);
        Assert.Equal(1, row.CallClicks);
        Assert.Equal(1, row.Enquiries);

        // 00:30 on 22 Nov in Lagos: "the last day" is 22 Nov only, so the 21st's calls are out; two days take them in.
        var nextNight = lateNight.AddDays(1);
        var oneDay = await AtAsync<ListingEventsService, ListingStatsDto>(agency.TenantId, nextNight, s => s.StatsAsync(1, Ct));
        Assert.Equal(0, oneDay.CallClicks);
        var twoDays = await AtAsync<ListingEventsService, ListingStatsDto>(agency.TenantId, nextNight, s => s.StatsAsync(2, Ct));
        Assert.Equal(1, twoDays.CallClicks);
    }

    // M-13

    [Fact]
    public async Task M13_Editing_a_pending_listing_keeps_its_submitted_time_and_queue_place()
    {
        var agency = await kit.AgencyAsync("Pending Editor"); // unverified, so the listing waits for review
        var body = await kit.ListingBodyAsync(title: "Pending edit flat");
        var id = (await kit.CreateListingAsync(agency.Client, body)).GetProperty("id").GetGuid();
        (await kit.SubmitAsync(agency.Client, id)).EnsureSuccessStatusCode();
        var admin = await kit.AdminAsync();

        async Task<DateTimeOffset> SubmittedAsync() =>
            (await Json(await admin.GetAsync("/api/admin/property/moderation", Ct))).EnumerateArray()
                .Single(i => i.GetProperty("listingId").GetGuid() == id).GetProperty("submittedAt").GetDateTimeOffset();

        var submitted = await SubmittedAsync();
        await Task.Delay(50, Ct);
        body["title"] = "Pending edit flat, freshly painted";
        (await agency.Client.PutAsJsonAsync($"/api/property/listings/{id}", body, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(submitted, await SubmittedAsync());
    }

    // Wave 1 leftover: the boost mismatch alert

    [Fact]
    public async Task A_boost_mismatch_alert_is_sent_once_when_the_webhook_and_return_page_arrive_together()
    {
        var agency = await kit.AgencyAsync("Racing Booster", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        (await agency.Client.PostAsJsonAsync($"/api/property/listings/{id}/boosts", new { code = "top-7" }, Ct)).EnsureSuccessStatusCode();
        var reference = await api.WithDbAsync(db => db.Boosts.IgnoreQueryFilters().Where(b => b.ListingId == id).Select(b => b.Reference).SingleAsync(Ct));

        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var scope = api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BoostService>().ConfirmAsync(reference, 1_000, "NGN", Ct);
        }));

        var alerts = await api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.TemplateKey == "boost.payment_mismatch" || n.TemplateKey == "payments.mismatch").ToListAsync(Ct));
        var mine = alerts.Where(n => n.Payload is { } p && p.TryGetValue("reference", out var r) && r == reference).ToList();
        Assert.Single(mine, n => n.TemplateKey == "boost.payment_mismatch");
        var finance = mine.Where(n => n.TemplateKey == "payments.mismatch").ToList();
        Assert.NotEmpty(finance);
        Assert.Equal(finance.Count, finance.Select(n => n.Recipient).Distinct().Count());
    }
}
