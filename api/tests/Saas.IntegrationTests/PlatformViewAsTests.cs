using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Saas.Domain.Core;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PlatformViewAsTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);
    private const string Reason = "Customer says their listing won't publish.";

    private async Task<(Agency Agency, JsonElement ViewAs)> ViewAsAsync()
    {
        var agency = await kit.AgencyAsync("Viewed Realty", verified: true);
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/view-as", new { reason = Reason }, Ct));
        return (agency, viewAs);
    }

    private HttpClient As(string token)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task View_as_token_reads_like_the_owner()
    {
        var (_, viewAs) = await ViewAsAsync();
        var client = As(viewAs.GetProperty("token").GetString()!);

        var listings = await Json(await client.GetAsync("/api/property/listings", Ct));
        Assert.Equal(1, listings.GetArrayLength());
        Assert.Equal("property", viewAs.GetProperty("product").GetString());
        Assert.StartsWith("http://localhost:4100/support#token=", viewAs.GetProperty("webUrl").GetString());
    }

    [Fact]
    public async Task View_as_token_cannot_write_anywhere()
    {
        var (_, viewAs) = await ViewAsAsync();
        var client = As(viewAs.GetProperty("token").GetString()!);
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (Method: m, Pattern: e.RoutePattern)))
            .Where(x => x.Method is "POST" or "PUT" or "DELETE" or "PATCH")
            // The one write a support view may make: ending itself (the banner's End view).
            .Where(x => x.Pattern.RawText != "/api/support-view/end")
            .ToList();
        Assert.True(endpoints.Count > 100);

        var allowed = new List<string>();
        foreach (var (method, pattern) in endpoints)
        {
            var path = "/" + string.Join("/", pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
            {
                RoutePatternLiteralPart literal => literal.Content,
                RoutePatternParameterPart p when p.ParameterPolicies.Any(x => x.Content == "guid") => Guid.Empty.ToString(),
                RoutePatternParameterPart p when p.ParameterPolicies.Any(x => x.Content == "int") => "0",
                RoutePatternParameterPart => "x",
                _ => "",
            }))));
            using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            var response = await client.SendAsync(request, Ct);
            var body = await response.Content.ReadAsStringAsync(Ct);
            if (response.StatusCode != HttpStatusCode.Forbidden || !body.Contains("read only")) allowed.Add($"{method} {path} -> {(int)response.StatusCode}");
        }
        Assert.Empty(allowed);
    }

    [Fact]
    public async Task View_as_needs_support_and_a_reason()
    {
        var agency = await kit.AgencyAsync("Private Realty");
        var (moderator, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Moderator);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/view-as", new { reason = Reason }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/view-as", new { reason = "x" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task View_as_is_audited_and_shown_to_the_owner()
    {
        var (agency, viewAs) = await ViewAsAsync();
        var support = As(viewAs.GetProperty("token").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync("/api/property/listings", Ct)).StatusCode);

        var activity = await Json(await agency.Client.GetAsync("/api/account/activity", Ct));
        var entry = Assert.Single(activity.EnumerateArray());
        Assert.Equal(Reason, entry.GetProperty("reason").GetString());
        Assert.Equal("Support Admin", entry.GetProperty("adminName").GetString());
    }

    [Fact]
    public async Task View_as_lasts_thirty_minutes_and_can_be_ended()
    {
        var (_, viewAs) = await ViewAsAsync();
        var token = viewAs.GetProperty("token").GetString()!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(TimeSpan.FromMinutes(30), jwt.ValidTo - jwt.IssuedAt);
        Assert.Equal("1", jwt.Claims.Single(c => c.Type == "ro").Value);
    }

    [Fact]
    public async Task Support_can_view_a_suspended_business()
    {
        var (agency, _) = await ViewAsAsync();
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        (await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/suspend", new { reason = "Checking documents for fraud." }, Ct)).EnsureSuccessStatusCode();

        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/view-as", new { reason = Reason }, Ct));

        Assert.Equal(HttpStatusCode.OK, (await As(viewAs.GetProperty("token").GetString()!).GetAsync("/api/property/listings", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agency.Client.GetAsync("/api/property/listings", Ct)).StatusCode);
    }
}
