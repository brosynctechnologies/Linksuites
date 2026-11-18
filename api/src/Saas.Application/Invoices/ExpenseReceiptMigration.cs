using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Common;

namespace Saas.Application.Invoices;

/// <summary>
/// Moves InvoiceLink expense receipts stored under the old public key (<c>{tenant}/expenses/…</c>) to a private key: copy the
/// object, point the expense (and its file row) at the new key, then delete the public copy. Works with any storage provider
/// (Local and R2). Safe to run again: only rows whose key is not yet private are touched, and a row whose file is missing is
/// left as it is and logged. Run by the invoice jobs under their advisory lock, so one API instance does it at a time.
/// </summary>
public sealed class ExpenseReceiptMigration(IAppDbContext db, IFileStorage storage, ILogger<ExpenseReceiptMigration> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var rows = await db.Expenses.IgnoreQueryFilters()
            .Where(e => e.ReceiptKey != null && !e.ReceiptKey.StartsWith("private/"))
            .OrderBy(e => e.CreatedAt).Take(500).ToListAsync(ct);
        var moved = 0;
        foreach (var expense in rows)
        {
            var oldKey = expense.ReceiptKey!;
            try
            {
                var ext = Path.GetExtension(oldKey).ToLowerInvariant();
                var newKey = ExpenseService.ReceiptKey(expense.TenantId, expense.Id, ext);
                await using (var copy = new MemoryStream())
                {
                    // Closed before the old object is deleted (a local file can't be deleted while it is open on Windows).
                    await using (var source = await storage.OpenReadAsync(oldKey, ct))
                    {
                        if (source is null)
                        {
                            logger.LogWarning("Expense {Expense} receipt {Key} is missing from storage; left unchanged", expense.Id, oldKey);
                            continue;
                        }
                        await source.CopyToAsync(copy, ct);
                    }
                    copy.Position = 0;
                    await storage.SaveAsync(newKey, copy, ExpenseService.ContentTypeOf(ext), ct);
                }
                expense.ReceiptKey = newKey;
                expense.ReceiptUrl = null;
                var file = await db.Files.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Key == oldKey, ct);
                if (file is not null)
                {
                    file.Key = newKey;
                    file.Url = newKey;
                }
                await db.SaveChangesAsync(ct);
                await storage.DeleteQuietlyAsync(oldKey, logger, ct);
                moved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not move expense {Expense} receipt {Key} to private storage", expense.Id, oldKey);
            }
        }
        if (moved > 0) logger.LogInformation("Moved {Count} expense receipts to private storage", moved);
        return moved;
    }
}
