using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Domain.Core;
using Saas.Domain.Property;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

/// <summary>Fixes for the PropertyLink and admin console recheck (property-admin.md): client and server numbers agree.</summary>
[Collection(nameof(ApiCollection))]
public sealed class PropertyAdminRecheckFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    private readonly PropertyKit kit = new(api);

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Lagos).DateTime);

    private static async Task<JsonElement> Ok(HttpResponseMessage r, string what)
    {
        Assert.True(r.IsSuccessStatusCode, $"{what}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    /// <summary>A live short-let: ₦50,000 a night, ₦10,000 cleaning.</summary>
    private async Task<(Agency Agency, Guid ListingId)> ShortLetAsync(string name)
    {
        var agency = await kit.AgencyAsync(name, verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"{name} apartment", purpose: ListingPurpose.ShortLet, price: 5_000_000));
        return (agency, id);
    }

    private static object Dates(Guid listingId, DateOnly checkIn, int nights = 3) =>
        new { listingId, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(nights).ToString("yyyy-MM-dd"), guests = 2, phone = "+2348066666666" };

    private async Task<JsonElement> PayAsync(HttpClient guest, JsonElement booking)
    {
        var reference = api.Gateway.Initialized.Last(i => booking.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, booking.GetProperty("totalMinor").GetInt64(), "NGN", DateTimeOffset.UtcNow);
        return await Ok(await guest.PostAsJsonAsync($"/api/stays/{booking.GetProperty("stayId").GetGuid()}/verify", new { reference }, Ct), "verify");
    }

    // I-1

    [Fact]
    public async Task I1_A_guest_pressing_reserve_again_releases_their_own_hold_and_is_not_blocked()
    {
        var (_, id) = await ShortLetAsync("Retry Homes");
        var guest = await kit.SeekerAsync("Retrier");
        var checkIn = Today.AddDays(30);

        var first = await Ok(await guest.PostAsJsonAsync("/api/stays", Dates(id, checkIn), Ct), "first reserve");
        // Back from Paystack: the quote and a second "Reserve and pay" both work for the guest holding the dates.
        await Ok(await guest.PostAsJsonAsync($"/api/listings/{id}/stay-quote", Dates(id, checkIn), Ct), "quote by the holder");
        var second = await Ok(await guest.PostAsJsonAsync("/api/stays", Dates(id, checkIn), Ct), "second reserve");

        var stays = await api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().Where(s => s.ListingId == id).ToListAsync(Ct));
        Assert.Equal(StayStatus.Expired, stays.Single(s => s.Id == first.GetProperty("stayId").GetGuid()).Status);
        Assert.Equal(StayStatus.Pending, stays.Single(s => s.Id == second.GetProperty("stayId").GetGuid()).Status);
        Assert.Single(stays, s => s.Status == StayStatus.Pending);

        // Everyone else is still refused while the hold is live.
        var other = await kit.SeekerAsync("Other Guest");
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync("/api/stays", Dates(id, checkIn), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.CreateClient().PostAsJsonAsync($"/api/listings/{id}/stay-quote", Dates(id, checkIn), Ct)).StatusCode);
    }

    [Fact]
    public async Task I1_The_calendar_leaves_out_the_guests_own_hold_for_them_only()
    {
        var (_, id) = await ShortLetAsync("Calendar Homes");
        var guest = await kit.SeekerAsync("Calendar Holder");
        var checkIn = Today.AddDays(35);
        await Ok(await guest.PostAsJsonAsync("/api/stays", Dates(id, checkIn), Ct), "reserve");

        static bool Booked(JsonElement a, DateOnly day) => a.GetProperty("booked").EnumerateArray()
            .Any(r => DateOnly.Parse(r.GetProperty("start").GetString()!) <= day && day < DateOnly.Parse(r.GetProperty("end").GetString()!));
        var mine = await Ok(await guest.GetAsync($"/api/listings/{id}/availability", Ct), "holder's calendar");
        var everyone = await Ok(await api.CreateClient().GetAsync($"/api/listings/{id}/availability", Ct), "public calendar");
        Assert.False(Booked(mine, checkIn)); // they can pick their own dates again after going back from Paystack
        Assert.True(Booked(everyone, checkIn));
    }

    // I-3

    [Fact]
    public async Task I3_Studio_stays_carry_the_hosts_net_after_refunds()
    {
        var (agency, id) = await ShortLetAsync("Net Homes");
        var late = await kit.SeekerAsync("Late Canceller");
        var lateBooking = await Ok(await late.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(1)), Ct), "late booking");
        await PayAsync(late, lateBooking);
        (await late.PostAsync($"/api/stays/{lateBooking.GetProperty("stayId").GetGuid()}/cancel", null, Ct)).EnsureSuccessStatusCode();

        var dropped = await kit.SeekerAsync("Dropped Guest");
        var droppedBooking = await Ok(await dropped.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(20)), Ct), "host-cancelled booking");
        await PayAsync(dropped, droppedBooking);
        (await agency.Client.PostAsync($"/api/property/stays/{droppedBooking.GetProperty("stayId").GetGuid()}/cancel", null, Ct)).EnsureSuccessStatusCode();

        var kept = await kit.SeekerAsync("Kept Guest");
        var keptBooking = await Ok(await kept.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(40)), Ct), "kept booking");
        await PayAsync(kept, keptBooking);

        var rows = (await Ok(await agency.Client.GetAsync("/api/property/stays", Ct), "studio stays")).EnumerateArray().ToList();
        long Net(JsonElement booking) => rows.Single(r => r.GetProperty("id").GetGuid() == booking.GetProperty("stayId").GetGuid()).GetProperty("hostNetMinor").GetInt64();
        var earnings = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().Where(e => e.SourceId == lateBooking.GetProperty("stayId").GetGuid()).SingleAsync(Ct));
        Assert.Equal(earnings.NetMinor, Net(lateBooking)); // ₦160,000 - ₦110,000 refund = ₦50,000 kept, less 10%
        Assert.Equal(4_500_000, Net(lateBooking));
        Assert.Equal(0, Net(droppedBooking));
        Assert.Equal(14_400_000, Net(keptBooking));
    }

    // I-4

    [Fact]
    public async Task I4_Plan_checkout_returns_to_each_products_own_billing_page()
    {
        async Task<string> CallbackAsync(HttpClient owner)
        {
            await Ok(await owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct), "checkout");
            return api.Gateway.Initialized.Last().CallbackUrl;
        }

        var agency = await kit.AgencyAsync("Billing Realty");
        Assert.Matches(@"/studio/billing\?ref=sub_", await CallbackAsync(agency.Client));

        var academy = api.CreateClient();
        var signup = await Ok(await api.SignUpAsync(academy, "/api/auth/signup", new
        {
            fullName = "Billing Tutor", email = $"billing-tutor-{Guid.NewGuid():N}@example.com", password = "password123", country = "NG",
            product = "courses", businessName = "Billing Academy",
        }, Ct), "course signup");
        academy.DefaultRequestHeaders.Authorization = new("Bearer", signup.GetProperty("accessToken").GetString());
        Assert.Matches(@"/studio/billing\?ref=sub_", await CallbackAsync(academy));

        var (invoices, _) = await api.SignupInvoicesAsync("Biller");
        Assert.Matches(@"/app/billing\?ref=sub_", await CallbackAsync(invoices));
    }

    // I-5

    [Fact]
    public async Task I5_Billing_says_whether_the_current_period_is_complimentary()
    {
        var paid = await kit.AgencyAsync("Paid Plan Realty");
        await api.MakeProAsync(paid.TenantId, "property");
        var paidSub = (await Ok(await paid.Client.GetAsync("/api/billing", Ct), "paid billing")).GetProperty("subscription");
        Assert.False(paidSub.GetProperty("complimentary").GetBoolean());

        var comped = await kit.AgencyAsync("Comped Plan Realty");
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        (await finance.PostAsJsonAsync($"/api/platform/businesses/{comped.TenantId}/comp",
            new { planCode = "pro", until = DateTimeOffset.UtcNow.AddDays(30), reason = "Partner, one month free." }, Ct)).EnsureSuccessStatusCode();
        var compSub = (await Ok(await comped.Client.GetAsync("/api/billing", Ct), "comp billing")).GetProperty("subscription");
        Assert.True(compSub.GetProperty("complimentary").GetBoolean());
    }

    // I-6

    [Fact]
    public async Task I6_The_term_and_listed_date_start_when_a_moderator_approves()
    {
        var agency = await kit.AgencyAsync("Queued Realty"); // unverified: listings wait for a moderator
        var id = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Queued flat"))).GetProperty("id").GetGuid();
        (await kit.SubmitAsync(agency.Client, id)).EnsureSuccessStatusCode();
        // Ten days in the queue.
        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(l => l.PublishedAt, l => l.PublishedAt == null ? null : l.PublishedAt.Value.AddDays(-10))
            .SetProperty(l => l.LastConfirmedAt, l => l.LastConfirmedAt == null ? null : l.LastConfirmedAt.Value.AddDays(-10))
            .SetProperty(l => l.ExpiresAt, l => l.ExpiresAt == null ? null : l.ExpiresAt.Value.AddDays(-10)), Ct));

        var admin = await kit.AdminAsync();
        var approvedAt = DateTimeOffset.UtcNow;
        (await admin.PostAsJsonAsync($"/api/admin/property/listings/{id}/decision", new { approve = true }, Ct)).EnsureSuccessStatusCode();

        var listing = await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().SingleAsync(l => l.Id == id, Ct));
        Assert.Equal(ListingStatus.Active, listing.Status);
        Assert.InRange(listing.ExpiresAt!.Value, approvedAt.AddDays(30).AddMinutes(-1), approvedAt.AddDays(30).AddMinutes(1));
        Assert.InRange(listing.PublishedAt!.Value, approvedAt.AddMinutes(-1), approvedAt.AddMinutes(1));
        Assert.InRange(listing.LastConfirmedAt!.Value, approvedAt.AddMinutes(-1), approvedAt.AddMinutes(1));
    }

    [Fact]
    public async Task I6_A_listing_waiting_for_review_has_no_term_yet()
    {
        var agency = await kit.AgencyAsync("Waiting Realty");
        var id = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Waiting flat"))).GetProperty("id").GetGuid();
        var submitted = await Ok(await kit.SubmitAsync(agency.Client, id), "submit");
        Assert.Equal("Pending", submitted.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("expiresAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("publishedAt").ValueKind);
    }

    // I-7

    [Fact]
    public async Task I7_Featured_listings_from_any_city_are_served_for_the_home_page()
    {
        var admin = await kit.AdminAsync();
        var abuja = await kit.LocationAsync("abuja");
        var areaName = $"Wuse {Guid.NewGuid():N}"[..14];
        var area = await Ok(await admin.PostAsJsonAsync("/api/admin/property/locations", new { name = areaName, kind = "Area", parentId = abuja, lat = 9.07, lng = 7.47 }, Ct), "area");

        var agency = await kit.AgencyAsync("Capital Realty", verified: true);
        var body = await kit.ListingBodyAsync(title: "Featured Wuse flat", lat: 9.07, lng: 7.47);
        body["locationId"] = area.GetProperty("id").GetGuid();
        var featured = await kit.LiveListingAsync(agency.Client, body);
        var plain = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Plain Lekki flat"));
        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == featured)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.FeaturedUntil, DateTimeOffset.UtcNow.AddDays(30)), Ct));

        var home = (await Ok(await api.CreateClient().GetAsync("/api/listings/featured", Ct), "featured")).EnumerateArray().ToList();
        Assert.Contains(home, c => c.GetProperty("id").GetGuid() == featured);
        Assert.DoesNotContain(home, c => c.GetProperty("id").GetGuid() == plain);
        Assert.All(home, c => Assert.True(c.GetProperty("featured").GetBoolean()));
    }

    // I-8

    [Fact]
    public async Task I8_Payments_show_refunds_and_what_was_actually_collected()
    {
        var (agency, id) = await ShortLetAsync("Refunding Homes");
        var guest = await kit.SeekerAsync("Refunded Guest");
        var booking = await Ok(await guest.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(15)), Ct), "booking");
        await PayAsync(guest, booking);
        (await agency.Client.PostAsync($"/api/property/stays/{booking.GetProperty("stayId").GetGuid()}/cancel", null, Ct)).EnsureSuccessStatusCode();
        var reference = await api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().Where(s => s.Id == booking.GetProperty("stayId").GetGuid()).Select(s => s.Reference).SingleAsync(Ct));

        var unpaid = await kit.SeekerAsync("Unpaid Guest");
        var unpaidBooking = await Ok(await unpaid.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(25)), Ct), "unpaid booking");
        (await unpaid.PostAsync($"/api/stays/{unpaidBooking.GetProperty("stayId").GetGuid()}/cancel", null, Ct)).EnsureSuccessStatusCode();

        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var rows = (await Ok(await finance.GetAsync("/api/platform/payments?product=property&pageSize=100", Ct), "payments")).GetProperty("items").EnumerateArray().ToList();
        var row = rows.Single(r => r.GetProperty("reference").GetString() == reference);
        Assert.Equal((16_000_000L, 16_000_000L, 0L), (row.GetProperty("amountMinor").GetInt64(), row.GetProperty("refundedMinor").GetInt64(), row.GetProperty("collectedMinor").GetInt64()));

        var csv = await (await finance.GetAsync("/api/platform/payments.csv?product=property", Ct)).Content.ReadAsStringAsync(Ct);
        var lines = csv.TrimEnd('\n').Split('\n');
        Assert.Equal("date,kind,product,business,amount,refunded,collected,currency,status,reference", lines[0]);
        Assert.Contains(",160000.00,160000.00,0.00,", lines.Single(l => l.Contains(reference)));
    }

    [Fact]
    public async Task I8_Collected_is_received_money_less_refunds_for_every_source()
    {
        var agency = await kit.AgencyAsync("Collected Realty");
        var paidRef = $"sub_paid_{Guid.NewGuid():N}";
        var pendingRef = $"sub_pending_{Guid.NewGuid():N}";
        var refundedRef = $"sub_refunded_{Guid.NewGuid():N}";
        await api.WithDbAsync(db =>
        {
            Payment P(string reference, PaymentStatus status) => new()
            {
                TenantId = agency.TenantId, AmountMinor = 1_000_000, Currency = "NGN", Provider = "paystack", ProviderRef = reference, Purpose = "subscription",
                Status = status, PaidAt = status == PaymentStatus.Pending ? null : DateTimeOffset.UtcNow,
            };
            db.Payments.AddRange(P(paidRef, PaymentStatus.Succeeded), P(pendingRef, PaymentStatus.Pending), P(refundedRef, PaymentStatus.Refunded));
            return db.SaveChangesAsync(Ct);
        });

        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var rows = (await Ok(await finance.GetAsync("/api/platform/payments?product=property&pageSize=100", Ct), "payments")).GetProperty("items").EnumerateArray().ToList();
        (long Refunded, long Collected) Of(string reference)
        {
            var r = rows.Single(x => x.GetProperty("reference").GetString() == reference);
            return (r.GetProperty("refundedMinor").GetInt64(), r.GetProperty("collectedMinor").GetInt64());
        }
        Assert.Equal((0L, 1_000_000L), Of(paidRef));
        Assert.Equal((0L, 0L), Of(pendingRef));
        Assert.Equal((1_000_000L, 0L), Of(refundedRef));
    }

    // I-9

    private Task AddSubscriptionAsync(Guid tenantId, string planCode, DateTimeOffset start, DateTimeOffset end, string provider) => api.WithDbAsync(async db =>
    {
        var plan = await db.Plans.FirstAsync(p => p.Product == "property" && p.Code == planCode, Ct);
        var sub = new Subscription
        {
            TenantId = tenantId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentPeriodStart = start, CurrentPeriodEnd = end, Provider = provider,
        };
        sub.Snapshot(plan, plan.PriceMinor, plan.Currency);
        db.Subscriptions.Add(sub);
        return await db.SaveChangesAsync(Ct);
    });

    [Fact]
    public async Task I9_List_filter_header_and_subscription_panel_agree_on_the_current_plan()
    {
        var now = DateTimeOffset.UtcNow;
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        // (a) A Pro period that ended, with its status never changed: the business is on Free everywhere.
        var lapsed = await kit.AgencyAsync($"Lapsed Realty {Guid.NewGuid():N}"[..20]);
        await AddSubscriptionAsync(lapsed.TenantId, "pro", now.AddDays(-45), now.AddDays(-15), "paystack");
        // (b) Paid Agency until soon, plus a comped Pro that runs longer: the higher plan in force wins everywhere,
        // so a comp never takes away a plan the business paid for.
        var both = await kit.AgencyAsync($"Overlap Realty {Guid.NewGuid():N}"[..21]);
        await AddSubscriptionAsync(both.TenantId, "agency", now.AddDays(-5), now.AddDays(15), "paystack");
        await AddSubscriptionAsync(both.TenantId, "pro", now.AddDays(-1), now.AddDays(75), "comp");

        async Task<(string Row, bool InPlan, string Header, JsonElement Sub)> ReadAsync(Agency a, string plan)
        {
            var name = await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == a.TenantId).Select(t => t.Name).SingleAsync(Ct));
            var q = Uri.EscapeDataString(name);
            var row = (await Ok(await support.GetAsync($"/api/platform/businesses?q={q}", Ct), "list")).GetProperty("items").EnumerateArray()
                .Single(r => r.GetProperty("id").GetGuid() == a.TenantId);
            var filtered = (await Ok(await support.GetAsync($"/api/platform/businesses?q={q}&plan={plan}", Ct), "filter")).GetProperty("items").EnumerateArray()
                .Any(r => r.GetProperty("id").GetGuid() == a.TenantId);
            var detail = await Ok(await support.GetAsync($"/api/platform/businesses/{a.TenantId}", Ct), "detail");
            return (row.GetProperty("planName").GetString()!, filtered, detail.GetProperty("planName").GetString()!, detail.GetProperty("subscription"));
        }

        var a = await ReadAsync(lapsed, "free");
        Assert.Equal(("Free", true, "Free"), (a.Row, a.InPlan, a.Header));
        Assert.Equal(JsonValueKind.Null, a.Sub.ValueKind);
        Assert.Equal("free", (await Ok(await lapsed.Client.GetAsync("/api/billing", Ct), "billing")).GetProperty("currentPlan").GetProperty("code").GetString());

        var b = await ReadAsync(both, "agency");
        Assert.Equal(("Agency", true, "Agency"), (b.Row, b.InPlan, b.Header));
        Assert.Equal(("agency", "paystack"), (b.Sub.GetProperty("planCode").GetString(), b.Sub.GetProperty("provider").GetString()));
        Assert.False((await ReadAsync(both, "pro")).InPlan);
        var billing = await Ok(await both.Client.GetAsync("/api/billing", Ct), "billing");
        Assert.Equal("agency", billing.GetProperty("currentPlan").GetProperty("code").GetString());
        Assert.False(billing.GetProperty("subscription").GetProperty("complimentary").GetBoolean());

        // (c) Once the paid Agency period ends, the longer comped Pro takes over.
        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.TenantId == both.TenantId && s.Provider == "paystack")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentPeriodEnd, now.AddMinutes(-1)), Ct));
        var c = await ReadAsync(both, "pro");
        Assert.Equal(("Pro", true, "Pro"), (c.Row, c.InPlan, c.Header));
        Assert.Equal("pro", (await Ok(await both.Client.GetAsync("/api/billing", Ct), "billing")).GetProperty("currentPlan").GetProperty("code").GetString());
    }

    // M-9

    [Fact]
    public async Task M9_My_stays_say_whether_the_guest_actually_paid()
    {
        var (_, id) = await ShortLetAsync("Trip Homes");
        var guest = await kit.SeekerAsync("Trip Guest");
        var abandoned = await Ok(await guest.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(10)), Ct), "abandoned");
        (await guest.PostAsync($"/api/stays/{abandoned.GetProperty("stayId").GetGuid()}/cancel", null, Ct)).EnsureSuccessStatusCode();
        var paid = await Ok(await guest.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(30)), Ct), "paid");
        await PayAsync(guest, paid);

        var mine = (await Ok(await guest.GetAsync("/api/me/stays", Ct), "my stays")).EnumerateArray().ToList();
        bool Paid(JsonElement booking) => mine.Single(s => s.GetProperty("id").GetGuid() == booking.GetProperty("stayId").GetGuid()).GetProperty("paid").GetBoolean();
        Assert.False(Paid(abandoned));
        Assert.True(Paid(paid));
    }

    // M-14

    [Fact]
    public async Task M14_Plan_subscriber_counts_leave_comps_out_and_count_them_apart()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        async Task<(int Paying, int Comped)> CountAsync()
        {
            var pro = (await Ok(await finance.GetAsync("/api/platform/plans", Ct), "plans")).EnumerateArray()
                .Single(p => p.GetProperty("product").GetString() == "property" && p.GetProperty("code").GetString() == "pro");
            return (pro.GetProperty("activeSubscribers").GetInt32(), pro.GetProperty("compSubscribers").GetInt32());
        }
        var before = await CountAsync();
        var agency = await kit.AgencyAsync("Counted Comp Realty");
        (await finance.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/comp",
            new { planCode = "pro", until = DateTimeOffset.UtcNow.AddDays(30), reason = "Launch partner, one month free." }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal((before.Paying, before.Comped + 1), await CountAsync());
    }
}
