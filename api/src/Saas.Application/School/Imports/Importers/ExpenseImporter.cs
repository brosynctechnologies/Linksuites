using Saas.Application.Common;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// What the school spent, one expense per row: <c>date</c>, <c>category</c>, <c>amount</c> in naira and an optional <c>note</c>. The checks
/// are the expense screen's (category up to 60 characters, amount above zero, date not in the future, note up to 300 characters), and each
/// row is saved through <see cref="ExpenseService.CreateAsync"/>, so a category already in use (ignoring case) keeps its spelling. Every valid
/// row is a new expense; receipt photos are added on the expenses page. The fee policy: proprietor, principal and bursar.
/// </summary>
public sealed class ExpenseImporter(ICurrentUser user, ExpenseService expenses, TimeProvider clock) : IImporter, IImportNotice
{
    public const string KindName = "expenses";
    public const string Sheet = "Expenses";
    public const string NoDate = "Enter the date the money was spent.";
    public const string FutureDate = "The date can't be in the future.";
    public const string NoCategory = "Enter a category, such as Transport.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Expense import finished";
    public string NoticeLink => "/app/fees/expenses";

    private static readonly ImportColumns AllColumns = new(
    [
        new("date", "date", true, "The day the money was spent, for example 2026-10-26 or 26/10/2026. Not in the future."),
        new("category", "category", true, $"What it was for, such as Diesel or Repairs. Up to {ExpenseService.MaxCategoryLength} characters."),
        new("amount", "amount", true, FeeImports.AmountHelp),
        new("note", "note", false, $"Optional. Up to {ExpenseService.MaxNoteLength} characters."),
    ]);

    private static readonly string[] Notes =
    [
        "One expense per row on the Expenses sheet. Delete nothing from the heading row.",
        "date: 2026-10-26, 26/10/2026 or an Excel date. Not in the future.",
        $"category: up to {ExpenseService.MaxCategoryLength} characters. A category already in use keeps its spelling, so \"diesel\" is saved as Diesel.",
        "amount: in naira with up to two decimal places, for example 45000 or 12,500.50.",
        $"note: optional, up to {ExpenseService.MaxNoteLength} characters.",
        "Every row is added as a new expense. Add receipt photos on the expenses page.",
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

    public Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var today = SchoolCalendar.Today(clock.GetUtcNow());
        IReadOnlyList<ImportRowResult> results = rows.Select(row =>
        {
            string Cell(string key) => (row.Cells.GetValueOrDefault(key) ?? "").Trim();
            var category = Cell("category");
            var dateText = Cell("date");
            var label = string.Join(", ", new[] { dateText, category }.Where(x => x.Length > 0));
            ImportRowResult Error(string message) => new(row.Row, ImportRowStatus.Error, label, message, null);

            if (dateText.Length == 0) return Error(NoDate);
            var date = FeeImports.ParseDate(dateText, out var dateError);
            if (date is null) return Error(dateError ?? NoDate);
            if (date > today) return Error(FutureDate);
            if (category.Length == 0) return Error(NoCategory);
            if (category.Length > ExpenseService.MaxCategoryLength) return Error($"Use {ExpenseService.MaxCategoryLength} characters or fewer for the category.");
            var amount = FeeImports.ParseNaira(Cell("amount"), out var amountError);
            if (amount is null) return Error(amountError!);
            if (amount <= 0) return Error(FeeImports.AboveZero);
            if (amount > ExpenseService.MaxAmountMinor) return Error("That amount is too large. Check it and try again.");
            var note = Cell("note");
            if (note.Length > ExpenseService.MaxNoteLength) return Error($"Use {ExpenseService.MaxNoteLength} characters or fewer for the note.");

            label = $"{date.Value:yyyy-MM-dd}, {category}";
            return new ImportRowResult(row.Row, ImportRowStatus.New, label, null,
                new SaveExpenseRequest(category, amount.Value, date, note.Length > 0 ? note : null));
        }).ToList();
        return Task.FromResult(results);
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var created = 0;
        foreach (var row in rows)
        {
            if (row.Status != ImportRowStatus.New || row.Plan is not SaveExpenseRequest request) continue;
            await expenses.CreateAsync(request, ct);
            created++;
        }
        return new ImportApplied(created, 0);
    }
}
