using ClosedXML.Excel;
using Saas.Application.School.Imports;

namespace Saas.Infrastructure.Schools;

/// <summary>
/// The downloadable .xlsx template: the data sheet first (the reader takes the first sheet), with bold headings, every column formatted as
/// text so 007, codes and phone numbers stay as typed, the heading row frozen and any roster rows filled in; then a "How to fill in" sheet.
/// </summary>
public sealed class ImportTemplateWriter : IImportTemplateWriter
{
    public byte[] Xlsx(ImportTemplate template)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(template.SheetName);
        for (var i = 0; i < template.Columns.Count; i++)
        {
            var column = sheet.Column(i + 1);
            column.Style.NumberFormat.Format = "@";
            column.Width = Math.Clamp(template.Columns[i].Heading.Length + 4, 14, 40);
            var heading = sheet.Cell(1, i + 1);
            heading.Value = template.Columns[i].Heading;
            heading.Style.Font.Bold = true;
            if (template.Columns[i].Hidden) column.Hide(); // row_version: read back on upload, never typed
        }
        for (var r = 0; r < template.Rows.Count; r++)
        {
            var row = template.Rows[r];
            for (var c = 0; c < Math.Min(row.Count, template.Columns.Count); c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                cell.Style.NumberFormat.Format = "@";
                // ClosedXML reads a leading apostrophe as Excel's "show as text" prefix and drops it, so a stored value that starts with one
                // (a comment such as '=x) gets a second one, keeping the cell's text exactly as stored.
                var value = row[c] ?? "";
                cell.Value = value.StartsWith('\'') ? "'" + value : value;
            }
        }
        sheet.SheetView.FreezeRows(1);

        var notes = workbook.AddWorksheet(ImportTemplates.NotesSheet);
        var line = 1;
        foreach (var note in template.Notes) notes.Cell(line++, 1).Value = note;
        if (template.Columns.Count > 0)
        {
            if (template.Notes.Count > 0) line++;
            string[] headings = ["Column", "Required", "What to enter"];
            for (var c = 0; c < headings.Length; c++)
            {
                notes.Cell(line, c + 1).Value = headings[c];
                notes.Cell(line, c + 1).Style.Font.Bold = true;
            }
            foreach (var column in template.Columns)
            {
                line++;
                notes.Cell(line, 1).Value = column.Heading;
                notes.Cell(line, 2).Value = column.Required ? "Yes" : "No";
                notes.Cell(line, 3).Value = column.Help;
            }
        }
        notes.Column(1).Width = 100;
        notes.Column(2).Width = 10;
        notes.Column(3).Width = 80;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
