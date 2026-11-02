using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;

namespace Saas.Application.Care;

public sealed record CreateTestRequest(Guid? PatientId, Guid? ConsultationId, Guid? AdmissionId, TestKind? Kind, string? Description, Guid? PriceItemId);

public sealed record RecordResultRequest(string? Text);

public sealed record TestRequestDto(
    Guid Id, Guid PatientId, Guid? ConsultationId, Guid? AdmissionId, Guid RequestedBy, string RequestedByName, TestKind Kind, string Description,
    Guid? PriceItemId, TestStatus Status, string? ResultText, bool HasFile, Guid? ResultedBy, string? ResultedByName, DateTimeOffset? ResultedAt,
    DateTimeOffset CreatedAt);

public sealed record TestResultDto(Guid Id, TestStatus Status, string? ResultText, bool HasFile, Guid? ResultedBy, DateTimeOffset? ResultedAt);

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
/// logged clinical read (area file). A result tells the patient's linked portal users with the generic sentence only.
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
            await billing.AddLineAsync(billId, itemId, 1, test.Id.ToString(), ct);
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

        // The generic notice goes to the portal users linked to the patient (none until Task 15 links portal accounts); staff are never told.
        await notices.ResultReadyAsync(test.PatientId, ct);
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

    /// <summary>Tests as DTOs, newest first, decrypted. No access check: the caller has opened the record.</summary>
    internal static async Task<List<TestRequestDto>> ReadManyAsync(IAppDbContext db, ClinicalText text, IQueryable<TestRequest> query, CancellationToken ct)
    {
        var rows = await query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var names = await ConsultationService.NamesAsync(db, rows.Select(t => t.RequestedBy).Concat(rows.Select(t => t.ResultedBy).OfType<Guid>()), ct);
        return rows.Select(t => ToDto(t, text.Read(t.Description) ?? "", text.Read(t.ResultText), names)).ToList();
    }

    private static TestRequestDto ToDto(TestRequest t, string description, string? result, IReadOnlyDictionary<Guid, string> names) =>
        new(t.Id, t.PatientId, t.ConsultationId, t.AdmissionId, t.RequestedBy, names.GetValueOrDefault(t.RequestedBy, ""), t.Kind, description,
            t.PriceItemId, t.Status, result, t.ResultFileKey is not null, t.ResultedBy,
            t.ResultedBy is { } by ? names.GetValueOrDefault(by) : null, t.ResultedAt, t.CreatedAt);

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
