using System.IO.Compression;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Saas.Application.School;

namespace Saas.Infrastructure.Schools;

/// <summary>
/// Reads .xlsx (the importer's own sheet, else the first sheet) and .csv files as text. Cells are read as stored: a formula is returned as its own
/// text (for example <c>=HYPERLINK("x")</c>) and never evaluated. Blank rows are skipped; each row keeps its number in the file.
/// </summary>
public sealed class SpreadsheetReader : ISpreadsheetReader
{
    private const int MaxColumns = 60;

    public IReadOnlyList<IReadOnlyDictionary<string, string>> Read(Stream content, string fileName, int maxRows = int.MaxValue, string? sheetName = null)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".xlsx" => ReadXlsx(content, maxRows, sheetName),
                ".csv" => ReadCsv(content, maxRows),
                _ => throw new InvalidDataException("Only .xlsx and .csv files can be read."),
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or SpreadsheetTooLargeException or Saas.Application.Common.BadRequestException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("The file could not be read.", ex);
        }
    }

    public static string NormaliseHeader(string? header) => Saas.Application.School.Imports.ImportColumns.KeyOf(header);

    /// <summary>Two columns whose headings read as one key would lose one column's cells, so the whole file is refused (400), for every kind.</summary>
    private static Saas.Application.Common.BadRequestException DuplicateHeading(string heading) =>
        Saas.Application.Common.BadRequestException.Field("file", $"The file has two columns called {heading.Trim()}. Keep one and upload it again.");

    // ---------- Excel ----------

    private static List<IReadOnlyDictionary<string, string>> ReadXlsx(Stream content, int maxRows, string? sheetName)
    {
        EnsureSafeToInflate(content);
        using var workbook = new XLWorkbook(content);
        var sheet = (sheetName is { Length: > 0 } ? workbook.Worksheets.FirstOrDefault(w => w.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase)) : null)
            ?? workbook.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("The workbook has no sheets.");
        using var used = sheet.RowsUsed().GetEnumerator();
        if (!used.MoveNext()) return [];

        var headerRow = used.Current;
        var lastColumn = Math.Min(headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0, MaxColumns);
        var headers = new List<(int Column, string Name)>();
        for (var col = 1; col <= lastColumn; col++)
        {
            var text = CellText(headerRow.Cell(col));
            var name = NormaliseHeader(text);
            if (name.Length == 0) continue;
            if (headers.Any(h => h.Name == name)) throw DuplicateHeading(text);
            headers.Add((col, name));
        }

        var rows = new List<IReadOnlyDictionary<string, string>>();
        while (rows.Count < maxRows && used.MoveNext())
        {
            var row = used.Current;
            var values = headers.ToDictionary(h => h.Name, h => CellText(row.Cell(h.Column)).Trim());
            if (values.Values.All(v => v.Length == 0)) continue;
            values[StudentImportColumns.RowKey] = row.RowNumber().ToString(CultureInfo.InvariantCulture);
            rows.Add(values);
        }
        return rows;
    }

    private const long MaxInflatedBytes = 50L * 1024 * 1024;
    private const long RatioCheckFrom = 1024 * 1024;
    private const int MaxRatio = 100;

    /// <summary>
    /// The 5 MB upload limit covers the compressed file. Before the workbook is loaded, inflate every part into nothing and refuse the file when
    /// the total passes 50 MB or a large part compresses more than 100 times. The sizes are counted as read, not as the file declares them.
    /// </summary>
    private static void EnsureSafeToInflate(Stream content)
    {
        content.Position = 0;
        long total = 0;
        using (var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true))
        {
            var buffer = new byte[81920];
            foreach (var entry in zip.Entries)
            {
                long size = 0;
                using var stream = entry.Open();
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    size += read;
                    if (total + size > MaxInflatedBytes) throw new SpreadsheetTooLargeException();
                }
                total += size;
                if (size > RatioCheckFrom && size > MaxRatio * Math.Max(1, entry.CompressedLength)) throw new SpreadsheetTooLargeException();
            }
        }
        content.Position = 0;
    }

    private static string CellText(IXLCell cell)
    {
        // Never evaluate: a formula cell yields its own text.
        if (cell.HasFormula) return "=" + cell.FormulaA1.TrimStart('=');
        var value = cell.Value;
        if (value.IsBlank) return "";
        if (value.IsText) return value.GetText();
        // An Excel time of day (a time-formatted number under one day) reads as the time typed, for example "09:00".
        if (value.IsTimeSpan && value.GetTimeSpan() is { Ticks: > 0 } span && span < TimeSpan.FromDays(1))
            return new DateTime(span.Ticks).ToString("HH:mm", CultureInfo.InvariantCulture);
        if (value.IsDateTime) return DateText(value.GetDateTime());
        if (value.IsBoolean) return value.GetBoolean() ? "TRUE" : "FALSE";
        if (value.IsNumber)
        {
            var number = value.GetNumber();
            if (cell.DataType == XLDataType.DateTime && number >= 1) return DateText(DateTime.FromOADate(number));
            return number == Math.Floor(number) && Math.Abs(number) < 1e15
                ? number.ToString("F0", CultureInfo.InvariantCulture)
                : number.ToString("R", CultureInfo.InvariantCulture);
        }
        return ""; // error values and durations carry nothing a student record can use
    }

    private static string DateText(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---------- CSV ----------

    private static List<IReadOnlyDictionary<string, string>> ReadCsv(Stream content, int maxRows)
    {
        using var reader = new StreamReader(content, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var records = ParseCsv(reader.ReadToEnd(), maxRows);
        if (records.Count == 0) return [];

        var headers = new List<(int Index, string Name)>();
        var headerRecord = records.FindIndex(r => r.Any(f => f.Trim().Length > 0));
        if (headerRecord < 0) return [];
        for (var i = 0; i < Math.Min(records[headerRecord].Count, MaxColumns); i++)
        {
            // A heading written by our CSV template carries the same formula escape as a cell (a component named "=CA1" is "'=CA1").
            var name = NormaliseHeader(Unescape(records[headerRecord][i].Trim()));
            if (name.Length == 0) continue;
            if (headers.Any(h => h.Name == name)) throw DuplicateHeading(records[headerRecord][i]);
            headers.Add((i, name));
        }

        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (var r = headerRecord + 1; r < records.Count && rows.Count < maxRows; r++)
        {
            var record = records[r];
            var values = headers.ToDictionary(h => h.Name, h => h.Index < record.Count ? Unescape(record[h.Index]).Trim() : "");
            if (values.Values.All(v => v.Length == 0)) continue;
            values[StudentImportColumns.RowKey] = (r + 1).ToString(CultureInfo.InvariantCulture);
            rows.Add(values);
        }
        return rows;
    }

    /// <summary>
    /// Takes off the apostrophe our CSV templates (Csv.Cell) put before a cell starting with = + - @, a tab or a carriage return (so a spreadsheet never runs it as a formula), so a
    /// downloaded template uploaded again reads exactly as stored. Only one apostrophe, and only when the cell is apostrophes followed by one of
    /// those characters (a template doubles the apostrophe of a stored value such as <c>'=x</c>, so it too reads back as stored).
    /// </summary>
    private static string Unescape(string field) =>
        Saas.Application.School.Imports.ImportTemplates.IsEscaped(field) ? field[1..] : field;

    /// <summary>RFC 4180: comma separated; fields may be quoted to hold commas, line breaks and doubled quotes. A leading byte order mark is dropped.</summary>
    public static List<List<string>> ParseCsv(string text, int maxDataRows = int.MaxValue)
    {
        if (text.StartsWith('﻿')) text = text[1..];
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;
        var filled = 0; // records with something in them; the first is the header
        var done = false;

        void EndField()
        {
            record.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void EndRecord()
        {
            EndField();
            if (record.Any(v => v.Trim().Length > 0) && ++filled > maxDataRows) done = true; // header plus maxDataRows, then one more to show it is over
            records.Add(record);
            record = [];
        }

        for (var i = 0; i < text.Length && !done; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"' when !fieldStarted:
                    quoted = true;
                    fieldStarted = true;
                    break;
                case ',':
                    EndField();
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRecord();
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    field.Append(ch);
                    fieldStarted = true;
                    break;
            }
        }
        if (quoted && !done) throw new InvalidDataException("A quoted value is never closed.");
        if (!done && (fieldStarted || field.Length > 0 || record.Count > 0)) EndRecord();
        return records;
    }
}
