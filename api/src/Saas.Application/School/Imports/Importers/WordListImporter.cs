using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Play word lists, one per row. A teacher or leadership, as on the Play screen; the bursar is refused before anything is looked up. A list is
/// for the whole school, one level, or one class subject (class and subject together). A teacher can target only the class subjects they teach,
/// as on the screen. The list's own rules are <see cref="PlayService.CheckWordList"/>, shared with the screen, and each row is saved through
/// <see cref="PlayService.CreateWordListAsync"/>. Every valid row is new.
/// </summary>
public sealed class WordListImporter(IAppDbContext db, PlayService play, SchoolAccess access) : IImporter, IImportNotice
{
    public const string KindName = "word-lists";
    public const string Sheet = "Word lists";
    public const string ClassMissing = "This class was not found.";
    public const string SubjectMissing = "This subject was not found.";
    public const string ClassSubjectMissing = "This class subject was not found.";
    public const string LevelMissing = "This level was not found.";
    public const string NeedSubject = "Enter the subject as well as the class.";
    public const string NeedClass = "Enter the class as well as the subject.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Word list import finished";
    public string NoticeLink => "/app/play";

    private static readonly ImportColumns AllColumns = new(
    [
        new("name", "name", true, $"The list's name, up to {PlayService.MaxNameLength} characters."),
        new("level", "level", false, "Optional. A level of the school, for example JSS 1. Leave blank for the whole school."),
        new("class", "class", false, "Optional, with subject. A class in the current session, for example JSS 1A."),
        new("subject", "subject", false, "Optional, with class. The subject's name or code, for example Mathematics or MTH."),
        new("words", "words", true,
            $"The words, separated by commas. Letters A to Z only, {PlayService.MinWordLength} to {PlayService.MaxWordLength} letters each, up to {PlayService.MaxWords} words."),
    ]);

    private static readonly string[] Notes =
    [
        "One word list per row on the Word lists sheet. Delete nothing from the heading row.",
        "Required: name and words.",
        $"words: separated by commas, for example cat, dog, goat. Letters A to Z only, {PlayService.MinWordLength} to {PlayService.MaxWordLength} letters each, up to {PlayService.MaxWords} words, each different.",
        "Separate words with commas only, not spaces or new lines.",
        "Leave level, class and subject blank for a list for the whole school.",
        "level: for a list for one level, for example JSS 1.",
        "class and subject: for a list for one class subject, for example JSS 1A and Mathematics. Fill both or neither, and leave level blank.",
        "A teacher can choose only the class subjects they teach.",
        "Every row is added as a new word list.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        play.RefuseBursar();
        return Task.CompletedTask;
    }

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(Sheet, AllColumns.All, [], Notes));

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var classes = await (from c in db.SchoolClasses.AsNoTracking()
                             where db.AcademicSessions.Any(s => s.Id == c.SessionId && s.IsCurrent)
                             select new { c.Id, c.Name }).ToListAsync(ct);
        var classByKey = new Dictionary<string, Guid>();
        foreach (var c in classes) classByKey.TryAdd(StudentImport.ClassKey(c.Name), c.Id);
        var subjects = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name, s.Code }).ToListAsync(ct);
        var classIds = classByKey.Values.ToList();
        var links = await db.ClassSubjects.AsNoTracking().Where(cs => classIds.Contains(cs.ClassId))
            .Select(cs => new { cs.Id, cs.ClassId, cs.SubjectId }).ToListAsync(ct);
        var levels = await db.Levels.AsNoTracking().Select(l => new { l.Id, l.Name }).ToListAsync(ct);
        // A teacher reaches only the class subjects they teach, through the screen's own check (asked once per class subject).
        var reach = new Dictionary<Guid, bool>();

        var results = new List<ImportRowResult>();
        foreach (var row in rows)
        {
            var cells = row.Cells;
            var name = Cell(cells, "name");
            try
            {
                var className = Cell(cells, "class");
                var subjectText = Cell(cells, "subject");
                if (className.Length > 0 && subjectText.Length == 0) throw BadRequestException.Field("subject", NeedSubject);
                if (subjectText.Length > 0 && className.Length == 0) throw BadRequestException.Field("class", NeedClass);

                Guid? classSubjectId = null;
                if (className.Length > 0)
                {
                    if (!classByKey.TryGetValue(StudentImport.ClassKey(className), out var classId)) throw BadRequestException.Field("class", ClassMissing);
                    var subject = subjects.FirstOrDefault(s => s.Name.Equals(subjectText, StringComparison.OrdinalIgnoreCase))
                        ?? subjects.FirstOrDefault(s => s.Code.Equals(subjectText, StringComparison.OrdinalIgnoreCase))
                        ?? throw BadRequestException.Field("subject", SubjectMissing);
                    var link = links.FirstOrDefault(l => l.ClassId == classId && l.SubjectId == subject.Id)
                        ?? throw BadRequestException.Field("subject", ClassSubjectMissing);
                    if (!access.IsLeadership)
                    {
                        if (!reach.TryGetValue(link.Id, out var teaches)) reach[link.Id] = teaches = await access.TeachesClassSubjectAsync(link.Id, ct);
                        if (!teaches) throw BadRequestException.Field("subject", ClassSubjectMissing);
                    }
                    classSubjectId = link.Id;
                }

                Guid? levelId = null;
                var levelText = Cell(cells, "level");
                if (levelText.Length > 0)
                {
                    var key = StudentImport.ClassKey(levelText);
                    levelId = levels.FirstOrDefault(l => StudentImport.ClassKey(l.Name) == key)?.Id ?? throw BadRequestException.Field("level", LevelMissing);
                }

                var words = Cell(cells, "words").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var (cleanName, cleanWords) = PlayService.CheckWordList(name, words, classSubjectId, levelId);
                results.Add(new ImportRowResult(row.Row, ImportRowStatus.New, Label(cleanName), null,
                    new SaveWordListRequest(classSubjectId, levelId, cleanName, cleanWords)));
            }
            catch (BadRequestException ex)
            {
                results.Add(new ImportRowResult(row.Row, ImportRowStatus.Error, Label(name), ex.Message, null));
            }
        }
        return results;
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var created = 0;
        foreach (var row in rows)
        {
            if (row.Status != ImportRowStatus.New || row.Plan is not SaveWordListRequest request) continue;
            await play.CreateWordListAsync(request, ct);
            created++;
        }
        return new ImportApplied(created, 0);
    }

    private static string Cell(IReadOnlyDictionary<string, string> cells, string key) => (cells.GetValueOrDefault(key) ?? "").Trim();

    private static string Label(string name) => name.Length <= 80 ? name : name[..77] + "...";
}
