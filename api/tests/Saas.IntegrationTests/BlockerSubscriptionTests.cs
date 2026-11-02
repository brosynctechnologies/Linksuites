using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Plan edits apply at renewal; hidden plans can be renewed by their subscribers; MRR reflects what was paid.</summary>
[Collection(nameof(ApiCollection))]
public sealed class BlockerSubscriptionTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static object PlanBody(string code, long price, long listings, bool hidden = false) => new
    {
        product = "property", code, name = $"Snap {code}", priceMinor = price, isHidden = hidden, sortOrder = 9,
        limits = new Dictionary<string, long> { ["listings"] = listings, ["listing_expiry_days"] = 45, ["listing_stats"] = 1, ["team_seats"] = 2 },
        features = new[] { $"{listings} listings" },
    };

    /// <summary>Checkout and confirm a payment for a plan, as the Paystack webhook would.</summary>
    private async Task PayAsync(HttpClient business, string code, long amount)
    {
        var checkout = await Json(await business.PostAsJsonAsync("/api/billing/checkout", new { planCode = code }, Ct));
        var reference = api.Gateway.Initialized.Last(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BillingService>().ConfirmPaymentAsync(reference, amount, "NGN", Ct);
    }

    private async Task<long> ListingsLimitAsync(HttpClient business) =>
        (await Json(await business.GetAsync("/api/billing", Ct))).GetProperty("currentPlan").GetProperty("limits").GetProperty("listings").GetInt64();

    [Fact]
    public async Task Plan_limit_edits_wait_for_the_next_renewal()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var code = $"s{Guid.NewGuid().ToString("N")[..8]}";
        var plan = await Json(await finance.PostAsJsonAsync("/api/platform/plans", PlanBody(code, 700_000, listings: 20), Ct));
        var agency = await kit.AgencyAsync("Snapshot Realty");
        await PayAsync(agency.Client, code, 700_000);
        Assert.Equal(20, await ListingsLimitAsync(agency.Client));

        (await finance.PutAsJsonAsync($"/api/platform/plans/{plan.GetProperty("id").GetGuid()}", PlanBody(code, 800_000, listings: 5), Ct)).EnsureSuccessStatusCode();
        Assert.Equal(20, await ListingsLimitAsync(agency.Client)); // what they paid for, until renewal

        await PayAsync(agency.Client, code, 800_000); // renewal at the new price and limits
        Assert.Equal(5, await ListingsLimitAsync(agency.Client));
    }

    [Fact]
    public async Task Hidden_plan_subscribers_can_renew_but_new_customers_cannot_buy_it()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var code = $"g{Guid.NewGuid().ToString("N")[..8]}";
        var plan = await Json(await finance.PostAsJsonAsync("/api/platform/plans", PlanBody(code, 600_000, listings: 15), Ct));
        var loyal = await kit.AgencyAsync("Grandfathered Realty");
        await PayAsync(loyal.Client, code, 600_000);
        (await finance.PutAsJsonAsync($"/api/platform/plans/{plan.GetProperty("id").GetGuid()}", PlanBody(code, 600_000, listings: 15, hidden: true), Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await loyal.Client.PostAsJsonAsync("/api/billing/checkout", new { planCode = code }, Ct)).StatusCode);
        var newcomer = await kit.AgencyAsync("Newcomer Realty");
        Assert.Equal(HttpStatusCode.NotFound, (await newcomer.Client.PostAsJsonAsync("/api/billing/checkout", new { planCode = code }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Mrr_reflects_prices_paid_not_list_price_edits()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var code = $"m{Guid.NewGuid().ToString("N")[..8]}";
        var plan = await Json(await finance.PostAsJsonAsync("/api/platform/plans", PlanBody(code, 500_000, listings: 10), Ct));
        var agency = await kit.AgencyAsync("Mrr Realty");
        await PayAsync(agency.Client, code, 500_000);
        var before = await Json(await finance.GetAsync("/api/platform/overview", Ct));

        (await finance.PutAsJsonAsync($"/api/platform/plans/{plan.GetProperty("id").GetGuid()}", PlanBody(code, 2_000_000, listings: 10), Ct)).EnsureSuccessStatusCode();

        var after = await Json(await finance.GetAsync("/api/platform/overview", Ct));
        Assert.Equal(before.GetProperty("mrrMinor").GetInt64(), after.GetProperty("mrrMinor").GetInt64());
        var ngn = after.GetProperty("mrrByCurrency").EnumerateArray().Single(m => m.GetProperty("currency").GetString() == "NGN");
        Assert.Equal(after.GetProperty("mrrMinor").GetInt64(), ngn.GetProperty("monthlyMinor").GetInt64());
    }
}
