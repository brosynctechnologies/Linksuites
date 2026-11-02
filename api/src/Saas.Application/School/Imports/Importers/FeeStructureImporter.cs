using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// What one level pays in one term, from a roster of fee items: one row per fee item (<c>fee_item</c>), its <c>amount</c> in naira, and the
/// <c>sheet_for</c> cell naming the session, level and term. The template lists every fee item with the stored amounts filled in. A blank
/// amount keeps what is stored (or leaves the item out of the level); an equal amount is unchanged and not sent. Saved with the fee structure
/// screen's own save (<see cref="FeeService.SaveStructureAsync"/>) for that term and level, with every stored line kept, so an import never
/// takes an item off a level. The fee policy: proprietor, principal and bursar.
/// </summary>
public sealed class FeeStructureImporter(IAppDbContext db, ICurrentUser user, FeeService fees) : IImporter, IImportNotice
{
    public const string KindName = "fee-structure";
    public const string Sheet = "Fee structure";
    public const string ItemColumnKey = "fee_item";
    public const string DownloadFor = "this level and term";
    public const string ItemMissing = "This fee item was not found. Add it on the fee items page first.";
    public const string Twice = "This fee item appears more than once in the file.";
    public const string NoItem = "Enter the fee item.";
    public const string NotZero = "Each amount must be more than zero.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Fee structure import finished";
    public string NoticeLink => "/app/fees/structure";

    private static readonly ImportColumn ItemColumn = new(ItemColumnKey, ItemColumnKey, true, "The fee item, as on the fee items page. Keep it as the template filled it.");
    private static readonly ImportColumn AmountColumn = new("amount", "amount", true, FeeImports.AmountHelp + " Leave blank to keep what is saved.");
    private static readonly ImportColumns AllColumns = new([ItemColumn, AmountColumn, SheetFor.Column, RowVersion.Column]);

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        FeeImports.EnsureFeeRole(user);
        scope.RequireGuid("termId");
        scope.RequireGuid("levelId");
        return Task.CompletedTask;
    }

    public async Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var rows = sheet.Items.Select(i => (IReadOnlyList<string>)
            [i.Name, sheet.Stored.TryGetValue(i.Id, out var amount) ? FeeImports.FormatNaira(amount) : "", sheet.SheetFor, Version(sheet, i.Id)]).ToList();
        var notes = new List<string>
        {
            $"Session: {sheet.SessionName}. Level: {sheet.LevelName}. Term: {sheet.TermName}. Upload this file only on that level and term.",
            "One row per fee item. Keep fee_item and sheet_for as they are.",
            $"sheet_for says which session, level and term the file is for ({sheet.SheetFor}). A file uploaded on another level or term is refused.",
            "amount: what this level pays for the item this term, in naira with up to two decimal places, for example 45000 or 12,500.50.",
            "A blank amount keeps what is saved. An item with no amount is not charged to this level. An amount can't be zero.",
            "An import never takes an item off a level. Do that on the fee structure page.",
            "The hidden row_version column records each item's saved amount when you downloaded this file. If an amount is changed on the fee structure page after that, its row is refused: download the template again.",
        };
        return new ImportTemplate(Sheet, AllColumns.All, rows, notes);
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var byName = sheet.Items.GroupBy(i => FeeImports.NameKey(i.Name)).ToDictionary(g => g.Key, g => g.First());
        var repeated = rows.Select(r => FeeImports.NameKey(r.Cells.GetValueOrDefault(ItemColumnKey))).Where(k => k.Length > 0)
            .GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        return rows.Select(row =>
        {
            var text = (row.Cells.GetValueOrDefault(ItemColumnKey) ?? "").Trim();
            ImportRowResult Error(string label, string message) => new(row.Row, ImportRowStatus.Error, label, message, null);

            if (SheetFor.Check(row, sheet.SheetFor, DownloadFor) is { } wrongSheet) return Error(text, wrongSheet);
            if (text.Length == 0) return Error(text, NoItem);
            var key = FeeImports.NameKey(text);
            if (!byName.TryGetValue(key, out var item)) return Error(text, ItemMissing);
            if (repeated.Contains(key)) return Error(item.Name, Twice);

            var cell = (row.Cells.GetValueOrDefault("amount") ?? "").Trim();
            if (cell.Length == 0) return new ImportRowResult(row.Row, ImportRowStatus.Unchanged, item.Name, null, null); // blank keeps the stored amount
            var amount = FeeImports.ParseNaira(cell, out var error);
            if (amount is null) return Error(item.Name, error!);
            if (amount <= 0) return Error(item.Name, NotZero);

            var stored = sheet.Stored.TryGetValue(item.Id, out var s) ? s : (long?)null;
            if (stored == amount) return new ImportRowResult(row.Row, ImportRowStatus.Unchanged, item.Name, null, null);
            return RowVersion.Guard(new ImportRowResult(row.Row, stored is null ? ImportRowStatus.New : ImportRowStatus.Update, item.Name, null,
                new FeeLineInput(item.Id, amount.Value)), row, Version(sheet, item.Id));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var changes = rows.Select(r => r.Plan).OfType<FeeLineInput>().ToList();
        if (changes.Count == 0) return new ImportApplied(0, 0);
        var termId = scope.RequireGuid("termId");
        var levelId = scope.RequireGuid("levelId");

        // The screen's save replaces the level's lines, so every stored line goes with it: an import changes and adds, never removes.
        var lines = await db.FeeStructureLines.AsNoTracking().Where(l => l.TermId == termId && l.LevelId == levelId)
            .Select(l => new FeeLineInput(l.FeeItemId, l.AmountMinor)).ToListAsync(ct);
        var byItem = lines.ToDictionary(l => l.FeeItemId);
        foreach (var change in changes) byItem[change.FeeItemId] = change;
        await fees.SaveStructureAsync(termId, levelId, new SaveFeeStructureRequest(byItem.Values.ToList()), ct);
        return new ImportApplied(rows.Count(r => r.Status == ImportRowStatus.New), rows.Count(r => r.Status == ImportRowStatus.Update));
    }

    // ---------- Sheet ----------

    private sealed record ItemRef(Guid Id, string Name);
    private sealed record StructureSheet(string SessionName, string TermName, string LevelName, IReadOnlyList<ItemRef> Items,
        IReadOnlyDictionary<Guid, long> Stored, string SheetFor);

    /// <summary>The row_version of an item's line: its stored amount in kobo, or blank when the level has no line for it.</summary>
    private static string Version(StructureSheet sheet, Guid itemId) =>
        RowVersion.Of(sheet.Stored.TryGetValue(itemId, out var amount) ? amount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");

    /// <summary>The term (404 "Term was not found."), the level (404 "Level was not found."), the fee items A to Z and the stored amounts.</summary>
    private async Task<StructureSheet> LoadAsync(ImportScope scope, CancellationToken ct)
    {
        var termId = scope.RequireGuid("termId");
        var levelId = scope.RequireGuid("levelId");
        var term = await (from t in db.Terms.AsNoTracking()
                          join s in db.AcademicSessions on t.SessionId equals s.Id
                          where t.Id == termId
                          select new { t.Name, SessionName = s.Name }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Term");
        var level = await db.Levels.AsNoTracking().Where(l => l.Id == levelId).Select(l => l.Name).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Level");
        var items = await db.FeeItems.AsNoTracking().OrderBy(i => i.Name).Select(i => new ItemRef(i.Id, i.Name)).ToListAsync(ct);
        var stored = await db.FeeStructureLines.AsNoTracking().Where(l => l.TermId == termId && l.LevelId == levelId)
            .ToDictionaryAsync(l => l.FeeItemId, l => l.AmountMinor, ct);
        return new StructureSheet(term.SessionName, term.Name, level, items, stored, SheetFor.Label(term.SessionName, level, term.Name));
    }
}
