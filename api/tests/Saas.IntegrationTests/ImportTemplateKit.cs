using System.Net;
using Saas.Infrastructure.Schools;

namespace Saas.IntegrationTests;

/// <summary>
/// Reads a downloaded roster template the way an upload is read, for tests that build a roster file by hand: each row's hidden
/// <c>row_version</c> (the stored values when the template was made), keyed by a column such as <c>admission_no</c>.
/// </summary>
internal static class ImportTemplateKit
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The CSV template at <paramref name="url"/> (for example <c>/api/school/imports/scores/template.csv?…</c>), read as the import reads it.</summary>
    public static async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> RowsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new SpreadsheetReader().Read(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)), "template.csv");
    }

    /// <summary>Each row's row_version by the value of <paramref name="key"/> (any case), for example by admission number or fee item.</summary>
    public static async Task<Dictionary<string, string>> VersionsAsync(HttpClient client, string url, Func<IReadOnlyDictionary<string, string>, string> key) =>
        (await RowsAsync(client, url)).ToDictionary(key, r => r.GetValueOrDefault("row_version") ?? "", StringComparer.OrdinalIgnoreCase);

    public static Task<Dictionary<string, string>> VersionsAsync(HttpClient client, string url, string keyColumn = "admission_no") =>
        VersionsAsync(client, url, r => r.GetValueOrDefault(keyColumn) ?? "");
}
