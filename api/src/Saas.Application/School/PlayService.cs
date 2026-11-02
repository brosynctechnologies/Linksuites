using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record SaveWordListRequest(Guid? ClassSubjectId, Guid? LevelId, string? Name, IReadOnlyList<string>? Words);
public sealed record WordListDto(Guid Id, Guid? ClassSubjectId, Guid? LevelId, string Name, IReadOnlyList<string> Words, Guid CreatedBy);
public sealed record SaveBrainTeaserRequest(Guid? LevelId, string? Prompt, string? Answer);
public sealed record BrainTeaserDto(Guid Id, Guid? LevelId, string Prompt, string Answer, bool Archived, Guid CreatedBy);
public sealed record PlayTeaserDto(Guid Id, string Prompt);
public sealed record WordSearchDto(string[][] Grid, IReadOnlyList<string> Words);
public sealed record PlaySettingsRequest(bool Enabled);
public sealed record PlaySettingsDto(bool Enabled);

public sealed class PlayOffException() : AppException(PlayService.Off, 404, "play_off");

/// <summary>
/// The student Play area: a word search and brain teasers. A teacher, or leadership, writes word lists and teasers; a class-subject
/// list needs a teacher of that class subject. Leadership turns Play off for the school. A student never sees a teaser's answer,
/// and nothing here writes a score or a report card. The bursar is refused before a class subject or a level is looked up.
/// </summary>
public sealed class PlayService(IAppDbContext db, ITenantContext tenant, ICurrentUser user, SchoolAccess access, StudentAccess students)
{
    public const string Off = "Play is turned off for this school.";
    public const int MaxNameLength = 100;
    public const int MaxWords = 20;
    public const int MinWordLength = 3;
    public const int MaxWordLength = 15;
    public const int MaxPromptLength = 500;
    public const int MaxAnswerLength = 200;
    public const int TeaserSample = 5;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();
    private Guid UserId => user.UserId ?? throw new ForbiddenException();

    public async Task<WordListDto> CreateWordListAsync(SaveWordListRequest r, CancellationToken ct)
    {
        RefuseBursar();
        var (name, words) = CheckWordList(r.Name, r.Words, r.ClassSubjectId, r.LevelId);
        if (r.ClassSubjectId is { } classSubjectId
            && (!await db.ClassSubjects.AnyAsync(c => c.Id == classSubjectId, ct)
                || (!access.IsLeadership && !await access.TeachesClassSubjectAsync(classSubjectId, ct))))
            throw new NotFoundException("Class subject");
        await RequireLevelAsync(r.LevelId, ct);

        var row = new WordList
        {
            TenantId = TenantId, ClassSubjectId = r.ClassSubjectId, LevelId = r.LevelId, Name = name, Words = words, CreatedBy = UserId,
        };
        db.WordLists.Add(row);
        await db.SaveChangesAsync(ct);
        return new WordListDto(row.Id, row.ClassSubjectId, row.LevelId, row.Name, row.Words, row.CreatedBy);
    }

    public async Task<BrainTeaserDto> CreateTeaserAsync(SaveBrainTeaserRequest r, CancellationToken ct)
    {
        RefuseBursar();
        var (prompt, answer) = CheckTeaser(r.Prompt, r.Answer);
        await RequireLevelAsync(r.LevelId, ct);

        var row = new BrainTeaser { TenantId = TenantId, LevelId = r.LevelId, Prompt = prompt, Answer = answer, Archived = false, CreatedBy = UserId };
        db.BrainTeasers.Add(row);
        await db.SaveChangesAsync(ct);
        return new BrainTeaserDto(row.Id, row.LevelId, row.Prompt, row.Answer, row.Archived, row.CreatedBy);
    }

    /// <summary>Word lists, newest first. Leadership sees every list; a teacher sees school and level lists and those of class subjects they teach.</summary>
    public async Task<IReadOnlyList<WordListDto>> ListWordListsAsync(CancellationToken ct)
    {
        RefuseBursar();
        var rows = await db.WordLists.AsNoTracking().OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id).ToListAsync(ct);
        if (!access.IsLeadership)
        {
            var taught = new HashSet<Guid>();
            foreach (var id in rows.Where(w => w.ClassSubjectId is not null).Select(w => w.ClassSubjectId!.Value).Distinct())
                if (await access.TeachesClassSubjectAsync(id, ct)) taught.Add(id);
            rows = rows.Where(w => w.ClassSubjectId is not { } id || taught.Contains(id)).ToList();
        }
        return rows.Select(w => new WordListDto(w.Id, w.ClassSubjectId, w.LevelId, w.Name, w.Words, w.CreatedBy)).ToList();
    }

    /// <summary>Every teaser with its answer, newest first. Staff only; the bursar is refused.</summary>
    public async Task<IReadOnlyList<BrainTeaserDto>> ListTeasersAsync(CancellationToken ct)
    {
        RefuseBursar();
        return await db.BrainTeasers.AsNoTracking().OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new BrainTeaserDto(t.Id, t.LevelId, t.Prompt, t.Answer, t.Archived, t.CreatedBy)).ToListAsync(ct);
    }

    public async Task<PlaySettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can turn Play on or off.");
        return new PlaySettingsDto(await db.SchoolProfiles.AsNoTracking().Select(p => p.PlayEnabled).FirstAsync(ct));
    }

    public async Task<PlaySettingsDto> SetEnabledAsync(PlaySettingsRequest r, CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can turn Play on or off.");
        var profile = await db.SchoolProfiles.FirstAsync(ct);
        profile.PlayEnabled = r.Enabled;
        await db.SaveChangesAsync(ct);
        return new PlaySettingsDto(profile.PlayEnabled);
    }

    /// <summary>
    /// The class subject's list when it is a subject of the student's class, else the school's list (the student's level first),
    /// else the built-in list for the student's stage.
    /// </summary>
    public async Task<WordSearchDto> WordSearchAsync(Guid? classSubjectId, CancellationToken ct)
    {
        var place = await OpenAsync(ct);
        List<string>? words = null;
        if (classSubjectId is { } id && await db.ClassSubjects.AnyAsync(c => c.Id == id && c.ClassId == place.ClassId, ct))
            words = await db.WordLists.AsNoTracking().Where(w => w.ClassSubjectId == id)
                .OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id).Select(w => w.Words).FirstOrDefaultAsync(ct);
        words ??= await db.WordLists.AsNoTracking()
            .Where(w => w.ClassSubjectId == null && (w.LevelId == null || w.LevelId == place.LevelId))
            .OrderByDescending(w => w.LevelId != null).ThenByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id)
            .Select(w => w.Words).FirstOrDefaultAsync(ct);
        words ??= [.. BuiltInWords(place.Stage)];
        return new WordSearchDto(WordSearch.Build(words, Random.Shared), words);
    }

    /// <summary>A random few unarchived teasers for every level or the student's level. The answer is never in the payload.</summary>
    public async Task<IReadOnlyList<PlayTeaserDto>> TeasersAsync(CancellationToken ct)
    {
        var place = await OpenAsync(ct);
        return await db.BrainTeasers.AsNoTracking()
            .Where(t => !t.Archived && (t.LevelId == null || t.LevelId == place.LevelId))
            .OrderBy(t => EF.Functions.Random()).Take(TeaserSample)
            .Select(t => new PlayTeaserDto(t.Id, t.Prompt)).ToListAsync(ct);
    }

    private sealed record StudentPlace(Guid ClassId, Guid LevelId, Stage Stage);

    private async Task<StudentPlace> OpenAsync(CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        if (!await db.SchoolProfiles.AsNoTracking().Select(p => p.PlayEnabled).FirstAsync(ct)) throw new PlayOffException();
        var place = await (from e in db.Enrolments.AsNoTracking()
                           where e.StudentId == me.StudentId && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent)
                           join c in db.SchoolClasses on e.ClassId equals c.Id
                           join l in db.Levels on c.LevelId equals l.Id
                           select new StudentPlace(c.Id, l.Id, l.Stage)).FirstOrDefaultAsync(ct);
        return place ?? throw new NotFoundException("Your class");
    }

    private async Task RequireLevelAsync(Guid? levelId, CancellationToken ct)
    {
        if (levelId is { } id && !await db.Levels.AnyAsync(l => l.Id == id, ct))
            throw BadRequestException.Field("levelId", "Choose a level of this school.");
    }

    internal void RefuseBursar()
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not set Play activities.");
    }

    /// <summary>
    /// A word list's own rules, shared by the screen and the import: the name, the words, and a class subject or a level but not both.
    /// Throws <see cref="BadRequestException"/> with the screen's sentence; returns the trimmed name and the upper-case words.
    /// </summary>
    internal static (string Name, List<string> Words) CheckWordList(string? name, IReadOnlyList<string>? words, Guid? classSubjectId, Guid? levelId)
    {
        var clean = Required(name, "name", "Enter a name for the list.", MaxNameLength);
        var list = ReadWords(words);
        if (classSubjectId is not null && levelId is not null)
            throw BadRequestException.Field("levelId", "Choose a class subject or a level, not both.");
        return (clean, list);
    }

    /// <summary>A brain teaser's own rules, shared by the screen and the import. Returns the trimmed prompt and answer.</summary>
    internal static (string Prompt, string Answer) CheckTeaser(string? prompt, string? answer) =>
        (Required(prompt, "prompt", "Write the teaser.", MaxPromptLength), Required(answer, "answer", "Write the answer.", MaxAnswerLength));

    private static List<string> ReadWords(IReadOnlyList<string>? words)
    {
        var clean = (words ?? []).Select(w => (w ?? "").Trim().ToUpperInvariant()).ToList();
        if (clean.Count == 0) throw BadRequestException.Field("words", "Add at least one word.");
        if (clean.Count > MaxWords) throw BadRequestException.Field("words", $"Use {MaxWords} words or fewer.");
        if (clean.Any(w => w.Any(c => c is < 'A' or > 'Z'))) throw BadRequestException.Field("words", "Use the letters A to Z only, with no spaces.");
        if (clean.Any(w => w.Length is < MinWordLength or > MaxWordLength))
            throw BadRequestException.Field("words", $"Each word needs {MinWordLength} to {MaxWordLength} letters.");
        if (clean.Distinct().Count() != clean.Count) throw BadRequestException.Field("words", "Each word must be different.");
        return clean;
    }

    private static string Required(string? value, string field, string empty, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw BadRequestException.Field(field, empty);
        if (text.Length > max) throw BadRequestException.Field(field, $"Use {max} characters or fewer.");
        return text;
    }

    private static IReadOnlyList<string> BuiltInWords(Stage stage) => stage switch
    {
        Stage.Creche => ["CAT", "DOG", "BALL", "CUP", "SUN", "HAT"],
        Stage.Nursery => ["APPLE", "BALL", "FISH", "GOAT", "MANGO", "DRUM"],
        Stage.Primary => ["SCHOOL", "PENCIL", "TEACHER", "RULER", "BOOK", "CHALK", "MARKET", "RIVER"],
        Stage.Jss => ["SCIENCE", "GRAMMAR", "FRACTION", "CIVICS", "ENERGY", "HISTORY", "MATTER", "NOUN"],
        _ => ["PHYSICS", "BIOLOGY", "ECONOMICS", "LITERATURE", "CHEMISTRY", "GEOGRAPHY", "CALCULUS", "GOVERNMENT"],
    };
}

/// <summary>
/// Builds a square word search. Each word is placed once, left to right or top to bottom, with no two words sharing a cell. The other
/// cells get random letters A–Z, and a fill that spells a word a second time is drawn again.
/// </summary>
public static class WordSearch
{
    public const int MinSize = 8;
    private const int Placements = 30;
    private const int Fills = 10;
    private const int Growth = 10;

    public static string[][] Build(IReadOnlyList<string> words, Random random)
    {
        if (words.Count == 0) throw new ArgumentException("A word search needs at least one word.", nameof(words));
        var size = Math.Max(Math.Max(words.Max(w => w.Length), MinSize), (int)Math.Ceiling(Math.Sqrt(words.Sum(w => w.Length) * 2.0)));
        // A word inside another word (ANGLE in TRIANGLE) reads again wherever the longer one is placed.
        var expected = words.Select((w, i) => 1 + words.Where((_, j) => j != i).Sum(other => Occurrences(other, w))).ToArray();
        char[,]? fallback = null;
        for (var grow = 0; grow <= Growth; grow++, size++)
        {
            for (var attempt = 0; attempt < Placements; attempt++)
            {
                if (Place(words, size, random) is not { } placed) continue;
                if (words.Select((w, i) => Count(placed, w) != expected[i]).Any(extra => extra))
                {
                    fallback ??= Fill(placed, random);
                    continue;
                }
                for (var fill = 0; fill < Fills; fill++)
                {
                    var filled = Fill(placed, random);
                    fallback ??= filled;
                    if (words.Select((w, i) => Count(filled, w) == expected[i]).All(same => same)) return Rows(filled);
                }
            }
        }
        return Rows(fallback ?? throw new InvalidOperationException("The words did not fit the grid."));
    }

    private static char[,]? Place(IReadOnlyList<string> words, int size, Random random)
    {
        var cells = new char[size, size];
        foreach (var word in words.OrderByDescending(w => w.Length))
        {
            var placed = false;
            for (var tries = 0; tries < 100 && !placed; tries++)
            {
                var across = random.Next(2) == 0;
                var row = random.Next(across ? size : size - word.Length + 1);
                var col = random.Next(across ? size - word.Length + 1 : size);
                var free = true;
                for (var i = 0; i < word.Length && free; i++)
                    free = cells[across ? row : row + i, across ? col + i : col] == '\0';
                if (!free) continue;
                for (var i = 0; i < word.Length; i++)
                    cells[across ? row : row + i, across ? col + i : col] = word[i];
                placed = true;
            }
            if (!placed) return null;
        }
        return cells;
    }

    private static char[,] Fill(char[,] placed, Random random)
    {
        var size = placed.GetLength(0);
        var cells = (char[,])placed.Clone();
        for (var r = 0; r < size; r++)
            for (var c = 0; c < size; c++)
                if (cells[r, c] == '\0') cells[r, c] = (char)('A' + random.Next(26));
        return cells;
    }

    /// <summary>How many times the word reads left to right or top to bottom. Empty cells break a word.</summary>
    private static int Count(char[,] cells, string word)
    {
        var size = cells.GetLength(0);
        var total = 0;
        for (var line = 0; line < size; line++)
        {
            var across = new string(Enumerable.Range(0, size).Select(i => cells[line, i] == '\0' ? '.' : cells[line, i]).ToArray());
            var down = new string(Enumerable.Range(0, size).Select(i => cells[i, line] == '\0' ? '.' : cells[i, line]).ToArray());
            total += Occurrences(across, word) + Occurrences(down, word);
        }
        return total;
    }

    private static int Occurrences(string line, string word)
    {
        var count = 0;
        for (var i = line.IndexOf(word, StringComparison.Ordinal); i >= 0; i = line.IndexOf(word, i + 1, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string[][] Rows(char[,] cells)
    {
        var size = cells.GetLength(0);
        return Enumerable.Range(0, size).Select(r => Enumerable.Range(0, size).Select(c => cells[r, c].ToString()).ToArray()).ToArray();
    }
}
