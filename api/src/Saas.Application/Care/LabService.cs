using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

// ---------- Catalogue ----------

public sealed record LabRangeInput(PatientSex? Sex, int? MinAgeYears, int? MaxAgeYears, decimal? Low, decimal? High, decimal? CriticalLow, decimal? CriticalHigh);
public sealed record LabAnalyteInput(Guid? Id, string? Name, string? Unit, IReadOnlyList<LabRangeInput>? Ranges);
public sealed record SaveLabTestRequest(string? Name, string? SampleType, Guid? PriceItemId, int? TurnaroundHours, bool? Active, IReadOnlyList<LabAnalyteInput>? Analytes);

public sealed record LabRangeDto(PatientSex? Sex, int? MinAgeYears, int? MaxAgeYears, decimal? Low, decimal? High, decimal? CriticalLow, decimal? CriticalHigh);
public sealed record LabAnalyteDto(Guid Id, string Name, string? Unit, IReadOnlyList<LabRangeDto> Ranges);
/// <summary>A catalogue test: the same for every patient, so not clinical (every member reads the catalogue).</summary>
public sealed record LabTestDto(
    Guid Id, string Name, string SampleType, Guid? PriceItemId, string? PriceItemName, long? PriceMinor, int TurnaroundHours, bool Active,
    IReadOnlyList<LabAnalyteDto> Analytes);

// ---------- Orders ----------

/// <summary>Where a lab order is: waiting for its sample, for its result, for a doctor's review, or done (reviewed).</summary>
public enum LabStage { AwaitingSample, AwaitingResult, AwaitingReview, Done }

/// <summary>
/// One row of the lab worklist (lab roles only): the patient's demographics, the catalogue test's name and sample type, the sample number
/// and times. Never the free-text description of a non-catalogue order, never a result (opening the order is a logged read).
/// </summary>
public sealed record LabWorklistRowDto(
    Guid Id, Guid PatientId, string PatientNumber, string PatientName, PatientSex Sex, int AgeYears, Guid? LabTestId, string? TestName,
    string? SampleType, string? SampleNumber, LabStage Stage, string RequestedByName, DateTimeOffset OrderedAt, DateTimeOffset? CollectedAt,
    DateTimeOffset? ReportedAt, DateTimeOffset? DueBy, bool Overdue);

/// <summary>An analyte of the ordered test with the reference range that applies to this patient (by sex and age).</summary>
public sealed record LabOrderAnalyteDto(Guid Id, string Name, string? Unit, decimal? Low, decimal? High, decimal? CriticalLow, decimal? CriticalHigh, string RangeText);

/// <summary>A recorded value with its flag and the range it was judged against (copied at entry). Superseded values were corrected.</summary>
public sealed record LabValueDto(
    Guid Id, Guid? AnalyteId, string Name, string? Unit, string Value, decimal? NumericValue, LabFlag Flag, decimal? Low, decimal? High,
    string RangeText, DateTimeOffset EnteredAt, bool Superseded);

/// <summary>One lab order as the lab opens it (a logged read, area lab).</summary>
public sealed record LabOrderDto(
    Guid Id, Guid PatientId, string PatientNumber, string PatientName, PatientSex Sex, int AgeYears, string Description, Guid? LabTestId,
    string? TestName, string? SampleType, int? TurnaroundHours, LabStage Stage, TestStatus Status, string? SampleNumber, string? CollectedByName,
    DateTimeOffset? CollectedAt, string RequestedByName, DateTimeOffset OrderedAt, IReadOnlyList<LabOrderAnalyteDto> Analytes,
    IReadOnlyList<LabValueDto> Values, string? ResultText, bool HasFile, string? ResultedByName, DateTimeOffset? ResultedAt, DateTimeOffset? ReviewedAt);

public sealed record LabSampleDto(Guid Id, string SampleNumber, DateTimeOffset CollectedAt, string CollectedByName);

public sealed record LabValueInput(Guid? AnalyteId, string? Value);
public sealed record EnterLabResultsRequest(IReadOnlyList<LabValueInput>? Values, string? Comment);
public sealed record AmendLabResultsRequest(IReadOnlyList<LabValueInput>? Values, string? Comment, string? Reason);

/// <summary>What result entry echoes: the values as flagged, the summary text and whether any is critical.</summary>
public sealed record LabResultEntryDto(Guid Id, TestStatus Status, IReadOnlyList<LabValueDto> Values, string ResultText, bool AnyCritical, DateTimeOffset ResultedAt);

/// <summary>What a sample label prints: the clinic, the patient's name, number, sex and age, the sample and the test.</summary>
public sealed record LabLabelData(
    string ClinicName, string PatientName, string PatientNumber, string Sex, int AgeYears, string SampleNumber, string TestName, string? SampleType,
    string CollectedAt);

// ---------- Turnaround ----------

/// <summary>
/// Turnaround per catalogue test (orders placed in the period): counts at each step, average minutes between steps, and how many were
/// reported within the test's target (from order to first result) or are late (reported late, or still waiting past the target).
/// </summary>
public sealed record LabTurnaroundRowDto(
    Guid? LabTestId, string Name, int TargetHours, int Ordered, int Collected, int Reported, int Reviewed, double? AvgOrderToCollectMinutes,
    double? AvgCollectToReportMinutes, double? AvgReportToReviewMinutes, double? AvgOrderToReportMinutes, int WithinTarget, int Late);
public sealed record LabTurnaroundDto(DateOnly From, DateOnly To, IReadOnlyList<LabTurnaroundRowDto> Rows, LabTurnaroundRowDto Total);

/// <summary>
/// Wave 4 (CareLink lab, module 4c item 1), on top of the tests and results flow (<see cref="TestService"/>): a lab catalogue per clinic
/// (tests with analytes, units and reference ranges by sex and age band, a turnaround target and a price item); sample collection with a
/// numbered, printable label; structured result entry against the ranges with automatic abnormal and critical flags (critical tells the
/// ordering doctor, as critical vitals do); turnaround tracked from order to collection, result and review; and the lab worklist.
/// The structured result also becomes the test's result text, so the record, the doctor's review (which still releases it to the portal)
/// and amendments work as for any result. Lab roles: clinical staff and lab scientists (<see cref="CareCaller.CanWorkLab"/>); opening an
/// order is a department read logged through <see cref="RecordGuard.OpenOrderAsync"/>.
/// </summary>
public sealed class LabService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, RecordGuard guard, ClinicalText text, CareNotices notices, ICarePdf pdf,
    ICurrentUser user, TimeProvider clock)
{
    public const string NameTaken = "There is already a lab test with that name.";
    public const string NotCatalogue = "This test isn't on the lab catalogue. Record its result as text or a file.";
    public const string NotLab = "This isn't a lab test.";
    public const string NotCollected = "Collect the sample first.";
    public const string AlreadyCollected = "This sample was already collected.";
    public const int MaxAnalytes = 40;
    public const int MaxRanges = 12;
    public const int MaxTextValue = 200;
    public const int MaxComment = 1000;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Catalogue ----------

    /// <summary>The catalogue, by name. Every member reads it (no patient data); <paramref name="active"/> filters.</summary>
    public async Task<IReadOnlyList<LabTestDto>> ListTestsAsync(bool? active, CancellationToken ct)
    {
        await access.CallerAsync(ct);
        var query = db.LabTests.AsNoTracking().Include(t => t.Analytes).AsQueryable();
        if (active is { } a) query = query.Where(t => t.Active == a);
        var rows = await query.OrderBy(t => t.Name).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public Task<LabTestDto> CreateTestAsync(SaveLabTestRequest r, CancellationToken ct) => SaveTestAsync(null, r, ct);

    public Task<LabTestDto> UpdateTestAsync(Guid id, SaveLabTestRequest r, CancellationToken ct) => SaveTestAsync(id, r, ct);

    /// <summary>
    /// Leadership and lab scientists (403 otherwise). 400 per field; 409 <c>lab_test_exists</c> for a name in use. An update replaces the
    /// analytes: those sent with their id are kept (results already recorded copied their name, unit and range, so nothing changes on them).
    /// </summary>
    private async Task<LabTestDto> SaveTestAsync(Guid? id, SaveLabTestRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (!caller.CanEditLabCatalogue) throw new ForbiddenException("Only lab scientists and the clinic's leadership can change the lab catalogue.");
        var name = CareOrders.Required("name", r.Name, 120, "Name the test.");
        var sampleType = CareOrders.Required("sampleType", r.SampleType, 60, "Say what sample the test needs.");
        var hours = r.TurnaroundHours ?? 24;
        if (hours is < 1 or > 720) throw BadRequestException.Field("turnaroundHours", "Enter a turnaround target from 1 to 720 hours.");
        await CareOrders.EnsurePriceItemAsync(db, r.PriceItemId, "priceItemId", ct);
        var analytes = ValidateAnalytes(r.Analytes);

        var test = id is { } existing
            ? await db.LabTests.Include(t => t.Analytes).FirstOrDefaultAsync(t => t.Id == existing, ct) ?? throw new NotFoundException("Lab test")
            : null;
        var lower = name.ToLowerInvariant();
        if (await db.LabTests.AnyAsync(t => t.Id != (id ?? Guid.Empty) && t.Name.ToLower() == lower, ct)) throw new ConflictException(NameTaken, "lab_test_exists");

        if (test is null)
        {
            test = new LabTest { TenantId = TenantId, Name = name, SampleType = sampleType };
            db.LabTests.Add(test);
        }
        test.Name = name;
        test.SampleType = sampleType;
        test.PriceItemId = r.PriceItemId;
        test.TurnaroundHours = hours;
        if (r.Active is { } active) test.Active = active;

        var keep = analytes.Where(a => a.Id is not null).Select(a => a.Id!.Value).ToHashSet();
        foreach (var gone in test.Analytes.Where(a => !keep.Contains(a.Id)).ToList())
        {
            test.Analytes.Remove(gone);
            db.LabAnalytes.Remove(gone);
        }
        for (var i = 0; i < analytes.Count; i++)
        {
            var input = analytes[i];
            var row = input.Id is { } aid ? test.Analytes.FirstOrDefault(a => a.Id == aid) : null;
            if (input.Id is not null && row is null) throw BadRequestException.Field($"analytes[{i}].id", "That analyte isn't on this test.");
            if (row is null)
            {
                row = new LabAnalyte { TenantId = TenantId, LabTestId = test.Id, Name = input.Name };
                test.Analytes.Add(row);
            }
            row.Position = i;
            row.Name = input.Name;
            row.Unit = input.Unit;
            row.Ranges = input.Ranges;
        }
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([test], ct)).Single();
    }

    private sealed record ValidAnalyte(Guid? Id, string Name, string? Unit, List<LabRange> Ranges);

    private static List<ValidAnalyte> ValidateAnalytes(IReadOnlyList<LabAnalyteInput>? input)
    {
        if (input is null || input.Count == 0) throw BadRequestException.Field("analytes", "Add at least one value the test reports.");
        if (input.Count > MaxAnalytes) throw BadRequestException.Field("analytes", $"A test can report at most {MaxAnalytes} values.");
        var result = new List<ValidAnalyte>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < input.Count; i++)
        {
            var a = input[i] ?? throw BadRequestException.Field($"analytes[{i}].name", "Name the value.");
            var name = CareOrders.Required($"analytes[{i}].name", a.Name, 80, "Name the value.");
            if (!names.Add(name)) throw BadRequestException.Field($"analytes[{i}].name", "Each value needs its own name.");
            var unit = CareOrders.Optional($"analytes[{i}].unit", a.Unit, 30);
            var ranges = a.Ranges ?? [];
            if (ranges.Count > MaxRanges) throw BadRequestException.Field($"analytes[{i}].ranges", $"A value can have at most {MaxRanges} ranges.");
            var valid = new List<LabRange>();
            for (var j = 0; j < ranges.Count; j++)
            {
                var r = ranges[j];
                var field = $"analytes[{i}].ranges[{j}]";
                if (r is null) throw BadRequestException.Field(field, "Fill in the range.");
                if (r.Sex is { } sex && !Enum.IsDefined(sex)) throw BadRequestException.Field($"{field}.sex", "Choose female, male or both.");
                if (r.MinAgeYears is < 0 or > 150 || r.MaxAgeYears is < 1 or > 151)
                    throw BadRequestException.Field($"{field}.minAgeYears", "Enter ages from 0 to 150 years.");
                if (r.MinAgeYears is { } min && r.MaxAgeYears is { } max && min >= max)
                    throw BadRequestException.Field($"{field}.maxAgeYears", "The age band must end after it starts.");
                if (r.Low is null && r.High is null && r.CriticalLow is null && r.CriticalHigh is null)
                    throw BadRequestException.Field($"{field}.low", "Enter at least one limit.");
                if (r.Low is { } lo && r.High is { } hi && lo > hi) throw BadRequestException.Field($"{field}.high", "The high limit must not be below the low limit.");
                if (r.CriticalLow is { } cl && (r.Low is { } l1 ? cl > l1 : r.High is { } h1 && cl >= h1))
                    throw BadRequestException.Field($"{field}.criticalLow", "The critical low limit must be at or below the low limit.");
                if (r.CriticalHigh is { } ch && (r.High is { } h2 ? ch < h2 : r.Low is { } l2 && ch <= l2))
                    throw BadRequestException.Field($"{field}.criticalHigh", "The critical high limit must be at or above the high limit.");
                valid.Add(new LabRange
                {
                    Sex = r.Sex, MinAgeYears = r.MinAgeYears, MaxAgeYears = r.MaxAgeYears, Low = r.Low, High = r.High, CriticalLow = r.CriticalLow,
                    CriticalHigh = r.CriticalHigh,
                });
            }
            result.Add(new ValidAnalyte(a.Id, name, unit, valid));
        }
        return result;
    }

    private async Task<List<LabTestDto>> ToDtosAsync(IReadOnlyList<LabTest> tests, CancellationToken ct)
    {
        var priceIds = tests.Select(t => t.PriceItemId).OfType<Guid>().Distinct().ToList();
        var prices = priceIds.Count == 0 ? [] : await db.PriceItems.AsNoTracking().Where(p => priceIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Name, p.PriceMinor }, ct);
        return tests.Select(t =>
        {
            var price = t.PriceItemId is { } pid ? prices.GetValueOrDefault(pid) : null;
            return new LabTestDto(t.Id, t.Name, t.SampleType, t.PriceItemId, price?.Name, price?.PriceMinor, t.TurnaroundHours, t.Active,
                t.Analytes.OrderBy(a => a.Position).Select(a => new LabAnalyteDto(a.Id, a.Name, a.Unit,
                    a.Ranges.Select(r => new LabRangeDto(r.Sex, r.MinAgeYears, r.MaxAgeYears, r.Low, r.High, r.CriticalLow, r.CriticalHigh)).ToList())).ToList());
        }).ToList();
    }

    // ---------- Worklist ----------

    private static IQueryable<TestRequest> LabOrders(IAppDbContext db) => db.TestRequests.Where(t => t.LabTestId != null || t.Kind == TestKind.Lab);

    /// <summary>
    /// The lab worklist (lab roles; 403 otherwise), oldest first. Without <paramref name="stage"/>: every order not yet reviewed (at most
    /// 300). With Done: orders reviewed in the last 7 days (newest first, at most 200). Carries no description or result, so it opens no
    /// record and writes no access-log row (as the doctors' awaiting-review list).
    /// </summary>
    public async Task<IReadOnlyList<LabWorklistRowDto>> WorklistAsync(LabStage? stage, CancellationToken ct)
    {
        access.EnsureLab(await access.CallerAsync(ct));
        var now = clock.GetUtcNow();
        var query = LabOrders(db).AsNoTracking();
        query = stage switch
        {
            LabStage.AwaitingSample => query.Where(t => t.Status == TestStatus.Requested && t.CollectedAt == null),
            LabStage.AwaitingResult => query.Where(t => t.Status == TestStatus.Requested && t.CollectedAt != null),
            LabStage.AwaitingReview => query.Where(t => t.Status == TestStatus.Resulted && t.ReviewedAt == null),
            LabStage.Done => query.Where(t => t.ReviewedAt != null && t.ReviewedAt >= now.AddDays(-7)),
            _ => query.Where(t => t.ReviewedAt == null),
        };
        query = stage == LabStage.Done ? query.OrderByDescending(t => t.ReviewedAt).ThenBy(t => t.Id).Take(200) : query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Take(300);
        var rows = await query
            .Join(db.Patients, t => t.PatientId, p => p.Id, (t, p) => new
            {
                t.Id, t.PatientId, p.Number, p.FirstName, p.LastName, p.Sex, p.DateOfBirth, t.LabTestId, t.SampleNumber, t.Status, t.CollectedAt,
                t.ResultedAt, t.ReviewedAt, t.RequestedBy, t.CreatedAt,
                Test = db.LabTests.Where(l => l.Id == t.LabTestId).Select(l => new { l.Name, l.SampleType, l.TurnaroundHours }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, rows.Select(r => r.RequestedBy), ct);
        var today = CareCalendar.Today(now);
        return rows.Select(r =>
        {
            var due = r.Test is null ? (DateTimeOffset?)null : r.CreatedAt.AddHours(r.Test.TurnaroundHours);
            var overdue = due is { } d && (r.ResultedAt is { } at ? at > d : now > d);
            return new LabWorklistRowDto(r.Id, r.PatientId, r.Number, $"{r.FirstName} {r.LastName}", r.Sex, LabRules.AgeYears(r.DateOfBirth, today),
                r.LabTestId, r.Test?.Name, r.Test?.SampleType, r.SampleNumber, Stage(r.Status, r.CollectedAt, r.ReviewedAt),
                names.GetValueOrDefault(r.RequestedBy, ""), r.CreatedAt, r.CollectedAt, r.ResultedAt, due, overdue);
        }).ToList();
    }

    private static LabStage Stage(TestStatus status, DateTimeOffset? collectedAt, DateTimeOffset? reviewedAt) =>
        reviewedAt is not null ? LabStage.Done
        : status == TestStatus.Resulted ? LabStage.AwaitingReview
        : collectedAt is null ? LabStage.AwaitingSample
        : LabStage.AwaitingResult;

    // ---------- One order ----------

    /// <summary>
    /// One lab order, opened by the lab: 403 without a lab role before anything is looked up, 404 for an unknown or another clinic's test
    /// (or one that isn't a lab order), then the department open (area lab, logged). The ranges shown are those for the patient's sex and
    /// age on the day the sample was collected (today before that).
    /// </summary>
    public async Task<LabOrderDto> GetOrderAsync(Guid id, string? reason, CancellationToken ct)
    {
        access.EnsureLab(await access.CallerAsync(ct));
        var patientId = await LabOrders(db).AsNoTracking().Where(t => t.Id == id).Select(t => (Guid?)t.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Lab order");
        await guard.OpenOrderAsync(patientId, RecordGuard.Areas.Lab, reason, ct);

        var test = await db.TestRequests.AsNoTracking().FirstAsync(t => t.Id == id, ct);
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patientId, ct);
        var lab = test.LabTestId is { } labId ? await db.LabTests.AsNoTracking().Include(l => l.Analytes).FirstOrDefaultAsync(l => l.Id == labId, ct) : null;
        var values = await db.LabResultValues.AsNoTracking().Where(v => v.TestRequestId == id)
            .OrderBy(v => v.SupersededAt == null ? 0 : 1).ThenByDescending(v => v.SupersededAt).ThenBy(v => v.Position).ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, new[] { test.RequestedBy }.Concat(new[] { test.CollectedBy, test.ResultedBy }.OfType<Guid>()), ct);
        var age = LabRules.AgeYears(patient.DateOfBirth, CareCalendar.Today(test.CollectedAt ?? clock.GetUtcNow()));
        var analytes = lab?.Analytes.OrderBy(a => a.Position).Select(a =>
        {
            var range = LabRules.Pick(a.Ranges, patient.Sex, age);
            return new LabOrderAnalyteDto(a.Id, a.Name, a.Unit, range?.Low, range?.High, range?.CriticalLow, range?.CriticalHigh,
                LabRules.RangeText(range?.Low, range?.High));
        }).ToList() ?? [];
        return new LabOrderDto(test.Id, patient.Id, patient.Number, $"{patient.FirstName} {patient.LastName}", patient.Sex, age,
            text.Read(test.Description) ?? "", test.LabTestId, lab?.Name, lab?.SampleType, lab?.TurnaroundHours,
            Stage(test.Status, test.CollectedAt, test.ReviewedAt), test.Status, test.SampleNumber,
            test.CollectedBy is { } cb ? names.GetValueOrDefault(cb) : null, test.CollectedAt, names.GetValueOrDefault(test.RequestedBy, ""), test.CreatedAt,
            analytes, values.Select(ToValueDto).ToList(), text.Read(test.ResultText), test.ResultFileKey is not null,
            test.ResultedBy is { } rb ? names.GetValueOrDefault(rb) : null, test.ResultedAt, test.ReviewedAt);
    }

    private LabValueDto ToValueDto(LabResultValue v) =>
        new(v.Id, v.AnalyteId, v.Name, v.Unit, v.NumericValue is { } n ? LabRules.Number(n) : text.Read(v.TextValue) ?? "", v.NumericValue, v.Flag,
            v.Low, v.High, LabRules.RangeText(v.Low, v.High), v.EnteredAt, v.SupersededAt is not null);

    /// <summary>
    /// Records the sample as collected now by the caller and gives it the clinic's next sample number (S{yyyy}-{00000}). Lab roles; 404 as
    /// for opening; 409 <c>not_lab</c>, <c>test_resulted</c> (a result came another way) or <c>sample_collected</c>. A write, not an open.
    /// </summary>
    public async Task<LabSampleDto> CollectAsync(Guid id, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureLab(caller);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(TestService.LockKey(TenantId, id), ct);
        var test = await db.TestRequests.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Lab order");
        if (test.LabTestId is null && test.Kind != TestKind.Lab) throw new ConflictException(NotLab, "not_lab");
        if (test.CollectedAt is not null) throw new ConflictException(AlreadyCollected, "sample_collected");
        if (test.Status == TestStatus.Resulted) throw new ConflictException(TestService.AlreadyResulted, "test_resulted");
        var now = Trim(clock.GetUtcNow());
        var number = await db.NextDocumentNumberAsync(DocumentCounter.CareSample, ct);
        test.SampleNumber = $"S{CareCalendar.Today(now).Year}-{number:00000}";
        test.CollectedBy = caller.UserId;
        test.CollectedAt = now;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        var name = (await ConsultationService.NamesAsync(db, [caller.UserId], ct)).GetValueOrDefault(caller.UserId, "");
        return new LabSampleDto(test.Id, test.SampleNumber, now, name);
    }

    /// <summary>
    /// The sample label as a PDF (70 × 35 mm): lab roles; 404 as for opening; 409 <c>sample_not_collected</c> before collection. A logged
    /// department read (area pdf): the label names the patient and the test.
    /// </summary>
    public async Task<(byte[] Pdf, string FileName)> LabelAsync(Guid id, string? reason, CancellationToken ct)
    {
        access.EnsureLab(await access.CallerAsync(ct));
        var row = await LabOrders(db).AsNoTracking().Where(t => t.Id == id)
            .Select(t => new { t.PatientId, t.SampleNumber, t.CollectedAt, t.LabTestId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Lab order");
        if (row.SampleNumber is null || row.CollectedAt is null) throw new ConflictException(NotCollected, "sample_not_collected");
        await guard.OpenOrderAsync(row.PatientId, RecordGuard.Areas.Pdf, reason, ct);
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == row.PatientId, ct);
        var lab = row.LabTestId is { } labId ? await db.LabTests.AsNoTracking().Where(l => l.Id == labId).Select(l => new { l.Name, l.SampleType }).FirstOrDefaultAsync(ct) : null;
        var clinic = await db.ClinicProfiles.AsNoTracking().Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "";
        var collected = row.CollectedAt.Value.ToOffset(CareCalendar.LagosOffset);
        var data = new LabLabelData(clinic, $"{patient.FirstName} {patient.LastName}", patient.Number, patient.Sex.ToString(),
            LabRules.AgeYears(patient.DateOfBirth, CareCalendar.Today(row.CollectedAt.Value)), row.SampleNumber, lab?.Name ?? "Lab test", lab?.SampleType,
            collected.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture));
        return (await pdf.LabLabelAsync(data, ct), $"label-{row.SampleNumber}.pdf");
    }

    // ---------- Results ----------

    /// <summary>
    /// Structured result entry for a catalogue order: one value per analyte, numbers judged against the patient's range (sex, and age on the
    /// collection day) and flagged; the summary becomes the test's result, which waits for a doctor's review as any result does. A critical
    /// value tells the ordering doctor (in-app, no value), unless they entered it. In order: 403 without a lab role; 400 <c>values</c> /
    /// <c>values[i].…</c>; 404; 409 <c>not_catalogue</c>, <c>test_resulted</c>, <c>sample_not_collected</c>. A write, not an open: the
    /// answer echoes what was entered.
    /// </summary>
    public async Task<LabResultEntryDto> EnterResultsAsync(Guid id, EnterLabResultsRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureLab(caller);
        if (r.Values is null || r.Values.Count == 0) throw BadRequestException.Field("values", "Enter the test's values.");
        var comment = CareOrders.Optional("comment", r.Comment, MaxComment);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(TestService.LockKey(TenantId, id), ct);
        var test = await LabOrders(db).FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Lab order");
        if (test.LabTestId is null) throw new ConflictException(NotCatalogue, "not_catalogue");
        if (test.Status == TestStatus.Resulted) throw new ConflictException(TestService.AlreadyResulted, "test_resulted");
        if (test.CollectedAt is null) throw new ConflictException(NotCollected, "sample_not_collected");

        var now = Trim(clock.GetUtcNow());
        var (values, summary, critical) = await BuildValuesAsync(test, r.Values, comment, caller.UserId, now, ct);
        db.LabResultValues.AddRange(values);
        test.Status = TestStatus.Resulted;
        test.ResultText = text.Protect(summary);
        test.ResultedBy = caller.UserId;
        test.ResultedAt = now;
        test.FirstResultedAt ??= now;
        if (critical && test.RequestedBy != caller.UserId) notices.CriticalResultFor(test.RequestedBy);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new LabResultEntryDto(test.Id, test.Status, values.Select(ToValueDto).ToList(), summary, critical, now);
    }

    /// <summary>
    /// Corrects a structured result entered in error (lab roles). Reason required (400 <c>reason</c>); 404; 409 <c>not_catalogue</c> or
    /// <c>test_not_resulted</c>. The recorded result is kept as a <see cref="TestResultAmendment"/> (as <see cref="TestService.AmendResultAsync"/>
    /// does) and its values marked superseded; the corrected values take their place and wait for a doctor's review again. Audited with ids only.
    /// </summary>
    public async Task<LabResultEntryDto> AmendResultsAsync(Guid id, AmendLabResultsRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureLab(caller);
        var why = CareOrders.Required("reason", r.Reason, TestService.MaxAmendReasonLength, "Say why the recorded result was wrong.");
        if (r.Values is null || r.Values.Count == 0) throw BadRequestException.Field("values", "Enter the test's values.");
        var comment = CareOrders.Optional("comment", r.Comment, MaxComment);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(TestService.LockKey(TenantId, id), ct);
        var test = await LabOrders(db).FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Lab order");
        if (test.LabTestId is null) throw new ConflictException(NotCatalogue, "not_catalogue");
        if (test.Status != TestStatus.Resulted) throw new ConflictException(TestService.NotResulted, "test_not_resulted");

        var now = Trim(clock.GetUtcNow());
        var (values, summary, critical) = await BuildValuesAsync(test, r.Values, comment, caller.UserId, now, ct);
        var amendment = new TestResultAmendment
        {
            TenantId = TenantId, TestRequestId = test.Id, PatientId = test.PatientId, ResultText = test.ResultText, ResultFileKey = test.ResultFileKey,
            ResultFileType = test.ResultFileType, ResultedBy = test.ResultedBy, ResultedAt = test.ResultedAt, ReviewedBy = test.ReviewedBy,
            ReviewedAt = test.ReviewedAt, SupersededBy = caller.UserId, SupersededAt = now, Reason = text.Protect(why),
        };
        db.TestResultAmendments.Add(amendment);
        await db.LabResultValues.Where(v => v.TestRequestId == id && v.SupersededAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.SupersededAt, (DateTimeOffset?)now), ct);
        db.LabResultValues.AddRange(values);
        test.ResultText = text.Protect(summary);
        test.ResultedBy = caller.UserId;
        test.ResultedAt = now;
        test.ReviewedBy = null;
        test.ReviewedAt = null;
        test.AmendedAt = now;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = TenantId, ActorId = caller.UserId, Action = "care.result_amended", EntityType = nameof(TestRequest), EntityId = test.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { testId = test.Id, patientId = test.PatientId, amendmentId = amendment.Id, amendedBy = caller.UserId }),
            Ip = user.IpAddress, CreatedAt = now,
        });
        if (critical && test.RequestedBy != caller.UserId) notices.CriticalResultFor(test.RequestedBy);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new LabResultEntryDto(test.Id, test.Status, values.Select(ToValueDto).ToList(), summary, critical, now);
    }

    /// <summary>Validates the values against the catalogue test's analytes (every analyte once) and flags them against the patient's ranges.</summary>
    private async Task<(List<LabResultValue> Values, string Summary, bool Critical)> BuildValuesAsync(
        TestRequest test, IReadOnlyList<LabValueInput> input, string? comment, Guid by, DateTimeOffset now, CancellationToken ct)
    {
        var analytes = await db.LabAnalytes.AsNoTracking().Where(a => a.LabTestId == test.LabTestId).OrderBy(a => a.Position).ToListAsync(ct);
        var patient = await db.Patients.AsNoTracking().Where(p => p.Id == test.PatientId).Select(p => new { p.Sex, p.DateOfBirth }).FirstAsync(ct);
        var age = LabRules.AgeYears(patient.DateOfBirth, CareCalendar.Today(test.CollectedAt ?? now));
        var byId = new Dictionary<Guid, string>();
        for (var i = 0; i < input.Count; i++)
        {
            var v = input[i];
            if (v?.AnalyteId is not { } aid || analytes.All(a => a.Id != aid))
                throw BadRequestException.Field($"values[{i}].analyteId", "That value isn't on this test.");
            if (!byId.TryAdd(aid, CareOrders.Required($"values[{i}].value", v.Value, MaxTextValue, "Enter the value.")))
                throw BadRequestException.Field($"values[{i}].analyteId", "Enter each value once.");
        }
        var missing = analytes.FirstOrDefault(a => !byId.ContainsKey(a.Id));
        if (missing is not null) throw BadRequestException.Field("values", $"Enter a value for {missing.Name}.");

        var rows = new List<LabResultValue>();
        var lines = new List<(string, string?, string, LabFlag, decimal?, decimal?)>();
        foreach (var a in analytes)
        {
            var raw = byId[a.Id];
            var range = LabRules.Pick(a.Ranges, patient.Sex, age);
            var numericRanges = a.Ranges.Count > 0;
            decimal? number = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            if (number is null && numericRanges)
                throw BadRequestException.Field($"values[{input.ToList().FindIndex(x => x.AnalyteId == a.Id)}].value", $"Enter {a.Name} as a number.");
            if (number is { } n && Math.Abs(n) >= 100_000_000m)
                throw BadRequestException.Field($"values[{input.ToList().FindIndex(x => x.AnalyteId == a.Id)}].value", $"{a.Name} is too large.");
            var flag = LabRules.Flag(number, range);
            rows.Add(new LabResultValue
            {
                TenantId = TenantId, TestRequestId = test.Id, PatientId = test.PatientId, AnalyteId = a.Id, Position = a.Position, Name = a.Name,
                Unit = a.Unit, NumericValue = number, TextValue = number is null ? text.Protect(raw) : null, Low = range?.Low, High = range?.High,
                CriticalLow = range?.CriticalLow, CriticalHigh = range?.CriticalHigh, Flag = flag, EnteredBy = by, EnteredAt = now,
            });
            lines.Add((a.Name, a.Unit, number is { } x ? LabRules.Number(x) : raw, flag, range?.Low, range?.High));
        }
        return (rows, LabRules.Summary(lines, comment), rows.Any(v => LabRules.IsCritical(v.Flag)));
    }

    // ---------- Turnaround ----------

    /// <summary>
    /// Turnaround for orders placed from <paramref name="from"/> to <paramref name="to"/> (Lagos days, at most 366; default the last 30
    /// days): lab roles and leadership. Catalogue tests by name, then "Other lab tests" for free-text lab orders (target 24 hours). Times
    /// count from the first result and the first review, so a later correction doesn't move them.
    /// </summary>
    public async Task<LabTurnaroundDto> TurnaroundAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (!caller.CanWorkLab && !caller.IsLeadership) throw new ForbiddenException(CareAccess.LabRefused);
        var now = clock.GetUtcNow();
        var end = to ?? CareCalendar.Today(now);
        var start = from ?? end.AddDays(-29);
        if (start > end) throw BadRequestException.Field("from", "The start date must be on or before the end date.");
        if (end.DayNumber - start.DayNumber > 366) throw BadRequestException.Field("from", "Choose at most a year.");
        var startAt = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();
        var endAt = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();
        var rows = await LabOrders(db).AsNoTracking().Where(t => t.CreatedAt >= startAt && t.CreatedAt < endAt)
            .Select(t => new { t.LabTestId, t.CreatedAt, t.CollectedAt, t.FirstResultedAt, t.ResultedAt, t.FirstReviewedAt, t.ReviewedAt }).ToListAsync(ct);
        var tests = await db.LabTests.AsNoTracking().Select(l => new { l.Id, l.Name, l.TurnaroundHours }).ToDictionaryAsync(l => l.Id, ct);

        const int otherTarget = 24;
        LabTurnaroundRowDto Row(Guid? labTestId, string name, int target, IEnumerable<(DateTimeOffset Ordered, DateTimeOffset? Collected, DateTimeOffset? Reported, DateTimeOffset? Reviewed, int Target)> items)
        {
            var list = items.ToList();
            static double? Avg(IEnumerable<double> xs) { var l = xs.ToList(); return l.Count == 0 ? null : Math.Round(l.Average(), 1); }
            var reported = list.Where(i => i.Reported is not null).ToList();
            var within = reported.Count(i => i.Reported!.Value <= i.Ordered.AddHours(i.Target));
            var late = reported.Count - within + list.Count(i => i.Reported is null && now > i.Ordered.AddHours(i.Target));
            return new LabTurnaroundRowDto(labTestId, name, target, list.Count, list.Count(i => i.Collected is not null), reported.Count,
                list.Count(i => i.Reviewed is not null),
                Avg(list.Where(i => i.Collected is not null).Select(i => (i.Collected!.Value - i.Ordered).TotalMinutes)),
                Avg(list.Where(i => i.Collected is not null && i.Reported is not null).Select(i => (i.Reported!.Value - i.Collected!.Value).TotalMinutes)),
                Avg(list.Where(i => i.Reported is not null && i.Reviewed is not null).Select(i => (i.Reviewed!.Value - i.Reported!.Value).TotalMinutes)),
                Avg(reported.Select(i => (i.Reported!.Value - i.Ordered).TotalMinutes)), within, late);
        }
        var shaped = rows.Select(r =>
        {
            var test = r.LabTestId is { } lid ? tests.GetValueOrDefault(lid) : null;
            return (r.LabTestId, Name: test?.Name, Item: (Ordered: r.CreatedAt, Collected: r.CollectedAt, Reported: r.FirstResultedAt ?? r.ResultedAt,
                Reviewed: r.FirstReviewedAt ?? r.ReviewedAt, Target: test?.TurnaroundHours ?? otherTarget));
        }).ToList();
        var result = shaped.Where(s => s.Name is not null).GroupBy(s => s.LabTestId).OrderBy(g => g.First().Name)
            .Select(g => Row(g.Key, g.First().Name!, g.First().Item.Target, g.Select(s => s.Item))).ToList();
        if (shaped.Any(s => s.Name is null)) result.Add(Row(null, "Other lab tests", otherTarget, shaped.Where(s => s.Name is null).Select(s => s.Item)));
        return new LabTurnaroundDto(start, end, result, Row(null, "All lab tests", 0, shaped.Select(s => s.Item)));
    }

    private static DateTimeOffset Trim(DateTimeOffset now) => now.AddTicks(-(now.Ticks % 10)); // Postgres keeps microseconds
}
