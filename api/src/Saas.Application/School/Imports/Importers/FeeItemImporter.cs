using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Fee items, one per row: <c>name</c> and <c>optional</c> (yes or no). A name the school already has (ignoring case and extra spaces)
/// updates that item's optional flag; a blank optional cell keeps it, and an equal one is unchanged. Any other name is a new item (a blank
/// optional cell means required). Saved through the fee items screen's own create and edit (<see cref="FeeService.CreateItemAsync"/>,
/// <see cref="FeeService.UpdateItemAsync"/>), so its name rules apply. Nothing is renamed or deleted. The fee policy: proprietor, principal
/// and bursar.
/// </summary>
public sealed class FeeItemImporter(IAppDbContext db, ICurrentUser user, FeeService fees) : IImporter, IImportNotice
{
    public const string KindName = "fee-items";
    public const string Sheet = "Fee items";
    public const string Twice = "This fee item appears more than once in the file.";
    public const string YesOrNo = "Enter yes or no for optional.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Fee item import finished";
    public string NoticeLink => "/app/fees/items";

    private static readonly ImportColumns AllColumns = new(
    [
        new("name", "name", true, "The fee item, for example Tuition or School bus. Up to 80 characters."),
        new("optional", "optional", false, "yes for an item only some students take, such as the bus; no for one everyone pays. Blank keeps the item as it is (no for a new item)."),
    ]);

    private static readonly string[] Notes =
    [
        "One fee item per row on the Fee items sheet. Delete nothing from the heading row.",
        "name: required, up to 80 characters. Name tuition with the word \"tuition\" so percentage discounts work.",
        "optional: yes or no. An optional item is added to a student's invoice only when you choose it.",
        "A name the school already has updates that item's optional setting. A blank optional cell leaves it as it is.",
        "Nothing is renamed or deleted by an import. Rename or delete items on the fee items page.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        FeeImports.EnsureFeeRole(user);
        return Task.CompletedTask;
    }

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(Sheet, AllColumns.All, [], Notes));

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var existing = await db.FeeItems.AsNoTracking().Select(i => new { i.Id, i.Name, i.IsOptional }).ToListAsync(ct);
        var byName = existing.GroupBy(i => FeeImports.NameKey(i.Name)).ToDictionary(g => g.Key, g => g.First());
        var repeated = rows.Select(r => FeeImports.NameKey(r.Cells.GetValueOrDefault("name"))).Where(k => k.Length > 0)
            .GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        return rows.Select(row =>
        {
            var name = string.Join(' ', (row.Cells.GetValueOrDefault("name") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            ImportRowResult Error(string message) => new(row.Row, ImportRowStatus.Error, name, message, null);

            if (name.Length == 0) return Error("Enter a name for the fee item.");
            if (name.Length > 80) return Error("Use 80 characters or fewer for the name.");
            var optional = FeeImports.ParseYesNo(row.Cells.GetValueOrDefault("optional"), out var ok);
            if (!ok) return Error(YesOrNo);
            var key = FeeImports.NameKey(name);
            if (repeated.Contains(key)) return Error(Twice);

            if (byName.TryGetValue(key, out var item))
            {
                if (optional is null || optional == item.IsOptional)
                    return new ImportRowResult(row.Row, ImportRowStatus.Unchanged, item.Name, null, null);
                return new ImportRowResult(row.Row, ImportRowStatus.Update, item.Name,
                    optional.Value ? "Becomes optional." : "Becomes required.", new FeeItemPlan(item.Id, item.Name, optional.Value));
            }
            return new ImportRowResult(row.Row, ImportRowStatus.New, name, null, new FeeItemPlan(null, name, optional ?? false));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            if (row.Plan is not FeeItemPlan plan) continue;
            var request = new SaveFeeItemRequest(plan.Name, plan.IsOptional);
            if (plan.Id is { } id)
            {
                await fees.UpdateItemAsync(id, request, ct);
                updated++;
            }
            else
            {
                await fees.CreateItemAsync(request, ct);
                created++;
            }
        }
        return new ImportApplied(created, updated);
    }

    /// <summary><see cref="Id"/> is null for a new item. An update keeps the stored name, so nothing is renamed.</summary>
    private sealed record FeeItemPlan(Guid? Id, string Name, bool IsOptional);
}
