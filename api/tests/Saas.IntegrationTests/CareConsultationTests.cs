using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 6: consultations, diagnoses, signing, addenda and the patient record timeline. A consultation belongs to the visit's
/// doctor; a signed note is final (corrections are addenda with author and time); clinical text is encrypted at rest; the record timeline
/// is clinical only and logged through RecordGuard; diagnoses take an optional code from the built-in list.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareConsultationTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string ClinicalRefused = "Only doctors and nurses can see clinical notes.";
    private const string SignedNote = "This note is signed. Add an addendum instead.";
    private const string AssignedToAnother = "This visit is assigned to another doctor.";
    private const string AuthorOnly = "Only the doctor who wrote this note can change it.";
    private const string DoctorsOnly = "Only doctors can write consultation notes.";
    private const string AddendaAfterSigning = "Addenda are for signed notes. Edit the draft instead.";
    private const string ReasonRequired = "Give a reason to open this patient's record.";
    private const string NoDoctor = "This visit has no doctor yet. Choose one at triage.";
    private const string NoNoteYet = "No note has been written for this visit yet.";
    private const string EmptyNote = "Write the complaint or plan before signing.";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        var body = JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (detail is not null) Assert.Equal(detail, body.GetProperty("detail").GetString());
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private Task<List<RecordAccess>> AccessLogAsync(Guid patientId) =>
        api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId).OrderBy(a => a.At).ToListAsync(Ct));

    private static Task<HttpResponseMessage> SendWithReasonAsync(HttpClient client, HttpMethod method, string path, string? reason, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: ApiFixture.Json);
        if (reason is not null) request.Headers.Add(RecordGuard.ReasonHeader, Uri.EscapeDataString(reason));
        return client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> OpenAsync(HttpClient client, Guid visitId) =>
        client.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct);

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid consultationId, object body) =>
        client.PutAsJsonAsync($"/api/care/consultations/{consultationId}", body, Ct);

    private static Task<HttpResponseMessage> SignAsync(HttpClient client, Guid consultationId) =>
        client.PostAsync($"/api/care/consultations/{consultationId}/sign", null, Ct);

    private static Task<HttpResponseMessage> AddendumAsync(HttpClient client, Guid consultationId, string text, string kind = "consultation") =>
        client.PostAsJsonAsync($"/api/care/notes/{kind}/{consultationId}/addenda", new { text }, Ct);

    private static object Note(string complaint = "Fever and chills for three days") => new
    {
        complaint,
        history = "Travelled to Ogun state last week",
        examination = "Temperature 38.9, pale conjunctivae",
        plan = "Artemether-lumefantrine, review in 3 days",
        diagnoses = new object[]
        {
            new { text = "Uncomplicated falciparum malaria", code = "B50.9", primary = true },
            new { text = "Mild dehydration", code = (string?)null, primary = false },
        },
    };

    private sealed record World(CareKit.Clinic Clinic, HttpClient Doctor, Guid DoctorId, HttpClient Nurse, Guid NurseId, HttpClient Desk, Guid PatientId, Guid VisitId);

    /// <summary>A clinic with a doctor, a nurse and the front desk; a patient checked in and triaged by the nurse for the doctor.</summary>
    private async Task<World> WorldAsync(string name)
    {
        var clinic = await kit.ClinicAsync(name);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = Id(await kit.PatientAsync(clinic, "Ifeoma", "Okafor"));
        var visitId = Id(await kit.CheckInAsync(desk, patientId));
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
        {
            vitals = new { temperatureC = 38.9, systolic = 118, diastolic = 76, pulse = 104, respRate = 18, spO2 = 97 },
            urgency = "Routine", doctorUserId = doctorId,
        }, Ct));
        return new World(clinic, doctor, doctorId, nurse, nurseId, desk, patientId, visitId);
    }

    [Fact]
    public async Task Signed_note_cannot_change()
    {
        var w = await WorldAsync("Signed Note Clinic");

        // The visit's doctor opens the consultation: a draft they author, the visit moves to WithDoctor, the open is logged.
        var before = (await AccessLogAsync(w.PatientId)).Count;
        var opened = await Json(await OpenAsync(w.Doctor, w.VisitId));
        var consultationId = Id(opened);
        Assert.Equal("Draft", opened.GetProperty("status").GetString());
        Assert.Equal(w.DoctorId, opened.GetProperty("doctorUserId").GetGuid());
        Assert.Equal(w.VisitId, opened.GetProperty("visitId").GetGuid());
        Assert.Equal("WithDoctor", (await Json(await w.Desk.GetAsync($"/api/care/visits/{w.VisitId}", Ct))).GetProperty("stage").GetString());
        var log = await AccessLogAsync(w.PatientId);
        Assert.Equal(before + 1, log.Count);
        Assert.Equal("consultation", log[^1].Area);
        Assert.Equal(w.DoctorId, log[^1].StaffUserId);
        Assert.False(log[^1].Emergency);

        // Opening again returns the same consultation (one per visit).
        Assert.Equal(consultationId, Id(await Json(await OpenAsync(w.Doctor, w.VisitId))));

        // The author saves the draft, with a coded and an uncoded diagnosis.
        var saved = await Json(await SaveAsync(w.Doctor, consultationId, Note()));
        Assert.Equal("Fever and chills for three days", saved.GetProperty("complaint").GetString());
        Assert.Equal("Artemether-lumefantrine, review in 3 days", saved.GetProperty("plan").GetString());
        var diagnoses = saved.GetProperty("diagnoses").EnumerateArray().ToList();
        Assert.Equal(2, diagnoses.Count);
        Assert.Contains(diagnoses, d => d.GetProperty("code").GetString() == "B50.9" && d.GetProperty("primary").GetBoolean());

        // Clinical text is encrypted at rest: the raw columns hold no plain text.
        var raw = await api.WithDbAsync(db => db.Database.SqlQuery<string>(
            $"SELECT complaint || history || examination || plan AS \"Value\" FROM consultations WHERE id = {consultationId}").FirstAsync(Ct));
        Assert.DoesNotContain("Fever", raw);
        Assert.DoesNotContain("Ogun", raw);
        Assert.DoesNotContain("pale", raw);
        Assert.DoesNotContain("lumefantrine", raw);
        var rawDiagnoses = await api.WithDbAsync(db => db.Database.SqlQuery<string>(
            $"SELECT string_agg(text, ' ') AS \"Value\" FROM diagnoses WHERE consultation_id = {consultationId}").FirstAsync(Ct));
        Assert.DoesNotContain("falciparum", rawDiagnoses);
        Assert.DoesNotContain("dehydration", rawDiagnoses);

        // Addenda are for signed notes.
        await AssertProblemAsync(await AddendumAsync(w.Nurse, consultationId, "Too early"), HttpStatusCode.Conflict, AddendaAfterSigning);

        // Signing locks it.
        var signed = await Json(await SignAsync(w.Doctor, consultationId));
        Assert.Equal("Signed", signed.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, signed.GetProperty("signedAt").ValueKind);

        // After signing: PUT and a second sign are 409 with the exact sentence, and nothing changes.
        await AssertProblemAsync(await SaveAsync(w.Doctor, consultationId, Note("Changed after signing")), HttpStatusCode.Conflict, SignedNote);
        await AssertProblemAsync(await SignAsync(w.Doctor, consultationId), HttpStatusCode.Conflict, SignedNote);
        // The database still holds the signed text and diagnoses, untouched by the refused PUT.
        var stored = await api.WithDbAsync(db => db.Consultations.IgnoreQueryFilters().FirstAsync(c => c.Id == consultationId, Ct));
        var clinicalText = api.Services.GetRequiredService<ClinicalText>();
        Assert.Equal("Fever and chills for three days", clinicalText.Read(stored.Complaint));
        Assert.Equal("Artemether-lumefantrine, review in 3 days", clinicalText.Read(stored.Plan));
        Assert.Equal(NoteStatus.Signed, stored.Status);
        var storedDiagnoses = await api.WithDbAsync(db => db.Diagnoses.IgnoreQueryFilters().Where(d => d.ConsultationId == consultationId).ToListAsync(Ct));
        Assert.Equal(["Mild dehydration", "Uncomplicated falciparum malaria"], storedDiagnoses.Select(d => clinicalText.Read(d.Text)).OrderBy(t => t).ToList());
        Assert.Contains(storedDiagnoses, d => d.Code == "B50.9" && d.Primary);
        Assert.Equal(consultationId, Id(await Json(await OpenAsync(w.Doctor, w.VisitId)))); // re-opening a signed note reads it

        // Any clinical staff member with access adds an addendum: author and time, text encrypted at rest.
        var addendum = await Json(await AddendumAsync(w.Nurse, consultationId, "Repeat malaria film negative on day 3"));
        Assert.Equal(w.NurseId, addendum.GetProperty("authorUserId").GetGuid());
        Assert.Equal("Nurse Staff", addendum.GetProperty("authorName").GetString());
        Assert.Equal("Consultation", addendum.GetProperty("noteKind").GetString());
        var at = addendum.GetProperty("at").GetDateTimeOffset();
        Assert.True(Math.Abs((DateTimeOffset.UtcNow - at).TotalMinutes) < 5);
        var rawAddendum = await api.WithDbAsync(db => db.Addenda.IgnoreQueryFilters().Where(a => a.NoteId == consultationId).Select(a => a.Text).FirstAsync(Ct));
        Assert.DoesNotContain("film", rawAddendum);
        await AssertProblemAsync(await AddendumAsync(w.Doctor, consultationId, "   "), HttpStatusCode.BadRequest, field: "text");
        await AssertProblemAsync(await AddendumAsync(w.Doctor, consultationId, "x", kind: "letter"), HttpStatusCode.NotFound);

        // Non-clinical staff can't add addenda.
        await AssertProblemAsync(await AddendumAsync(w.Desk, consultationId, "Desk note"), HttpStatusCode.Forbidden, ClinicalRefused);

        // An unrelated doctor needs a reason (emergency access) to add one.
        var (stranger, _) = await kit.StaffAsync(w.Clinic, "Doctor");
        await AssertProblemAsync(await AddendumAsync(stranger, consultationId, "Seen in passing"), HttpStatusCode.Conflict, ReasonRequired);
        await Json(await SendWithReasonAsync(stranger, HttpMethod.Post, $"/api/care/notes/consultation/{consultationId}/addenda",
            "Covering the ward tonight", new { text = "Patient stable overnight" }));
        Assert.True((await AccessLogAsync(w.PatientId))[^1].Emergency);

        // The timeline shows the original note unchanged, with its addenda (author and time), oldest first.
        var record = await Json(await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct));
        var note = Assert.Single(record.GetProperty("consultations").EnumerateArray());
        Assert.Equal("Fever and chills for three days", note.GetProperty("complaint").GetString());
        Assert.Equal("Signed", note.GetProperty("status").GetString());
        var addenda = note.GetProperty("addenda").EnumerateArray().ToList();
        Assert.Equal(2, addenda.Count);
        Assert.Equal("Repeat malaria film negative on day 3", addenda[0].GetProperty("text").GetString());
        Assert.Equal(w.NurseId, addenda[0].GetProperty("authorUserId").GetGuid());
        Assert.Equal("Nurse Staff", addenda[0].GetProperty("authorName").GetString());
        Assert.Equal(at, addenda[0].GetProperty("at").GetDateTimeOffset());
        Assert.Equal("Patient stable overnight", addenda[1].GetProperty("text").GetString());

        // Signing writes an audit row with no clinical text.
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.note_signed").ToListAsync(Ct)));
        Assert.Equal(consultationId.ToString(), audit.EntityId);
        Assert.DoesNotContain("Fever", audit.AfterJson ?? "");
        Assert.DoesNotContain("malaria", audit.AfterJson ?? "", StringComparison.OrdinalIgnoreCase);

        // No audit row of the clinic holds any of the note's clinical text.
        var audits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == w.Clinic.TenantId).ToListAsync(Ct));
        Assert.NotEmpty(audits);
        foreach (var row in audits)
            foreach (var marker in new[] { "Fever", "malaria", "film", "lumefantrine", "Ogun", "conjunctivae", "dehydration", "overnight", "Changed after" })
            {
                Assert.DoesNotContain(marker, row.AfterJson ?? "", StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(marker, row.BeforeJson ?? "", StringComparison.OrdinalIgnoreCase);
            }
    }

    [Fact]
    public async Task Only_author_edits_draft()
    {
        var w = await WorldAsync("Author Clinic");
        var (otherDoctor, otherDoctorId) = await kit.StaffAsync(w.Clinic, "Doctor");
        var (cashier, _) = await kit.StaffAsync(w.Clinic, "Cashier");

        // Only the visit's doctor or the medical director opens the consultation. Another doctor is refused, and gains no care relationship.
        await AssertProblemAsync(await OpenAsync(otherDoctor, w.VisitId), HttpStatusCode.Forbidden, AssignedToAnother);
        await AssertProblemAsync(await OpenAsync(w.Nurse, w.VisitId), HttpStatusCode.Forbidden, DoctorsOnly);
        await AssertProblemAsync(await OpenAsync(w.Desk, w.VisitId), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await OpenAsync(cashier, w.VisitId), HttpStatusCode.Forbidden, ClinicalRefused);
        Assert.False(await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().AnyAsync(a => a.StaffUserId == otherDoctorId, Ct)));
        Assert.False(await api.WithDbAsync(db => db.Consultations.IgnoreQueryFilters().AnyAsync(c => c.VisitId == w.VisitId, Ct)));
        Assert.Equal("WaitingDoctor", (await Json(await w.Desk.GetAsync($"/api/care/visits/{w.VisitId}", Ct))).GetProperty("stage").GetString());

        var consultationId = Id(await Json(await OpenAsync(w.Doctor, w.VisitId)));

        // The director opens the same consultation (one per visit) but doesn't author it.
        Assert.Equal(consultationId, Id(await Json(await OpenAsync(w.Clinic.Owner, w.VisitId))));

        // Only the author saves or signs the draft.
        await AssertProblemAsync(await SaveAsync(w.Clinic.Owner, consultationId, Note()), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await SaveAsync(otherDoctor, consultationId, Note()), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await SaveAsync(w.Nurse, consultationId, Note()), HttpStatusCode.Forbidden, DoctorsOnly); // Task 16: the role is checked before the note
        await AssertProblemAsync(await SaveAsync(w.Desk, consultationId, Note()), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await SignAsync(w.Clinic.Owner, consultationId), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await SignAsync(otherDoctor, consultationId), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await SignAsync(w.Desk, consultationId), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await SignAsync(cashier, consultationId), HttpStatusCode.Forbidden, ClinicalRefused);

        // An empty note can't be signed.
        await AssertProblemAsync(await SignAsync(w.Doctor, consultationId), HttpStatusCode.Conflict, EmptyNote);
        await Json(await SaveAsync(w.Doctor, consultationId, Note()));

        // Validation: an unknown code, two primaries, and an empty diagnosis are field errors.
        await AssertProblemAsync(await SaveAsync(w.Doctor, consultationId, new { complaint = "x", diagnoses = new[] { new { text = "Something", code = "ZZ9.9", primary = true } } }),
            HttpStatusCode.BadRequest, field: "diagnoses[0].code");
        await AssertProblemAsync(await SaveAsync(w.Doctor, consultationId, new
        {
            complaint = "x",
            diagnoses = new[] { new { text = "One", code = (string?)null, primary = true }, new { text = "Two", code = (string?)null, primary = true } },
        }), HttpStatusCode.BadRequest, field: "diagnoses");
        await AssertProblemAsync(await SaveAsync(w.Doctor, consultationId, new { complaint = "x", diagnoses = new[] { new { text = " ", code = (string?)null, primary = false } } }),
            HttpStatusCode.BadRequest, field: "diagnoses[0].text");
        await AssertProblemAsync(await SaveAsync(w.Doctor, consultationId, new { complaint = new string('x', 10_001) }), HttpStatusCode.BadRequest, field: "complaint");

        // A code alone takes the list's name; the full replace keeps only the new diagnoses.
        var coded = await Json(await SaveAsync(w.Doctor, consultationId, new { complaint = "Fever", diagnoses = new[] { new { text = (string?)null, code = "b54", primary = true } } }));
        var only = Assert.Single(coded.GetProperty("diagnoses").EnumerateArray());
        Assert.Equal("B54", only.GetProperty("code").GetString());
        Assert.Equal("Malaria, unspecified", only.GetProperty("text").GetString());
        Assert.Equal(1, await api.WithDbAsync(db => db.Diagnoses.IgnoreQueryFilters().CountAsync(d => d.ConsultationId == consultationId, Ct)));

        // A doctor of an appointment opens without triage: the visit's doctor gets a VisitDoctor relationship, and the visit moves on.
        var patientId = Id(await kit.PatientAsync(w.Clinic, "Tobi", "Lawal"));
        var visitId = Id(await kit.CheckInAsync(w.Desk, patientId));
        await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().Where(v => v.Id == visitId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.DoctorUserId, otherDoctorId), Ct));
        await AssertProblemAsync(await OpenAsync(w.Doctor, visitId), HttpStatusCode.Forbidden, AssignedToAnother);
        await Json(await OpenAsync(otherDoctor, visitId));
        Assert.True(await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().AnyAsync(a =>
            a.StaffUserId == otherDoctorId && a.VisitId == visitId && a.Kind == CareAssignmentKind.VisitDoctor && a.EndedAt == null, Ct)));
        Assert.Equal("WithDoctor", (await Json(await w.Desk.GetAsync($"/api/care/visits/{visitId}", Ct))).GetProperty("stage").GetString());
        await Json(await OpenAsync(otherDoctor, visitId)); // the relationship is kept, not duplicated
        Assert.Equal(1, await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().CountAsync(a => a.StaffUserId == otherDoctorId && a.VisitId == visitId, Ct)));

        // A visit with no doctor yet: a doctor can't claim it (that would make a care relationship out of nothing); the director can,
        // and becomes its doctor.
        var walkInId = Id(await kit.PatientAsync(w.Clinic, "Sade", "Bello"));
        var walkIn = Id(await kit.CheckInAsync(w.Desk, walkInId));
        await AssertProblemAsync(await OpenAsync(w.Doctor, walkIn), HttpStatusCode.Conflict, NoDoctor);
        Assert.False(await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().AnyAsync(a => a.VisitId == walkIn, Ct)));
        await Json(await OpenAsync(w.Clinic.Owner, walkIn));
        Assert.NotEqual(JsonValueKind.Null, (await Json(await w.Desk.GetAsync($"/api/care/visits/{walkIn}", Ct))).GetProperty("doctorUserId").ValueKind);

        // A closed visit without a consultation can't start one.
        var closedPatient = Id(await kit.PatientAsync(w.Clinic, "Done", "Patient"));
        var closed = Id(await kit.CheckInAsync(w.Desk, closedPatient));
        await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().Where(v => v.Id == closed)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.DoctorUserId, w.DoctorId), Ct));
        await Json(await w.Desk.PostAsJsonAsync($"/api/care/visits/{closed}/stage", new { stage = "Done" }, Ct));
        await AssertProblemAsync(await OpenAsync(w.Doctor, closed), HttpStatusCode.Conflict, "This visit is closed.");
    }

    [Fact]
    public async Task Record_timeline_is_logged_and_clinical_only()
    {
        var w = await WorldAsync("Timeline Clinic");
        var (cashier, _) = await kit.StaffAsync(w.Clinic, "Cashier");
        var (admin, _) = await kit.StaffAsync(w.Clinic, "Admin");
        (await w.Clinic.Owner.PutAsJsonAsync($"/api/care/patients/{w.PatientId}/clinical-facts",
            new { allergies = new[] { "Sulfonamides" }, conditions = new[] { "Asthma" }, bloodGroup = "O+", genotype = "AS" }, Ct)).EnsureSuccessStatusCode();
        var consultationId = Id(await Json(await OpenAsync(w.Doctor, w.VisitId)));
        await Json(await SaveAsync(w.Doctor, consultationId, Note()));
        await Json(await SignAsync(w.Doctor, consultationId));
        var path = $"/api/care/patients/{w.PatientId}/record";

        // Non-clinical staff are refused, and nothing is logged.
        var before = (await AccessLogAsync(w.PatientId)).Count;
        foreach (var client in new[] { w.Desk, cashier, admin })
            await AssertProblemAsync(await client.GetAsync(path, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        Assert.Equal(before, (await AccessLogAsync(w.PatientId)).Count);

        // The doctor reads the timeline: one log row, area record.
        var record = await Json(await w.Doctor.GetAsync(path, Ct));
        var log = await AccessLogAsync(w.PatientId);
        Assert.Equal(before + 1, log.Count);
        Assert.Equal("record", log[^1].Area);
        Assert.Equal(w.DoctorId, log[^1].StaffUserId);
        Assert.False(log[^1].Emergency);

        // Everything that exists so far, and empty sections for what later tasks fill.
        var patient = record.GetProperty("patient");
        Assert.Equal(w.PatientId, Id(patient));
        Assert.Equal("Sulfonamides", patient.GetProperty("allergies")[0].GetString());
        Assert.Equal("AS", patient.GetProperty("genotype").GetString());
        var visit = Assert.Single(record.GetProperty("visits").EnumerateArray());
        Assert.Equal(w.VisitId, Id(visit));
        Assert.Equal(consultationId, visit.GetProperty("consultationId").GetGuid());
        Assert.Equal("WithDoctor", visit.GetProperty("stage").GetString());
        var consultation = Assert.Single(record.GetProperty("consultations").EnumerateArray());
        Assert.Equal("Temperature 38.9, pale conjunctivae", consultation.GetProperty("examination").GetString());
        Assert.Equal("Travelled to Ogun state last week", consultation.GetProperty("history").GetString());
        Assert.Equal("Doctor Staff", consultation.GetProperty("doctorName").GetString());
        Assert.Equal(2, consultation.GetProperty("diagnoses").GetArrayLength());
        Assert.Equal(0, consultation.GetProperty("addenda").GetArrayLength());
        var vitals = Assert.Single(record.GetProperty("vitals").EnumerateArray());
        Assert.Equal(38.9m, vitals.GetProperty("temperatureC").GetDecimal());
        Assert.Equal(104, vitals.GetProperty("pulse").GetInt32());
        var diagnoses = record.GetProperty("diagnoses").EnumerateArray().ToList();
        Assert.Equal(2, diagnoses.Count);
        Assert.Contains(diagnoses, d => d.GetProperty("text").GetString() == "Uncomplicated falciparum malaria" && d.GetProperty("code").GetString() == "B50.9"
            && d.GetProperty("consultationId").GetGuid() == consultationId);
        var bill = Assert.Single(record.GetProperty("bills").EnumerateArray());
        Assert.Equal(w.VisitId, bill.GetProperty("visitId").GetGuid());
        foreach (var section in new[] { "prescriptions", "results", "admissions", "dischargeSummaries" })
            Assert.Equal(0, record.GetProperty(section).GetArrayLength());

        // A doctor with no care relationship needs a reason (header only); with one, the open is an emergency.
        var (stranger, strangerId) = await kit.StaffAsync(w.Clinic, "Doctor");
        await AssertProblemAsync(await stranger.GetAsync(path, Ct), HttpStatusCode.Conflict, ReasonRequired);
        await AssertProblemAsync(await stranger.GetAsync(path + "?reason=Checking", Ct), HttpStatusCode.BadRequest);
        await Json(await SendWithReasonAsync(stranger, HttpMethod.Get, path, "Second opinion requested"));
        var emergency = (await AccessLogAsync(w.PatientId))[^1];
        Assert.True(emergency.Emergency);
        Assert.Equal(strangerId, emergency.StaffUserId);
        Assert.Equal("record", emergency.Area);

        // Another clinic's patient is 404.
        var other = await kit.ClinicAsync("Timeline Other Clinic");
        var foreign = Id(await kit.PatientAsync(other, "Femi", "Ola"));
        await AssertProblemAsync(await w.Clinic.Owner.GetAsync($"/api/care/patients/{foreign}/record", Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Diagnosis_code_search()
    {
        // The built-in list: about 200 common conditions, unique codes, malaria included.
        Assert.InRange(DiagnosisList.All.Count, 180, 400);
        Assert.Equal(DiagnosisList.All.Count, DiagnosisList.All.Select(d => d.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(DiagnosisList.All, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));

        var clinic = await kit.ClinicAsync("Codes Clinic");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");

        var malaria = (await Json(await doctor.GetAsync("/api/care/diagnosis-codes?q=malaria", Ct))).EnumerateArray().ToList();
        Assert.NotEmpty(malaria);
        Assert.All(malaria, m =>
        {
            Assert.Contains("malaria", m.GetProperty("name").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(m.GetProperty("code").GetString()));
        });
        Assert.Contains(malaria, m => m.GetProperty("code").GetString() == "B54");

        // By code (exact first), case-insensitive, limited.
        var byCode = (await Json(await doctor.GetAsync("/api/care/diagnosis-codes?q=b50", Ct))).EnumerateArray().ToList();
        Assert.NotEmpty(byCode);
        Assert.All(byCode, m => Assert.StartsWith("B50", m.GetProperty("code").GetString()));
        Assert.Equal("I10", (await Json(await doctor.GetAsync("/api/care/diagnosis-codes?q=I10", Ct)))[0].GetProperty("code").GetString());
        Assert.Equal(0, (await Json(await doctor.GetAsync("/api/care/diagnosis-codes?q=zzzz", Ct))).GetArrayLength());
        Assert.InRange((await Json(await doctor.GetAsync("/api/care/diagnosis-codes", Ct))).GetArrayLength(), 1, DiagnosisList.MaxResults);

        // A clinical tool: non-clinical staff are refused.
        await AssertProblemAsync(await desk.GetAsync("/api/care/diagnosis-codes?q=malaria", Ct), HttpStatusCode.Forbidden, ClinicalRefused);
    }

    // ---------- Fix round 1 ----------

    private Task<int> AuthorChangesAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.AuditLogs.CountAsync(a => a.TenantId == tenantId && a.Action == "care.note_author_changed", Ct));

    [Fact]
    public async Task Authorship_follows_the_visit_doctor()
    {
        var w = await WorldAsync("Authorship Clinic");
        var ownerId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == w.Clinic.TenantId && m.Role == Saas.Domain.Core.MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));

        // The director opens Doctor A's visit first: a read view only. No note yet, so 409, and nothing is written or moved.
        await AssertProblemAsync(await OpenAsync(w.Clinic.Owner, w.VisitId), HttpStatusCode.Conflict, NoNoteYet);
        Assert.False(await api.WithDbAsync(db => db.Consultations.IgnoreQueryFilters().AnyAsync(c => c.VisitId == w.VisitId, Ct)));
        Assert.False(await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().AnyAsync(a => a.StaffUserId == ownerId, Ct)));
        Assert.Equal("WaitingDoctor", (await Json(await w.Desk.GetAsync($"/api/care/visits/{w.VisitId}", Ct))).GetProperty("stage").GetString());

        // Doctor A opens, saves and signs; in between, the director's open reads A's draft without taking it.
        var consultationId = Id(await Json(await OpenAsync(w.Doctor, w.VisitId)));
        var asDirector = await Json(await OpenAsync(w.Clinic.Owner, w.VisitId));
        Assert.Equal(consultationId, Id(asDirector));
        Assert.Equal(w.DoctorId, asDirector.GetProperty("doctorUserId").GetGuid());
        Assert.False(await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().AnyAsync(a => a.StaffUserId == ownerId, Ct)));
        await Json(await SaveAsync(w.Doctor, consultationId, Note()));
        Assert.Equal("Signed", (await Json(await SignAsync(w.Doctor, consultationId))).GetProperty("status").GetString());

        // A second patient: Doctor A drafts, then the visit goes back to the queue and is re-triaged to Doctor B.
        var (doctorB, doctorBId) = await kit.StaffAsync(w.Clinic, "Doctor");
        var patientId = Id(await kit.PatientAsync(w.Clinic, "Kola", "Adeyemi"));
        var visitId = Id(await kit.CheckInAsync(w.Desk, patientId));
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new { vitals = new { pulse = 80 }, urgency = "Routine", doctorUserId = w.DoctorId }, Ct));
        var draftId = Id(await Json(await OpenAsync(w.Doctor, visitId)));
        await Json(await SaveAsync(w.Doctor, draftId, Note("Cough and fever")));
        await Json(await w.Desk.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingDoctor" }, Ct));
        Assert.Equal(0, await AuthorChangesAsync(w.Clinic.TenantId));
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new { vitals = new { pulse = 82 }, urgency = "Routine", doctorUserId = doctorBId }, Ct));

        // The draft now belongs to Doctor B (audited without clinical text). Doctor A can no longer save or sign it.
        Assert.Equal(doctorBId, await api.WithDbAsync(db => db.Consultations.IgnoreQueryFilters().Where(c => c.Id == draftId).Select(c => c.DoctorUserId).FirstAsync(Ct)));
        var moved = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.note_author_changed").ToListAsync(Ct)));
        Assert.Equal(draftId.ToString(), moved.EntityId);
        Assert.Contains(doctorBId.ToString(), moved.AfterJson);
        Assert.DoesNotContain("Cough", moved.AfterJson ?? "");
        Assert.DoesNotContain("malaria", moved.AfterJson ?? "", StringComparison.OrdinalIgnoreCase);
        await AssertProblemAsync(await SaveAsync(w.Doctor, draftId, Note("Doctor A again")), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await SignAsync(w.Doctor, draftId), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await OpenAsync(w.Doctor, visitId), HttpStatusCode.Forbidden, AssignedToAnother);

        // Doctor B opens the same draft, with A's text kept, and saves and signs it.
        var asB = await Json(await OpenAsync(doctorB, visitId));
        Assert.Equal(draftId, Id(asB));
        Assert.Equal(doctorBId, asB.GetProperty("doctorUserId").GetGuid());
        Assert.Equal("Cough and fever", asB.GetProperty("complaint").GetString());
        await Json(await SaveAsync(doctorB, draftId, Note("Cough and fever, seen by B")));
        Assert.Equal("Signed", (await Json(await SignAsync(doctorB, draftId))).GetProperty("status").GetString());

        // A draft left with another author (written before this rule) is handed to the visit's doctor when they open it.
        var thirdPatient = Id(await kit.PatientAsync(w.Clinic, "Ada", "Legacy"));
        var thirdVisit = Id(await kit.CheckInAsync(w.Desk, thirdPatient));
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/visits/{thirdVisit}/triage", new { vitals = new { pulse = 70 }, urgency = "Routine", doctorUserId = w.DoctorId }, Ct));
        var legacyId = Id(await Json(await OpenAsync(w.Doctor, thirdVisit)));
        await api.WithDbAsync(db => db.Consultations.IgnoreQueryFilters().Where(c => c.Id == legacyId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.DoctorUserId, ownerId), Ct));
        Assert.Equal(w.DoctorId, (await Json(await OpenAsync(w.Doctor, thirdVisit))).GetProperty("doctorUserId").GetGuid());
        await Json(await SaveAsync(w.Doctor, legacyId, Note()));
        Assert.Equal(2, await AuthorChangesAsync(w.Clinic.TenantId));
    }

    [Fact]
    public async Task Timeline_shows_only_this_patients_records_and_drafts_with_status()
    {
        var w = await WorldAsync("Isolation Clinic");
        await Json(await w.Clinic.Owner.PutAsJsonAsync($"/api/care/patients/{w.PatientId}/clinical-facts",
            new { allergies = new[] { "Aspirin" }, conditions = Array.Empty<string>(), bloodGroup = "A+", genotype = "AA" }, Ct));
        var firstNote = Id(await Json(await OpenAsync(w.Doctor, w.VisitId)));
        await Json(await SaveAsync(w.Doctor, firstNote, new
        {
            complaint = "First patient headache", plan = "First patient plan",
            diagnoses = new[] { new { text = "First patient migraine", code = "G43.9", primary = true } },
        }));
        await Json(await SignAsync(w.Doctor, firstNote));
        await Json(await AddendumAsync(w.Doctor, firstNote, "First patient addendum"));

        // A second patient in the same clinic, same doctor: own visit, vitals, a draft with a diagnosis, own bill.
        var secondPatient = Id(await kit.PatientAsync(w.Clinic, "Second", "Person"));
        await Json(await w.Clinic.Owner.PutAsJsonAsync($"/api/care/patients/{secondPatient}/clinical-facts",
            new { allergies = new[] { "Codeine" }, conditions = Array.Empty<string>(), bloodGroup = "B-", genotype = "SS" }, Ct));
        var secondVisit = Id(await kit.CheckInAsync(w.Desk, secondPatient));
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/visits/{secondVisit}/triage",
            new { vitals = new { temperatureC = 36.2, pulse = 61 }, urgency = "Routine", doctorUserId = w.DoctorId }, Ct));
        var secondNote = Id(await Json(await OpenAsync(w.Doctor, secondVisit)));
        await Json(await SaveAsync(w.Doctor, secondNote, new
        {
            complaint = "Second patient back pain", plan = "Second patient plan",
            diagnoses = new[] { new { text = "Second patient lumbago", code = "M54.5", primary = true } },
        }));

        var first = await Json(await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct));
        var second = await Json(await w.Doctor.GetAsync($"/api/care/patients/{secondPatient}/record", Ct));

        void OnlyOwn(JsonElement record, Guid patientId, Guid visitId, Guid noteId, string own, string other)
        {
            var text = record.GetRawText();
            Assert.Contains(own, text);
            Assert.DoesNotContain(other, text);
            Assert.Equal(visitId, Id(Assert.Single(record.GetProperty("visits").EnumerateArray())));
            Assert.Equal(noteId, Id(Assert.Single(record.GetProperty("consultations").EnumerateArray())));
            Assert.All(record.GetProperty("vitals").EnumerateArray(), v => Assert.Equal(patientId, v.GetProperty("patientId").GetGuid()));
            Assert.Single(record.GetProperty("vitals").EnumerateArray());
            Assert.All(record.GetProperty("diagnoses").EnumerateArray(), d => Assert.Equal(patientId, d.GetProperty("patientId").GetGuid()));
            Assert.Single(record.GetProperty("diagnoses").EnumerateArray());
            Assert.Equal(visitId, Assert.Single(record.GetProperty("bills").EnumerateArray()).GetProperty("visitId").GetGuid());
        }
        OnlyOwn(first, w.PatientId, w.VisitId, firstNote, "First patient", "Second patient");
        OnlyOwn(second, secondPatient, secondVisit, secondNote, "Second patient", "First patient");
        Assert.DoesNotContain("Codeine", first.GetRawText());
        Assert.DoesNotContain("Aspirin", second.GetRawText());
        Assert.DoesNotContain(secondNote.ToString(), first.GetRawText());
        Assert.DoesNotContain(firstNote.ToString(), second.GetRawText());

        // Drafts show on the staff timeline with their status; signed notes with theirs.
        var draft = Assert.Single(second.GetProperty("consultations").EnumerateArray());
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("signedAt").ValueKind);
        Assert.Equal("Second patient back pain", draft.GetProperty("complaint").GetString());
        Assert.Equal("Signed", Assert.Single(first.GetProperty("consultations").EnumerateArray()).GetProperty("status").GetString());
    }

    // ---------- Locks (deterministic: hold the lock on another connection and watch the request queue behind it) ----------

    /// <summary>A transaction on its own connection holding an advisory lock until disposed.</summary>
    private sealed class HeldLock(IServiceScope scope, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await tx.CommitAsync();
            await tx.DisposeAsync();
            scope.Dispose();
        }
    }

    private async Task<HeldLock> HoldAsync(long key)
    {
        var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Saas.Infrastructure.Persistence.AppDbContext>();
        var tx = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", Ct);
        return new HeldLock(scope, tx);
    }

    /// <summary>Waits until <paramref name="count"/> sessions are queued (not granted) on the advisory lock <paramref name="key"/>; fails after 20 s.</summary>
    private async Task WaitForWaitersAsync(long key, int count)
    {
        var high = (long)((ulong)key >> 32);
        var low = key & 0xFFFFFFFFL;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var waiting = await api.WithDbAsync(db => db.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND classid::bigint = {high} AND objid::bigint = {low} AND objsubid = 1").FirstAsync(Ct));
            if (waiting >= count) return;
            Assert.True(DateTime.UtcNow < deadline, $"Expected {count} request(s) waiting on lock {key}, saw {waiting}: the operation doesn't take the lock.");
            await Task.Delay(50, Ct);
        }
    }

    [Fact]
    public async Task Save_and_sign_wait_for_the_note_lock()
    {
        var w = await WorldAsync("Note Lock Clinic");
        var consultationId = Id(await Json(await OpenAsync(w.Doctor, w.VisitId)));
        await Json(await SaveAsync(w.Doctor, consultationId, Note("Before the race")));
        var key = ConsultationService.NoteLockKey(w.Clinic.TenantId, consultationId);

        Task<HttpResponseMessage> sign, save;
        await using (await HoldAsync(key))
        {
            sign = SignAsync(w.Doctor, consultationId);
            await WaitForWaitersAsync(key, 1);
            save = SaveAsync(w.Doctor, consultationId, Note("During the race"));
            await WaitForWaitersAsync(key, 2);
            Assert.False(sign.IsCompleted);
            Assert.False(save.IsCompleted);
        }
        var signed = await sign;
        var saved = await save;
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);

        // Whatever the order, the signed note is never changed afterwards: either the save ran first (and was signed),
        // or it was refused as signed.
        var stored = await api.WithDbAsync(db => db.Consultations.IgnoreQueryFilters().FirstAsync(c => c.Id == consultationId, Ct));
        var complaint = api.Services.GetRequiredService<ClinicalText>().Read(stored.Complaint);
        Assert.Equal(NoteStatus.Signed, stored.Status);
        if (saved.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("During the race", complaint);
        }
        else
        {
            await AssertProblemAsync(saved, HttpStatusCode.Conflict, SignedNote);
            Assert.Equal("Before the race", complaint);
        }
        Assert.Equal("Signed", (await Json(await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct)))
            .GetProperty("consultations")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Concurrent_first_opens_make_one_note()
    {
        var w = await WorldAsync("Visit Lock Clinic");
        var key = ConsultationService.VisitLockKey(w.Clinic.TenantId, w.VisitId);

        Task<HttpResponseMessage> first, second;
        await using (await HoldAsync(key))
        {
            first = OpenAsync(w.Doctor, w.VisitId);
            second = OpenAsync(w.Doctor, w.VisitId);
            await WaitForWaitersAsync(key, 2);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
        }
        var a = await Json(await first);
        var b = await Json(await second);
        Assert.Equal(Id(a), Id(b));
        Assert.Equal(1, await api.WithDbAsync(db => db.Consultations.IgnoreQueryFilters().CountAsync(c => c.VisitId == w.VisitId, Ct)));
        Assert.Equal(1, await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters()
            .CountAsync(x => x.VisitId == w.VisitId && x.StaffUserId == w.DoctorId && x.Kind == CareAssignmentKind.VisitDoctor, Ct)));

        // Re-triage takes the same visit lock, so it can't slip between the doctor check and the draft.
        await Json(await w.Desk.PostAsJsonAsync($"/api/care/visits/{w.VisitId}/stage", new { stage = "WaitingDoctor" }, Ct));
        Task<HttpResponseMessage> triage;
        await using (await HoldAsync(key))
        {
            triage = w.Nurse.PostAsJsonAsync($"/api/care/visits/{w.VisitId}/triage", new { vitals = new { pulse = 90 }, urgency = "Routine", doctorUserId = w.DoctorId }, Ct);
            await WaitForWaitersAsync(key, 1);
            Assert.False(triage.IsCompleted);
        }
        await Json(await triage);
    }
}
