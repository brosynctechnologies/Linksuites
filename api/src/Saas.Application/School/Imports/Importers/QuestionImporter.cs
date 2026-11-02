using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Question bank questions, one per row. A subject teacher writes for the subjects they teach; leadership for every subject; the bursar is
/// refused before anything is looked up. The question's own rules are <see cref="CbtService.CheckQuestion"/>, shared with the question screen,
/// and each row is saved through <see cref="CbtService.CreateQuestionAsync"/>. Every valid row is new; nothing in the bank is changed.
/// </summary>
public sealed class QuestionImporter(IAppDbContext db, CbtService cbt) : IImporter, IImportNotice
{
    public const string KindName = "questions";
    public const string Sheet = "Questions";
    public const string SubjectMissing = "This subject was not found.";
    public const string LevelMissing = "This level was not found.";
    public const string NoSubject = "Enter the subject.";
    public const string BadLetter = "Enter the letter of the correct option, from A to H.";
    public const string Gap = "Fill in the options from option_a with no gaps.";
    public const string Twice = "This question appears more than once in the file.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Question import finished";
    public string NoticeLink => "/app/questions";

    private const NumberStyles MarksStyle = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    private static readonly string[] Letters = ["a", "b", "c", "d", "e", "f", "g", "h"];
    private static readonly string[] OptionKeys = Letters.Select(l => "option_" + l).ToArray();

    private static readonly ImportColumns AllColumns = new(
    [
        new("subject", "subject", true, "The subject's name or code, for example Mathematics or MTH. A teacher can use only the subjects they teach."),
        new("level", "level", false, "Optional. A level of the school, for example JSS 1. Leave blank for every level."),
        new("question", "question", true, "The question, up to 2,000 characters."),
        .. OptionKeys.Select((k, i) => new ImportColumn(k, k, i < 2,
            i < 2 ? "An answer option. At least two are needed." : "Optional. Fill the options in order, with no gaps.")),
        new("correct", "correct", true, "The letter of the correct option, A to H."),
        new("marks", "marks", true, "More than 0 and up to 1000, with at most one decimal place, for example 2 or 1.5."),
    ]);

    private static readonly string[] Notes =
    [
        "One question per row on the Questions sheet. Delete nothing from the heading row.",
        "Required: subject, question, option_a, option_b, correct and marks.",
        "subject: the subject's name or code. A teacher can add questions only for the subjects they teach.",
        "level: optional, for example JSS 1. Leave it blank for a question that suits every level.",
        "Options: fill option_a, option_b and onwards in order, up to option_h. Each option must be different.",
        "correct: the letter of the right option, for example B.",
        "marks: more than 0 and up to 1000, with at most one decimal place.",
        "Every row is added to the bank as a new question. The same question twice in one file is an error.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        cbt.RefuseBursar();
        return Task.CompletedTask;
    }

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(Sheet, AllColumns.All, [], Notes));

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var writable = await cbt.WritableSubjectIdsAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name, s.Code }).ToListAsync(ct);
        // Only numeric levels, as on the question screen: computer-based tests are never set for a developmental (nursery, creche) level.
        var levels = await db.Levels.AsNoTracking().Where(l => l.AssessmentMode == Saas.Domain.School.AssessmentMode.Numeric)
            .Select(l => new { l.Id, l.Name }).ToListAsync(ct);

        var checkedRows = rows.Select(row =>
        {
            string? error = null;
            SaveBankQuestionRequest? plan = null;
            var cells = row.Cells;
            var stem = cells.GetValueOrDefault("question") ?? "";
            Guid? subjectId = null;
            try
            {
                var subjectText = cells.GetValueOrDefault("subject") ?? "";
                if (subjectText.Length == 0) throw BadRequestException.Field("subject", NoSubject);
                var subject = subjects.FirstOrDefault(s => s.Name.Equals(subjectText, StringComparison.OrdinalIgnoreCase))
                    ?? subjects.FirstOrDefault(s => s.Code.Equals(subjectText, StringComparison.OrdinalIgnoreCase));
                if (subject is null || !writable.Contains(subject.Id)) throw BadRequestException.Field("subject", SubjectMissing);
                subjectId = subject.Id;

                Guid? levelId = null;
                var levelText = cells.GetValueOrDefault("level") ?? "";
                if (levelText.Length > 0)
                {
                    var key = Squash(levelText);
                    levelId = levels.FirstOrDefault(l => Squash(l.Name) == key)?.Id ?? throw BadRequestException.Field("level", LevelMissing);
                }

                var filled = OptionKeys.Select(k => cells.GetValueOrDefault(k) ?? "").ToList();
                var last = filled.FindLastIndex(o => o.Length > 0);
                var options = filled.Take(last + 1).ToList();
                if (options.Any(o => o.Length == 0)) throw BadRequestException.Field("options", Gap);

                var letter = (cells.GetValueOrDefault("correct") ?? "").ToLowerInvariant();
                var index = Array.IndexOf(Letters, letter);
                if (index < 0) throw BadRequestException.Field("correct", BadLetter);
                if (index >= options.Count && options.Count >= 2)
                    throw BadRequestException.Field("correct", $"There is no option {letter.ToUpperInvariant()} for this question.");

                // A decimal point but no thousands separator: "1,5" is refused with the marks sentence, never read as 15.
                var marks = decimal.TryParse(cells.GetValueOrDefault("marks"), MarksStyle, CultureInfo.InvariantCulture, out var m) ? m : 0;
                var clean = CbtService.CheckQuestion(stem, options, index, marks);
                plan = new SaveBankQuestionRequest(subject.Id, levelId, clean.Stem, clean.Options, clean.CorrectIndex, clean.Marks);
            }
            catch (BadRequestException ex)
            {
                error = ex.Message;
            }
            return (Row: row, Stem: stem, SubjectId: subjectId, Error: error, Plan: plan);
        }).ToList();

        // The same question for the same subject twice in one file is an error on every copy.
        var repeated = checkedRows.Where(r => r.SubjectId is not null && r.Stem.Length > 0)
            .GroupBy(r => (r.SubjectId, Stem: SameText(r.Stem)))
            .Where(g => g.Count() > 1).SelectMany(g => g.Select(r => r.Row.Row)).ToHashSet();

        return checkedRows.Select(r =>
        {
            var error = r.Error ?? (repeated.Contains(r.Row.Row) ? Twice : null);
            return error is null
                ? new ImportRowResult(r.Row.Row, ImportRowStatus.New, Label(r.Stem), null, r.Plan)
                : new ImportRowResult(r.Row.Row, ImportRowStatus.Error, Label(r.Stem), error, null);
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var created = 0;
        foreach (var row in rows)
        {
            if (row.Status != ImportRowStatus.New || row.Plan is not SaveBankQuestionRequest request) continue;
            await cbt.CreateQuestionAsync(request, ct);
            created++;
        }
        return new ImportApplied(created, 0);
    }

    /// <summary>Lower case with every space removed, so "JSS 1", "jss1" and "Jss  1" match.</summary>
    private static string Squash(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();

    /// <summary>Lower case with runs of spaces made one, so "What is 2 + 2?" and "what is  2 + 2?" are the same question.</summary>
    private static string SameText(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static string Label(string stem) => stem.Length <= 80 ? stem : stem[..77] + "...";
}
