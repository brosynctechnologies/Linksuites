using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary>One column of the broadsheet. <see cref="Text"/> cells are quoted and formula-escaped in the CSV; number cells stay plain so a spreadsheet can add them up.</summary>
public sealed record BroadsheetColumn(string Header, bool Text);

/// <summary>
/// One class sheet. <see cref="Developmental"/> is a nursery or creche sheet: area names and scale labels, with no total, average, position or WAEC grade.
/// </summary>
public sealed record BroadsheetData(
    string SchoolName, string ClassName, string TermTitle, bool Developmental,
    IReadOnlyList<BroadsheetColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// The class broadsheet for leadership. Figures are the same full grid as <see cref="ResultService.GetAsync"/> (every subject, refreshed when stale).
/// A nursery sheet prints developmental labels. Traits are left off.
/// </summary>
public sealed class BroadsheetService(IAppDbContext db, ResultService results, ISchoolPdf pdf)
{
    public async Task<byte[]> PdfAsync(Guid classId, Guid termId, CancellationToken ct) =>
        await pdf.BroadsheetAsync(await BuildAsync(classId, termId, ct), ct);

    public async Task<byte[]> CsvAsync(Guid classId, Guid termId, CancellationToken ct) =>
        ToCsv(await BuildAsync(classId, termId, ct));

    /// <summary>UTF-8 with a byte order mark and CRLF, like the debtors export. Every text cell goes through <see cref="Saas.Application.Platform.Csv.Cell"/>.</summary>
    public static byte[] ToCsv(BroadsheetData data)
    {
        var cell = Saas.Application.Platform.Csv.Cell;
        var sb = new StringBuilder();
        sb.Append(string.Join(',', data.Columns.Select(c => cell(c.Header)))).Append("\r\n");
        foreach (var row in data.Rows)
        {
            var parts = new string[row.Count];
            for (var i = 0; i < row.Count; i++)
                parts[i] = data.Columns[i].Text ? cell(row[i]) : row[i];
            sb.Append(string.Join(',', parts)).Append("\r\n");
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private async Task<BroadsheetData> BuildAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var view = await results.GetAsync(classId, termId, ct);
        // The mode the rows were computed in (the level's mode only when there are none), so a published sheet keeps its layout.
        var developmental = view.Mode == AssessmentMode.Developmental;
        var school = await db.SchoolProfiles.AsNoTracking().Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "School";

        var subjects = developmental
            ? []
            : view.Students.SelectMany(s => s.Subjects).Select(s => s.Subject).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        var areas = developmental
            ? view.Students.SelectMany(s => s.Developmental).Select(d => d.Area).Distinct().ToList()
            : [];

        var columns = new List<BroadsheetColumn> { new("Admission number", true), new("Student", true) };
        if (developmental)
        {
            foreach (var area in areas) columns.Add(new(area, true));
        }
        else
        {
            foreach (var subject in subjects)
            {
                columns.Add(new(subject, false));
                columns.Add(new($"{subject} grade", true));
            }
            columns.Add(new("Total", false));
            columns.Add(new("Average", false));
            columns.Add(new("Position", false));
        }
        columns.Add(new("Present", false));
        columns.Add(new("Late", false));
        columns.Add(new("Absent", false));
        columns.Add(new("Excused", false));
        columns.Add(new("Open days", false));

        var rows = new List<IReadOnlyList<string>>(view.Students.Count);
        foreach (var student in view.Students)
        {
            var cells = new List<string> { student.AdmissionNo, student.Name };
            if (developmental)
            {
                var byArea = student.Developmental.GroupBy(d => d.Area).ToDictionary(g => g.Key, g => g.First().Scale);
                foreach (var area in areas)
                {
                    var scale = byArea.GetValueOrDefault(area);
                    cells.Add(string.IsNullOrWhiteSpace(scale) ? "Not yet rated" : scale!);
                }
            }
            else
            {
                var bySubject = student.Subjects.GroupBy(s => s.Subject).ToDictionary(g => g.Key, g => g.First());
                foreach (var name in subjects)
                {
                    if (bySubject.TryGetValue(name, out var subject))
                    {
                        cells.Add(Num(subject.Total));
                        cells.Add(subject.Grade);
                    }
                    else
                    {
                        cells.Add("");
                        cells.Add("");
                    }
                }
                cells.Add(Num(student.Total));
                cells.Add(Num(student.Average));
                cells.Add(Num(student.Position));
            }
            // Rows published before late, absent and excused were counted leave those cells blank rather than print a false zero.
            cells.Add(Num(student.DaysPresent));
            cells.Add(Num(student.DaysLate));
            cells.Add(Num(student.DaysAbsent));
            cells.Add(Num(student.DaysExcused));
            cells.Add(Num(student.DaysOpen));
            rows.Add(cells);
        }
        return new BroadsheetData(school, view.ClassName, view.TermName, developmental, columns, rows);
    }

    private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(int? value) => value is { } v ? Num(v) : "";
}
