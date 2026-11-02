using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary>One subject row of a report card. The text is already formatted; the renderer only lays it out.</summary>
public sealed record ReportCardSubject(string Name, IReadOnlyList<string> Scores, string Total, string Grade, string ClassAverage, string Position, string Remark);

/// <summary>One psychomotor or affective row. <see cref="Rating"/> is "4 of 5", never an exam mark.</summary>
public sealed record ReportCardTraitRow(string Name, string Rating);

/// <summary>A trait block. <see cref="Heading"/> is sentence case, such as "Psychomotor".</summary>
public sealed record ReportCardTraitBlock(string Heading, IReadOnlyList<ReportCardTraitRow> Rows);

/// <summary>One developmental area on a nursery card. <see cref="Scale"/> is a label such as Excellent, not a grade.</summary>
public sealed record ReportCardDevelopmentalRow(string Area, string Scale, string? Comment);

/// <summary>Everything one report card prints. Images are bytes read from private storage (or null); text is formatted by <see cref="ReportCardService"/>.</summary>
public sealed record ReportCardData(
    string SchoolName, string? Motto, string? Address, byte[]? Logo, byte[]? Signature,
    string StudentName, string AdmissionNo, string ClassName, string Gender, string? DateOfBirth, byte[]? Photo,
    string TermTitle, IReadOnlyList<string> ComponentHeaders, IReadOnlyList<ReportCardSubject> Subjects,
    string Total, string Average, string Position, string Attendance,
    string? TeacherRemark, string? PrincipalRemark, string? NextTermBegins, IReadOnlyList<string> Notices, bool ShowBranding,
    AssessmentMode Mode = AssessmentMode.Numeric,
    IReadOnlyList<ReportCardTraitBlock>? Traits = null,
    IReadOnlyList<ReportCardDevelopmentalRow>? Developmental = null);

public sealed record ReportCardBundleRequestedDto(Guid Id);
/// <summary>A bundle's status and, once Ready, its download link. The class and term let a page opened from the notification show which cards these are.</summary>
public sealed record ReportCardBundleDto(string Status, string? Url, Guid ClassId, string ClassName, Guid TermId);

/// <summary>Wording shared by the report card and its tests.</summary>
public static class ReportCardText
{
    /// <summary>1st, 2nd, 3rd, 4th ... 11th, 12th, 13th, 21st, 101st, 111th.</summary>
    public static string Ordinal(int n)
    {
        var lastTwo = Math.Abs(n) % 100;
        var suffix = lastTwo is >= 11 and <= 13 ? "th" : (Math.Abs(n) % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n.ToString(CultureInfo.InvariantCulture)}{suffix}";
    }

    /// <summary>"2nd of 30".</summary>
    public static string Position(int position, int classSize) => $"{Ordinal(position)} of {classSize.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Scores and totals: up to one decimal place, no trailing zero.</summary>
    public static string Score(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Averages: two decimal places.</summary>
    public static string Average(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>"Present 1, late 1, absent 0, excused 0 of 2 days".</summary>
    public static string Attendance(int present, int late, int absent, int excused, int open) =>
        $"Present {present.ToString(CultureInfo.InvariantCulture)}, late {late.ToString(CultureInfo.InvariantCulture)}, absent {absent.ToString(CultureInfo.InvariantCulture)}, excused {excused.ToString(CultureInfo.InvariantCulture)} of {open.ToString(CultureInfo.InvariantCulture)} days";

    /// <summary>
    /// The four-count line, or "Present 1 of 2 days" for a row published before late, absent and excused were counted
    /// (those rows store null rather than a false zero).
    /// </summary>
    public static string Attendance(int present, int? late, int? absent, int? excused, int open) =>
        late is { } l && absent is { } a && excused is { } e
            ? Attendance(present, l, a, e, open)
            : $"Present {present.ToString(CultureInfo.InvariantCulture)} of {open.ToString(CultureInfo.InvariantCulture)} days";
}

/// <summary>
/// Report cards from the stored results. Leadership and the class teacher open a student's card (a draft while the class is not
/// published); the class bundle is queued here and built by <see cref="BuildBundleAsync"/>, which the report card job calls.
/// Published rows are frozen and never recomputed; unpublished ones are refreshed first, exactly like the results page.
/// </summary>
public sealed class ReportCardService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, SchoolAccess access, ResultService results, AcademicService academic,
    PlanService plans, IFileStorage storage, ISchoolPdf pdf, SchoolPhotoSigner signer, SchoolFeed feed, TimeProvider clock, ILogger<ReportCardService> log)
{
    public const int MaxBundleStudents = 500;
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- One student ----------

    /// <summary>
    /// A card is always the result row of (enrolment, class, term). Without <paramref name="requestedClassId"/>: the one class the student has
    /// a row in, 400 when they have rows in several classes, and their current class (computed first) when they have none.
    /// Access is checked against the class actually used.
    /// </summary>
    public async Task<byte[]> RenderAsync(Guid enrolmentId, Guid termId, Guid? requestedClassId, CancellationToken ct)
    {
        var enrolment = await db.Enrolments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == enrolmentId, ct) ?? throw new NotFoundException("Report card");
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");
        if (term.SessionId != enrolment.SessionId) throw new NotFoundException("Report card");

        var rowClasses = await db.TermResults.AsNoTracking().Where(r => r.EnrolmentId == enrolmentId && r.TermId == termId)
            .Select(r => r.ClassId).Distinct().ToListAsync(ct);
        Guid classId;
        if (requestedClassId is { } asked)
        {
            // Only a class the student has a row in, or is in now.
            if (!rowClasses.Contains(asked) && asked != enrolment.ClassId) throw new NotFoundException("Report card");
            classId = asked;
        }
        else if (rowClasses.Count > 1) throw BadRequestException.Field("classId", "This student was in more than one class this term. Choose the class.");
        else classId = rowClasses.Count == 1 ? rowClasses[0] : enrolment.ClassId;

        await results.EnsureCanReadClassAsync(classId, termId, ct);
        var cards = await BuildAsync(classId, termId, [enrolmentId], false, ct);
        if (cards.Count == 0) throw new NotFoundException("Report card");
        return await pdf.ReportCardAsync(cards[0], ct);
    }

    /// <summary>
    /// The card the parent and student portals print: only from a published row of (enrolment, class, term), never a draft and never
    /// recomputed. The caller has already checked the child is theirs and the results hold. Without <paramref name="requestedClassId"/>:
    /// the one class with a published row, 400 when there are several; 404 when there is none (or the class asked for has none).
    /// </summary>
    public async Task<byte[]> RenderPublishedAsync(Guid enrolmentId, Guid termId, Guid? requestedClassId, CancellationToken ct)
    {
        var published = await db.TermResults.AsNoTracking()
            .Where(r => r.EnrolmentId == enrolmentId && r.TermId == termId && r.PublishedAt != null)
            .Select(r => r.ClassId).Distinct().ToListAsync(ct);
        Guid classId;
        if (requestedClassId is { } asked)
        {
            if (!published.Contains(asked)) throw new NotFoundException("Report card");
            classId = asked;
        }
        else if (published.Count > 1) throw BadRequestException.Field("classId", "This student was in more than one class this term. Choose the class.");
        else if (published.Count == 1) classId = published[0];
        else throw new NotFoundException("Report card");

        var cards = await BuildAsync(classId, termId, [enrolmentId], true, ct);
        if (cards.Count == 0) throw new NotFoundException("Report card");
        return await pdf.ReportCardAsync(cards[0], ct);
    }

    // ---------- Class bundle ----------

    /// <summary>Queues the bundle. A bundle of the same class and term that this member already asked for and is still waiting is returned instead of a second one.</summary>
    public async Task<ReportCardBundleRequestedDto> RequestBundleAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        await results.EnsureCanReadClassAsync(classId, termId, ct);
        var userId = user.UserId ?? throw new ForbiddenException();
        var waiting = await db.ReportCardBundles.AsNoTracking()
            .Where(b => b.ClassId == classId && b.TermId == termId && b.RequestedBy == userId && b.Status == ReportCardBundleStatus.Queued)
            .Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
        if (waiting is { } existing) return new ReportCardBundleRequestedDto(existing);

        var bundle = new ReportCardBundle { TenantId = TenantId, ClassId = classId, TermId = termId, RequestedBy = userId };
        db.ReportCardBundles.Add(bundle);
        await db.SaveChangesAsync(ct);
        return new ReportCardBundleRequestedDto(bundle.Id);
    }

    /// <summary>The school's leadership and the member who asked for the bundle read its status. Ready bundles carry a short-lived download link.</summary>
    public async Task<ReportCardBundleDto> GetBundleAsync(Guid id, CancellationToken ct)
    {
        var bundle = await db.ReportCardBundles.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Report card bundle");
        if (!access.IsLeadership && (user.UserId is not { } userId || bundle.RequestedBy != userId))
            throw new ForbiddenException("Only the principal and the person who asked for these report cards can open them.");
        var className = await db.SchoolClasses.AsNoTracking().Where(c => c.Id == bundle.ClassId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "";
        return new ReportCardBundleDto(bundle.Status.ToString(), bundle.Status == ReportCardBundleStatus.Ready ? signer.BundleUrl(bundle.Id) : null,
            bundle.ClassId, className, bundle.TermId);
    }

    /// <summary>
    /// Builds a queued bundle's PDF into private storage and marks it Ready, or Failed when it cannot be built. Called by the report card
    /// job inside the school's tenant scope; there is no signed-in user, so access was checked when the bundle was requested.
    /// Bundles older than 7 days (and their files) may be deleted by the job.
    /// </summary>
    public async Task BuildBundleAsync(Guid id, CancellationToken ct)
    {
        var bundle = await db.ReportCardBundles.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Report card bundle");
        if (bundle.Status != ReportCardBundleStatus.Queued) return;

        try
        {
            var cards = await BuildAsync(bundle.ClassId, bundle.TermId, null, false, ct);
            if (cards.Count == 0) throw new InvalidOperationException("The class has no results to print.");
            var bytes = await pdf.ReportCardsAsync(cards, ct);
            var key = $"private/school/{TenantId}/report-cards/{bundle.Id:N}.pdf";
            await using (var stream = new MemoryStream(bytes)) await storage.SaveAsync(key, stream, "application/pdf", ct);
            bundle.FileKey = key;
            bundle.Status = ReportCardBundleStatus.Ready;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Report card bundle {BundleId} could not be built.", id);
            bundle.Status = ReportCardBundleStatus.Failed;
        }
        bundle.CompletedAt = clock.GetUtcNow();
        var className = await db.SchoolClasses.Where(c => c.Id == bundle.ClassId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "the class";
        if (bundle.Status == ReportCardBundleStatus.Ready)
            feed.Notify(bundle.RequestedBy, "school.report_cards_ready", $"Report cards for {className} are ready",
                "Download them from the results page.", $"/app/results?bundle={bundle.Id}");
        else
            // Bundles are built from draft results too, so the advice is about results, not publishing.
            feed.Notify(bundle.RequestedBy, "school.report_cards_failed", $"Report cards for {className} could not be made",
                "Check the class has results, then try again.", $"/app/results?bundle={bundle.Id}");
        await db.SaveChangesAsync(ct);
    }

    public static readonly TimeSpan BundleRetention = TimeSpan.FromDays(7);

    /// <summary>The ids of the school's bundles still waiting to be built, oldest first. For the report card job.</summary>
    public Task<List<Guid>> QueuedBundleIdsAsync(CancellationToken ct) =>
        db.ReportCardBundles.AsNoTracking().Where(b => b.Status == ReportCardBundleStatus.Queued).OrderBy(b => b.CreatedAt).Select(b => b.Id).ToListAsync(ct);

    /// <summary>Deletes the school's bundles older than 7 days (row and stored file). Returns how many were deleted. For the report card job.</summary>
    public async Task<int> DeleteOldBundlesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var before = now - BundleRetention;
        var old = await db.ReportCardBundles.Where(b => b.CreatedAt < before).OrderBy(b => b.CreatedAt).Take(200).ToListAsync(ct);
        foreach (var bundle in old)
        {
            if (bundle.FileKey is { } key)
            {
                try { await storage.DeleteAsync(key, ct); }
                catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
                {
                    log.LogWarning(ex, "Report card bundle {BundleId}: the stored file could not be deleted.", bundle.Id);
                    continue; // the row stays so the file is tried again next time
                }
            }
            db.ReportCardBundles.Remove(bundle);
        }
        await db.SaveChangesAsync(ct);
        return old.Count;
    }

    /// <summary>The PDF behind a valid signed link, or null. Reads across schools: the signature is the only gate.</summary>
    public async Task<StudentPhotoFile?> BundleByLinkAsync(Guid id, long expires, string signature, CancellationToken ct)
    {
        if (!signer.IsBundleValid(id, expires, signature)) throw new ForbiddenException("This download link has expired.");
        var key = await db.ReportCardBundles.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.Id == id && b.Status == ReportCardBundleStatus.Ready).Select(b => b.FileKey).FirstOrDefaultAsync(ct);
        if (key is null) return null;
        if (storage.TemporaryUrl(key, SchoolPhotoSigner.Lifetime) is { } url) return new StudentPhotoFile(null, url);
        var stream = await storage.OpenReadAsync(key, ct);
        return stream is null ? null : new StudentPhotoFile(stream, null);
    }

    // ---------- Gathering ----------

    /// <summary>
    /// The report cards of the class's term, in alphabetical order, or only those of <paramref name="enrolmentIds"/>. With
    /// <paramref name="publishedOnly"/> the stored rows are read as they are (never recomputed) and nothing is returned unless they are published.
    /// </summary>
    private async Task<List<ReportCardData>> BuildAsync(Guid classId, Guid termId, IReadOnlyList<Guid>? enrolmentIds, bool publishedOnly, CancellationToken ct)
    {
        var view = publishedOnly ? await results.ReadStoredAsync(classId, termId, ct) : await results.ReadForReportCardsAsync(classId, termId, ct);
        if (publishedOnly && view.PublishedAt is null) return []; // unpublished since the caller looked: no draft for families
        var students = view.Students.Where(s => enrolmentIds is null || enrolmentIds.Contains(s.EnrolmentId)).ToList();
        if (students.Count == 0) return [];
        if (students.Count > MaxBundleStudents) throw new InvalidOperationException($"A bundle holds at most {MaxBundleStudents} report cards.");

        var cls = await db.SchoolClasses.AsNoTracking().FirstAsync(c => c.Id == classId, ct);
        var term = await db.Terms.AsNoTracking().FirstAsync(t => t.Id == termId, ct);
        var sessionName = await db.AcademicSessions.AsNoTracking().Where(s => s.Id == cls.SessionId).Select(s => s.Name).FirstAsync(ct);
        var profile = await db.SchoolProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        var logo = profile?.LogoKey is { } logoKey ? await ReadAsync(logoKey, ct) : null;
        var signature = profile?.SignatureKey is { } signatureKey ? await ReadAsync(signatureKey, ct) : null;
        var showBranding = !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct);
        var nextTerm = await NextTermBeginsAsync(term, ct);

        var components = await academic.EnsureSchemeAsync(cls.LevelId, ct);
        var headers = components.Select(c => $"{c.Name} ({ReportCardText.Score(c.MaxScore)})").ToList();
        var classSubjects = await db.ClassSubjects.AsNoTracking().Where(c => c.ClassId == classId)
            .Join(db.Subjects.AsNoTracking(), c => c.SubjectId, s => s.Id, (c, s) => new { c.Id, s.Name, s.Code }).ToListAsync(ct);
        var subjectIdByName = classSubjects.GroupBy(c => (c.Name, c.Code)).ToDictionary(g => g.Key, g => g.First().Id);
        var classSubjectIds = classSubjects.Select(c => c.Id).ToList();
        var wanted = students.Select(s => s.EnrolmentId).ToList();
        var componentIds = components.Select(c => c.Id).ToList();
        var scoreRows = await db.Scores.AsNoTracking()
            .Where(s => s.TermId == termId && classSubjectIds.Contains(s.ClassSubjectId) && wanted.Contains(s.EnrolmentId) && componentIds.Contains(s.ComponentId))
            .Select(s => new { s.EnrolmentId, s.ClassSubjectId, s.ComponentId, s.Value }).ToListAsync(ct);
        var scores = scoreRows.ToDictionary(s => (s.EnrolmentId, s.ClassSubjectId, s.ComponentId), s => s.Value);

        var studentIds = students.Select(s => s.StudentId).ToList();
        var people = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var traitBlocks = await TraitBlocksAsync(cls.LevelId, termId, wanted, ct);

        var draft = view.PublishedAt is null;
        var cards = new List<ReportCardData>(students.Count);
        foreach (var student in students.OrderBy(s => s.Name).ThenBy(s => s.AdmissionNo))
        {
            people.TryGetValue(student.StudentId, out var person);
            // The card follows the mode the row was computed in, not the level's mode today.
            var mode = student.Mode;
            var photo = person?.PhotoKey is { } photoKey ? await ReadAsync(photoKey, ct) : null;
            var subjects = student.Subjects.Select(s =>
            {
                subjectIdByName.TryGetValue((s.Subject, s.SubjectCode), out var classSubjectId);
                var cells = components.Select(c => scores.TryGetValue((student.EnrolmentId, classSubjectId, c.Id), out var v) ? ReportCardText.Score(v) : "-").ToList();
                return new ReportCardSubject(s.Subject, cells, ReportCardText.Score(s.Total), s.Grade, ReportCardText.Average(s.ClassAverage), ReportCardText.Ordinal(s.Position), s.Remark ?? "");
            }).ToList();

            var notices = new List<string>();
            if (draft) notices.Add("Draft — not published");
            if (student.Incomplete)
                notices.Add(mode == AssessmentMode.Developmental ? "Some areas have not been rated yet." : "Some scores have not been entered yet.");
            traitBlocks.TryGetValue(student.EnrolmentId, out var traits);
            var developmentalRows = student.Developmental.Count == 0
                ? null
                : student.Developmental.Select(d => new ReportCardDevelopmentalRow(d.Area, string.IsNullOrWhiteSpace(d.Scale) ? "Not yet rated" : d.Scale, d.Comment)).ToList();
            cards.Add(new ReportCardData(
                profile?.Name ?? "School", profile?.Motto, profile?.Address, logo, signature,
                student.Name, student.AdmissionNo, cls.Name, person?.Gender.ToString() ?? "", person?.DateOfBirth?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture), photo,
                $"{term.Name}, {sessionName} session", headers, mode == AssessmentMode.Developmental ? [] : subjects,
                ReportCardText.Score(student.Total), ReportCardText.Average(student.Average), ReportCardText.Position(student.Position, student.ClassSize),
                ReportCardText.Attendance(student.DaysPresent, student.DaysLate, student.DaysAbsent, student.DaysExcused, student.DaysOpen),
                student.TeacherRemark, student.PrincipalRemark, nextTerm, notices, showBranding,
                mode, traits is { Count: > 0 } ? traits : null, developmentalRows));
        }
        return cards;
    }

    /// <summary>Psychomotor and affective ratings for the card. A kind with no ratings is left out, so a numeric card with none is unchanged.</summary>
    private async Task<Dictionary<Guid, List<ReportCardTraitBlock>>> TraitBlocksAsync(Guid levelId, Guid termId, List<Guid> enrolmentIds, CancellationToken ct)
    {
        async Task<List<TraitCriterion>> Criteria(TraitKind kind)
        {
            var own = await db.TraitCriteria.AsNoTracking().Where(c => c.LevelId == levelId && c.Kind == kind).OrderBy(c => c.Order).ThenBy(c => c.Name).ToListAsync(ct);
            if (own.Count > 0) return own;
            return await db.TraitCriteria.AsNoTracking().Where(c => c.LevelId == null && c.Kind == kind).OrderBy(c => c.Order).ThenBy(c => c.Name).ToListAsync(ct);
        }

        var psychomotor = await Criteria(TraitKind.Psychomotor);
        var affective = await Criteria(TraitKind.Affective);
        var criterionIds = psychomotor.Select(c => c.Id).Concat(affective.Select(c => c.Id)).ToList();
        var ratings = criterionIds.Count == 0 || enrolmentIds.Count == 0
            ? []
            : await db.TraitRatings.AsNoTracking()
                .Where(r => r.TermId == termId && enrolmentIds.Contains(r.EnrolmentId) && criterionIds.Contains(r.CriterionId))
                .Select(r => new { r.EnrolmentId, r.CriterionId, r.Value }).ToListAsync(ct);
        var byEnrolment = ratings.GroupBy(r => r.EnrolmentId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.CriterionId, r => r.Value));

        List<ReportCardTraitBlock> Blocks(Guid enrolmentId)
        {
            if (!byEnrolment.TryGetValue(enrolmentId, out var mine)) return [];
            ReportCardTraitBlock? Block(string heading, IReadOnlyList<TraitCriterion> criteria)
            {
                var rows = criteria.Where(c => mine.ContainsKey(c.Id)).Select(c => new ReportCardTraitRow(c.Name, $"{mine[c.Id]} of 5")).ToList();
                return rows.Count == 0 ? null : new ReportCardTraitBlock(heading, rows);
            }
            return new ReportCardTraitBlock?[] { Block("Psychomotor", psychomotor), Block("Affective", affective) }.Where(b => b is not null).Cast<ReportCardTraitBlock>().ToList();
        }

        return enrolmentIds.ToDictionary(id => id, Blocks);
    }

    /// <summary>The date set on the term, else the start of the next term (even in the next session). Null when there is neither.</summary>
    private async Task<string?> NextTermBeginsAsync(Term term, CancellationToken ct)
    {
        var date = term.NextTermStarts
            ?? await db.Terms.AsNoTracking().Where(t => t.StartDate > term.StartDate).OrderBy(t => t.StartDate).Select(t => (DateOnly?)t.StartDate).FirstOrDefaultAsync(ct);
        return date?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
    }

    private async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(key, ct);
            if (stream is null) return null;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            return null; // a missing or unreadable image prints as the placeholder or is left out
        }
    }
}
