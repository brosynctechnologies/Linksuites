using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Property;

namespace Saas.IntegrationTests;

/// <summary>Shared setup for property tests: agencies, seekers and listing bodies.</summary>
public sealed class PropertyKit(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record Agency(HttpClient Client, Guid TenantId, string Email);

    public async Task<Agency> AgencyAsync(string name, bool verified = false)
    {
        var client = api.CreateClient();
        var email = $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = $"{name} Owner", email, password = "password123", country = "NG", product = "property", businessName = name,
        }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        if (verified) await VerifyAsync(tenantId);
        return new Agency(client, tenantId, email);
    }

    public Task VerifyAsync(Guid tenantId) => api.WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        await db.AgencyProfiles.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).ExecuteUpdateAsync(s => s.SetProperty(a => a.VerifiedAt, now), Ct);
        return await db.AgentProfiles.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).ExecuteUpdateAsync(s => s.SetProperty(a => a.VerifiedAt, now), Ct);
    });

    public async Task<HttpClient> SeekerAsync(string name, string? email = null)
    {
        var client = api.CreateClient();
        email ??= $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/seeker/signup", new { fullName = $"{name} Seeker", email, password = "password123" }, Ct);
        if (!response.IsSuccessStatusCode)
            response = await client.PostAsJsonAsync("/api/auth/seeker/login", new { email, password = "password123" }, Ct);
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(response)).GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>The config admin, signed in to the admin panel (password + emailed code).</summary>
    public async Task<HttpClient> AdminAsync()
    {
        await SeekerAsync("Admin", ApiFixture.AdminEmail); // makes sure the account exists
        return (await PlatformAuthTests.PanelLoginAsync(api, ApiFixture.AdminEmail, "password123")).Client;
    }

    public Task<Guid> LocationAsync(string slug) => api.WithDbAsync(db => db.Locations.Where(l => l.Slug == slug).Select(l => l.Id).SingleAsync(Ct));
    public Task<Guid> TypeAsync(string slug) => api.WithDbAsync(db => db.PropertyTypes.Where(t => t.Slug == slug).Select(t => t.Id).SingleAsync(Ct));
    public Task<Guid> AmenityAsync(string slug) => api.WithDbAsync(db => db.Amenities.Where(t => t.Slug == slug).Select(t => t.Id).SingleAsync(Ct));

    /// <summary>A valid rent listing body; override fields with the <paramref name="change"/> callback.</summary>
    public async Task<Dictionary<string, object?>> ListingBodyAsync(string title = "3 bedroom flat in Lekki", string area = "lekki-lagos",
        double lat = 6.4474, double lng = 3.4720, long price = 3_500_000_00, int beds = 3, ListingPurpose purpose = ListingPurpose.Rent,
        object[]? costs = null, Guid[]? amenityIds = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = title, ["description"] = "Bright, newly built flat close to the expressway.", ["purpose"] = purpose.ToString(),
            ["propertyTypeId"] = await TypeAsync("apartment"), ["locationId"] = await LocationAsync(area), ["address"] = "12 Admiralty Way",
            ["lat"] = lat, ["lng"] = lng, ["priceMinor"] = price, ["pricePeriod"] = purpose == ListingPurpose.ShortLet ? "Night" : purpose == ListingPurpose.Sale ? "Total" : "Year",
            ["bedrooms"] = beds, ["bathrooms"] = beds, ["toilets"] = beds + 1, ["sizeSqm"] = 140, ["furnished"] = false,
            ["costs"] = costs ?? new object[] { new { kind = "AgencyFee", label = "Agency fee", amountMinor = 350_000_00 }, new { kind = "Caution", label = "Caution deposit", amountMinor = 300_000_00 } },
            ["amenityIds"] = amenityIds ?? [],
        };
        if (purpose == ListingPurpose.ShortLet)
            body["shortLet"] = new { cleaningFeeMinor = 1_000_000, minNights = 1, maxGuests = 4, checkInTime = "14:00", checkOutTime = "12:00", houseRules = "No parties" };
        return body;
    }

    public async Task<JsonElement> CreateListingAsync(HttpClient agency, Dictionary<string, object?> body)
    {
        var response = await agency.PostAsJsonAsync("/api/property/listings", body, Ct);
        response.EnsureSuccessStatusCode();
        return await Json(response);
    }

    public Task<HttpResponseMessage> SubmitAsync(HttpClient agency, Guid listingId) => agency.PostAsync($"/api/property/listings/{listingId}/submit", null, Ct);

    /// <summary>Creates and submits a listing, returning its id. The agency must be verified for it to go live.</summary>
    public async Task<Guid> LiveListingAsync(HttpClient agency, Dictionary<string, object?> body)
    {
        var id = (await CreateListingAsync(agency, body)).GetProperty("id").GetGuid();
        (await SubmitAsync(agency, id)).EnsureSuccessStatusCode();
        return id;
    }

    public static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
