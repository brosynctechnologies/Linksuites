using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Common;
using Saas.Application.Property;
using Saas.Domain.Core;
using Saas.Infrastructure.Property;

namespace Saas.IntegrationTests;

/// <summary>Least privilege for customer personal data, business names in the audit log, and photo originals.</summary>
[Collection(nameof(ApiCollection))]
public sealed class BlockerPrivacyTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";

    [Fact]
    public async Task Finance_sees_member_names_and_roles_but_not_contact_details()
    {
        var name = Unique("Private Realty");
        var agency = await kit.AgencyAsync(name);
        var phone = $"+23480{Random.Shared.Next(10_000_000, 99_999_999)}";
        await api.WithDbAsync(db => db.Users.Where(u => u.Email == agency.Email).ExecuteUpdateAsync(s => s.SetProperty(u => u.PhoneNumber, phone), Ct));
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        var member = (await Json(await finance.GetAsync($"/api/platform/businesses/{agency.TenantId}", Ct))).GetProperty("members")[0];
        Assert.Equal($"{name} Owner", member.GetProperty("name").GetString());
        Assert.Equal("Owner", member.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, member.GetProperty("email").ValueKind);
        var row = (await Json(await finance.GetAsync($"/api/platform/businesses?q={Uri.EscapeDataString(name)}", Ct))).GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("ownerEmail").ValueKind);
        Assert.Equal(0, (await Json(await finance.GetAsync($"/api/platform/businesses?q={Uri.EscapeDataString(phone)}", Ct))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Json(await finance.GetAsync($"/api/platform/businesses?q={Uri.EscapeDataString(agency.Email)}", Ct))).GetProperty("total").GetInt32());

        var supportMember = (await Json(await support.GetAsync($"/api/platform/businesses/{agency.TenantId}", Ct))).GetProperty("members")[0];
        Assert.Equal(agency.Email, supportMember.GetProperty("email").GetString());
        Assert.Equal(1, (await Json(await support.GetAsync($"/api/platform/businesses?q={Uri.EscapeDataString(phone)}", Ct))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Health_masks_failed_email_recipients_except_for_support()
    {
        var recipient = $"ada-{Guid.NewGuid():N}@example.com";
        var id = await api.WithDbAsync(async db =>
        {
            var n = new Notification { Recipient = recipient, TemplateKey = "test.failed", Status = NotificationStatus.Failed, Error = "Mailbox full", Attempts = 5 };
            db.Notifications.Add(n);
            await db.SaveChangesAsync(Ct);
            return n.Id;
        });
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        static string RecipientIn(JsonElement health, Guid id) =>
            health.GetProperty("failures").EnumerateArray().Single(f => f.GetProperty("id").GetGuid() == id).GetProperty("recipient").GetString()!;
        Assert.Equal("a…@example.com", RecipientIn(await Json(await finance.GetAsync("/api/platform/health", Ct)), id));
        Assert.Equal(recipient, RecipientIn(await Json(await support.GetAsync("/api/platform/health", Ct)), id));
    }

    [Fact]
    public async Task Audit_rows_name_the_business_and_filter_by_product()
    {
        var name = Unique("Audited Realty");
        var agency = await kit.AgencyAsync(name);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        (await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/suspend", new { reason = "Checking an audit trail entry." }, Ct)).EnsureSuccessStatusCode();
        (await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/reactivate", new { reason = "Audit trail entry checked." }, Ct)).EnsureSuccessStatusCode();

        var rows = (await Json(await support.GetAsync($"/api/platform/audit?tenantId={agency.TenantId}&product=property", Ct))).GetProperty("items");
        Assert.NotEqual(0, rows.GetArrayLength());
        Assert.All(rows.EnumerateArray(), r => Assert.Equal((name, "property"), (r.GetProperty("businessName").GetString(), r.GetProperty("product").GetString())));
        Assert.Equal(0, (await Json(await support.GetAsync($"/api/platform/audit?tenantId={agency.TenantId}&product=bookings", Ct))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Agency_staff_cannot_read_earnings_payouts_or_the_bank_account()
    {
        var agency = await kit.AgencyAsync("Staffed Realty");
        await api.MakeProAsync(agency.TenantId, "property"); // Pro has 3 seats
        var email = $"agent-{Guid.NewGuid():N}@example.com";
        (await agency.Client.PostAsJsonAsync("/api/team/invites", new { email, role = "Staff" }, Ct)).EnsureSuccessStatusCode();
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == email).FirstAsync(Ct))).Payload["link"];
        var accepted = await Json(await api.CreateClient().PostAsJsonAsync($"/api/auth/invites/{link[(link.LastIndexOf('/') + 1)..]}/accept",
            new { fullName = "Agency Staff", password = "password123" }, Ct));
        var staff = api.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accepted.GetProperty("accessToken").GetString());

        foreach (var path in new[] { "/api/property/earnings", "/api/property/payouts", "/api/property/payout-account" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(path, Ct)).StatusCode);
            Assert.True((await agency.Client.GetAsync(path, Ct)).IsSuccessStatusCode, path);
        }
    }

    [Fact]
    public async Task A_photo_that_fails_to_process_has_its_original_deleted()
    {
        var agency = await kit.AgencyAsync("Broken Photo Realty");
        var listingId = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(Enumerable.Repeat((byte)0x42, 4000)).ToArray(); // JPEG header, unreadable body
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "broken.jpg" } };
        var mediaId = (await Json(await agency.Client.PostAsync($"/api/property/listings/{listingId}/media", form, Ct))).GetProperty("id").GetGuid();

        using (var scope = api.Services.CreateScope())
        {
            var processor = ActivatorUtilities.CreateInstance<PhotoProcessor>(scope.ServiceProvider);
            while (await processor.ProcessNextAsync(Ct)) { }
        }

        var media = await api.WithDbAsync(db => db.ListingMedia.IgnoreQueryFilters().FirstAsync(m => m.Id == mediaId, Ct));
        Assert.Equal("Failed", media.Status.ToString());
        using var scope2 = api.Services.CreateScope();
        Assert.Null(await scope2.ServiceProvider.GetRequiredService<IFileStorage>().OpenReadAsync(media.OriginalKey, Ct));
    }

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public void Originals_are_stored_with_the_detected_content_type(string ext, string contentType) =>
        Assert.Equal(contentType, ListingMediaService.ContentTypeFor(ext));
}
