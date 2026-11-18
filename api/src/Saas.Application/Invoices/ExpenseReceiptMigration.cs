using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Common;

namespace Saas.Application.Invoices;

/// <summary>
/// Moves InvoiceLink expense receipts stored under the old public key (<c>{tenant}/expenses/…</c>) to a private key: copy the
/// object, point the expense (and its file row) at the new key, then delete the public copy. Works with any storage provider
/// (Local and R2). Run by the invoice jobs under their advisory lock, so one API instance does it at a time. Safe to run again:
/// <list type="bullet">
/// <item>the private key is fixed per expense, so a retry overwrites its own copy instead of leaving orphans;</item>
/// <item>the rows are updated only while they still point at the old key, so a receipt uploaded meanwhile is never overwritten
/// (the copy is then removed);</item>
/// <item>nothing is tracked between rows, so one failure never spoils the rest of the batch;</item>
/// <item>a receipt whose file is gone is cleared (and logged), so it can't sit at the head of the queue forever.</item>
/// </list>
/// </summary>
public sealed class ExpenseReceiptMigration(IAppDbContext db, IFileStorage storage, ILogger<ExpenseReceiptMigration> logger)
{
    public const int BatchSize = 500;

    /// <summary>The private key a migrated receipt gets: one per expense, so running again overwrites rather than duplicates.</summary>
    public static string MigratedKey(Guid tenantId, Guid expenseId, string ext) => $"private/invoices/{tenantId}/expenses/{expenseId:N}/migrated{ext}";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var rows = await db.Expenses.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.ReceiptKey != null && !e.ReceiptKey.StartsWith("private/"))
            .OrderBy(e => e.CreatedAt).Take(BatchSize)
            .Select(e => new { e.Id, e.TenantId, ReceiptKey = e.ReceiptKey! })
            .ToListAsync(ct);
        var moved = 0;
        foreach (var expense in rows)
        {
            var oldKey = expense.ReceiptKey;
            try
            {
                var ext = Path.GetExtension(oldKey).ToLowerInvariant();
                var newKey = MigratedKey(expense.TenantId, expense.Id, ext);
                await using (var copy = new MemoryStream())
                {
                    // Closed before the old object is deleted (a local file can't be deleted while it is open on Windows).
                    await using (var source = await storage.OpenReadAsync(oldKey, ct))
                    {
                        if (source is null)
                        {
                            logger.LogWarning("Expense {Expense} receipt {Key} is missing from storage; the receipt is cleared", expense.Id, oldKey);
                            await db.Expenses.IgnoreQueryFilters().Where(e => e.Id == expense.Id && e.ReceiptKey == oldKey)
                                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReceiptKey, (string?)null).SetProperty(e => e.ReceiptUrl, (string?)null), ct);
                            continue;
                        }
                        await source.CopyToAsync(copy, ct);
                    }
                    copy.Position = 0;
                    await storage.SaveAsync(newKey, copy, ExpenseService.ContentTypeOf(ext), ct);
                }

                // Only while the row still points at the old key: a receipt uploaded meanwhile wins, and our copy goes.
                var updated = await db.Expenses.IgnoreQueryFilters().Where(e => e.Id == expense.Id && e.ReceiptKey == oldKey)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReceiptKey, newKey).SetProperty(e => e.ReceiptUrl, (string?)null), ct);
                if (updated == 0)
                {
                    await storage.DeleteQuietlyAsync(newKey, logger, ct);
                    continue;
                }
                await db.Files.IgnoreQueryFilters().Where(f => f.Key == oldKey)
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.Key, newKey).SetProperty(f => f.Url, newKey), ct);
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
