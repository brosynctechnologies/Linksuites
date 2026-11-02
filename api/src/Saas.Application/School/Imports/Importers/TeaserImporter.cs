using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Play brain teasers, one per row. A teacher or leadership, as on the Play screen; the bursar is refused before anything is looked up. The
/// teaser's own rules (and limits) are <see cref="PlayService.CheckTeaser"/>, shared with the screen, and each row is saved through
/// <see cref="PlayService.CreateTeaserAsync"/>. Every valid row is new.
/// </summary>
public sealed class TeaserImporter(IAppDbContext db, PlayService play) : IImporter, IImportNotice
{
    public const string KindName = "teasers";
    public const string Sheet = "Teasers";
    public const string LevelMissing = "This level was not found.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Brain teaser import finished";
    public string NoticeLink => "/app/play";

    private static readonly ImportColumns AllColumns = new(
    [
        new("level", "level", false, "Optional. A level of the school, for example JSS 1. Leave blank for every level."),
        new("prompt", "prompt", true, $"The teaser, up to {PlayService.MaxPromptLength} characters."),
        new("answer", "answer", true, $"The answer, up to {PlayService.MaxAnswerLength} characters. Students never see it."),
    ]);

    private static readonly string[] Notes =
    [
        "One brain teaser per row on the Teasers sheet. Delete nothing from the heading row.",
        "Required: prompt and answer.",
        "level: optional, for example JSS 1. Leave it blank for a teaser for every level.",
        $"prompt: up to {PlayService.MaxPromptLength} characters. answer: up to {PlayService.MaxAnswerLength} characters; students never see it.",
        "Every row is added as a new teaser.",
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
        var levels = await db.Levels.AsNoTracking().Select(l => new { l.Id, l.Name }).ToListAsync(ct);

        return rows.Select(row =>
        {
            var prompt = (row.Cells.GetValueOrDefault("prompt") ?? "").Trim();
            try
            {
                Guid? levelId = null;
                var levelText = (row.Cells.GetValueOrDefault("level") ?? "").Trim();
                if (levelText.Length > 0)
                {
                    var key = StudentImport.ClassKey(levelText);
                    levelId = levels.FirstOrDefault(l => StudentImport.ClassKey(l.Name) == key)?.Id ?? throw BadRequestException.Field("level", LevelMissing);
                }
                var (cleanPrompt, cleanAnswer) = PlayService.CheckTeaser(prompt, row.Cells.GetValueOrDefault("answer"));
                return new ImportRowResult(row.Row, ImportRowStatus.New, Label(cleanPrompt), null,
                    new SaveBrainTeaserRequest(levelId, cleanPrompt, cleanAnswer));
            }
            catch (BadRequestException ex)
            {
                return new ImportRowResult(row.Row, ImportRowStatus.Error, Label(prompt), ex.Message, null);
            }
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var created = 0;
        foreach (var row in rows)
        {
            if (row.Status != ImportRowStatus.New || row.Plan is not SaveBrainTeaserRequest request) continue;
            await play.CreateTeaserAsync(request, ct);
            created++;
        }
        return new ImportApplied(created, 0);
    }

    private static string Label(string prompt) => prompt.Length <= 80 ? prompt : prompt[..77] + "...";
}
