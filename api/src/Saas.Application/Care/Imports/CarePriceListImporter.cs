using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Application.School.Imports;
using Saas.Application.School.Imports.Importers;
using Saas.Domain.Care;

namespace Saas.Application.Care.Imports;

/// <summary>
/// The price list, one item per row (kind <c>care-price-list</c>): <c>name</c>, <c>category</c> and <c>price</c> in naira. The price-list
/// screen's access (leadership or the cashier) and rules: a name the clinic already has (ignoring case and extra spaces) updates that item's
/// category and price, where a blank cell keeps what is saved and an equal row is Unchanged; any other name is a new item and needs both.
/// Saved through <see cref="PriceListService"/>'s create and edit. Nothing is renamed, deactivated or deleted. Bills keep the prices they charged.
/// </summary>
public sealed class CarePriceListImporter(IAppDbContext db, CareAccess access, PriceListService prices)
    : IImporter, IImportNotice, IImportDeclaredColumnsOnly
{
    public const string KindName = "care-price-list";
    public const string Twice = "This price item appears more than once in the file.";
    public const string BadCategory = "Choose Consultation, Procedure, Test, Drug, Bed, Registration or Other.";

    public string Kind => KindName;
    public string Product => Billing.Products.Care;
    public string SheetName => "Price list";
    public string NoticeTitle => "Price list import finished";
    public string NoticeLink => "/app/price-list";

    private static readonly ImportColumns AllColumns = new(
    [
        new("name", "name", true, "The item, for example General consultation. Up to 120 characters. A name already on the list updates that item."),
        new("category", "category", true, "Consultation, Procedure, Test, Drug, Bed, Registration or Other. Blank keeps an existing item's category."),
        new("price", "price", true, FeeImports.AmountHelp + " Blank keeps an existing item's price."),
    ]);

    private static readonly string[] Notes =
    [
        "One price item per row on the Price list sheet. Delete nothing from the heading row.",
        "name: required. A name already on the price list (ignoring capitals and extra spaces) updates that item's category and price.",
        "category: Consultation, Procedure, Test, Drug, Bed, Registration or Other.",
        "price: in naira, for example 5000 or 5,000.50. Bills already made keep the price they charged.",
        "A blank cell keeps what is saved. Nothing is renamed, deactivated or deleted by an import.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    private sealed record Plan(Guid? Id, string Name, PriceCategory Category, long PriceMinor);

    private static readonly PriceCategory[] Categories = Enum.GetValues<PriceCategory>();

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct) => PriceListService.EnsureWriter(await access.CallerAsync(ct));

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(SheetName, AllColumns.All, [], Notes));

    public IReadOnlyList<string> DroppedColumnWarnings(IReadOnlyList<string> dropped) => CareImports.Warnings(dropped);

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var items = await db.PriceItems.AsNoTracking().Select(i => new { i.Id, i.Name, i.Category, i.PriceMinor }).ToListAsync(ct);
        var byName = items.GroupBy(i => CareImports.Key(i.Name)).ToDictionary(g => g.Key, g => g.First());
        var repeated = rows.Select(r => CareImports.Key(r.Cells.GetValueOrDefault("name"))).Where(k => k.Length > 0)
            .GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        return rows.Select(row =>
        {
            var name = string.Join(' ', (row.Cells.GetValueOrDefault("name") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            ImportRowResult Error(string message) => new(row.Row, ImportRowStatus.Error, name, message, null);

            if (name.Length == 0) return Error("Enter the item's name.");
            if (name.Length > 120) return Error("The name can have at most 120 characters.");
            var key = CareImports.Key(name);
            if (repeated.Contains(key)) return Error(Twice);

            PriceCategory? category = null;
            var categoryCell = (row.Cells.GetValueOrDefault("category") ?? "").Trim();
            if (categoryCell.Length > 0)
            {
                // Exactly one category name, ignoring case. Enum.TryParse would also take numbers ("3", "+3") and comma lists
                // ("Procedure, Test" is 1 | 2, which is Drug), so the cell is matched against the names themselves.
                category = Categories.FirstOrDefault(c => string.Equals(c.ToString(), categoryCell, StringComparison.OrdinalIgnoreCase), (PriceCategory)(-1));
                if (!Enum.IsDefined(category.Value)) return Error(BadCategory);
            }
            long? price = null;
            var priceCell = (row.Cells.GetValueOrDefault("price") ?? "").Trim();
            if (priceCell.Length > 0)
            {
                price = FeeImports.ParseNaira(priceCell, out var error);
                if (price is null) return Error(error!);
                if (price > 100_000_000_000) return Error("The price is too large.");
            }

            if (byName.TryGetValue(key, out var item))
            {
                var newCategory = category ?? item.Category;
                var newPrice = price ?? item.PriceMinor;
                if (newCategory == item.Category && newPrice == item.PriceMinor)
                    return new ImportRowResult(row.Row, ImportRowStatus.Unchanged, item.Name, null, null);
                // The stored name is kept, so an import never renames an item.
                return new ImportRowResult(row.Row, ImportRowStatus.Update, item.Name, null, new Plan(item.Id, item.Name, newCategory, newPrice));
            }
            if (category is null) return Error(BadCategory);
            if (price is null) return Error("Enter the price.");
            return new ImportRowResult(row.Row, ImportRowStatus.New, name, null, new Plan(null, name, category.Value, price.Value));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            if (row.Status == ImportRowStatus.Error || row.Plan is not Plan plan) continue;
            var request = new SavePriceItemRequest(plan.Name, plan.Category, plan.PriceMinor);
            if (plan.Id is { } id)
            {
                await prices.UpdateAsync(id, request, ct);
                updated++;
            }
            else
            {
                await prices.CreateAsync(request, ct);
                created++;
            }
        }
        return new ImportApplied(created, updated);
    }
}
