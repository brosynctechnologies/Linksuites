using Microsoft.EntityFrameworkCore;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PlatformTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Postgis_is_available()
    {
        var version = await api.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS postgis", Ct);
            return await db.Database.SqlQueryRaw<string>("SELECT postgis_version() AS \"Value\"").SingleAsync(Ct);
        });
        Assert.StartsWith("3.", version);
    }
}
