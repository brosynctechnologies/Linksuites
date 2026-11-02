using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School.Imports;

/// <summary>A preview held for the confirm. <see cref="Headings"/> are the file's column keys, in file order.</summary>
internal sealed record HeldImport(string Token, string FileName, IReadOnlyList<ImportRowResult> Rows, IReadOnlyList<string> Headings);

/// <summary>How a confirm finds and reports its batch. The generic route binds the batch to its user; the older student route lets any leader confirm.</summary>
internal sealed record ConfirmOptions(string? Kind = null, bool CreatorOnly = true, string? AuditAction = null, Func<ImportRowResult, object>? ErrorRow = null,
    string Product = Billing.Products.School);

/// <summary>
/// Excel and CSV import for every kind. Preview: access check, file limits (5 MB, 2,000 rows, .xlsx or .csv, formulas read as text), required
/// columns, then the importer's evaluation; the rows are held for 30 minutes for the user who uploaded them. Confirm: the same user, the access
/// check again, a fresh evaluation (data may have changed), and either nothing (any error row) or every row in one transaction.
/// </summary>
public sealed class ImportEngine(
    IEnumerable<IImporter> importers, IAppDbContext db, ITenantContext tenant, ICurrentUser user, ISpreadsheetReader reader,
    IImportTemplateWriter writer, InAppNotifications feed, TimeProvider clock, Care.CareNotices careNotices)
{
    public const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    public const string TooManyRows = "The file has more than 2,000 rows. Import at most 2,000 rows at a time and upload the rest in another file.";
    private const string TooBig = "This file is larger than 5 MB. Split it into smaller files and upload them one at a time.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>The SchoolLink importer for a route's kind, or 404 "Import was not found."</summary>
    public IImporter Find(string kind) => Find(Billing.Products.School, kind);

    /// <summary>The importer for a route's kind under <paramref name="product"/>'s routes, or 404 "Import was not found."</summary>
    public IImporter Find(string product, string kind) =>
        importers.FirstOrDefault(i => i.Product == product && string.Equals(i.Kind, kind, StringComparison.OrdinalIgnoreCase))
        ?? throw new NotFoundException("Import");

    /// <summary>The brand in template file names: schoollink-students-template.xlsx, carelink-care-patients-template.csv.</summary>
    private static string Brand(string product) => product switch
    {
        Billing.Products.Care => "carelink",
        Billing.Products.School => "schoollink",
        _ => product,
    };

    // ---------- Template ----------

    public Task<ImportFile> TemplateAsync(string kind, ImportScope scope, bool csv, CancellationToken ct) =>
        TemplateAsync(Billing.Products.School, kind, scope, csv, ct);

    public async Task<ImportFile> TemplateAsync(string product, string kind, ImportScope scope, bool csv, CancellationToken ct)
    {
        var importer = Find(product, kind);
        await importer.EnsureAccessAsync(scope, ct);
        var template = await importer.TemplateAsync(scope, ct);
        var name = $"{Brand(product)}-{importer.Kind}-template";
        return csv
            ? new ImportFile(ImportTemplates.Csv(template), ImportTemplates.CsvType, name + ".csv")
            : new ImportFile(writer.Xlsx(template), ImportTemplates.XlsxType, name + ".xlsx");
    }

    // ---------- Preview ----------

    public Task<ImportPreviewResult> PreviewAsync(string kind, ImportScope scope, Upload? upload, CancellationToken ct) =>
        PreviewAsync(Billing.Products.School, kind, scope, upload, ct);

    public async Task<ImportPreviewResult> PreviewAsync(string product, string kind, ImportScope scope, Upload? upload, CancellationToken ct)
    {
        var importer = Find(product, kind);
        var held = await HoldAsync(importer, scope, upload, "rows", ct);
        var warnings = new List<string>();
        if (await IgnoredHeadingsAsync(importer, scope, held.Headings, ct) is { Count: > 0 } ignored)
        {
            warnings.Add(ignored.Count == 1
                ? $"This column was ignored: {ignored[0]}."
                : $"These columns were ignored: {string.Join(", ", ignored.Take(ignored.Count - 1))} and {ignored[^1]}.");
            if (importer is IImportDeclaredColumnsOnly declared) warnings.AddRange(declared.DroppedColumnWarnings(ignored));
        }
        if (importer is IImportWarnings w) warnings.AddRange(await w.WarningsAsync(scope, held.Rows, ct));
        var removes = importer is IImportRemovals r ? await r.RemovalsAsync(scope, held.Rows, ct) : 0;
        return new ImportPreviewResult(held.Token, importer.Kind, held.Rows.Select(ToPreviewRow).ToList(), Count(held.Rows) with { Remove = removes }, warnings);
    }

    /// <summary>The file's headings that match no column of the importer (fixed or scope-dependent), so a misspelt heading is not silently skipped.</summary>
    private static async Task<List<string>> IgnoredHeadingsAsync(IImporter importer, ImportScope scope, IReadOnlyList<string> headings, CancellationToken ct)
    {
        var known = importer.Columns(scope).All.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        if (importer is IImportScopeColumns extra) known.UnionWith((await extra.ScopeColumnsAsync(scope, ct)).Select(c => c.Key));
        return headings.Where(h => !known.Contains(h)).ToList();
    }

    /// <summary>Checks, reads and evaluates the file, then holds the rows. <paramref name="noun"/> names what the rows are in the file messages.</summary>
    internal async Task<HeldImport> HoldAsync(IImporter importer, ImportScope scope, Upload? upload, string noun, CancellationToken ct)
    {
        await importer.EnsureAccessAsync(scope, ct);
        if (upload is null || upload.Length <= 0) throw BadRequestException.Field("file", "Choose a file to upload.");
        if (upload.Length > StudentImport.MaxBytes) throw BadRequestException.Field("file", TooBig);
        var extension = Path.GetExtension(upload.FileName ?? "").ToLowerInvariant();
        if (extension is not (".xlsx" or ".csv")) throw BadRequestException.Field("file", "Upload an .xlsx or .csv file. Download the template to start.");

        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        if (buffer.Length > StudentImport.MaxBytes) throw BadRequestException.Field("file", TooBig);
        buffer.Position = 0;

        // A workbook is read from the importer's own sheet when it has one, otherwise from its first sheet. The name is the importer's
        // metadata: the template is not built here, so nothing that depends on the scope runs before the column check below.
        var sheetName = extension == ".xlsx" ? importer.SheetName : null;

        IReadOnlyList<IReadOnlyDictionary<string, string>> parsed;
        try
        {
            parsed = reader.Read(buffer, upload.FileName!, StudentImport.MaxRows + 1, sheetName);
        }
        catch (SpreadsheetTooLargeException)
        {
            throw BadRequestException.Field("file", $"This file is too large to import. Split it into files of up to 2,000 {noun}.");
        }
        catch (InvalidDataException)
        {
            throw BadRequestException.Field("file", $"We couldn't read this file. Upload the .xlsx or .csv template with your {noun} filled in.");
        }

        if (parsed.Count == 0) throw BadRequestException.Field("file", $"The file has no {noun}. Fill in the template and upload it again.");
        if (parsed.Count > StudentImport.MaxRows) throw BadRequestException.Field("file", TooManyRows);

        var columns = importer.Columns(scope);
        var missing = columns.Required.Where(c => !parsed[0].ContainsKey(c.Key)).Select(c => c.Heading).ToList();
        if (missing.Count > 0)
            throw BadRequestException.Field("file", missing.Count == 1
                ? $"The file is missing the column {missing[0]}. Download the template to see every column."
                : $"The file is missing the columns {string.Join(", ", missing.Take(missing.Count - 1))} and {missing[^1]}. Download the template to see every column.");

        // Every declared column is present (blank when the file lacks it). The file's other columns are kept too: a roster importer reads
        // columns that depend on the scope (for example one per assessment component) by their headings.
        // An importer that takes only its declared columns gets nothing else, so other columns' values are never held or stored.
        var declaredOnly = importer is IImportDeclaredColumnsOnly;
        var rows = parsed.Select(p =>
        {
            var cells = columns.All.ToDictionary(c => c.Key, c => (p.GetValueOrDefault(c.Key) ?? "").Trim());
            if (!declaredOnly)
                foreach (var (key, value) in p)
                    if (key != StudentImportColumns.RowKey) cells.TryAdd(key, (value ?? "").Trim());
            return new ImportRowData(int.TryParse(p.GetValueOrDefault(StudentImportColumns.RowKey), CultureInfo.InvariantCulture, out var n) ? n : 0, cells);
        }).ToList();

        var evaluated = await importer.EvaluateAsync(scope, rows, ct);

        // Held for 30 minutes. Old batches of this school (held or confirmed) are cleared on the way, in one statement that doesn't count
        // rows, so two previews clearing the same batches at once don't fail.
        var now = clock.GetUtcNow();
        await db.ImportBatches.Where(b => b.ExpiresAt < now).ExecuteDeleteAsync(ct);
        var batch = new ImportBatch
        {
            TenantId = TenantId, Token = StudentImport.NewToken(), Kind = importer.Kind,
            Scope = scope.Values.Count > 0 ? JsonSerializer.Serialize(scope.Values, Json) : null,
            Payload = JsonSerializer.Serialize(new ImportPayload(upload.FileName ?? "", rows), Json),
            ExpiresAt = now.Add(StudentImport.HoldFor), CreatedBy = user.UserId ?? Guid.Empty,
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return new HeldImport(batch.Token, upload.FileName ?? "", evaluated, parsed[0].Keys.Where(k => k != StudentImportColumns.RowKey).ToList());
    }

    // ---------- Confirm ----------

    /// <summary>
    /// Saves a held preview. Another user's, another school's or an expired token is 404 "This import has expired. Upload the file again."
    /// A token already confirmed, confirmed again by the same user within its life (a confirm whose response was lost), returns the first
    /// confirm's result with <see cref="ImportApplied.AlreadyImported"/> and saves nothing.
    /// </summary>
    public Task<ImportApplied> ConfirmAsync(string token, CancellationToken ct) => ConfirmAsync(token, new ConfirmOptions(), ct);

    /// <summary>The confirm under <paramref name="product"/>'s routes: a token held for another product's kind is "expired" (404).</summary>
    public Task<ImportApplied> ConfirmAsync(string product, string token, CancellationToken ct) =>
        ConfirmAsync(token, new ConfirmOptions(Product: product), ct);

    internal async Task<ImportApplied> ConfirmAsync(string token, ConfirmOptions options, CancellationToken ct)
    {
        var tenantId = TenantId;
        var userId = user.UserId ?? Guid.Empty;
        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Token == token && b.TenantId == tenantId, ct);
        if (batch is null || (options.Kind is { } kind && batch.Kind != kind) || (options.CreatorOnly && batch.CreatedBy != userId)
            || !importers.Any(i => i.Product == options.Product && i.Kind == batch.Kind))
            throw new ImportExpiredException();
        if (batch.ExpiresAt < clock.GetUtcNow())
        {
            await db.ImportBatches.Where(b => b.Id == batch.Id).ExecuteDeleteAsync(ct);
            throw new ImportExpiredException();
        }
        if (batch.Status == ImportBatchStatus.Confirmed) return AlreadyImported(batch, userId);

        var importer = Find(options.Product, batch.Kind);
        var scope = batch.Scope is null ? ImportScope.Empty : new ImportScope(JsonSerializer.Deserialize<Dictionary<string, string>>(batch.Scope, Json) ?? []);
        await importer.EnsureAccessAsync(scope, ct);

        var payload = JsonSerializer.Deserialize<ImportPayload>(batch.Payload, Json) ?? new ImportPayload("", []);
        var rows = await importer.EvaluateAsync(scope, payload.Rows, ct);
        var errors = rows.Where(r => r.Status == ImportRowStatus.Error).ToList();
        if (errors.Count > 0)
        {
            var toRow = options.ErrorRow ?? (r => ToPreviewRow(r));
            throw new ImportErrorsException(ErrorsMessage, errors.Select(toRow).ToList());
        }

        var outer = db.Database.CurrentTransaction;
        await using var tx = outer is null ? await db.Database.BeginTransactionAsync(ct) : null;
        // The batch is claimed first: a second confirm of the same token waits on this row, then finds it confirmed and returns that result.
        var claimed = await db.ImportBatches.Where(b => b.Id == batch.Id && b.Status == ImportBatchStatus.Held)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, ImportBatchStatus.Confirmed), ct);
        if (claimed == 0)
        {
            var done = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batch.Id, ct);
            if (done is { Status: ImportBatchStatus.Confirmed }) return AlreadyImported(done, userId);
            throw new ImportExpiredException();
        }

        var applied = await importer.ApplyAsync(scope, rows, ct);
        var result = JsonSerializer.Serialize(applied with { AlreadyImported = false }, Json);
        await db.ImportBatches.Where(b => b.Id == batch.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.Result, result), ct);
        var fileName = payload.FileName;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId, ActorId = user.UserId, Action = options.AuditAction ?? $"imports.{importer.Kind}", EntityType = nameof(ImportBatch),
            EntityId = batch.Id.ToString(),
            // Removals (timetable only) are recorded when there are any; other kinds keep their audit shape.
            AfterJson = applied.Removed > 0
                ? JsonSerializer.Serialize(new { kind = importer.Kind, created = applied.Created, updated = applied.Updated, removed = applied.Removed, fileName }, Json)
                : JsonSerializer.Serialize(new { kind = importer.Kind, created = applied.Created, updated = applied.Updated, fileName }, Json),
            Ip = user.IpAddress, CreatedAt = clock.GetUtcNow(),
        });
        if (user.UserId is { } runBy)
        {
            var notice = importer as IImportNotice;
            var counts = applied.Removed > 0
                ? $"{applied.Created} added, {applied.Updated} updated and {applied.Removed} removed"
                : $"{applied.Created} added and {applied.Updated} updated";
            // Saved with the import in its transaction, so the notice exists only if the import commits. A CareLink import goes through
            // CareNotices, which never repeats the file's name (it can name a condition; carry, Task 12).
            if (importer.Product == Billing.Products.Care)
                careNotices.ImportFinishedFor(runBy, notice?.NoticeTitle ?? "Import finished", counts, notice?.NoticeLink ?? "/app");
            else
                feed.Add(runBy, importer.Product, $"{importer.Product}.import_finished", notice?.NoticeTitle ?? "Import finished",
                    $"{counts} from {(fileName.Length > 0 ? fileName : "your file")}.", notice?.NoticeLink ?? "/app", tenantId);
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return applied;
    }

    /// <summary>
    /// The result of a batch that was already confirmed, for the user who confirmed it. Anyone else (the older student route lets any leader
    /// confirm) gets the "expired" 404, as for any token that isn't theirs.
    /// </summary>
    private static ImportApplied AlreadyImported(ImportBatch batch, Guid userId)
    {
        if (batch.CreatedBy != userId || batch.Result is null) throw new ImportExpiredException();
        var applied = JsonSerializer.Deserialize<ImportApplied>(batch.Result, Json) ?? throw new ImportExpiredException();
        return applied with { AlreadyImported = true };
    }

    // ---------- Shapes ----------

    public static ImportPreviewRow ToPreviewRow(ImportRowResult row) => new(row.Row, row.Status.ToString(), row.Label, row.Message);

    public static ImportCounts Count(IReadOnlyList<ImportRowResult> rows) => new(
        rows.Count(r => r.Status == ImportRowStatus.New), rows.Count(r => r.Status == ImportRowStatus.Update),
        rows.Count(r => r.Status == ImportRowStatus.Unchanged), rows.Count(r => r.Status == ImportRowStatus.Error));
}
