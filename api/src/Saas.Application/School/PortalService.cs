using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record PortalChildDto(Guid StudentId, string Name, string AdmissionNo, string ClassName, string SchoolName);
/// <summary>A class's published figures for the term. Absent while the term is unpublished or held.</summary>
public sealed record PortalResultClassDto(Guid ClassId, string ClassName, decimal Total, decimal Average, int Position, int ClassSize);
public sealed record PortalTermDto(Guid TermId, string TermName, string SessionName, bool IsCurrent, bool Published, bool Held, IReadOnlyList<PortalResultClassDto> Classes);
public sealed record PortalAttendanceDto(Guid TermId, string TermName, Guid ClassId, string ClassName, int Present, int Absent, int Late, int Excused, int Open);
public sealed record PortalFeesDto(long Balance, IReadOnlyList<ParentInvoiceDto> Invoices);
/// <summary><c>Fees</c> is null in the student portal; <c>Attendance</c> and <c>Timetable</c> are null while the child has no class this term.</summary>
public sealed record PortalOverviewDto(
    PortalChildDto Child, IReadOnlyList<PortalTermDto> Results, PortalAttendanceDto? Attendance, PortalFeesDto? Fees, PortalTimetableDto? Timetable);
public sealed record PortalSubjectDto(string Subject, string SubjectCode, decimal Total, string Grade, string? Remark, decimal ClassAverage, int Position);
public sealed record PortalResultEntryDto(
    Guid ClassId, string ClassName, decimal Total, decimal Average, int Position, int ClassSize, int DaysPresent, int? DaysAbsent, int? DaysLate, int? DaysExcused, int DaysOpen,
    string? TeacherRemark, string? PrincipalRemark, IReadOnlyList<PortalSubjectDto> Subjects, AssessmentMode Mode = AssessmentMode.Numeric);
/// <summary>One entry per class with a published row (more than one when the child changed class during the term).</summary>
public sealed record PortalResultsDto(Guid TermId, string TermName, string SessionName, bool Published, IReadOnlyList<PortalResultEntryDto> Entries);

/// <summary>
/// The parent and student portals: overview, a term's results, the report card, attendance and the timetable. Every parent entry point
/// asks <see cref="ParentAccess.RequireChildAsync"/> first (404 for anyone else's child, then the request is scoped to the child's school),
/// every student entry point <see cref="StudentAccess.RequireSelfAsync"/>. After that the reads use the child's own student id and only
/// published result rows of the child's own enrolments; the results hold (<see cref="FinanceService.IsHeldAsync"/>) refuses the figures.
/// </summary>
public sealed class PortalService(
    IAppDbContext db, ParentAccess parents, StudentAccess students, ResultService results, ReportCardService reportCards,
    FinanceService finance, FeePaymentService payments, TimetableService timetables)
{
    public const string ResultsHeld = "Results are held. Please see the school office.";

    // ---------- Parents ----------

    public async Task<PortalOverviewDto> ParentOverviewAsync(Guid studentId, CancellationToken ct)
    {
        var child = await parents.RequireChildAsync(studentId, ct);
        var invoices = await payments.ParentInvoicesAsync(child.StudentId, ct);
        var balance = invoices.Where(i => i.Status != nameof(InvoiceStatus.Cancelled)).Sum(i => i.Balance);
        return await OverviewAsync(child.StudentId, child.SchoolName, new PortalFeesDto(balance, invoices), ct);
    }

    public async Task<PortalResultsDto> ParentResultsAsync(Guid studentId, Guid termId, CancellationToken ct)
    {
        var child = await parents.RequireChildAsync(studentId, ct);
        return await ResultsAsync(child.StudentId, termId, ct);
    }

    public async Task<byte[]> ParentReportCardAsync(Guid studentId, Guid termId, Guid? classId, CancellationToken ct)
    {
        var child = await parents.RequireChildAsync(studentId, ct);
        return await ReportCardAsync(child.StudentId, termId, classId, ct);
    }

    public async Task<PortalAttendanceDto?> ParentAttendanceAsync(Guid studentId, CancellationToken ct)
    {
        var child = await parents.RequireChildAsync(studentId, ct);
        return await AttendanceAsync(child.StudentId, ct);
    }

    public async Task<PortalTimetableDto?> ParentTimetableAsync(Guid studentId, CancellationToken ct)
    {
        var child = await parents.RequireChildAsync(studentId, ct);
        return await timetables.ForClassAsync(await CurrentClassIdAsync(child.StudentId, ct), ct);
    }

    // ---------- Students ----------

    public async Task<PortalOverviewDto> StudentOverviewAsync(CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        return await OverviewAsync(me.StudentId, me.SchoolName, null, ct);
    }

    public async Task<PortalResultsDto> StudentResultsAsync(Guid termId, CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        return await ResultsAsync(me.StudentId, termId, ct);
    }

    public async Task<byte[]> StudentReportCardAsync(Guid termId, Guid? classId, CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        return await ReportCardAsync(me.StudentId, termId, classId, ct);
    }

    public async Task<PortalTimetableDto?> StudentTimetableAsync(CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        return await timetables.ForClassAsync(await CurrentClassIdAsync(me.StudentId, ct), ct);
    }

    // ---------- Shared (the caller has already passed the gate; the tenant filter is on) ----------

    private async Task<PortalOverviewDto> OverviewAsync(Guid studentId, string schoolName, PortalFeesDto? fees, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == studentId, ct);
        var currentClassId = await CurrentClassIdAsync(studentId, ct);
        var className = currentClassId is { } cid ? await db.SchoolClasses.AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstAsync(ct) : "";
        var child = new PortalChildDto(student.Id, ParentAccess.FullName(student.FirstName, student.MiddleName, student.LastName), student.AdmissionNo, className, schoolName);

        // Every term of every session the child was enrolled in, newest first.
        var terms = await (from e in db.Enrolments.AsNoTracking()
                           where e.StudentId == studentId
                           join t in db.Terms.AsNoTracking() on e.SessionId equals t.SessionId
                           join s in db.AcademicSessions.AsNoTracking() on t.SessionId equals s.Id
                           orderby t.StartDate descending, t.Number descending
                           select new { EnrolmentId = e.Id, Term = t, SessionName = s.Name }).ToListAsync(ct);
        var enrolmentIds = terms.Select(t => t.EnrolmentId).Distinct().ToList();
        var published = await (from r in db.TermResults.AsNoTracking()
                               where enrolmentIds.Contains(r.EnrolmentId) && r.PublishedAt != null
                               join c in db.SchoolClasses.AsNoTracking() on r.ClassId equals c.Id
                               orderby c.Name
                               select new { r.EnrolmentId, r.TermId, r.ClassId, ClassName = c.Name, r.Total, r.Average, r.Position, r.ClassSize }).ToListAsync(ct);

        var list = new List<PortalTermDto>(terms.Count);
        foreach (var t in terms)
        {
            var rows = published.Where(r => r.EnrolmentId == t.EnrolmentId && r.TermId == t.Term.Id).ToList();
            var held = await finance.IsHeldAsync(studentId, t.Term.Id, ct);
            var classes = held ? [] : rows.Select(r => new PortalResultClassDto(r.ClassId, r.ClassName, r.Total, r.Average, r.Position, r.ClassSize)).ToList();
            list.Add(new PortalTermDto(t.Term.Id, t.Term.Name, t.SessionName, t.Term.IsCurrent, rows.Count > 0, held, classes));
        }

        return new PortalOverviewDto(child, list, await AttendanceAsync(studentId, ct), fees, await timetables.ForClassAsync(currentClassId, ct));
    }

    private async Task<PortalResultsDto> ResultsAsync(Guid studentId, Guid termId, CancellationToken ct)
    {
        var (term, sessionName, enrolmentId) = await TermOfChildAsync(studentId, termId, ct);
        if (await finance.IsHeldAsync(studentId, termId, ct)) throw new ForbiddenException(ResultsHeld);

        var classIds = await db.TermResults.AsNoTracking()
            .Where(r => r.EnrolmentId == enrolmentId && r.TermId == termId && r.PublishedAt != null)
            .Select(r => r.ClassId).Distinct().ToListAsync(ct);
        var entries = new List<PortalResultEntryDto>();
        foreach (var classId in classIds)
        {
            var view = await results.ReadStoredAsync(classId, termId, ct);
            if (view.PublishedAt is null) continue; // unpublished since the lookup above
            // Only the child's own row of the class: never anyone else's.
            if (view.Students.FirstOrDefault(s => s.EnrolmentId == enrolmentId) is not { } mine) continue;
            entries.Add(new PortalResultEntryDto(classId, view.ClassName, mine.Total, mine.Average, mine.Position, mine.ClassSize, mine.DaysPresent, mine.DaysAbsent, mine.DaysLate, mine.DaysExcused, mine.DaysOpen,
                mine.TeacherRemark, mine.PrincipalRemark,
                mine.Subjects.Select(s => new PortalSubjectDto(s.Subject, s.SubjectCode, s.Total, s.Grade, s.Remark, s.ClassAverage, s.Position)).ToList(), mine.Mode));
        }
        return new PortalResultsDto(term.Id, term.Name, sessionName, entries.Count > 0, entries.OrderBy(e => e.ClassName).ToList());
    }

    private async Task<byte[]> ReportCardAsync(Guid studentId, Guid termId, Guid? classId, CancellationToken ct)
    {
        var (_, _, enrolmentId) = await TermOfChildAsync(studentId, termId, ct);
        if (await finance.IsHeldAsync(studentId, termId, ct)) throw new ForbiddenException(ResultsHeld);
        return await reportCards.RenderPublishedAsync(enrolmentId, termId, classId, ct);
    }

    /// <summary>The term (of this school: the tenant filter applies) and the child's enrolment in its session. 404 when either is missing.</summary>
    private async Task<(Term Term, string SessionName, Guid EnrolmentId)> TermOfChildAsync(Guid studentId, Guid termId, CancellationToken ct)
    {
        var row = await (from t in db.Terms.AsNoTracking()
                         where t.Id == termId
                         join e in db.Enrolments.AsNoTracking() on t.SessionId equals e.SessionId
                         where e.StudentId == studentId
                         join s in db.AcademicSessions.AsNoTracking() on t.SessionId equals s.Id
                         select new { Term = t, SessionName = s.Name, EnrolmentId = e.Id }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Term");
        return (row.Term, row.SessionName, row.EnrolmentId);
    }

    /// <summary>The child's class in the current session, or null.</summary>
    private Task<Guid?> CurrentClassIdAsync(Guid studentId, CancellationToken ct) =>
        db.Enrolments.AsNoTracking()
            .Where(e => e.StudentId == studentId && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent))
            .Select(e => (Guid?)e.ClassId).FirstOrDefaultAsync(ct);

    /// <summary>This term's attendance in the child's current class, counted as the results count it. Null without a class this term.</summary>
    private async Task<PortalAttendanceDto?> AttendanceAsync(Guid studentId, CancellationToken ct)
    {
        var current = await (from t in db.Terms.AsNoTracking()
                             where t.IsCurrent
                             join e in db.Enrolments.AsNoTracking() on t.SessionId equals e.SessionId
                             where e.StudentId == studentId
                             join c in db.SchoolClasses.AsNoTracking() on e.ClassId equals c.Id
                             select new { Term = t, EnrolmentId = e.Id, ClassId = c.Id, ClassName = c.Name }).FirstOrDefaultAsync(ct);
        if (current is null) return null;
        var (present, absent, late, excused, open) = await results.TermAttendanceAsync(current.ClassId, current.Term.Id, current.EnrolmentId, ct);
        return new PortalAttendanceDto(current.Term.Id, current.Term.Name, current.ClassId, current.ClassName, present, absent, late, excused, open);
    }
}
