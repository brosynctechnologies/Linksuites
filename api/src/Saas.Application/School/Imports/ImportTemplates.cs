using System.Text;

namespace Saas.Application.School.Imports;

/// <summary>
/// Writes a template as an .xlsx workbook: the data sheet first (bold headings, every column formatted as text so 007 and phone numbers stay
/// as typed, the heading row frozen, roster rows prefilled), then a "How to fill in" sheet with the notes and what each column takes.
/// </summary>
public interface IImportTemplateWriter
{
    byte[] Xlsx(ImportTemplate template);
}

public static class ImportTemplates
{
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string CsvType = "text/csv";
    public const string NotesSheet = "How to fill in";

    /// <summary>
    /// UTF-8 with a byte order mark (so Excel reads accents and the naira sign), the heading line, then any roster rows. Every data cell goes
    /// through <see cref="Saas.Application.Platform.Csv.Cell"/>, so a prefilled value starting with = + - or @ (a student's name, a stored
    /// comment) opens in a spreadsheet as text, never as a formula. Headings get the same apostrophe (<see cref="Heading"/>), since some are
    /// names the school typed (an assessment component, a subject, a trait item); the reader takes it off again.
    /// </summary>
    public static byte[] Csv(ImportTemplate template)
    {
        var text = new StringBuilder();
        text.Append(string.Join(',', template.Columns.Select(c => Heading(c.Heading)))).Append("\r\n");
        foreach (var row in template.Rows) text.Append(string.Join(',', row.Select(TemplateCell))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }

    /// <summary>
    /// <see cref="Saas.Application.Platform.Csv.Cell"/>, plus one more apostrophe before a value that itself starts with apostrophes and then
    /// one of = + - @, a tab or a carriage return (a stored comment such as <c>'=x</c>). The reader takes exactly one apostrophe off a cell
    /// shaped like that, so the value reads back as stored and a free-text round trip is lossless.
    /// </summary>
    public static string TemplateCell(string? value)
    {
        var v = value ?? "";
        if (v.Length > 1 && v[0] == '\'' && IsEscaped(v)) v = "'" + v;
        return Saas.Application.Platform.Csv.Cell(v);
    }

    /// <summary>
    /// A CSV heading with the data cells' formula escape (one apostrophe before = + - @, a tab or a carriage return, and one more before a
    /// heading that already looks escaped), quoted only when RFC 4180 needs it, so plain headings stay plain (<c>admission_no</c>).
    /// </summary>
    public static string Heading(string? heading)
    {
        var v = heading ?? "";
        if (v.Length > 1 && v[0] == '\'' && IsEscaped(v)) v = "'" + v;
        else if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return Field(v);
    }

    /// <summary>True when the value is one or more apostrophes followed by = + - @, a tab or a carriage return: the shape the CSV escape makes.</summary>
    public static bool IsEscaped(string value)
    {
        var i = 0;
        while (i < value.Length && value[i] == '\'') i++;
        return i > 0 && i < value.Length && value[i] is '=' or '+' or '-' or '@' or '\t' or '\r';
    }

    /// <summary>RFC 4180: a field holding a comma, quote or line break is quoted, with quotes doubled.</summary>
    public static string Field(string? value)
    {
        var s = value ?? "";
        return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
