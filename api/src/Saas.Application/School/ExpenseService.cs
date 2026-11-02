using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record SaveExpenseRequest(string? Category, long AmountMinor, DateOnly? Date, string? Note);
public sealed record ExpenseDto(Guid Id, string Category, long AmountMinor, DateOnly Date, string? Note, bool HasReceipt, string? ReceiptUrl, Guid? RecordedBy, DateTimeOffset CreatedAt);

/// <summary>
/// What the school spends: category (free text, at most 60 characters), amount, date (not in the future), note and an optional receipt photo.
/// Owner, Admin and the bursar manage them. The receipt is a private file: resized and re-encoded as WebP like a student photo, and shown only
/// through a short-lived signed link with its own purpose (a student photo link can't open it).
/// </summary>
public sealed class ExpenseService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, IFileStorage storage, IStudentPhotoProcessor images, SchoolPhotoSigner signer, TimeProvider clock)
{
    public const int MaxCategoryLength = 60;
    public const int MaxNoteLength = 300;
    /// <summary>Longest side of a stored receipt photo: larger than a student photo so the printed amounts stay readable.</summary>
    public const int ReceiptMaxSide = 1600;
    public const long MaxAmountMinor = 10_000_000_000_000;
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();
    private DateOnly Today => SchoolCalendar.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<ExpenseDto>> ListAsync(Guid? termId, DateOnly? from, DateOnly? to, string? category, int? skip, int? take, CancellationToken ct)
    {
        var query = db.SchoolExpenses.AsNoTracking().AsQueryable();
        if (termId is { } id)
        {
            var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Term");
            query = query.Where(x => x.Date >= term.StartDate && x.Date <= term.EndDate);
        }
        if (from is { } f) query = query.Where(x => x.Date >= f);
        if (to is { } t) query = query.Where(x => x.Date <= t);
        if (!string.IsNullOrWhiteSpace(category))
        {
            var lower = category.Trim().ToLower();
            query = query.Where(x => x.Category.ToLower() == lower);
        }
        var rows = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt)
            .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 200, 1, 1000)).ToListAsync(ct);
        return rows.Select(Dto).ToList();
    }

    public async Task<ExpenseDto> GetAsync(Guid id, CancellationToken ct) => Dto(await FindAsync(id, ct, track: false));

    /// <summary>The distinct categories in use, A to Z.</summary>
    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct) =>
        await db.SchoolExpenses.AsNoTracking().Select(x => x.Category).Distinct().OrderBy(c => c).ToListAsync(ct);

    public async Task<ExpenseDto> CreateAsync(SaveExpenseRequest r, CancellationToken ct)
    {
        var (category, date, note) = await CleanAsync(r, ct);
        var expense = new SchoolExpense { TenantId = TenantId, Category = category, AmountMinor = r.AmountMinor, Date = date, Note = note, RecordedBy = user.UserId };
        db.SchoolExpenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return Dto(expense);
    }

    public async Task<ExpenseDto> UpdateAsync(Guid id, SaveExpenseRequest r, CancellationToken ct)
    {
        var expense = await FindAsync(id, ct);
        var (category, date, note) = await CleanAsync(r, ct);
        expense.Category = category;
        expense.AmountMinor = r.AmountMinor;
        expense.Date = date;
        expense.Note = note;
        await db.SaveChangesAsync(ct);
        return Dto(expense);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var expense = await FindAsync(id, ct);
        var key = expense.ReceiptKey;
        db.SchoolExpenses.Remove(expense);
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(key, ct);
    }

    // ---------- Receipt photo ----------

    public async Task<ExpenseDto> UploadReceiptAsync(Guid id, Upload upload, CancellationToken ct)
    {
        var expense = await FindAsync(id, ct);
        var (buffer, _, _) = await ImageUploads.ReadAsync(upload, ct);
        byte[] webp;
        using (buffer)
        {
            try
            {
                webp = images.ToWebp(buffer, ReceiptMaxSide);
            }
            catch (InvalidDataException)
            {
                throw BadRequestException.Field("file", "We couldn't read this photo. Upload a JPEG, PNG or WebP image.");
            }
        }
        // Under "private/": never served from /uploads. A new key per upload, so a replaced receipt is never shown from a stale link.
        var key = $"private/school/{TenantId}/expenses/{expense.Id:N}-{Guid.NewGuid():N}.webp";
        await using (var stream = new MemoryStream(webp)) await storage.SaveAsync(key, stream, "image/webp", ct);
        var old = expense.ReceiptKey;
        expense.ReceiptKey = key;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
        return Dto(expense);
    }

    public async Task DeleteReceiptAsync(Guid id, CancellationToken ct)
    {
        var expense = await FindAsync(id, ct);
        var old = expense.ReceiptKey;
        expense.ReceiptKey = null;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
    }

    /// <summary>The receipt behind a valid signed link, or null. Reads across schools: the signature is the only gate.</summary>
    public async Task<StudentPhotoFile?> ReceiptByLinkAsync(Guid expenseId, long expires, string signature, CancellationToken ct)
    {
        if (!signer.IsExpenseReceiptValid(expenseId, expires, signature)) throw new ForbiddenException("This receipt link has expired.");
        var key = await db.SchoolExpenses.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == expenseId).Select(x => x.ReceiptKey).FirstOrDefaultAsync(ct);
        if (key is null) return null;
        if (storage.TemporaryUrl(key, SchoolPhotoSigner.Lifetime) is { } url) return new StudentPhotoFile(null, url);
        var stream = await storage.OpenReadAsync(key, ct);
        return stream is null ? null : new StudentPhotoFile(stream, null);
    }

    // ---------- Helpers ----------

    private async Task<SchoolExpense> FindAsync(Guid id, CancellationToken ct, bool track = true)
    {
        var query = track ? db.SchoolExpenses.AsQueryable() : db.SchoolExpenses.AsNoTracking();
        return await query.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Expense");
    }

    /// <summary>Validates the request. A category that matches one already in use (ignoring case) takes that spelling, so the list stays tidy.</summary>
    private async Task<(string Category, DateOnly Date, string? Note)> CleanAsync(SaveExpenseRequest r, CancellationToken ct)
    {
        var category = r.Category?.Trim() ?? "";
        if (category.Length == 0) throw BadRequestException.Field("category", "Enter a category, such as Transport.");
        if (category.Length > MaxCategoryLength) throw BadRequestException.Field("category", $"Use {MaxCategoryLength} characters or fewer for the category.");
        if (r.AmountMinor <= 0) throw BadRequestException.Field("amountMinor", "Enter an amount above zero.");
        if (r.AmountMinor > MaxAmountMinor) throw BadRequestException.Field("amountMinor", "That amount is too large. Check it and try again.");
        var date = r.Date ?? throw BadRequestException.Field("date", "Enter the date the money was spent.");
        if (date > Today) throw BadRequestException.Field("date", "The date can't be in the future.");
        var note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        if (note?.Length > MaxNoteLength) throw BadRequestException.Field("note", $"Use {MaxNoteLength} characters or fewer for the note.");
        var lower = category.ToLower();
        var existing = await db.SchoolExpenses.AsNoTracking().Where(x => x.Category.ToLower() == lower).OrderBy(x => x.CreatedAt).Select(x => x.Category).FirstOrDefaultAsync(ct);
        return (existing ?? category, date, note);
    }

    private ExpenseDto Dto(SchoolExpense x) =>
        new(x.Id, x.Category, x.AmountMinor, x.Date, x.Note, x.ReceiptKey is not null, x.ReceiptKey is null ? null : signer.ExpenseReceiptUrl(x.Id), x.RecordedBy, x.CreatedAt);

    private async Task DeleteQuietlyAsync(string? key, CancellationToken ct)
    {
        if (key is null) return;
        try
        {
            await storage.DeleteAsync(key, ct);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            // The old file is orphaned, not harmful; the expense already points elsewhere.
        }
    }
}
