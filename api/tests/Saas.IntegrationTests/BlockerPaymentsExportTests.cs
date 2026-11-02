using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>The payments list and CSV cover every payment in the range, and the CSV can't carry spreadsheet formulas.</summary>
[Collection(nameof(ApiCollection))]
public sealed class BlockerPaymentsExportTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    /// <summary>Inserts subscription payments on one day long ago, so no other test's rows share the range.</summary>
    private Task SeedPaymentsAsync(Guid tenantId, int count, string day, string reference) => api.WithDbAsync(db => db.Database.ExecuteSqlRawAsync($"""
        INSERT INTO payments (id, metadata, tenant_id, amount_minor, currency, status, provider, provider_ref, purpose, created_at, paid_at)
        SELECT gen_random_uuid(), jsonb_build_object(), '{tenantId}', 100000, 'NGN', 'Succeeded', 'paystack',
               CASE WHEN g = 1 THEN '{reference}' ELSE 'bulk-' || g END, 'subscription',
               TIMESTAMPTZ '{day} 08:00:00+00' + g * INTERVAL '1 second', TIMESTAMPTZ '{day} 08:00:00+00' + g * INTERVAL '1 second'
        FROM generate_series(1, {count}) g
        """, Ct));

    [Fact]
    public async Task Payments_list_and_csv_include_every_row_in_range()
    {
        var agency = await kit.AgencyAsync("Volume Realty");
        await SeedPaymentsAsync(agency.TenantId, 2100, "2001-03-04", "bulk-first");
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        const string range = "from=2001-03-04T00:00:00Z&to=2001-03-05T00:00:00Z";

        var last = await Json(await finance.GetAsync($"/api/platform/payments?{range}&page=21&pageSize=100", Ct));
        Assert.Equal(2100, last.GetProperty("total").GetInt32());
        Assert.Equal(100, last.GetProperty("items").GetArrayLength());
        Assert.Equal("bulk-first", last.GetProperty("items")[99].GetProperty("reference").GetString()); // oldest, last

        var csv = await (await finance.GetAsync($"/api/platform/payments.csv?{range}", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Equal(2101, csv.TrimEnd('\n').Split('\n').Length); // header + every row
    }

    [Fact]
    public async Task Csv_neutralises_values_a_spreadsheet_would_run_as_formulas()
    {
        var agency = await kit.AgencyAsync("Formula Realty");
        // Names reach the export from many places (renames, other products); this one starts like a formula.
        await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == agency.TenantId).ExecuteUpdateAsync(t => t.SetProperty(x => x.Name, "=Formula Realty"), Ct));
        await SeedPaymentsAsync(agency.TenantId, 1, "2001-05-06", "+SUM(A1)");
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);

        var csv = await (await finance.GetAsync("/api/platform/payments.csv?from=2001-05-06T00:00:00Z&to=2001-05-07T00:00:00Z", Ct)).Content.ReadAsStringAsync(Ct);
        var row = csv.TrimEnd('\n').Split('\n')[1];
        Assert.Contains("\"'=Formula Realty\"", row);
        Assert.Contains("\"'+SUM(A1)\"", row);
        Assert.DoesNotContain(",=", row);
        Assert.DoesNotContain(",+", row);
    }
}
