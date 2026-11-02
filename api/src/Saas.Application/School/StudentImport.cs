using System.Globalization;
using System.Security.Cryptography;
using Saas.Application.Common;
using Saas.Application.School.Imports;
using Saas.Application.School.Imports.Importers;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record ImportRowDto(int Row, string Status, string AdmissionNo, string Name, string ClassName, string? Message);
public sealed record ImportPreviewDto(
    string Token, IReadOnlyList<ImportRowDto> Rows, int NewCount, int UpdateCount, int ErrorCount, bool FitsPlan, int? Room);
public sealed record ImportResultDto(int Created, int Updated);

/// <summary>Reads one sheet of an .xlsx file (the named sheet, else the first), or a .csv file, as rows of text keyed by column name.</summary>
public interface ISpreadsheetReader
{
    /// <summary>
    /// One dictionary per non-blank row, keyed by the lower-case header with spaces turned into underscores, plus
    /// <see cref="StudentImportColumns.RowKey"/> holding the row number in the file. Cells are the stored text: formulas are never evaluated.
    /// Throws <see cref="InvalidDataException"/> when the file cannot be read. Stops after <paramref name="maxRows"/> rows, so a caller that
    /// passes its limit plus one can tell the file is over it without reading the rest. An .xlsx is read from the sheet called
    /// <paramref name="sheetName"/> (any case) when it has one, otherwise from its first sheet.
    /// </summary>
    IReadOnlyList<IReadOnlyDictionary<string, string>> Read(Stream content, string fileName, int maxRows = int.MaxValue, string? sheetName = null);
}

public static class StudentImportColumns
{
    public const string RowKey = "_row";

    public static readonly string[] All =
    [
        "admission_no", "first_name", "middle_name", "last_name", "gender", "date_of_birth", "class",
        "guardian1_name", "guardian1_phone", "guardian1_email", "guardian1_relationship",
        "guardian2_name", "guardian2_phone", "guardian2_email", "guardian2_relationship",
    ];

    public static readonly string[] Required = ["admission_no", "first_name", "last_name", "gender", "class"];
}

/// <summary>The import was refused because rows have errors. The rows travel in the problem response, in the shape of the route that confirmed.</summary>
public sealed class ImportErrorsException(string message, IReadOnlyList<object> rows) : AppException(message, 409, "import_errors")
{
    public IReadOnlyList<object> Rows { get; } = rows;
}

/// <summary>The workbook inflates far beyond its upload size.</summary>
public sealed class SpreadsheetTooLargeException() : Exception("This file is too large to import.");

public sealed class ImportExpiredException() : AppException("This import has expired. Upload the file again.", 404, "import_expired");

/// <summary>The pure parts of the import: dates, genders and class names as they appear in a spreadsheet.</summary>
public static class StudentImport
{
    public const int MaxRows = 2000;
    public const long MaxBytes = 5 * 1024 * 1024;
    public static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(30);

    private static readonly string[] IsoFormats = ["yyyy-MM-dd", "yyyy-M-d", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"];
    private static readonly string[] NigerianFormats = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy"];

    /// <summary>ISO first, then day/month/year (the Nigerian order), then an Excel serial number. Null when blank; false when none fit.</summary>
    public static bool TryParseDate(string? raw, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var s = raw.Trim();
        if (DateTime.TryParseExact(s, IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            date = DateOnly.FromDateTime(iso);
            return true;
        }
        if (DateTime.TryParseExact(s, NigerianFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            date = DateOnly.FromDateTime(local);
            return true;
        }
        if (double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var serial) && serial is >= 1 and < 2958466)
        {
            date = DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays(Math.Floor(serial)));
            return true;
        }
        return false;
    }

    public static bool TryParseGender(string? raw, out Gender gender)
    {
        gender = Gender.Female;
        switch ((raw ?? "").Trim().ToLowerInvariant())
        {
            case "f" or "female": return true;
            case "m" or "male": gender = Gender.Male; return true;
            default: return false;
        }
    }

    /// <summary>"jss1a" and "JSS 1A" are the same class.</summary>
    public static string ClassKey(string? name) => new string((name ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

/// <summary>One uploaded row as held between the preview and the confirm: the trimmed cell text by column.</summary>
public sealed record ImportRowData(int Row, Dictionary<string, string> Cells);
public sealed record ImportPayload(string FileName, List<ImportRowData> Rows);

/// <summary>
/// The older <c>/students/import</c> routes, kept with their response shapes. The work is the import engine's with the students importer:
/// the preview holds the rows for 30 minutes; the confirm checks them again and saves everything in one transaction, or nothing. Unlike the
/// generic routes, any leader of the school may confirm a student preview.
/// </summary>
public sealed class StudentImportService(ImportEngine engine, StudentImporter importer)
{
    private static readonly ConfirmOptions Confirm =
        new(Kind: StudentImporter.KindName, CreatorOnly: false, AuditAction: "students.import", ErrorRow: StudentImporter.Dto);

    public async Task<byte[]> XlsxTemplateAsync(CancellationToken ct) =>
        (await engine.TemplateAsync(StudentImporter.KindName, ImportScope.Empty, csv: false, ct)).Content;

    public async Task<byte[]> CsvTemplateAsync(CancellationToken ct) =>
        (await engine.TemplateAsync(StudentImporter.KindName, ImportScope.Empty, csv: true, ct)).Content;

    public async Task<ImportPreviewDto> PreviewAsync(Upload upload, CancellationToken ct)
    {
        var held = await engine.HoldAsync(importer, ImportScope.Empty, upload, "students", ct);
        var counts = ImportEngine.Count(held.Rows);
        var room = await importer.RoomAsync(ct);
        return new ImportPreviewDto(held.Token, held.Rows.Select(StudentImporter.Dto).ToList(), counts.New, counts.Update, counts.Error,
            room is null || counts.New <= room, room);
    }

    public async Task<ImportResultDto> ConfirmAsync(string token, CancellationToken ct)
    {
        var applied = await engine.ConfirmAsync(token, Confirm, ct);
        return new ImportResultDto(applied.Created, applied.Updated);
    }
}
