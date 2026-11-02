using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Domain.Property;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PropertySeedTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lagos_areas_are_seeded_under_Lagos()
    {
        var chain = await api.WithDbAsync(async db =>
        {
            var lekki = await db.Locations.SingleAsync(l => l.Slug == "lekki-lagos", Ct);
            var city = await db.Locations.SingleAsync(l => l.Id == lekki.ParentId, Ct);
            var state = await db.Locations.SingleAsync(l => l.Id == city.ParentId, Ct);
            return (lekki.Kind, city.Name, city.Kind, state.Name, state.Kind, StateCount: await db.Locations.CountAsync(l => l.Kind == LocationKind.State, Ct));
        });
        Assert.Equal((LocationKind.Area, "Lagos", LocationKind.City, "Lagos State", LocationKind.State, 37), chain);
    }

    [Fact]
    public async Task Property_plans_are_seeded()
    {
        var plans = await api.WithDbAsync(db => db.Plans.Where(p => p.Product == Products.Property && p.SortOrder < 9).OrderBy(p => p.SortOrder) /* plans other tests create use sort order 9 */.ToListAsync(Ct));
        Assert.Equal(["free", "pro", "agency"], plans.Select(p => p.Code));
        var pro = plans[1];
        Assert.Equal(1_000_000, pro.PriceMinor);
        Assert.Equal(30, pro.Limits[LimitKeys.Listings]);
        Assert.Equal(60, pro.Limits[LimitKeys.ListingExpiryDays]);
        Assert.Equal(-1, plans[2].Limits[LimitKeys.Listings]);
        Assert.Equal(30, plans[0].Limits[LimitKeys.ListingExpiryDays]);
    }
}
