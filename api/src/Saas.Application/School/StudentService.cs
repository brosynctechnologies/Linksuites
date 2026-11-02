using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Courses;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record StudentGuardianDto(Guid GuardianId, string FullName, string? Phone, string? Email, string Relationship, bool IsPrimary, bool HasAccount);
public sealed record GuardianChildDto(Guid StudentId, string Name, string ClassName);
public sealed record ClassGuardianDto(Guid GuardianId, string FullName, string? Phone, string? Email, bool HasAccount, IReadOnlyList<GuardianChildDto> Children);
/// <summary><c>Guardians</c> is filled on a single student and left null in lists. <c>HasLogin</c> is true when the student has their own login.</summary>
public sealed record StudentDto(
    Guid Id, string AdmissionNo, string FirstName, string? MiddleName, string LastName, string FullName, Gender Gender, DateOnly? DateOfBirth,
    StudentStatus Status, DateOnly? StatusDate, string? StatusReason, DateOnly AdmittedAt, string? PhotoUrl,
    Guid? ClassId, string? ClassName, IReadOnlyList<StudentGuardianDto>? Guardians, bool HasLogin);
public sealed record CreateStudentRequest(
    string AdmissionNo, string FirstName, string? MiddleName, string LastName, Gender Gender, DateOnly? DateOfBirth, Guid ClassId, DateOnly? AdmittedAt);
public sealed record UpdateStudentRequest(
    string AdmissionNo, string FirstName, string? MiddleName, string LastName, Gender Gender, DateOnly? DateOfBirth, DateOnly? AdmittedAt);
public sealed record TransferStudentRequest(Guid ClassId);
public sealed record ChangeStudentStatusRequest(StudentStatus Status, DateOnly? Date, string? Reason);
public sealed record SaveGuardianRequest(string FullName, string? Phone, string? Email, string? Relationship, bool? IsPrimary);
public sealed record StudentPhotoFile(Stream? Content, string? RedirectUrl);

/// <summary>Shrinks an uploaded photo and re-encodes it as WebP. Implemented where the image library lives.</summary>
public interface IStudentPhotoProcessor
{
    /// <summary>Throws <see cref="InvalidDataException"/> when the bytes are not a readable image.</summary>
    byte[] ToWebp(Stream image, int maxSide) => ToWebp(image, maxSide, maxSide);

    /// <summary>Shrinks to fit inside the box (never enlarges), keeping the proportions. Throws <see cref="InvalidDataException"/> when the bytes are not a readable image.</summary>
    byte[] ToWebp(Stream image, int maxWidth, int maxHeight);
}

/// <summary>
/// Short-lived signed links to private school files: student photos, the school logo, report card bundles, expense receipts, announcement
/// attachments, message images and assignment files. Each kind has its own
/// purpose in the signature, so a link for one cannot open another. The signature covers the purpose, the id and the expiry.
/// </summary>
public sealed class SchoolPhotoSigner(MediaOptions options, AppUrls urls, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private const string PhotoPurpose = "school-photo";
    private const string LogoPurpose = "school-logo";
    private const string BundlePurpose = "school-bundle";
    private const string ExpensePurpose = "school-expense-receipt";
    private const string AnnouncementPurpose = "school-announcement-file";
    private const string MessagePurpose = "school-message-image";
    private const string AssignmentPurpose = "school-assignment-file";

    public string Url(Guid studentId) => Link("photos", PhotoPurpose, studentId);
    public string AnnouncementFileUrl(Guid announcementId) => Link("announcement-files", AnnouncementPurpose, announcementId);
    public bool IsAnnouncementFileValid(Guid announcementId, long expires, string signature) => Check(AnnouncementPurpose, announcementId, expires, signature);
    public string MessageImageUrl(Guid messageId) => Link("message-images", MessagePurpose, messageId);
    public bool IsMessageImageValid(Guid messageId, long expires, string signature) => Check(MessagePurpose, messageId, expires, signature);
    public string AssignmentFileUrl(Guid submissionId) => Link("assignment-files", AssignmentPurpose, submissionId);
    public bool IsAssignmentFileValid(Guid submissionId, long expires, string signature) => Check(AssignmentPurpose, submissionId, expires, signature);
    public string ExpenseReceiptUrl(Guid expenseId) => Link("expense-receipts", ExpensePurpose, expenseId);
    public bool IsExpenseReceiptValid(Guid expenseId, long expires, string signature) => Check(ExpensePurpose, expenseId, expires, signature);
    public string LogoUrl(Guid tenantId) => Link("logos", LogoPurpose, tenantId);
    public string BundleUrl(Guid bundleId) => Link("report-cards/bundles", BundlePurpose, bundleId);

    public bool IsValid(Guid studentId, long expires, string signature) => Check(PhotoPurpose, studentId, expires, signature);
    public bool IsLogoValid(Guid tenantId, long expires, string signature) => Check(LogoPurpose, tenantId, expires, signature);
    public bool IsBundleValid(Guid bundleId, long expires, string signature) => Check(BundlePurpose, bundleId, expires, signature);

    private string Link(string path, string purpose, Guid id)
    {
        var expires = clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        return $"{urls.ApiBaseUrl.TrimEnd('/')}/api/school/{path}/{id}/{expires}/{Sign(purpose, id, expires)}";
    }

    private bool Check(string purpose, Guid id, long expires, string signature) =>
        expires >= clock.GetUtcNow().ToUnixTimeSeconds()
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(purpose, id, expires)), Encoding.ASCII.GetBytes(signature));

    private string Sign(string purpose, Guid id, long expires)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.SigningKey), Encoding.UTF8.GetBytes($"{purpose}.{id:N}.{expires}"));
        return Convert.ToBase64String(mac)[..32].Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>
/// Students, their enrolment in the current session and their guardians. Leadership writes; the bursar reads everyone; a teacher reads
/// only the students of classes they teach. Guardian contact details go to the same readers.
/// </summary>
public sealed class StudentService(
    IAppDbContext db, ITenantContext tenant, PlanService plans, SchoolAccess access, IFileStorage storage, IStudentPhotoProcessor photos,
    SchoolPhotoSigner signer, IDbErrors dbErrors, TimeProvider clock)
{
    public const int PhotoMaxSide = 512;
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Phone and email matching ----------

    /// <summary>Nigerian numbers become +234XXXXXXXXXX; other international numbers keep their country code. Null when blank or not a phone number.</summary>
    public static bool TryNormalisePhone(string? raw, out string? normalised)
    {
        normalised = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var s = raw.Trim();
        var plus = s.StartsWith('+');
        var digits = new string(s.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("00")) { digits = digits[2..]; plus = true; }
        if (digits.StartsWith("234"))
        {
            var national = digits[3..];
            if (national.StartsWith('0')) national = national[1..];
            if (national.Length != 10) return false;
            normalised = "+234" + national;
            return true;
        }
        if (plus)
        {
            if (digits.Length is < 8 or > 15 || digits[0] == '0') return false;
            normalised = "+" + digits;
            return true;
        }
        if (digits.Length == 11 && digits[0] == '0') { normalised = "+234" + digits[1..]; return true; }
        if (digits.Length == 10 && digits[0] is '7' or '8' or '9') { normalised = "+234" + digits; return true; }
        return false;
    }

    public static string? NormaliseEmail(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();

    /// <summary>The guardian this phone number (normalised to +234) or, failing that, this email belongs to. Used by the import and by adding guardians.</summary>
    public async Task<Guardian?> GuardianMatchAsync(string? phone, string? email, CancellationToken ct)
    {
        if (TryNormalisePhone(phone, out var p) && p is not null)
        {
            var byPhone = await db.Guardians.Where(g => g.Phone == p).OrderBy(g => g.CreatedAt).FirstOrDefaultAsync(ct);
            if (byPhone is not null) return byPhone;
        }
        var e = NormaliseEmail(email);
        return e is null ? null : await db.Guardians.Where(g => g.Email == e).OrderBy(g => g.CreatedAt).FirstOrDefaultAsync(ct);
    }

    // ---------- Enrolment ----------

    internal async Task<Guid> CurrentSessionIdAsync(CancellationToken ct) =>
        await db.AcademicSessions.Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
            ?? throw BadRequestException.Field("sessionId", "Set a current session first.");

    /// <summary>The student's enrolment in the current session, or null.</summary>
    public async Task<Enrolment?> CurrentEnrolmentAsync(Guid studentId, CancellationToken ct)
    {
        var sessionId = await db.AcademicSessions.Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return sessionId is null ? null : await db.Enrolments.FirstOrDefaultAsync(e => e.StudentId == studentId && e.SessionId == sessionId, ct);
    }

    private async Task<SchoolClass> CurrentSessionClassAsync(Guid classId, CancellationToken ct)
    {
        var sessionId = await CurrentSessionIdAsync(ct);
        return await db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == classId && c.SessionId == sessionId, ct)
            ?? throw BadRequestException.Field("classId", "Choose a class of the current session.");
    }

    // ---------- Reading ----------

    public async Task<IReadOnlyList<StudentDto>> ListAsync(string? q, Guid? classId, string? status, int? skip, int? take, CancellationToken ct)
    {
        StudentStatus? wanted = StudentStatus.Active;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status.Equals("all", StringComparison.OrdinalIgnoreCase)) wanted = null;
            else if (Enum.TryParse<StudentStatus>(status, true, out var parsed)) wanted = parsed;
            else throw BadRequestException.Field("status", "Use Active, Graduated, Withdrawn, Transferred or all.");
        }

        var sessionId = await db.AcademicSessions.Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        var query = db.Students.AsNoTracking().AsQueryable();
        if (wanted is { } w) query = query.Where(s => s.Status == w);

        if (!access.IsLeadership && !access.IsBursar)
        {
            // A teacher sees only the students of the classes they teach.
            var taught = await access.TaughtClassIdsAsync(ct);
            if (classId is { } requested && !taught.Contains(requested)) throw new ForbiddenException("You can only see students of classes you teach.");
            if (taught.Count == 0) return [];
            var list = taught.ToList();
            query = query.Where(s => db.Enrolments.Any(e => e.StudentId == s.Id && e.SessionId == sessionId && list.Contains(e.ClassId)));
        }
        if (classId is { } cid)
            query = query.Where(s => db.Enrolments.Any(e => e.StudentId == s.Id && e.SessionId == sessionId && e.ClassId == cid));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(s => s.AdmissionNo.ToLower().Contains(term) || s.FirstName.ToLower().Contains(term) || s.LastName.ToLower().Contains(term)
                || (s.MiddleName != null && s.MiddleName.ToLower().Contains(term))
                || (s.FirstName + " " + s.LastName).ToLower().Contains(term));
        }

        var students = await query.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ThenBy(s => s.AdmissionNo)
            .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 500, 1, 2000)).ToListAsync(ct);
        var classes = await ClassNamesAsync(students.Select(s => s.Id).ToList(), sessionId, ct);
        return students.Select(s => ToDto(s, classes.TryGetValue(s.Id, out var c) ? c : null, null)).ToList();
    }

    /// <summary>
    /// Guardians of the active students of the current session (of one class when <paramref name="classId"/> is given), optionally only those
    /// with no email (they are invited one by one over WhatsApp), each with their phone and all their current children. Leadership and the bursar.
    /// </summary>
    public async Task<IReadOnlyList<ClassGuardianDto>> GuardiansAsync(bool withoutEmail, Guid? classId, CancellationToken ct)
    {
        var sessionId = await db.AcademicSessions.Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (sessionId is null) return [];
        var inScope = db.Enrolments.Where(e => e.SessionId == sessionId && (classId == null || e.ClassId == classId)
            && db.Students.Any(s => s.Id == e.StudentId && s.Status == StudentStatus.Active)).Select(e => e.StudentId);
        var guardians = await db.Guardians.AsNoTracking()
            .Where(g => (!withoutEmail || g.Email == null) && db.StudentGuardians.Any(l => l.GuardianId == g.Id && inScope.Contains(l.StudentId)))
            .OrderBy(g => g.FullName).Take(2000).ToListAsync(ct);
        if (guardians.Count == 0) return [];

        var guardianIds = guardians.Select(g => g.Id).ToList();
        var children = await (from l in db.StudentGuardians.AsNoTracking()
                              where guardianIds.Contains(l.GuardianId)
                              join s in db.Students.AsNoTracking() on l.StudentId equals s.Id
                              where s.Status == StudentStatus.Active
                              select new { l.GuardianId, s.Id, s.FirstName, s.MiddleName, s.LastName }).ToListAsync(ct);
        var classes = await ClassNamesAsync(children.Select(c => c.Id).Distinct().ToList(), sessionId, ct);
        var byGuardian = children.ToLookup(c => c.GuardianId);
        return guardians.Select(g => new ClassGuardianDto(g.Id, g.FullName, g.Phone, g.Email, g.UserId is not null,
            byGuardian[g.Id].Select(c => new GuardianChildDto(c.Id, ParentAccess.FullName(c.FirstName, c.MiddleName, c.LastName),
                    classes.TryGetValue(c.Id, out var cls) ? cls.ClassName : ""))
                .OrderBy(c => c.Name).ToList())).ToList();
    }

    public async Task<StudentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Student");
        return await DetailAsync(student, true, ct);
    }

    private async Task<StudentDto> DetailAsync(Student student, bool enforceRead, CancellationToken ct)
    {
        var sessionId = await db.AcademicSessions.Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        (Guid ClassId, string ClassName)? enrolled = (await ClassNamesAsync([student.Id], sessionId, ct)).TryGetValue(student.Id, out var found) ? found : null;
        if (enforceRead && !access.IsLeadership && !access.IsBursar
            && (enrolled is null || !await access.TeachesClassAsync(enrolled.Value.ClassId, ct)))
            throw new ForbiddenException("You can only see students of classes you teach.");

        var rows = await db.StudentGuardians.AsNoTracking().Where(l => l.StudentId == student.Id)
            .Join(db.Guardians.AsNoTracking(), l => l.GuardianId, g => g.Id, (l, g) => new { Link = l, Guardian = g })
            .OrderByDescending(x => x.Link.IsPrimary).ThenBy(x => x.Link.CreatedAt).ToListAsync(ct);
        // Only leadership, the bursar and the student's teachers get this far, and all of them may see contact details.
        var guardians = rows.Select(x => new StudentGuardianDto(x.Guardian.Id, x.Guardian.FullName, x.Guardian.Phone, x.Guardian.Email,
            x.Link.Relationship, x.Link.IsPrimary, x.Guardian.UserId is not null)).ToList();
        return ToDto(student, enrolled, guardians);
    }

    private async Task<Dictionary<Guid, (Guid ClassId, string ClassName)>> ClassNamesAsync(List<Guid> studentIds, Guid? sessionId, CancellationToken ct)
    {
        if (studentIds.Count == 0 || sessionId is null) return [];
        var rows = await db.Enrolments.AsNoTracking().Where(e => e.SessionId == sessionId && studentIds.Contains(e.StudentId))
            .Join(db.SchoolClasses.AsNoTracking(), e => e.ClassId, c => c.Id, (e, c) => new { e.StudentId, c.Id, c.Name }).ToListAsync(ct);
        return rows.ToDictionary(r => r.StudentId, r => (r.Id, r.Name));
    }

    private StudentDto ToDto(Student s, (Guid ClassId, string ClassName)? cls, IReadOnlyList<StudentGuardianDto>? guardians) =>
        new(s.Id, s.AdmissionNo, s.FirstName, s.MiddleName, s.LastName,
            string.Join(' ', new[] { s.FirstName, s.MiddleName, s.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))),
            s.Gender, s.DateOfBirth, s.Status, s.StatusDate, s.StatusReason, s.AdmittedAt,
            s.PhotoKey is null ? null : signer.Url(s.Id), cls?.ClassId, cls?.ClassName, guardians, s.UserId is not null);

    // ---------- Writing ----------

    internal sealed record Clean(string AdmissionNo, string FirstName, string? MiddleName, string LastName, DateOnly? DateOfBirth);

    internal Clean Validate(string? admissionNo, string? first, string? middle, string? last, DateOnly? dob)
    {
        var no = (admissionNo ?? "").Trim();
        if (no.Length == 0) throw BadRequestException.Field("admissionNo", "Enter the admission number.");
        if (no.Length > 30) throw BadRequestException.Field("admissionNo", "The admission number can have at most 30 characters.");
        var f = (first ?? "").Trim();
        if (f.Length == 0) throw BadRequestException.Field("firstName", "Enter the first name.");
        var l = (last ?? "").Trim();
        if (l.Length == 0) throw BadRequestException.Field("lastName", "Enter the last name.");
        var m = string.IsNullOrWhiteSpace(middle) ? null : middle.Trim();
        if (f.Length > 60 || l.Length > 60 || m is { Length: > 60 }) throw BadRequestException.Field("firstName", "Names can have at most 60 characters.");
        if (dob is { } d && d > SchoolCalendar.Today(clock.GetUtcNow())) throw BadRequestException.Field("dateOfBirth", "The date of birth can't be in the future.");
        return new Clean(no, f, m, l, dob);
    }

    private async Task EnsureAdmissionFreeAsync(Guid? ignoreId, string admissionNo, CancellationToken ct)
    {
        var lower = admissionNo.ToLower();
        if (await db.Students.AnyAsync(s => s.Id != ignoreId && s.AdmissionNo.ToLower() == lower, ct))
            throw DuplicateAdmission();
    }

    private static ConflictException DuplicateAdmission() =>
        new("Another student has that admission number. Enter a different admission number.", "duplicate_admission_no");

    private async Task EnsureRoomForActiveAsync(CancellationToken ct) =>
        await plans.EnsureCanAddAsync(LimitKeys.Students, await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct), "Students", ct);

    public async Task<StudentDto> CreateAsync(CreateStudentRequest r, CancellationToken ct)
    {
        var c = Validate(r.AdmissionNo, r.FirstName, r.MiddleName, r.LastName, r.DateOfBirth);
        var cls = await CurrentSessionClassAsync(r.ClassId, ct);
        await EnsureAdmissionFreeAsync(null, c.AdmissionNo, ct);
        await EnsureRoomForActiveAsync(ct);

        var tenantId = TenantId;
        var student = new Student
        {
            TenantId = tenantId, AdmissionNo = c.AdmissionNo, FirstName = c.FirstName, MiddleName = c.MiddleName, LastName = c.LastName,
            Gender = r.Gender, DateOfBirth = c.DateOfBirth, AdmittedAt = r.AdmittedAt ?? SchoolCalendar.Today(clock.GetUtcNow()),
        };
        db.Students.Add(student);
        db.Enrolments.Add(new Enrolment { TenantId = tenantId, StudentId = student.Id, SessionId = cls.SessionId, ClassId = cls.Id });
        await SaveAsync(ct);
        return await DetailAsync(student, false, ct);
    }

    public async Task<StudentDto> UpdateAsync(Guid id, UpdateStudentRequest r, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Student");
        var c = Validate(r.AdmissionNo, r.FirstName, r.MiddleName, r.LastName, r.DateOfBirth);
        await EnsureAdmissionFreeAsync(id, c.AdmissionNo, ct);
        student.AdmissionNo = c.AdmissionNo;
        if (student.UserId is { } loginId && await db.Users.FirstOrDefaultAsync(u => u.Id == loginId, ct) is { } login)
        {
            // The student's login address follows the admission number, in the same save.
            var email = StudentLogins.EmailFor(c.AdmissionNo, await db.SchoolProfiles.Select(p => p.Slug).FirstAsync(ct));
            if (!string.Equals(login.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                var normalised = email.ToUpperInvariant();
                if (await db.Users.AnyAsync(u => u.Id != loginId && u.NormalizedEmail == normalised, ct))
                    throw new ConflictException("Another account already uses that admission number for sign-in. Enter a different admission number.", "student_login_taken");
                login.Email = email;
                login.UserName = email;
                login.NormalizedEmail = normalised;
                login.NormalizedUserName = normalised;
            }
        }
        student.FirstName = c.FirstName;
        student.MiddleName = c.MiddleName;
        student.LastName = c.LastName;
        student.Gender = r.Gender;
        student.DateOfBirth = c.DateOfBirth;
        if (r.AdmittedAt is { } admitted) student.AdmittedAt = admitted;
        await SaveAsync(ct);
        return await DetailAsync(student, false, ct);
    }

    /// <summary>
    /// True when anything beyond the single current-session enrolment refers to the student. Deleting is only for mistakes.
    /// Later tasks (attendance, scores, results, invoices, payments, messages) must add their tables here.
    /// </summary>
    public async Task<bool> HasHistoryAsync(Guid studentId, CancellationToken ct)
    {
        var sessionId = await db.AcademicSessions.Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return await db.Enrolments.AnyAsync(e => e.StudentId == studentId && e.SessionId != sessionId, ct)
            || await db.Enrolments.Where(e => e.StudentId == studentId).Join(db.AttendanceMarks, e => e.Id, m => m.EnrolmentId, (e, m) => m.Id).AnyAsync(ct)
            || await db.Enrolments.Where(e => e.StudentId == studentId).Join(db.Scores, e => e.Id, s => s.EnrolmentId, (e, s) => s.Id).AnyAsync(ct)
            || await db.Enrolments.Where(e => e.StudentId == studentId).Join(db.StudentInvoices, e => e.Id, i => i.EnrolmentId, (e, i) => i.Id).AnyAsync(ct)
            // Money received (or a charge that didn't match) is a record the school must keep.
            || await db.Enrolments.Where(e => e.StudentId == studentId).Join(db.StudentInvoices, e => e.Id, i => i.EnrolmentId, (e, i) => i.Id)
                .Join(db.FeePayments.Where(p => p.Status != FeePaymentStatus.Pending), i => i, p => p.InvoiceId, (i, p) => p.Id).AnyAsync(ct)
            // A computed row alone is not history (results are derived from scores), but published results must not be deleted.
            || await db.Enrolments.Where(e => e.StudentId == studentId).Join(db.TermResults.Where(r => r.PublishedAt != null), e => e.Id, r => r.EnrolmentId, (e, r) => r.Id).AnyAsync(ct)
            || await db.MessageThreads.AnyAsync(t => t.StudentId == studentId, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Student");
        if (await HasHistoryAsync(id, ct))
            throw new ConflictException("This student has records. Withdraw or transfer them instead of deleting.", "student_has_history");
        var guardianIds = await db.StudentGuardians.Where(l => l.StudentId == id).Select(l => l.GuardianId).ToListAsync(ct);
        var photoKey = student.PhotoKey;
        db.Students.Remove(student); // enrolments and guardian links go with the student
        await db.SaveChangesAsync(ct);
        if (student.UserId is { } loginId)
        {
            // The login goes with the student, so the admission number can be used again.
            await db.RefreshTokens.Where(t => t.UserId == loginId).ExecuteDeleteAsync(ct);
            await db.Users.Where(u => u.Id == loginId).ExecuteDeleteAsync(ct);
        }
        await RemoveOrphanGuardiansAsync(guardianIds, ct);
        if (photoKey is not null) await storage.DeleteAsync(photoKey, ct);
    }

    public async Task<StudentDto> TransferAsync(Guid id, TransferStudentRequest r, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Student");
        if (student.Status != StudentStatus.Active)
            throw new ConflictException("Only active students can move class. Set the student back to active first.", "student_not_active");
        var cls = await CurrentSessionClassAsync(r.ClassId, ct);
        var enrolment = await db.Enrolments.FirstOrDefaultAsync(e => e.StudentId == id && e.SessionId == cls.SessionId, ct);
        if (enrolment is null)
            db.Enrolments.Add(new Enrolment { TenantId = TenantId, StudentId = id, SessionId = cls.SessionId, ClassId = cls.Id });
        else
            enrolment.ClassId = cls.Id;
        await SaveAsync(ct);
        return await DetailAsync(student, false, ct);
    }

    public async Task<StudentDto> ChangeStatusAsync(Guid id, ChangeStudentStatusRequest r, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Student");
        if (!Enum.IsDefined(r.Status)) throw BadRequestException.Field("status", "Choose Active, Graduated, Withdrawn or Transferred.");
        var reason = string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason.Trim();
        if (reason is { Length: > 300 }) throw BadRequestException.Field("reason", "The reason can have at most 300 characters.");
        if (student.Status != StudentStatus.Active && r.Status == StudentStatus.Active) await EnsureRoomForActiveAsync(ct);
        student.Status = r.Status;
        student.StatusDate = r.Date ?? SchoolCalendar.Today(clock.GetUtcNow());
        student.StatusReason = reason;
        await SaveAsync(ct);
        // A student who has left is signed out everywhere; StudentAccess refuses their access token from now on.
        if (r.Status != StudentStatus.Active) await EndStudentSessionsAsync(student, ct);
        return await DetailAsync(student, false, ct);
    }

    internal async Task EndStudentSessionsAsync(Student student, CancellationToken ct)
    {
        if (student.UserId is not { } userId) return;
        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    internal async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex))
        {
            throw DuplicateAdmission();
        }
    }

    // ---------- Photo ----------

    public async Task<StudentDto> UploadPhotoAsync(Guid id, Upload upload, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Student");
        var (buffer, _, _) = await ImageUploads.ReadAsync(upload, ct);
        byte[] webp;
        using (buffer)
        {
            try
            {
                webp = photos.ToWebp(buffer, PhotoMaxSide);
            }
            catch (InvalidDataException)
            {
                throw BadRequestException.Field("file", "We couldn't read this photo. Upload a JPEG, PNG or WebP image.");
            }
        }
        // Under "private/": never served from /uploads. Parents and staff see it only through a signed link.
        var key = $"private/school/{TenantId}/students/{student.Id:N}.webp";
        await using (var stream = new MemoryStream(webp)) await storage.SaveAsync(key, stream, "image/webp", ct);
        student.PhotoKey = key;
        await db.SaveChangesAsync(ct);
        return await DetailAsync(student, false, ct);
    }

    /// <summary>The photo behind a valid signed link, or null. Reads across schools: the signature is the only gate.</summary>
    public async Task<StudentPhotoFile?> PhotoByLinkAsync(Guid studentId, long expires, string signature, CancellationToken ct)
    {
        if (!signer.IsValid(studentId, expires, signature)) throw new ForbiddenException("This photo link has expired.");
        var key = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => s.Id == studentId).Select(s => s.PhotoKey).FirstOrDefaultAsync(ct);
        if (key is null) return null;
        if (storage.TemporaryUrl(key, SchoolPhotoSigner.Lifetime) is { } url) return new StudentPhotoFile(null, url);
        var stream = await storage.OpenReadAsync(key, ct);
        return stream is null ? null : new StudentPhotoFile(stream, null);
    }

    // ---------- Guardians ----------

    private async Task<Student> StudentForWriteAsync(Guid id, CancellationToken ct) =>
        await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Student");

    internal static (string Name, string? Phone, string? Email, string Relationship) CleanGuardian(SaveGuardianRequest r)
    {
        var name = (r.FullName ?? "").Trim();
        if (name.Length == 0) throw BadRequestException.Field("fullName", "Enter the guardian's name.");
        if (name.Length > 120) throw BadRequestException.Field("fullName", "The name can have at most 120 characters.");
        if (!TryNormalisePhone(r.Phone, out var phone))
            throw BadRequestException.Field("phone", "Enter a phone number such as 0803 123 4567 or +234 803 123 4567.");
        var email = NormaliseEmail(r.Email);
        if (email is not null && (email.Length > 200 || !MailAddress.TryCreate(email, out var mail) || mail.Address != email || !email.Split('@')[^1].Contains('.')))
            throw BadRequestException.Field("email", "Enter a valid email address.");
        var relationship = string.IsNullOrWhiteSpace(r.Relationship) ? "Guardian" : r.Relationship.Trim();
        if (relationship.Length > 40) throw BadRequestException.Field("relationship", "The relationship can have at most 40 characters.");
        return (name, phone, email, relationship);
    }

    public async Task<StudentDto> AddGuardianAsync(Guid studentId, SaveGuardianRequest r, CancellationToken ct)
    {
        var student = await StudentForWriteAsync(studentId, ct);
        var (name, phone, email, relationship) = CleanGuardian(r);
        var guardian = await GuardianMatchAsync(phone, email, ct);
        if (guardian is null)
        {
            guardian = new Guardian { TenantId = TenantId, FullName = name, Phone = phone, Email = email };
            db.Guardians.Add(guardian);
        }
        else
        {
            if (await db.StudentGuardians.AnyAsync(l => l.StudentId == studentId && l.GuardianId == guardian.Id, ct))
                throw new ConflictException("This guardian is already linked to the student.", "guardian_already_linked");
            guardian.Phone ??= phone; // a sibling's guardian gains the contact we just learned
            guardian.Email ??= email;
        }
        var hasPrimary = await db.StudentGuardians.AnyAsync(l => l.StudentId == studentId && l.IsPrimary, ct);
        var makePrimary = r.IsPrimary == true || !hasPrimary;
        if (makePrimary) await ClearPrimaryAsync(studentId, ct);
        db.StudentGuardians.Add(new StudentGuardian { TenantId = TenantId, StudentId = studentId, GuardianId = guardian.Id, Relationship = relationship, IsPrimary = makePrimary });
        await db.SaveChangesAsync(ct);
        return await DetailAsync(student, false, ct);
    }

    public async Task<StudentDto> UpdateGuardianAsync(Guid studentId, Guid guardianId, SaveGuardianRequest r, CancellationToken ct)
    {
        var student = await StudentForWriteAsync(studentId, ct);
        var link = await db.StudentGuardians.FirstOrDefaultAsync(l => l.StudentId == studentId && l.GuardianId == guardianId, ct) ?? throw new NotFoundException("Guardian");
        var guardian = await db.Guardians.FirstAsync(g => g.Id == guardianId, ct);
        var (name, phone, email, relationship) = CleanGuardian(r);
        if (phone is not null && await db.Guardians.AnyAsync(g => g.Id != guardianId && g.Phone == phone, ct))
            throw new ConflictException("Another guardian has that phone number. Add that guardian to the student instead.", "duplicate_guardian_phone");
        if (email is not null && await db.Guardians.AnyAsync(g => g.Id != guardianId && g.Email == email, ct))
            throw new ConflictException("Another guardian has that email address. Add that guardian to the student instead.", "duplicate_guardian_email");
        guardian.FullName = name;
        guardian.Phone = phone;
        guardian.Email = email;
        link.Relationship = relationship;
        if (r.IsPrimary == true && !link.IsPrimary)
        {
            await ClearPrimaryAsync(studentId, ct);
            link.IsPrimary = true;
        }
        await db.SaveChangesAsync(ct);
        return await DetailAsync(student, false, ct);
    }

    public async Task<StudentDto> SetPrimaryGuardianAsync(Guid studentId, Guid guardianId, CancellationToken ct)
    {
        var student = await StudentForWriteAsync(studentId, ct);
        var link = await db.StudentGuardians.FirstOrDefaultAsync(l => l.StudentId == studentId && l.GuardianId == guardianId, ct) ?? throw new NotFoundException("Guardian");
        await ClearPrimaryAsync(studentId, ct);
        link.IsPrimary = true;
        await db.SaveChangesAsync(ct);
        return await DetailAsync(student, false, ct);
    }

    public async Task RemoveGuardianAsync(Guid studentId, Guid guardianId, CancellationToken ct)
    {
        _ = await StudentForWriteAsync(studentId, ct);
        var link = await db.StudentGuardians.FirstOrDefaultAsync(l => l.StudentId == studentId && l.GuardianId == guardianId, ct) ?? throw new NotFoundException("Guardian");
        db.StudentGuardians.Remove(link);
        await db.SaveChangesAsync(ct);
        if (link.IsPrimary)
        {
            // Another guardian takes over as primary.
            var next = await db.StudentGuardians.Where(l => l.StudentId == studentId).OrderBy(l => l.CreatedAt).FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.IsPrimary = true;
                await db.SaveChangesAsync(ct);
            }
        }
        await RemoveOrphanGuardiansAsync([guardianId], ct);
    }

    private async Task ClearPrimaryAsync(Guid studentId, CancellationToken ct)
    {
        foreach (var other in await db.StudentGuardians.Where(l => l.StudentId == studentId && l.IsPrimary).ToListAsync(ct)) other.IsPrimary = false;
    }

    /// <summary>A guardian with no children left and no parent account has no reason to exist.</summary>
    private async Task RemoveOrphanGuardiansAsync(List<Guid> guardianIds, CancellationToken ct)
    {
        if (guardianIds.Count == 0) return;
        var orphans = await db.Guardians.Where(g => guardianIds.Contains(g.Id) && g.UserId == null && !db.StudentGuardians.Any(l => l.GuardianId == g.Id)).ToListAsync(ct);
        if (orphans.Count == 0) return;
        db.Guardians.RemoveRange(orphans);
        await db.SaveChangesAsync(ct);
    }
}
