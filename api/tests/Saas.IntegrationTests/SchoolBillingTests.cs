using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink's plan and billing page: Paystack returns the proprietor there, and only the proprietor can pay.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolBillingTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Ok(HttpResponseMessage r, string what)
    {
        Assert.True(r.IsSuccessStatusCode, $"{what}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    [Fact]
    public async Task Plan_checkout_returns_to_the_schools_billing_page()
    {
        var school = await kit.SchoolAsync("Billing Return School");
        var checkout = await Ok(await school.Owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = "standard" }, Ct), "checkout");
        var init = api.Gateway.Initialized.Last(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));

        Assert.Matches(@"/app/billing\?ref=sub_[a-z0-9]+$", init.CallbackUrl);
        Assert.EndsWith($"/app/billing?ref={init.Reference}", init.CallbackUrl);
    }

    [Fact]
    public async Task The_principal_reads_the_plan_but_only_the_proprietor_pays()
    {
        var school = await kit.SchoolAsync("Billing Roles School");
        var (principal, _) = await kit.StaffAsync(school, MemberRole.Admin, "Principal"); // moves the school to Standard for the seat

        var billing = await Ok(await principal.GetAsync("/api/billing", Ct), "principal billing");
        Assert.Equal("standard", billing.GetProperty("currentPlan").GetProperty("code").GetString());
        Assert.Equal(["free", "standard", "premium"], billing.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await principal.PostAsJsonAsync("/api/billing/checkout", new { planCode = "standard" }, Ct)).StatusCode);
    }
}
