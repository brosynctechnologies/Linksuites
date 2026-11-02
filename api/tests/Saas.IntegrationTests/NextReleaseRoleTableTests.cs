using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Every admin route × every admin role. The table is the intended design (docs/PLATFORM_API.md); a route missing from it fails
/// the test, so a new admin route can't ship without someone deciding who may use it. SuperAdmin may use everything.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed partial class NextReleaseRoleTableTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Any = "any";
    private static readonly PlatformRole Mod = PlatformRole.Moderator, Fin = PlatformRole.Finance, Sup = PlatformRole.Support;

    /// <summary>"METHOD pattern" → roles allowed besides SuperAdmin (null = any admin).</summary>
    private static readonly Dictionary<string, PlatformRole[]?> Table = new()
    {
        ["GET /api/platform/me"] = null,
        ["GET /api/platform/health"] = null,
        ["GET /api/platform/overview"] = null,
        ["GET /api/admin/overview"] = null,

        ["GET /api/platform/businesses/"] = [Sup, Fin],
        ["GET /api/platform/businesses/{id:guid}"] = [Sup, Fin],
        ["POST /api/platform/businesses/{id:guid}/suspend"] = [Sup],
        ["POST /api/platform/businesses/{id:guid}/reactivate"] = [Sup],
        ["POST /api/platform/businesses/{id:guid}/view-as"] = [Sup],
        ["POST /api/platform/view-as/{sessionId:guid}/end"] = [Sup],
        ["GET /api/platform/audit"] = [Sup],
        ["GET /api/platform/users/"] = [Sup],
        ["GET /api/platform/users/{id:guid}"] = [Sup],
        ["POST /api/platform/users/{id:guid}/unlock"] = [Sup],
        ["POST /api/platform/users/{id:guid}/send-login-code"] = [Sup],
        ["POST /api/platform/users/{id:guid}/send-password-reset"] = [Sup],
        ["POST /api/platform/users/{id:guid}/sign-out-everywhere"] = [Sup],

        ["POST /api/platform/businesses/{id:guid}/comp"] = [Fin],
        ["GET /api/platform/payments"] = [Fin],
        ["GET /api/platform/payments.csv"] = [Fin],
        ["GET /api/platform/plans/"] = [Fin],
        ["POST /api/platform/plans/"] = [Fin],
        ["PUT /api/platform/plans/{id:guid}"] = [Fin],
        ["GET /api/admin/payouts"] = [Fin],
        ["POST /api/admin/payouts/{id:guid}/paid"] = [Fin],
        ["POST /api/admin/payouts/{id:guid}/reject"] = [Fin],

        ["GET /api/admin/categories"] = [Mod],
        ["POST /api/admin/categories"] = [Mod],
        ["PUT /api/admin/categories/{id:guid}"] = [Mod],
        ["GET /api/admin/course-reviews"] = [Mod],
        ["POST /api/admin/course-reviews/{id:guid}"] = [Mod],
        ["GET /api/admin/property/moderation"] = [Mod],
        ["POST /api/admin/property/listings/{id:guid}/decision"] = [Mod],
        ["GET /api/admin/property/locations"] = [Mod],
        ["POST /api/admin/property/locations"] = [Mod],
        ["PUT /api/admin/property/locations/{id:guid}"] = [Mod],
        ["GET /api/admin/property/reports"] = [Mod],
        ["POST /api/admin/property/reports/{id:guid}/decision"] = [Mod],
        ["GET /api/admin/property/reviews"] = [Mod],
        ["POST /api/admin/property/reviews/{id:guid}/decision"] = [Mod],
        ["GET /api/admin/property/verifications"] = [Mod],
        ["POST /api/admin/property/verifications/{id:guid}/decision"] = [Mod],
        ["GET /api/admin/property/verifications/{id:guid}/documents/{index:int}"] = [Mod],

        ["GET /api/platform/admins/"] = [],
        ["POST /api/platform/admins/"] = [],
        ["PUT /api/platform/admins/{id:guid}"] = [],
        ["DELETE /api/platform/admins/{id:guid}"] = [],
    };

    /// <summary>Sign-in routes, not role-gated.</summary>
    private static readonly HashSet<string> Exempt = ["POST /api/platform/auth/login", "POST /api/platform/auth/verify", "POST /api/platform/auth/logout"];

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Parameter();

    private List<(string Method, string Pattern)> AdminRoutes()
    {
        _ = api.CreateClient();
        return api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } p && (p.StartsWith("/api/platform") || p.StartsWith("/api/admin")))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (m, e.RoutePattern.RawText!)))
            .Where(r => !Exempt.Contains($"{r.m} {r.Item2}")).Distinct().ToList();
    }

    [Fact]
    public void Every_admin_route_is_in_the_table()
    {
        var missing = AdminRoutes().Select(r => $"{r.Method} {r.Pattern}").Where(k => !Table.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Decide who may use: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task Each_role_reaches_exactly_its_routes()
    {
        var clients = new Dictionary<PlatformRole, HttpClient>();
        foreach (var role in Enum.GetValues<PlatformRole>()) clients[role] = (await PlatformAuthTests.PanelAdminAsync(api, role)).Client;
        var (customer, _) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.Support); // a customer-app token, not a console session
        var failures = new List<string>();

        foreach (var (method, pattern) in AdminRoutes())
        {
            var allowed = Table[$"{method} {pattern}"];
            var path = Parameter().Replace(pattern, m => m.Value.Contains(":int") ? "0" : Guid.NewGuid().ToString());
            foreach (var (role, client) in clients.Append(new(default, customer)).Select((c, i) => (i < clients.Count ? c.Key : (PlatformRole?)null, c.Value)))
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (method is "POST" or "PUT") request.Content = JsonContent.Create(new { });
                var status = (await client.SendAsync(request, Ct)).StatusCode;
                var should = role is { } r && (r == PlatformRole.SuperAdmin || allowed is null || allowed.Contains(r));
                if (should && (status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized || (int)status >= 500))
                    failures.Add($"{role} {method} {pattern} → {(int)status}, expected access");
                if (!should && status is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized))
                    failures.Add($"{role?.ToString() ?? "customer token"} {method} {pattern} → {(int)status}, expected 403");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
