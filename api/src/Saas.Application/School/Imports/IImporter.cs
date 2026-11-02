using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Saas.Application.Common;

namespace Saas.Application.School.Imports;

/// <summary>
/// One kind of upload (students, scores, attendance...). The engine owns the file, the limits and the held batch; the importer says which
/// columns it reads, who may use it, what each row would do and how to save the rows. Apply goes through the existing service methods.
/// </summary>
public interface IImporter
{
    /// <summary>The lower-case name in the route, for example <c>students</c>. Stored on the held batch.</summary>
    string Kind { get; }

    /// <summary>
    /// The product whose routes serve this kind (<see cref="Billing.Products"/>). A kind is found only under its own product's routes, so a
    /// school can't reach a clinic's import and the other way round. SchoolLink's importers keep the default.
    /// </summary>
    string Product => Billing.Products.School;

    /// <summary>The data sheet of the template. A workbook is read from this sheet (any case) when it has one, else from its first sheet.</summary>
    string SheetName { get; }

    ImportColumns Columns(ImportScope scope);

    /// <summary>Runs first on template, preview and confirm, before anything is looked up. Throws 403 (or 404 for a record the user can't see).</summary>
    Task EnsureAccessAsync(ImportScope scope, CancellationToken ct);

    Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct);

    /// <summary>Checks every row against the school's current data and returns one result per row, in file order. Saves nothing.</summary>
    Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct);

    /// <summary>Saves rows that have no errors. Called inside the engine's transaction, with the results of a fresh evaluation.</summary>
    Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct);
}

/// <summary>Optional: notes shown above the preview rows (for example "your plan has room for 2 more students").</summary>
public interface IImportWarnings
{
    Task<IReadOnlyList<string>> WarningsAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct);
}

/// <summary>
/// Optional: columns that depend on the scope (for example one per assessment component), read by heading on top of
/// <see cref="IImporter.Columns"/>. The engine uses them only to tell which headings of a file match no column.
/// </summary>
public interface IImportScopeColumns
{
    Task<IReadOnlyList<ImportColumn>> ScopeColumnsAsync(ImportScope scope, CancellationToken ct);
}

/// <summary>Optional: how many stored records the confirm would remove (the timetable's lessons missing from the file), for the preview counts.</summary>
public interface IImportRemovals
{
    Task<int> RemovalsAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct);
}

/// <summary>
/// Optional: the importer reads only its declared columns. Every other column of the file is dropped before the rows are held, so its values
/// are never stored (not even in the 30-minute held batch). The engine still warns that the column was ignored; <see cref="DroppedColumnWarnings"/>
/// may add notes about those headings (CareLink: clinical columns are never imported).
/// </summary>
public interface IImportDeclaredColumnsOnly
{
    IReadOnlyList<string> DroppedColumnWarnings(IReadOnlyList<string> dropped);
}

/// <summary>Optional: the title and page of the "import finished" notice. Without it the notice is "Import finished" with no page.</summary>
public interface IImportNotice
{
    string NoticeTitle { get; }
    string NoticeLink { get; }
}

/// <summary>What the upload is for (a class, a term, a date...), from the query string. Held with the batch so the confirm uses the same scope.</summary>
public sealed class ImportScope(IReadOnlyDictionary<string, string> values)
{
    public static readonly ImportScope Empty = new(new Dictionary<string, string>());

    public IReadOnlyDictionary<string, string> Values { get; } = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

    public string? Get(string key) => Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    public Guid RequireGuid(string key) =>
        Guid.TryParse(Get(key), out var id) ? id : throw BadRequestException.Field(key, $"Choose the {Describe(key)} before uploading the file.");

    /// <summary>ISO, day/month/year or an Excel serial, as in the files.</summary>
    public DateOnly RequireDate(string key) =>
        StudentImport.TryParseDate(Get(key), out var date) && date is { } d
            ? d
            : throw BadRequestException.Field(key, $"Choose the {Describe(key)} before uploading the file.");

    /// <summary>"classSubjectId" reads as "class subject".</summary>
    private static string Describe(string key)
    {
        var name = key.EndsWith("Id", StringComparison.Ordinal) && key.Length > 2 ? key[..^2] : key;
        var words = new List<string>();
        var start = 0;
        for (var i = 1; i <= name.Length; i++)
        {
            if (i == name.Length || char.IsUpper(name[i]) || name[i] is '_' or '-')
            {
                var word = name[start..i].Trim('_', '-');
                if (word.Length > 0) words.Add(word.ToLower(CultureInfo.InvariantCulture));
                start = i;
            }
        }
        return string.Join(' ', words);
    }
}

/// <summary>The columns of a file. <see cref="ImportColumn.Key"/> is the heading as the reader sees it: lower case, spaces as underscores.</summary>
public sealed record ImportColumns(IReadOnlyList<ImportColumn> All)
{
    public IEnumerable<ImportColumn> Required => All.Where(c => c.Required);

    /// <summary>A heading as the reader keys it: trimmed, lower case, runs of spaces, tabs and hyphens as one underscore ("Mid term" → "mid_term").</summary>
    public static string KeyOf(string? heading) =>
        string.Join('_', (heading ?? "").Trim().ToLowerInvariant().Split([' ', '\t', '-'], StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>A column of a file. A <see cref="Hidden"/> column (row_version) is hidden in the .xlsx template; CSV has no hidden columns.</summary>
public sealed record ImportColumn(string Key, string Heading, bool Required, string Help, bool Hidden = false);

/// <summary>A downloadable template. <see cref="Rows"/> is empty for list templates and prefilled for rosters (one row per student).</summary>
public sealed record ImportTemplate(string SheetName, IReadOnlyList<ImportColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string> Notes);

public enum ImportRowStatus { New, Update, Unchanged, Error }

/// <summary>What one row would do. <see cref="Plan"/> is the importer's own typed payload for <see cref="IImporter.ApplyAsync"/>.</summary>
public sealed record ImportRowResult(int Row, ImportRowStatus Status, string Label, string? Message, object? Plan);

/// <summary>
/// What a confirm saved. <see cref="Removed"/> is for the one import that removes (the timetable replaces the class's week); it is left out of
/// the JSON when 0, so every other kind's result keeps its shape. <see cref="AlreadyImported"/> is true only on a repeat confirm of a token
/// that was already confirmed (the first confirm's result, returned again); it is left out of the JSON otherwise.
/// </summary>
public sealed record ImportApplied(int Created, int Updated,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int Removed = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool AlreadyImported = false);

public sealed record ImportPreviewResult(string Token, string Kind, IReadOnlyList<ImportPreviewRow> Rows, ImportCounts Counts, IReadOnlyList<string> Warnings);
public sealed record ImportPreviewRow(int Row, string Status, string Label, string? Message);
/// <summary>The rows by status. <see cref="Remove"/> counts what the confirm would remove (only the timetable removes; 0 for every other kind).</summary>
public sealed record ImportCounts(int New, int Update, int Unchanged, int Error, int Remove = 0);

/// <summary>A downloaded template file.</summary>
public sealed record ImportFile(byte[] Content, string ContentType, string FileName);

/// <summary>A roster row matched to the class's enrolment, or the reason it can't be.</summary>
public sealed record RosterMatch<T>(ImportRowData Row, T? Enrolment, string? Error);

/// <summary>
/// Matches the rows of a roster template (one per student) to the class's roster by admission number, ignoring case. A student who is not
/// in the class is an error, and a student who appears twice is an error on every one of their rows (never "last row wins").
/// </summary>
public static class RosterRows
{
    public const string AdmissionColumn = "admission_no";
    public const string NotInClass = "This student is not in this class.";
    public const string MoreThanOnce = "This student appears more than once in the file.";
    public const string NoAdmissionNo = "Enter the admission number.";

    public static IReadOnlyList<RosterMatch<T>> Match<T>(IReadOnlyDictionary<string, T> roster, IReadOnlyList<ImportRowData> rows, string column = AdmissionColumn)
    {
        var byKey = new Dictionary<string, T>();
        foreach (var (admissionNo, enrolment) in roster) byKey.TryAdd(Key(admissionNo), enrolment);

        var counts = rows.Select(r => Key(r.Cells.GetValueOrDefault(column))).Where(k => k.Length > 0)
            .GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());

        return rows.Select(row =>
        {
            var key = Key(row.Cells.GetValueOrDefault(column));
            if (key.Length == 0) return new RosterMatch<T>(row, default, NoAdmissionNo);
            if (!byKey.TryGetValue(key, out var enrolment)) return new RosterMatch<T>(row, default, NotInClass);
            if (counts[key] > 1) return new RosterMatch<T>(row, default, MoreThanOnce);
            return new RosterMatch<T>(row, enrolment, null);
        }).ToList();
    }

    private static string Key(string? admissionNo) => (admissionNo ?? "").Trim().ToLowerInvariant();
}

/// <summary>
/// The <c>sheet_for</c> column every roster template carries: the scope the template was made for, starting with the session, for example
/// "2026/2027 / JSS 1A / Mathematics / First term", prefilled on every row. A row whose cell names another scope is an error, so a template
/// for one class or subject is never saved onto another that happens to share the roster, and last session's template (same class, subject and
/// term names, a repeating student on both rosters) can't pass for this session's. Compared ignoring case and extra spaces. The separator is
/// " / " (plain ASCII), so the value survives CSV and an Excel re-save.
/// </summary>
public static class SheetFor
{
    public const string ColumnKey = "sheet_for";
    public const string Separator = " / ";
    public const string ClassAndSubject = "this class and subject";

    public static readonly ImportColumn Column = new(ColumnKey, ColumnKey, true,
        "Which session, class and term (and subject or list) this sheet is for. Filled in by the template; leave it as it is.");

    /// <summary>
    /// The label for a scope: the session's name first (so last session's template never matches), then the scope's parts, for example
    /// ("2026/2027", "JSS 1A", "Mathematics", "First term").
    /// </summary>
    public static string Label(string session, params string?[] parts) =>
        string.Join(Separator, new[] { session }.Concat(parts).Select(p => Squash(p)).Where(p => p.Length > 0));

    /// <summary>
    /// Null when the row's cell names this scope, otherwise the row's error message. <paramref name="download"/> ends the message: "Download the
    /// template for {download}." (for example "this class and term").
    /// </summary>
    public static string? Check(ImportRowData row, string expected, string download = ClassAndSubject)
    {
        var found = Squash(row.Cells.GetValueOrDefault(ColumnKey));
        if (found.Length == 0) return $"Keep the sheet_for cell as the template filled it. Download the template for {download}.";
        return string.Equals(Normalise(found), Normalise(expected), StringComparison.Ordinal)
            ? null
            : $"This sheet was made for {found}. Download the template for {download}.";
    }

    /// <summary>Lower case, no spaces around the separators, runs of spaces as one.</summary>
    private static string Normalise(string value) =>
        string.Join('/', value.ToLowerInvariant().Split('/').Select(Squash));

    private static string Squash(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// The hidden <c>row_version</c> column every roster template carries except attendance (which has its <c>register_version</c> row): a short
/// hash of the stored values that row can edit, as they were when the template was made. A row that would change something is an error when
/// its stored values no longer match the file's version (someone changed them on the screen after the download), on preview and again on
/// confirm, so a stale file never puts old values back. A row that changes nothing is never refused. Nothing stored hashes to a blank version,
/// so a row for something not saved yet (a new timetable lesson) may leave the cell blank. The column is required, so a file without it is the
/// engine's missing-column 400.
/// </summary>
public static class RowVersion
{
    public const string ColumnKey = "row_version";
    public const string Stale = "Someone changed this row after you downloaded the template. Download it again.";

    public static readonly ImportColumn Column = new(ColumnKey, ColumnKey, true,
        "Filled in by the template (a hidden column in Excel). It records the saved values of the row when you downloaded the file, so a row " +
        "changed on the screen since then is refused instead of overwritten. Leave it as it is.", Hidden: true);

    /// <summary>"v" and 12 hex digits of a SHA-256 of the values in order, or blank when every value is blank (nothing stored).</summary>
    public static string Of(IEnumerable<string?> stored)
    {
        var values = stored.Select(v => v ?? "").ToList();
        if (values.All(v => v.Length == 0)) return "";
        // Length-prefixed, so ("ab", "") and ("a", "b") differ.
        var text = string.Concat(values.Select(v => v.Length.ToString(CultureInfo.InvariantCulture) + ":" + v + ";"));
        return "v" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 6).ToLowerInvariant();
    }

    public static string Of(params string?[] stored) => Of((IEnumerable<string?>)stored);

    /// <summary>True when the row's row_version cell (trimmed, any case) is <paramref name="current"/>.</summary>
    public static bool Matches(ImportRowData row, string current) =>
        string.Equals((row.Cells.GetValueOrDefault(ColumnKey) ?? "").Trim(), current, StringComparison.OrdinalIgnoreCase);

    /// <summary>The result as it is, unless it would change something (New or Update) and the stored values changed since the file was made.</summary>
    public static ImportRowResult Guard(ImportRowResult result, ImportRowData row, string current) =>
        result.Status is ImportRowStatus.New or ImportRowStatus.Update && !Matches(row, current)
            ? result with { Status = ImportRowStatus.Error, Message = Stale, Plan = null }
            : result;
}
