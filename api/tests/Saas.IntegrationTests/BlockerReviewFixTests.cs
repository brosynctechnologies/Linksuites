using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Property;

namespace Saas.IntegrationTests;

/// <summary>Fixes from the final review of the release blockers.</summary>
[Collection(nameof(ApiCollection))]
public sealed class BlockerReviewFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Lagos).DateTime);
    private readonly PropertyKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private HttpClient Bearer(string token)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Jti(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement.GetProperty("jti").GetString()!;
    }

    // I1
    [Fact]
    public async Task Removing_an_admin_ends_their_open_support_views()
    {
        var agency = await kit.AgencyAsync("Offboarded View Realty");
        var (support, supportUser, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/view-as", new { reason = "Checking a reported layout problem." }, Ct));
        var viewer = Bearer(viewAs.GetProperty("token").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/property/listings", Ct)).StatusCode);

        var (super, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.SuperAdmin);
        var rowId = await api.WithDbAsync(db => db.PlatformAdmins.Where(a => a.UserId == supportUser).Select(a => a.Id).SingleAsync(Ct));
        (await super.DeleteAsync($"/api/platform/admins/{rowId}", Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync("/api/property/listings", Ct)).StatusCode);
    }

    // I2
    [Fact]
    public async Task Finance_does_not_see_raw_audit_data_in_business_detail()
    {
        var agency = await kit.AgencyAsync("Raw Audit Realty");
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        (await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/suspend", new { reason = "Checking an audit trail entry." }, Ct)).EnsureSuccessStatusCode();
        (await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/reactivate", new { reason = "Audit trail entry checked." }, Ct)).EnsureSuccessStatusCode();
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);

        var rows = (await Json(await finance.GetAsync($"/api/platform/businesses/{agency.TenantId}", Ct))).GetProperty("recentAudit");
        Assert.NotEqual(0, rows.GetArrayLength());
        Assert.All(rows.EnumerateArray(), r => Assert.Equal((JsonValueKind.Null, JsonValueKind.Null),
            (r.GetProperty("beforeJson").ValueKind, r.GetProperty("afterJson").ValueKind)));
        var supportRows = (await Json(await support.GetAsync($"/api/platform/businesses/{agency.TenantId}", Ct))).GetProperty("recentAudit");
        Assert.Contains(supportRows.EnumerateArray(), r => r.GetProperty("afterJson").ValueKind == JsonValueKind.String);
    }

    // I3
    [Fact]
    public async Task Course_staff_cannot_read_earnings_payouts_or_the_bank_account()
    {
        var owner = api.CreateClient();
        var email = $"school-{Guid.NewGuid():N}@example.com";
        var signup = await Json(await owner.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "School Owner", email, password = "password123", country = "NG", product = "courses", businessName = "Private School Academy",
        }, Ct));
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signup.GetProperty("accessToken").GetString());
        var tenantId = signup.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.MakeProAsync(tenantId, "courses"); // Pro has 3 seats
        (await owner.PutAsJsonAsync("/api/academy/payout-account", new { bankName = "GTBank", accountNumber = "0123456789", accountName = "School Owner" }, Ct)).EnsureSuccessStatusCode();
        var staffEmail = $"tutor-{Guid.NewGuid():N}@example.com";
        (await owner.PostAsJsonAsync("/api/team/invites", new { email = staffEmail, role = "Staff" }, Ct)).EnsureSuccessStatusCode();
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == staffEmail).FirstAsync(Ct))).Payload["link"];
        var accepted = await Json(await api.CreateClient().PostAsJsonAsync($"/api/auth/invites/{link[(link.LastIndexOf('/') + 1)..]}/accept",
            new { fullName = "Tutor", password = "password123" }, Ct));
        var staff = Bearer(accepted.GetProperty("accessToken").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/studio/earnings", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/studio/payouts", Ct)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(await staff.GetAsync("/api/academy", Ct))).GetProperty("payoutAccount").ValueKind);
        Assert.Equal("0123456789", (await Json(await owner.GetAsync("/api/academy", Ct))).GetProperty("payoutAccount").GetProperty("accountNumber").GetString());
    }

    // I4
    [Fact]
    public async Task Cancelling_a_pending_stay_while_its_payment_lands_never_keeps_the_money_without_a_stay()
    {
        var agency = await kit.AgencyAsync("Racing Homes", verified: true);
        var listingId = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Racing apartment", purpose: ListingPurpose.ShortLet, price: 5_000_000));
        var guest = await kit.SeekerAsync("Racer");
        for (var i = 0; i < 6; i++)
        {
            var checkIn = Today.AddDays(20 + i * 4);
            var booking = await Json(await guest.PostAsJsonAsync("/api/stays", new
            {
                listingId, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(2).ToString("yyyy-MM-dd"), guests = 2, phone = "+2348066666666",
            }, Ct));
            var reference = api.Gateway.Initialized.Last(x => booking.GetProperty("authorizationUrl").GetString()!.EndsWith(x.Reference)).Reference;
            var total = booking.GetProperty("totalMinor").GetInt64();
            api.Gateway.Charges[reference] = new VerifiedCharge(reference, total, "NGN", DateTimeOffset.UtcNow);
            var stayId = booking.GetProperty("stayId").GetGuid();

            await Task.WhenAll(
                guest.PostAsync($"/api/stays/{stayId}/cancel", null, Ct),
                guest.PostAsJsonAsync($"/api/stays/{stayId}/verify", new { reference }, Ct));

            var stay = await api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == stayId, Ct));
            if (stay.Status == StayStatus.Cancelled && stay.PaidAt is not null)
                lock (api.Gateway.Refunds) Assert.Contains(api.Gateway.Refunds, r => r.Reference == reference && r.AmountMinor == total);
            if (stay.Status == StayStatus.Cancelled)
                Assert.False(await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().AnyAsync(e => e.SourceId == stayId && !e.Reversed, Ct)), "cancelled stay kept an earning");
        }
    }

    // M1
    [Fact]
    public async Task A_business_comped_on_a_hidden_plan_can_pay_to_continue_it()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var code = $"h{Guid.NewGuid().ToString("N")[..8]}";
        (await finance.PostAsJsonAsync("/api/platform/plans", new
        {
            product = "property", code, name = "Partner", priceMinor = 900_000, isHidden = true, sortOrder = 9,
            limits = new Dictionary<string, long> { ["listings"] = 40, ["listing_expiry_days"] = 60, ["listing_stats"] = 1, ["team_seats"] = 3 },
            features = new[] { "40 listings" },
        }, Ct)).EnsureSuccessStatusCode();
        var agency = await kit.AgencyAsync("Partner Realty");
        (await finance.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/comp",
            new { planCode = code, until = DateTimeOffset.UtcNow.AddDays(10), reason = "Launch partner for the first quarter." }, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await agency.Client.PostAsJsonAsync("/api/billing/checkout", new { planCode = code }, Ct)).StatusCode);
    }

    // M2
    [Fact]
    public async Task Support_is_told_when_the_reset_email_limit_stops_a_send()
    {
        var agency = await kit.AgencyAsync("Many Resets Realty");
        var userId = await api.WithDbAsync(db => db.Users.Where(u => u.Email == agency.Email).Select(u => u.Id).SingleAsync(Ct));
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await support.PostAsync($"/api/platform/users/{userId}/send-password-reset", null, Ct)).StatusCode);

        var fourth = await support.PostAsync($"/api/platform/users/{userId}/send-password-reset", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
    }

    // M3
    [Fact]
    public async Task Ending_a_view_by_id_cannot_end_an_admin_console_session()
    {
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var (finance, _, financeToken) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);

        Assert.Equal(HttpStatusCode.NotFound, (await support.PostAsync($"/api/platform/view-as/{Jti(financeToken)}/end", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/api/platform/me", Ct)).StatusCode);
    }

    // M5
    [Fact]
    public async Task Deleting_a_listing_deletes_its_photo_files()
    {
        var agency = await kit.AgencyAsync("Short Lived Realty");
        var listingId = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();
        var form = new MultipartFormDataContent { { new ByteArrayContent(PropertyMediaTests.Photo(3)), "file", "photo.png" } };
        var mediaId = (await Json(await agency.Client.PostAsync($"/api/property/listings/{listingId}/media", form, Ct))).GetProperty("id").GetGuid();
        var originalKey = await api.WithDbAsync(db => db.ListingMedia.IgnoreQueryFilters().Where(m => m.Id == mediaId).Select(m => m.OriginalKey).SingleAsync(Ct));

        (await agency.Client.DeleteAsync($"/api/property/listings/{listingId}", Ct)).EnsureSuccessStatusCode();

        using var scope = api.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IFileStorage>().OpenReadAsync(originalKey, Ct));
    }

    // M6
    [Theory]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    public void Invoice_exports_neutralise_tab_and_carriage_return_formulas(string value) =>
        Assert.StartsWith("\"'", Saas.Application.Invoices.Csv.Cell(value));

    // Business detail price (snapshot)
    [Fact]
    public async Task Business_detail_shows_the_price_the_business_paid()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var code = $"d{Guid.NewGuid().ToString("N")[..8]}";
        object Body(long price) => new
        {
            product = "property", code, name = "Detail plan", priceMinor = price, isHidden = false, sortOrder = 9,
            limits = new Dictionary<string, long> { ["listings"] = 10, ["listing_expiry_days"] = 45, ["listing_stats"] = 1, ["team_seats"] = 2 },
            features = new[] { "10 listings" },
        };
        var plan = await Json(await finance.PostAsJsonAsync("/api/platform/plans", Body(400_000), Ct));
        var agency = await kit.AgencyAsync("Detail Price Realty");
        var checkout = await Json(await agency.Client.PostAsJsonAsync("/api/billing/checkout", new { planCode = code }, Ct));
        var reference = api.Gateway.Initialized.Last(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BillingService>().ConfirmPaymentAsync(reference, 400_000, "NGN", Ct);
        (await finance.PutAsJsonAsync($"/api/platform/plans/{plan.GetProperty("id").GetGuid()}", Body(999_000), Ct)).EnsureSuccessStatusCode();

        var detail = await Json(await finance.GetAsync($"/api/platform/businesses/{agency.TenantId}", Ct));
        Assert.Equal(400_000, detail.GetProperty("subscription").GetProperty("priceMinor").GetInt64());
    }

    // Health error text
    [Fact]
    public async Task Health_masks_addresses_inside_failure_messages()
    {
        var recipient = $"grace-{Guid.NewGuid():N}@example.com";
        var id = await api.WithDbAsync(async db =>
        {
            var n = new Notification { Recipient = recipient, TemplateKey = "test.failed", Status = NotificationStatus.Failed, Attempts = 5,
                Error = $"550 5.1.1 <{recipient}>: Recipient address rejected" };
            db.Notifications.Add(n);
            await db.SaveChangesAsync(Ct);
            return n.Id;
        });
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);

        var failure = (await Json(await finance.GetAsync("/api/platform/health", Ct))).GetProperty("failures").EnumerateArray().Single(f => f.GetProperty("id").GetGuid() == id);
        Assert.DoesNotContain(recipient, failure.GetProperty("error").GetString());
        Assert.Contains("g…@example.com", failure.GetProperty("error").GetString());
    }

    // Admins' reset links go to the console
    [Fact]
    public async Task A_support_sent_reset_for_an_admin_links_to_the_console()
    {
        var (_, adminUser) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.Moderator);
        var email = await api.WithDbAsync(db => db.Users.Where(u => u.Id == adminUser).Select(u => u.Email!).SingleAsync(Ct));
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        (await support.PostAsync($"/api/platform/users/{adminUser}/send-password-reset", null, Ct)).EnsureSuccessStatusCode();

        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "password.reset" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct))).Payload["link"];
        Assert.StartsWith(api.Services.GetRequiredService<AppUrls>().Web("admin") + "/reset-password#", link);
    }
}
