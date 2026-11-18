using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;

namespace Saas.Application.Care;

public sealed record CreateTestRequest(Guid? PatientId, Guid? ConsultationId, Guid? AdmissionId, TestKind? Kind, string? Description, Guid? PriceItemId);

public sealed record RecordResultRequest(string? Text);

public sealed record TestRequestDto(
    Guid Id, Guid PatientId, Guid? ConsultationId, Guid? AdmissionId, Guid RequestedBy, string RequestedByName, TestKind Kind, string Description,
    Guid? PriceItemId, TestStatus Status, string? ResultText, bool HasFile, Guid? ResultedBy, string? ResultedByName, DateTimeOffset? ResultedAt,
    DateTimeOffset CreatedAt, Guid? ReviewedBy = null, string? ReviewedByName = null, DateTimeOffset? ReviewedAt = null,
    DateTimeOffset? AmendedAt = null, IReadOnlyList<TestSupersededDto>? Superseded = null);

/// <summary>
/// Wave 1: an earlier result of the test that was entered in error, for clinical staff only: what it said, who recorded and reviewed it, and
/// who marked it entered in error, when and why. Newest first on the test.
/// </summary>
public sealed record TestSupersededDto(
    Guid Id, string? ResultText, bool HasFile, Guid? ResultedBy, string? ResultedByName, DateTimeOffset? ResultedAt, Guid? ReviewedBy,
    string? ReviewedByName, DateTimeOffset? ReviewedAt, Guid SupersededBy, string? SupersededByName, DateTimeOffset SupersededAt, string Reason);

/// <summary>
/// Wave 1: a corrected result. <c>Reason</c> (required) says why the recorded one was wrong; <c>KeepFile</c> keeps the current result file
/// on the corrected result when no new file is attached (otherwise the corrected result has only what is sent).
/// </summary>
public sealed record AmendResultRequest(string? Text, string? Reason, bool? KeepFile);

public sealed record TestResultDto(Guid Id, TestStatus Status, string? ResultText, bool HasFile, Guid? ResultedBy, DateTimeOffset? ResultedAt);

/// <summary>A doctor's review of a result (post-build): who and when. <c>ReviewedBy</c> is null for a result reviewed before reviews existed.</summary>
public sealed record TestReviewDto(Guid Id, Guid? ReviewedBy, string? ReviewedByName, DateTimeOffset ReviewedAt);

/// <summary>
/// A result waiting for a doctor's review: the test's id, kind and dates, the patient's number and name, and where it was ordered. Never the
/// test's description or its result (the doctor opens the record, a logged read, to review it).
/// </summary>
public sealed record AwaitingReviewDto(
    Guid Id, Guid PatientId, string PatientNumber, string PatientName, TestKind Kind, Guid? ConsultationId, Guid? AdmissionId, string? RecordedByName,
    DateTimeOffset RecordedAt);

public sealed record TestFileLinkDto(string Url, DateTimeOffset ExpiresAt, string ContentType);

public sealed record CareFile(Stream? Content, string? RedirectUrl, string ContentType);

/// <summary>
/// Who hears a patient notice: the portal users linked to the patient. The app's lookup is <see cref="PatientLinkRecipients"/> (Task 15:
/// active <see cref="Saas.Domain.Care.PatientLink"/> rows, none for a deceased patient); tests may swap in their own.
/// </summary>
public interface ICarePortalRecipients
{
    Task<IReadOnlyList<Guid>> ForPatientAsync(Guid patientId, CancellationToken ct);
}

/// <summary>
/// Short-lived signed links to private clinical files (result files). The signature covers the purpose, the file's row and the expiry,
/// and sits in the path (<c>/api/care/result-files/{id}/{expires}/{signature}</c>). Issued only after a logged clinical read.
/// </summary>
public sealed class CareFileSigner(Saas.Application.Courses.MediaOptions options, AppUrls urls, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private const string ResultPurpose = "care-result-file";

    public (string Url, DateTimeOffset ExpiresAt) ResultFileUrl(Guid testId)
    {
        var expiresAt = clock.GetUtcNow().Add(Lifetime);
        var expires = expiresAt.ToUnixTimeSeconds();
        return ($"{urls.ApiBaseUrl.TrimEnd('/')}/api/care/result-files/{testId}/{expires}/{Sign(ResultPurpose, testId, expires)}",
            DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    /// <summary>What is left of a link expiring at <paramref name="expires"/> (Unix seconds): at least 1 second, at most <see cref="Lifetime"/>.</summary>
    public TimeSpan Remaining(long expires) =>
        TimeSpan.FromSeconds(Math.Clamp(expires - clock.GetUtcNow().ToUnixTimeSeconds(), 1, (long)Lifetime.TotalSeconds));

    public bool IsResultFileValid(Guid testId, long expires, string signature) =>
        expires >= clock.GetUtcNow().ToUnixTimeSeconds()
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(Sign(ResultPurpose, testId, expires)), System.Text.Encoding.ASCII.GetBytes(signature));

    private const string SupersededPurpose = "care-superseded-file";

    /// <summary>Wave 2: the signed link to the file of a corrected (superseded) result, by its amendment row.</summary>
    public (string Url, DateTimeOffset ExpiresAt) SupersededFileUrl(Guid amendmentId)
    {
        var expiresAt = clock.GetUtcNow().Add(Lifetime);
        var expires = expiresAt.ToUnixTimeSeconds();
        return ($"{urls.ApiBaseUrl.TrimEnd('/')}/api/care/superseded-result-files/{amendmentId}/{expires}/{Sign(SupersededPurpose, amendmentId, expires)}",
            DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    public bool IsSupersededFileValid(Guid amendmentId, long expires, string signature) =>
        expires >= clock.GetUtcNow().ToUnixTimeSeconds()
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(Sign(SupersededPurpose, amendmentId, expires)), System.Text.Encoding.ASCII.GetBytes(signature));

    private string Sign(string purpose, Guid id, long expires)
    {
        var mac = System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(options.SigningKey), System.Text.Encoding.UTF8.GetBytes($"{purpose}.{id:N}.{expires}"));
        return Convert.ToBase64String(mac)[..32].Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>
/// Tests: the visit's doctor requests one from the consultation (or, from Task 8, an admission); a test with a price item goes on the
/// visit's bill. Doctors and nurses record the result: text and/or a file (a PDF, or an image re-encoded to WebP), once. The description
/// and result text are stored through <see cref="ClinicalText"/>; the file is private and served by short-lived signed links issued after a
/// logged clinical read (area file). A result waits for a doctor's (or the director's) review: only then does the portal show it and the
/// patient's linked portal users hear the generic sentence (post-build user decision).
/// </summary>
public sealed class TestService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, RecordGuard guard, ClinicalText text, BillingService billing, IFileStorage storage,
    Saas.Application.School.IStudentPhotoProcessor images, CareFileSigner signer, CareNotices notices,
    TimeProvider clock)
{
    public const string DoctorsOnly = "Only doctors can request tests.";
    public const string AlreadyResulted = "This test already has a result.";
    /// <summary>The patient's notice kind (<see cref="CareNotices.ResultReadyFor"/>): never the test, the result, a drug or a diagnosis.</summary>
    public const string ResultReadyKind = CareNotices.ResultReady;
    public const int MaxDescriptionLength = 300;
    public const int MaxResultLength = 10_000;
    public const long MaxFileBytes = 10 * 1024 * 1024;
    private const int ImageMaxSide = 2400;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    public async Task<TestRequestDto> RequestAsync(CreateTestRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly);
        if (r.Kind is not { } kind || !Enum.IsDefined(kind)) throw BadRequestException.Field("kind", "Choose lab, imaging or other.");
        var description = CareOrders.Required("description", r.Description, MaxDescriptionLength, "Write the test you're requesting.");
        var context = await CareOrders.ResolveAsync(db, caller, r.PatientId, r.ConsultationId, r.AdmissionId, clock.GetUtcNow(), ct);
        await CareOrders.EnsurePriceItemAsync(db, r.PriceItemId, "priceItemId", ct);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await CareOrders.LockAdmissionAsync(db, TenantId, context, ct); // an admission test is serialised with discharge, as prescriptions are
        var test = new TestRequest
        {
            TenantId = TenantId, PatientId = context.PatientId, ConsultationId = context.ConsultationId, AdmissionId = context.AdmissionId,
            RequestedBy = caller.UserId, Kind = kind, Description = text.Protect(description), PriceItemId = r.PriceItemId,
        };
        db.TestRequests.Add(test);
        await db.SaveChangesAsync(ct);
        // Tests are charged whether or not the clinic dispenses drugs; the line shows the price item's name.
        if (r.PriceItemId is { } itemId && await CareOrders.BillAsync(billing, context, ct) is { } billId)
        {
            await billing.AddLineAsync(billId, itemId, 1, test.Id.ToString(), ct);
            await CareOrders.BillDueIfVisitDoneAsync(db, notices, context, billId, ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);

        var names = await ConsultationService.NamesAsync(db, [caller.UserId], ct);
        return ToDto(test, description, null, names);
    }

    /// <summary>
    /// Records the result: text and/or a file, by a doctor or a nurse (the medical director too). 400 <c>text</c> when neither is given,
    /// 400 <c>file</c> for anything but a PDF or a JPEG, PNG or WebP image (at most 10 MB), 409 when the test already has a result.
    /// A write, not a record open: the answer echoes only the result just recorded, never the request's description.
    /// </summary>
    public async Task<TestResultDto> RecordResultAsync(Guid id, string? resultText, Upload? file, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        var body = CareOrders.Optional("text", resultText, MaxResultLength);
        if (body is null && file is null) throw BadRequestException.Field("text", "Write the result or attach the result file.");
        var stored = file is null ? null : await ReadFileAsync(file, ct);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId, id), ct);
        var test = await db.TestRequests.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Test");
        if (test.Status == TestStatus.Resulted) throw new ConflictException(AlreadyResulted, "test_resulted");

        string? key = null;
        if (stored is { } f)
        {
            // The key holds ids only, never the uploaded file name (it can name the result).
            key = $"private/care/{TenantId}/results/{test.Id:N}-{Guid.NewGuid():N}{f.Extension}";
            await using var stream = new MemoryStream(f.Bytes);
            await storage.SaveAsync(key, stream, f.ContentType, ct);
        }
        var now = clock.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % 10)); // Postgres keeps microseconds
        test.Status = TestStatus.Resulted;
        test.ResultText = body is null ? null : text.Protect(body);
        test.ResultFileKey = key;
        test.ResultFileType = stored?.ContentType;
        test.ResultedBy = caller.UserId;
        test.ResultedAt = now;

        // Post-build (user decision): nobody is told yet. The patient hears "a new result is ready" when a doctor reviews it (ReviewAsync).
        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (key is not null) await DeleteQuietlyAsync(key);
            throw;
        }
        return new TestResultDto(test.Id, test.Status, body, key is not null, caller.UserId, now);
    }

    public const int MaxAmendReasonLength = 500;

    /// <summary>
    /// Wave 1: replaces a result that was entered in error. Doctors and nurses (and the director), as for recording. In order: 403 without
    /// clinical access; 400 <c>reason</c> when no reason is given; 400 <c>text</c> when the corrected result has neither text nor a file (a
    /// kept file counts); 400 <c>file</c> as for recording; 404 for an unknown or another clinic's test; 409 <c>test_not_resulted</c> when
    /// there is no result to correct. The recorded result is kept unchanged as a <see cref="TestResultAmendment"/> (with who replaced it,
    /// when and the reason, stored through <see cref="ClinicalText"/>); the corrected one takes its place, recorded by the caller now, and
    /// waits for a doctor's review again before the portal shows it (the portal then notes it was corrected). Audited with ids only. A write,
    /// not a record open: the answer echoes the corrected result.
    /// </summary>
    public async Task<TestResultDto> AmendResultAsync(Guid id, string? resultText, Upload? file, string? reason, bool keepFile, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        var why = CareOrders.Required("reason", reason, MaxAmendReasonLength, "Say why the recorded result was wrong.");
        var body = CareOrders.Optional("text", resultText, MaxResultLength);
        if (body is null && file is null && !keepFile) throw BadRequestException.Field("text", "Write the corrected result or attach the corrected file.");
        var stored = file is null ? null : await ReadFileAsync(file, ct);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId, id), ct); // the result lock: an amendment never interleaves with a result or a review
        var test = await db.TestRequests.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Test");
        if (test.Status != TestStatus.Resulted) throw new ConflictException(NotResulted, "test_not_resulted");
        var kept = file is null && keepFile ? test.ResultFileKey : null;
        if (body is null && stored is null && kept is null) throw BadRequestException.Field("text", "Write the corrected result or attach the corrected file.");

        string? key = null;
        if (stored is { } f)
        {
            key = $"private/care/{TenantId}/results/{test.Id:N}-{Guid.NewGuid():N}{f.Extension}";
            await using var stream = new MemoryStream(f.Bytes);
            await storage.SaveAsync(key, stream, f.ContentType, ct);
        }
        var now = clock.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % 10)); // Postgres keeps microseconds
        var amendment = new TestResultAmendment
        {
            TenantId = TenantId, TestRequestId = test.Id, PatientId = test.PatientId, ResultText = test.ResultText, ResultFileKey = test.ResultFileKey,
            ResultFileType = test.ResultFileType, ResultedBy = test.ResultedBy, ResultedAt = test.ResultedAt, ReviewedBy = test.ReviewedBy,
            ReviewedAt = test.ReviewedAt, SupersededBy = caller.UserId, SupersededAt = now, Reason = text.Protect(why),
        };
        db.TestResultAmendments.Add(amendment);
        var keptType = kept is null ? null : test.ResultFileType;
        test.ResultText = body is null ? null : text.Protect(body);
        test.ResultFileKey = key ?? kept;
        test.ResultFileType = stored?.ContentType ?? keptType;
        test.ResultedBy = caller.UserId;
        test.ResultedAt = now;
        // The correction is a new result: it waits for a doctor's review before the portal shows it (and the patient is told again then).
        test.ReviewedBy = null;
        test.ReviewedAt = null;
        test.AmendedAt = now;
        db.AuditLogs.Add(new Saas.Domain.Core.AuditLog
        {
            TenantId = TenantId, ActorId = caller.UserId, Action = "care.result_amended", EntityType = nameof(TestRequest), EntityId = test.Id.ToString(),
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { testId = test.Id, patientId = test.PatientId, amendmentId = amendment.Id, amendedBy = caller.UserId }),
            CreatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (key is not null) await DeleteQuietlyAsync(key);
            throw;
        }
        return new TestResultDto(test.Id, test.Status, body, test.ResultFileKey is not null, caller.UserId, now);
    }

    public const string DoctorsReview = "Only doctors can review results.";
    public const string NotResulted = "This test has no result to review yet.";

    /// <summary>
    /// Post-build (user decision): a doctor or the medical director reviews a recorded result, which releases it to the patient's portal and
    /// sends the patient's generic "result ready" notice. 403 "Only doctors and nurses can see clinical notes." without clinical access, 403
    /// "Only doctors can review results." for a nurse; 404 for an unknown or another clinic's test; 409 when there is no result yet.
    /// Idempotent: a reviewed result answers with its first review and nothing is written or sent again. Audited with ids only. A write,
    /// not a record open (the answer carries no description or result).
    /// </summary>
    public async Task<TestReviewDto> ReviewAsync(Guid id, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsReview);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId, id), ct); // the result lock: a review and a second review (or a result) never interleave
        var test = await db.TestRequests.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Test");
        if (test.Status != TestStatus.Resulted) throw new ConflictException(NotResulted, "test_not_resulted");
        if (test.ReviewedAt is { } already)
            return new TestReviewDto(test.Id, test.ReviewedBy, await ReviewerNameAsync(test.ReviewedBy, ct), already);

        var now = clock.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % 10)); // Postgres keeps microseconds
        test.ReviewedBy = caller.UserId;
        test.ReviewedAt = now;
        db.AuditLogs.Add(new Saas.Domain.Core.AuditLog
        {
            TenantId = TenantId, ActorId = caller.UserId, Action = "care.result_reviewed", EntityType = nameof(TestRequest), EntityId = test.Id.ToString(),
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { testId = test.Id, patientId = test.PatientId, reviewedBy = caller.UserId }),
            CreatedAt = now,
        });
        // The generic notice goes to the portal users linked to the patient, committed with the review; staff are never told.
        await notices.ResultReadyAsync(test.PatientId, ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new TestReviewDto(test.Id, caller.UserId, await ReviewerNameAsync(caller.UserId, ct), now);
    }

    private async Task<string?> ReviewerNameAsync(Guid? userId, CancellationToken ct) =>
        userId is { } u ? (await ConsultationService.NamesAsync(db, [u], ct)).GetValueOrDefault(u) : null;

    /// <summary>
    /// Results waiting for a doctor's review, oldest first (at most 200): doctors and the medical director only (403 as for
    /// <see cref="ReviewAsync"/>). Carries no description or result, so it opens no record and writes no access-log row.
    /// </summary>
    public async Task<IReadOnlyList<AwaitingReviewDto>> AwaitingReviewAsync(CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsReview);
        var rows = await db.TestRequests.AsNoTracking()
            .Where(t => t.Status == TestStatus.Resulted && t.ReviewedAt == null)
            .OrderBy(t => t.ResultedAt).ThenBy(t => t.Id).Take(200)
            .Join(db.Patients, t => t.PatientId, p => p.Id, (t, p) => new
            {
                t.Id, t.PatientId, p.Number, p.FirstName, p.LastName, t.Kind, t.ConsultationId, t.AdmissionId, t.ResultedBy, t.ResultedAt,
            })
            .ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, rows.Select(r => r.ResultedBy).OfType<Guid>(), ct);
        return rows.Select(r => new AwaitingReviewDto(r.Id, r.PatientId, r.Number, $"{r.FirstName} {r.LastName}", r.Kind, r.ConsultationId, r.AdmissionId,
            r.ResultedBy is { } by ? names.GetValueOrDefault(by) : null, r.ResultedAt!.Value)).ToList();
    }

    /// <summary>
    /// A short-lived link to the result file. A clinical read: 403 without clinical access before anything is looked up, 404 for an unknown
    /// or another clinic's test, then <see cref="RecordGuard.OpenAsync"/> (area file), then 404 when the test has no file.
    /// </summary>
    public async Task<TestFileLinkDto> FileLinkAsync(Guid id, string? reason, CancellationToken ct)
    {
        access.EnsureClinical(await access.CallerAsync(ct));
        var patientId = await db.TestRequests.AsNoTracking().Where(t => t.Id == id).Select(t => (Guid?)t.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Test");
        await guard.OpenAsync(patientId, RecordGuard.Areas.File, reason, ct);
        var file = await db.TestRequests.AsNoTracking().Where(t => t.Id == id).Select(t => new { t.ResultFileKey, t.ResultFileType }).FirstAsync(ct);
        if (file.ResultFileKey is null) throw new NotFoundException("Result file");
        var (url, expiresAt) = signer.ResultFileUrl(id);
        return new TestFileLinkDto(url, expiresAt, file.ResultFileType ?? "application/octet-stream");
    }

    /// <summary>
    /// The file behind a valid signed link (403 otherwise), or null when it is gone. Reads across clinics: the signature, issued after a
    /// logged clinical read, is the only gate. Cloud storage answers with a presigned redirect; local storage streams.
    /// </summary>
    public async Task<CareFile?> FileByLinkAsync(Guid id, long expires, string signature, CancellationToken ct)
    {
        if (!signer.IsResultFileValid(id, expires, signature)) throw new ForbiddenException("This link has expired. Open the result again.");
        var row = await db.TestRequests.IgnoreQueryFilters().AsNoTracking().Where(t => t.Id == id)
            .Select(t => new { t.ResultFileKey, t.ResultFileType }).FirstOrDefaultAsync(ct);
        if (row?.ResultFileKey is null) return null;
        var type = row.ResultFileType ?? "application/octet-stream";
        // The presigned redirect lives only for what is left of this link (ruling M1), never a fresh window.
        if (storage.TemporaryUrl(row.ResultFileKey, signer.Remaining(expires)) is { } url) return new CareFile(null, url, type);
        var stream = await storage.OpenReadAsync(row.ResultFileKey, ct);
        return stream is null ? null : new CareFile(stream, null, type);
    }

    /// <summary>
    /// Wave 2: a short-lived link to the file of a corrected (superseded) result, as for the current file: a clinical read (403 without
    /// clinical access before anything is looked up; 404 for an unknown test or an amendment of another test), logged through
    /// <see cref="RecordGuard.OpenAsync"/> (area file), then 404 when that superseded result had no file.
    /// </summary>
    public async Task<TestFileLinkDto> SupersededFileLinkAsync(Guid id, Guid amendmentId, string? reason, CancellationToken ct)
    {
        access.EnsureClinical(await access.CallerAsync(ct));
        var patientId = await db.TestRequests.AsNoTracking().Where(t => t.Id == id).Select(t => (Guid?)t.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Test");
        var file = await db.TestResultAmendments.AsNoTracking().Where(a => a.Id == amendmentId && a.TestRequestId == id)
            .Select(a => new { a.ResultFileKey, a.ResultFileType }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Test");
        await guard.OpenAsync(patientId, RecordGuard.Areas.File, reason, ct);
        if (file.ResultFileKey is null) throw new NotFoundException("Result file");
        var (url, expiresAt) = signer.SupersededFileUrl(amendmentId);
        return new TestFileLinkDto(url, expiresAt, file.ResultFileType ?? "application/octet-stream");
    }

    /// <summary>The superseded file behind a valid signed link (403 otherwise), or null when it is gone. As <see cref="FileByLinkAsync"/>.</summary>
    public async Task<CareFile?> SupersededFileByLinkAsync(Guid amendmentId, long expires, string signature, CancellationToken ct)
    {
        if (!signer.IsSupersededFileValid(amendmentId, expires, signature)) throw new ForbiddenException("This link has expired. Open the result again.");
        var row = await db.TestResultAmendments.IgnoreQueryFilters().AsNoTracking().Where(a => a.Id == amendmentId)
            .Select(a => new { a.ResultFileKey, a.ResultFileType }).FirstOrDefaultAsync(ct);
        if (row?.ResultFileKey is null) return null;
        var type = row.ResultFileType ?? "application/octet-stream";
        if (storage.TemporaryUrl(row.ResultFileKey, signer.Remaining(expires)) is { } url) return new CareFile(null, url, type);
        var stream = await storage.OpenReadAsync(row.ResultFileKey, ct);
        return stream is null ? null : new CareFile(stream, null, type);
    }

    /// <summary>Tests as DTOs, newest first, decrypted. No access check: the caller has opened the record.</summary>
    internal static async Task<List<TestRequestDto>> ReadManyAsync(IAppDbContext db, ClinicalText text, IQueryable<TestRequest> query, CancellationToken ct)
    {
        var rows = await query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).ToListAsync(ct);
        if (rows.Count == 0) return [];
        // Wave 1: each test's results entered in error, newest first.
        var ids = rows.Where(t => t.AmendedAt != null).Select(t => t.Id).ToList();
        List<TestResultAmendment> amendments = ids.Count == 0 ? [] : await db.TestResultAmendments.AsNoTracking().Where(a => ids.Contains(a.TestRequestId))
            .OrderByDescending(a => a.SupersededAt).ThenByDescending(a => a.Id).ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db,
            rows.Select(t => t.RequestedBy).Concat(rows.Select(t => t.ResultedBy).OfType<Guid>()).Concat(rows.Select(t => t.ReviewedBy).OfType<Guid>())
                .Concat(amendments.Select(a => a.SupersededBy)).Concat(amendments.Select(a => a.ResultedBy).OfType<Guid>())
                .Concat(amendments.Select(a => a.ReviewedBy).OfType<Guid>()), ct);
        var byTest = amendments.ToLookup(a => a.TestRequestId);
        return rows.Select(t => ToDto(t, text.Read(t.Description) ?? "", text.Read(t.ResultText), names) with
        {
            Superseded = byTest[t.Id].Select(a => new TestSupersededDto(a.Id, text.Read(a.ResultText), a.ResultFileKey is not null, a.ResultedBy,
                a.ResultedBy is { } by ? names.GetValueOrDefault(by) : null, a.ResultedAt, a.ReviewedBy, a.ReviewedBy is { } rb ? names.GetValueOrDefault(rb) : null,
                a.ReviewedAt, a.SupersededBy, names.GetValueOrDefault(a.SupersededBy), a.SupersededAt, text.Read(a.Reason) ?? "")).ToList(),
        }).ToList();
    }

    private static TestRequestDto ToDto(TestRequest t, string description, string? result, IReadOnlyDictionary<Guid, string> names) =>
        new(t.Id, t.PatientId, t.ConsultationId, t.AdmissionId, t.RequestedBy, names.GetValueOrDefault(t.RequestedBy, ""), t.Kind, description,
            t.PriceItemId, t.Status, result, t.ResultFileKey is not null, t.ResultedBy,
            t.ResultedBy is { } by ? names.GetValueOrDefault(by) : null, t.ResultedAt, t.CreatedAt,
            t.ReviewedBy, t.ReviewedBy is { } rb ? names.GetValueOrDefault(rb) : null, t.ReviewedAt, t.AmendedAt, []);

    /// <summary>Serialises recording one test's result, so two results can't both land.</summary>
    public static long LockKey(Guid tenantId, Guid testId) =>
        BitConverter.ToInt64(testId.ToByteArray(), 8) ^ BitConverter.ToInt64(tenantId.ToByteArray(), 0) ^ 0x0CA7E57;

    private sealed record StoredFile(byte[] Bytes, string ContentType, string Extension);

    /// <summary>A PDF as uploaded, or an image re-encoded to WebP (which drops its metadata). The real type comes from the bytes.</summary>
    private async Task<StoredFile> ReadFileAsync(Upload upload, CancellationToken ct)
    {
        const string tooBig = "Result files must be 10 MB or smaller.";
        if (upload.Length <= 0) throw BadRequestException.Field("file", "Choose the result file.");
        if (upload.Length > MaxFileBytes) throw BadRequestException.Field("file", tooBig);
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxFileBytes) throw BadRequestException.Field("file", tooBig);
        if (buffer.Length >= 5 && buffer.GetBuffer().AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return new StoredFile(buffer.ToArray(), "application/pdf", ".pdf");
        buffer.Position = 0;
        try
        {
            return new StoredFile(images.ToWebp(buffer, ImageMaxSide), "image/webp", ".webp");
        }
        catch (InvalidDataException)
        {
            throw BadRequestException.Field("file", "Attach a PDF or a JPEG, PNG or WebP image.");
        }
    }

    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception)
        {
            // The row wasn't saved, so the orphaned private file is never linked; leave it rather than hide the original error.
        }
    }
}
