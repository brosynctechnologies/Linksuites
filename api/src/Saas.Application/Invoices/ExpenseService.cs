using System.Globalization;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed record SaveExpenseRequest(
    DateOnly Date, string Category, string? Vendor, string? Description, long AmountMinor, string Currency, long TaxMinor,
    PaymentMethod PaymentMethod, string? Reference, Guid? ClientId, string? Notes, decimal? ExchangeRate);

public sealed record ExpenseDto(
    Guid Id, DateOnly Date, string Category, string? Vendor, string? Description, long AmountMinor, string Currency, long TaxMinor,
    PaymentMethod PaymentMethod, string? Reference, Guid? ClientId, string? Notes, decimal ExchangeRate, long BaseAmountMinor,
    string? ReceiptUrl, string? ClientName, DateTimeOffset CreatedAt);

public sealed class SaveExpenseRequestValidator : AbstractValidator<SaveExpenseRequest>
{
    public SaveExpenseRequestValidator()
    {
        RuleFor(x => x.Category).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Vendor).MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.AmountMinor).GreaterThan(0).LessThanOrEqualTo(100_000_000_000);
        RuleFor(x => x.TaxMinor).GreaterThanOrEqualTo(0).LessThanOrEqualTo(x => x.AmountMinor)
            .WithMessage("Tax can't be more than the amount.");
        RuleFor(x => x.Currency).Must(c => Money.SupportedCurrencies.Contains(c)).WithMessage("Unsupported currency.");
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).LessThan(10_000_000).When(x => x.ExchangeRate is not null);
    }
}

public sealed class ExpenseService(IAppDbContext db, InvoiceWorkspace ws, PlanService plans, IFileStorage storage)
{
    public static readonly string[] DefaultCategories =
    [
        "Advertising", "Bank charges", "Equipment", "Internet & phone", "Rent", "Salaries", "Software", "Supplies",
        "Transport", "Utilities", "Other",
    ];

    public async Task<Paged<ExpenseDto>> ListAsync(DateOnly? from, DateOnly? to, string? category, string? q, int page, int pageSize, CancellationToken ct)
    {
        await EnsurePlanAsync(ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Filter(from, to, category, q);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new Paged<ExpenseDto>(await ToDtosAsync(rows, ct), page, pageSize, total);
    }

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct)
    {
        var used = await db.Expenses.Select(e => e.Category).Distinct().ToListAsync(ct);
        return DefaultCategories.Union(used, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<ExpenseDto> GetAsync(Guid id, CancellationToken ct)
    {
        await EnsurePlanAsync(ct);
        return (await ToDtosAsync([await LoadAsync(id, ct)], ct))[0];
    }

    public async Task<ExpenseDto> SaveAsync(Guid? id, SaveExpenseRequest r, CancellationToken ct)
    {
        await EnsurePlanAsync(ct);
        var business = await ws.BusinessAsync(ct);
        if (r.ClientId is { } clientId && !await db.Clients.AnyAsync(c => c.Id == clientId, ct))
            throw BadRequestException.Field("clientId", "That client does not exist.");

        Expense expense;
        if (id is null)
        {
            expense = new Expense { Category = r.Category.Trim(), Currency = r.Currency };
            db.Expenses.Add(expense);
        }
        else
        {
            expense = await LoadAsync(id.Value, ct);
        }
        expense.Date = r.Date;
        expense.Category = r.Category.Trim();
        expense.Vendor = Clean(r.Vendor);
        expense.Description = Clean(r.Description);
        expense.AmountMinor = r.AmountMinor;
        expense.Currency = r.Currency.ToUpperInvariant();
        expense.TaxMinor = r.TaxMinor;
        expense.ExchangeRate = await ws.ResolveRateAsync(expense.Currency, r.ExchangeRate, r.Date, business, ct);
        expense.BaseAmountMinor = ExchangeRateService.ToBase(expense.AmountMinor, expense.ExchangeRate);
        expense.PaymentMethod = r.PaymentMethod;
        expense.Reference = Clean(r.Reference);
        expense.ClientId = r.ClientId;
        expense.Notes = Clean(r.Notes);
        await db.SaveChangesAsync(ct);
        return await GetAsync(expense.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var expense = await LoadAsync(id, ct);
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(ct);
        if (expense.ReceiptKey is not null) await storage.DeleteAsync(expense.ReceiptKey, ct);
    }

    public async Task<ExpenseDto> UploadReceiptAsync(Guid id, Upload upload, CancellationToken ct)
    {
        await EnsurePlanAsync(ct);
        var expense = await LoadAsync(id, ct);
        var (buffer, ext, mime) = await ReadReceiptAsync(upload, ct);
        await using var _ = buffer;
        var oldKey = expense.ReceiptKey;
        var key = $"{expense.TenantId}/expenses/{expense.Id:N}/{Guid.CreateVersion7():N}{ext}";
        expense.ReceiptUrl = await storage.SaveAsync(key, buffer, mime, ct);
        expense.ReceiptKey = key;
        db.Files.Add(new StoredFile { Key = key, Url = expense.ReceiptUrl, MimeType = mime, SizeBytes = buffer.Length, Purpose = "expense-receipt" });
        await db.SaveChangesAsync(ct);
        if (oldKey is not null) await storage.DeleteAsync(oldKey, ct);
        return await GetAsync(id, ct);
    }

    public async Task<ExpenseDto> RemoveReceiptAsync(Guid id, CancellationToken ct)
    {
        var expense = await LoadAsync(id, ct);
        var key = expense.ReceiptKey;
        expense.ReceiptKey = null;
        expense.ReceiptUrl = null;
        await db.SaveChangesAsync(ct);
        if (key is not null) await storage.DeleteAsync(key, ct);
        return await GetAsync(id, ct);
    }

    public async Task<byte[]> ExportCsvAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await EnsurePlanAsync(ct);
        var rows = await ToDtosAsync(await Filter(from, to, null, null).OrderBy(e => e.Date).ToListAsync(ct), ct);
        var business = await ws.BusinessAsync(ct);
        var sb = new StringBuilder($"Date,Category,Vendor,Description,Currency,Amount,Tax,Exchange rate,Amount ({business.Currency}),Payment method,Reference,Client\r\n");
        foreach (var e in rows)
        {
            sb.AppendJoin(',', e.Date.ToString("yyyy-MM-dd"), Csv.Cell(e.Category), Csv.Cell(e.Vendor), Csv.Cell(e.Description), e.Currency,
                Csv.Major(e.AmountMinor), Csv.Major(e.TaxMinor), e.ExchangeRate.ToString("0.########", CultureInfo.InvariantCulture),
                Csv.Major(e.BaseAmountMinor), e.PaymentMethod, Csv.Cell(e.Reference), Csv.Cell(e.ClientName));
            sb.Append("\r\n");
        }
        return Csv.Bytes(sb);
    }

    private IQueryable<Expense> Filter(DateOnly? from, DateOnly? to, string? category, string? q)
    {
        var query = db.Expenses.AsNoTracking();
        if (from is { } f) query = query.Where(e => e.Date >= f);
        if (to is { } t) query = query.Where(e => e.Date <= t);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(e => (e.Vendor != null && e.Vendor.ToLower().Contains(term))
                || (e.Description != null && e.Description.ToLower().Contains(term)) || e.Category.ToLower().Contains(term));
        }
        return query;
    }

    private async Task EnsurePlanAsync(CancellationToken ct)
    {
        if (!await plans.HasFeatureAsync(LimitKeys.Expenses, ct))
            throw new PlanLimitException("Expenses are part of the Business plan. Upgrade to track spending and profit.");
    }

    private async Task<Expense> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Expense");

    private async Task<List<ExpenseDto>> ToDtosAsync(IReadOnlyList<Expense> rows, CancellationToken ct)
    {
        var clientIds = rows.Where(e => e.ClientId is not null).Select(e => e.ClientId!.Value).Distinct().ToList();
        var names = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return rows.Select(e => new ExpenseDto(e.Id, e.Date, e.Category, e.Vendor, e.Description, e.AmountMinor, e.Currency, e.TaxMinor,
            e.PaymentMethod, e.Reference, e.ClientId, e.Notes, e.ExchangeRate, e.BaseAmountMinor, e.ReceiptUrl,
            e.ClientId is { } c ? names.GetValueOrDefault(c) : null, e.CreatedAt)).ToList();
    }

    /// <summary>Receipts may be a photo or a PDF.</summary>
    private static async Task<(MemoryStream, string, string)> ReadReceiptAsync(Upload upload, CancellationToken ct)
    {
        if (upload.Length > ImageUploads.MaxBytes) throw BadRequestException.Field("file", "Receipts must be 5 MB or smaller.");
        var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(5, buffer.Length));
        if (header.SequenceEqual("%PDF-"u8))
        {
            buffer.Position = 0;
            return (buffer, ".pdf", "application/pdf");
        }
        buffer.Position = 0;
        return await ImageUploads.ReadAsync(upload with { Content = buffer, Length = buffer.Length }, ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>CSV helpers shared by exports. Cells starting with = + - @, tab or CR are prefixed so spreadsheets don't run them as formulas.</summary>
public static class Csv
{
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var formula = value[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
        return formula || value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{(formula ? "'" : "")}{value.Replace("\"", "\"\"")}\""
            : value;
    }

    public static string Major(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public static byte[] Bytes(StringBuilder sb) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
}
