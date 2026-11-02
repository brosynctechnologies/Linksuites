using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Domain.Care;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 16: the clinic on the platform (the admin console with usage, the support view) and the role table for every
/// <c>/api/care</c> route; plus the Task 16 carries: check-in against a death, the covering doctor, the director's unsigned-drafts list and
/// the access log's area labels.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed partial class CarePlatformTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"expected {(int)status}, got {(int)r.StatusCode} {body}");
        if (detail is not null) Assert.Equal(detail, JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString());
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private HttpClient As(string token)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string CareWebUrl
    {
        get
        {
            var url = api.Services.GetRequiredService<IConfiguration>()["App:ProductWebUrls:care"];
            Assert.False(string.IsNullOrEmpty(url), "App:ProductWebUrls:care is not configured.");
            return url;
        }
    }

    /// <summary>Support's read-only view of the clinic (the director's identity with the view-as claim).</summary>
    private async Task<HttpClient> SupportViewAsync(CareKit.Clinic clinic)
    {
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{clinic.TenantId}/view-as",
            new { reason = "The director says the bed board won't load." }, Ct));
        Assert.Equal("care", viewAs.GetProperty("product").GetString());
        Assert.StartsWith($"{CareWebUrl}/support#token=", viewAs.GetProperty("webUrl").GetString());
        return As(viewAs.GetProperty("token").GetString()!);
    }

    /// <summary>A portal account linked to <paramref name="patientId"/> through a link-only (WhatsApp) invite.</summary>
    private async Task<HttpClient> PortalAsync(CareKit.Clinic clinic, Guid patientId)
    {
        var invite = await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { phone = "08031234567" }, Ct));
        var link = invite.GetProperty("link").GetString()!;
        var client = api.CreateClient();
        var auth = await Json(await client.PostAsJsonAsync("/api/auth/parent/signup",
            new { fullName = "Portal Family", email = $"portal-{Guid.NewGuid():N}@example.com", password = "password123" }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        await Json(await client.PostAsJsonAsync($"/api/care-portal/invites/{link[(link.LastIndexOf('/') + 1)..]}/accept", new { relationship = "Self" }, Ct));
        return client;
    }

    /// <summary>An Admin whom the medical director has given the Doctor clinical role (Task 1 ruling: only the Owner can).</summary>
    private async Task<HttpClient> AdminDoctorAsync(CareKit.Clinic clinic)
    {
        var (client, userId) = await kit.StaffAsync(clinic, "Admin");
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/staff/{userId}", new { clinicalRole = "Doctor" }, Ct));
        var me = await Json(await client.GetAsync("/api/care/staff/me", Ct));
        Assert.Equal("Doctor", me.GetProperty("clinicalRole").GetString());
        return client;
    }

    // ---------- Admin console ----------

    [Fact]
    public async Task Clinic_appears_in_console_with_usage()
    {
        var name = $"Console Clinic {Guid.NewGuid().ToString("N")[..6]}";
        var clinic = await kit.ClinicAsync(name);
        await kit.StaffAsync(clinic, "Doctor"); // moves the clinic to the Clinic plan (beds 20)
        var ward = await kit.WardAsync(clinic, "Console ward");
        await kit.BedAsync(clinic, ward, "Bed 1");
        await kit.BedAsync(clinic, ward, "Bed 2");
        var retired = await kit.BedAsync(clinic, ward, "Bed 3");
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/wards/{ward}/beds/{retired}", new { label = "Bed 3", active = false }, Ct));
        var ada = Id(await kit.PatientAsync(clinic, "Ada", "Console"));
        await kit.PatientAsync(clinic, "Bola", "Console", "1985-01-01");
        var gone = Id(await kit.PatientAsync(clinic, "Chidi", "Console", "1970-03-03"));
        await Json(await clinic.Owner.PostAsync($"/api/care/patients/{gone}/inactive", null, Ct));
        // Clinical facts the console must never show, even to the roles that see audit details.
        (await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{ada}/clinical-facts",
            new { allergies = new[] { "Consolecillin" }, conditions = new[] { "Consolitis" }, bloodGroup = "O+", genotype = "AA" }, Ct)).EnsureSuccessStatusCode();

        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var found = await Json(await support.GetAsync($"/api/platform/businesses?product=care&q={Uri.EscapeDataString(name)}", Ct));
        var row = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal((clinic.TenantId, "care", "Active"), (row.GetProperty("id").GetGuid(), row.GetProperty("product").GetString(), row.GetProperty("status").GetString()));

        var detail = await Json(await support.GetAsync($"/api/platform/businesses/{clinic.TenantId}", Ct));
        var usage = detail.GetProperty("usage").EnumerateArray().ToDictionary(u => u.GetProperty("key").GetString()!, u => (Used: u.GetProperty("used").GetInt64(), Limit: u.GetProperty("limit").GetInt64()));
        Assert.Equal((2L, 15L), usage["team_seats"]); // the director and the doctor, of the Clinic plan's 15
        Assert.Equal((2L, 20L), usage["beds"]); // active beds only
        Assert.Equal((3L, -1L), usage["patients"]); // every registered patient (inactive included); no limit
        Assert.Equal(CareWebUrl, detail.GetProperty("webUrl").GetString());
        Assert.Equal(2, detail.GetProperty("members").GetArrayLength());

        var (super, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.SuperAdmin);
        var full = await (await super.GetAsync($"/api/platform/businesses/{clinic.TenantId}", Ct)).Content.ReadAsStringAsync(Ct);
        foreach (var marker in new[] { "Consolecillin", "Consolitis" }) Assert.DoesNotContain(marker, full);
    }

    // ---------- Support view ----------

    [Fact]
    public async Task Support_view_sees_no_clinical_fields()
    {
        var walk = new CareAccessTests(api);
        var routes = walk.CareRoutes().Select(r => r.Route).ToList();
        Assert.Empty(routes.Where(r => !CareAccessTests.Routes.ContainsKey(r)));
        var world = await walk.SeedWalkAsync();
        var view = await SupportViewAsync(world.Clinic);
        var patientId = world.Ids["patient"];

        // It reads like a non-clinical member: demographics yes, clinical fields never, on every GET route.
        var patient = await Json(await view.GetAsync($"/api/care/patients/{patientId}", Ct));
        Assert.Equal(patientId, Id(patient));
        var failures = new List<string>();
        foreach (var route in routes) await CareAccessTests.ScanAsync("Support view", view, route, CareAccessTests.Routes[route](world), failures);
        Assert.True(failures.Count == 0, string.Join("\n", failures));

        // Clinical reads are refused outright (no emergency path: the view isn't clinical), and so is the director's access log.
        foreach (var path in new[]
        {
            $"/api/care/patients/{patientId}/record", $"/api/care/visits/{world.Ids["visit"]}/vitals", $"/api/care/admissions/{world.Ids["admission"]}",
            $"/api/care/admissions/{world.Ids["admission"]}/discharge-summary", $"/api/care/prescriptions/{world.Ids["prescription"]}/pdf",
        })
            await AssertProblemAsync(await view.GetAsync(path, Ct), HttpStatusCode.Forbidden, CareAccess.ClinicalRefused);
        Assert.Equal(HttpStatusCode.Forbidden, (await view.GetAsync($"/api/care/patients/{patientId}/access-log", Ct)).StatusCode);

        // It writes nothing, clinical or not.
        Task<int> CountAsync() => api.WithDbAsync(async db =>
            await db.Addenda.IgnoreQueryFilters().CountAsync(a => a.TenantId == world.Clinic.TenantId, Ct)
            + await db.Patients.IgnoreQueryFilters().CountAsync(p => p.TenantId == world.Clinic.TenantId, Ct)
            + await db.CarePayments.IgnoreQueryFilters().CountAsync(p => p.TenantId == world.Clinic.TenantId, Ct)
            + await db.CareAssignments.IgnoreQueryFilters().CountAsync(a => a.TenantId == world.Clinic.TenantId, Ct));
        var before = await CountAsync();
        foreach (var write in new Func<Task<HttpResponseMessage>>[]
        {
            () => view.PostAsJsonAsync("/api/care/patients", new { firstName = "Support", lastName = "View", sex = "Female", dateOfBirth = "1990-01-01" }, Ct),
            () => view.PutAsJsonAsync($"/api/care/patients/{patientId}/clinical-facts", new { allergies = new[] { "Support" } }, Ct),
            () => view.PostAsJsonAsync($"/api/care/notes/consultation/{world.Ids["consultation"]}/addenda", new { text = "Support addendum" }, Ct),
            () => view.PostAsJsonAsync($"/api/care/bills/{world.Ids["bill"]}/payments", new { amountMinor = 1000, method = "Cash" }, Ct),
            () => view.PutAsJsonAsync("/api/care/settings/profile", new { name = "Renamed by support" }, Ct),
            () => view.PostAsJsonAsync($"/api/care/admissions/{world.Ids["admission"]}/care-team", new { userId = world.Ids["doctor"] }, Ct),
        })
        {
            var response = await write();
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("read only", await response.Content.ReadAsStringAsync(Ct));
        }
        Assert.Equal(before, await CountAsync());
    }

    // ---------- Role table ----------

    /// <summary>
    /// Columns: Owner (medical director), Admin, Admin with the Doctor clinical role, Doctor, Nurse, FrontDesk, Cashier, a portal token,
    /// support's view-as. Y: served (2xx, or 400/402/404/409/422 for made-up input or a record rule such as emergency access). N: 403.
    /// S: the route lets the role through and the service may still refuse this record (a care-team or author check: 403 with another
    /// sentence than the role's). Never 401, 3xx, 405, 415, 429 or 5xx.
    /// </summary>
    private const string
        Everyone = "YYYYYYYNY",          // every member reads; the support view reads
        EveryoneWrites = "YYYYYYYNN",    // every member (the cashier too); the support view never writes
        Leaders = "YYYNNNNNY",           // leadership reads
        LeadersWrite = "YYYNNNNNN",
        DeskReads = "YYYYYYNNY",         // front desk and above (clinical staff and leadership); not the cashier
        DeskWrites = "YYYYYYNNN",
        Clinical = "YNYYYNNNN",          // doctors, nurses, the director (an Admin only with a clinical role); never the support view
        Doctors = "YNYYNNNNN",           // doctors and the director
        DoctorsS = "YNSSNNNNN",          // doctors and the director; the service checks this admission's or note's doctor
        ClinicalS = "YNSSSNNNN",         // clinical staff; the service checks the care relationship
        Money = "YYYNNNYNN",             // the cashier and leadership write money
        MoneyReads = "YYYNNNYNY",        // the cashier and leadership read reports
        Inviters = "YYYNNYNNN",          // front desk and leadership manage portal invites and links
        InviterReads = "YYYNNYNNN",      // ruling (Task 16): not the support view (portal accounts' contact details)
        OwnerWrites = "YNNNNNNNN",
        OwnerReads = "YNNNNNNNY";

    private static readonly string[] RoleNames = ["Owner", "Admin", "Admin+Doctor", "Doctor", "Nurse", "FrontDesk", "Cashier", "portal token", "support view"];

    /// <summary>"METHOD pattern" → expected outcome per column (see <see cref="Everyone"/>). Built from the spec's role table and the ledger rulings.</summary>
    private static readonly Dictionary<string, string> Table = new()
    {
        // Setup and staff (Task 1): leadership manages staff and settings; everyone reads the setup and themselves.
        ["GET /api/care/setup"] = Everyone,
        ["PUT /api/care/settings/profile"] = LeadersWrite,
        ["GET /api/care/staff"] = Leaders,
        ["GET /api/care/staff/me"] = Everyone,
        ["PUT /api/care/staff/{userId:guid}"] = LeadersWrite,
        // Price list (Task 2): everyone reads; leadership or the cashier writes.
        ["GET /api/care/price-list"] = Everyone,
        ["POST /api/care/price-list"] = Money,
        ["PUT /api/care/price-list/{id:guid}"] = Money,
        // Patients (Task 2): everyone searches and reads demographics; front desk and above register and edit; clinical facts are clinical.
        ["GET /api/care/patients"] = Everyone,
        ["POST /api/care/patients"] = DeskWrites,
        ["GET /api/care/patients/{id:guid}"] = Everyone,
        ["GET /api/care/patients/{id:guid}/access-log"] = OwnerWrites, // Task 3 ruling: the director only, never through the support view
        ["PUT /api/care/patients/{id:guid}"] = DeskWrites,
        ["PUT /api/care/patients/{id:guid}/clinical-facts"] = Clinical,
        ["POST /api/care/patients/{id:guid}/inactive"] = DeskWrites,
        ["POST /api/care/patients/{id:guid}/active"] = DeskWrites,
        // Portal invites (Task 15 ruling): front desk and leadership only.
        ["POST /api/care/patients/{id:guid}/invites"] = Inviters,
        ["GET /api/care/patients/{id:guid}/invites"] = InviterReads,
        ["POST /api/care/patients/{id:guid}/invites/{inviteId:guid}/cancel"] = Inviters,
        ["GET /api/care/patients/{id:guid}/portal-links"] = InviterReads,
        ["DELETE /api/care/patients/{id:guid}/portal-links/{linkId:guid}"] = Inviters,
        // Doctors and appointments (Task 4): front desk and above; the cashier gets 403. A schedule is leadership's or that doctor's.
        ["GET /api/care/doctors"] = DeskReads,
        ["GET /api/care/doctors/{userId:guid}/schedule"] = Leaders, // ruling (Task 16): leadership or that doctor (not this table's); the desk books from slots
        ["PUT /api/care/doctors/{userId:guid}/schedule"] = LeadersWrite, // another doctor's schedule: the table's doctor isn't this one
        ["GET /api/care/doctors/{userId:guid}/slots"] = DeskReads,
        ["GET /api/care/appointments"] = DeskReads,
        ["POST /api/care/appointments"] = DeskWrites,
        ["PUT /api/care/appointments/{id:guid}"] = DeskWrites,
        ["POST /api/care/appointments/{id:guid}/cancel"] = DeskWrites,
        // Visits and the queue (Task 5): front desk and above check in; every member reads the queue and a visit (the cashier takes
        // payments from it); vitals and triage are clinical; the stage is moved by every member (the cashier only once paid).
        ["POST /api/care/visits"] = DeskWrites,
        ["GET /api/care/queue"] = Everyone,
        ["GET /api/care/visits/{id:guid}"] = Everyone,
        ["GET /api/care/visits/{id:guid}/vitals"] = Clinical,
        ["POST /api/care/visits/{id:guid}/triage"] = Clinical,
        ["POST /api/care/visits/{id:guid}/stage"] = EveryoneWrites,
        // Consultations (Task 6): the visit's doctor or the director; addenda by any clinical staff member.
        ["POST /api/care/visits/{id:guid}/consultation"] = DoctorsS,
        ["PUT /api/care/consultations/{id:guid}"] = DoctorsS,
        ["POST /api/care/consultations/{id:guid}/sign"] = DoctorsS,
        ["POST /api/care/notes/{kind}/{id:guid}/addenda"] = Clinical,
        ["GET /api/care/diagnosis-codes"] = Clinical,
        ["GET /api/care/patients/{id:guid}/record"] = Clinical,
        // Prescriptions and tests (Task 7): doctors order; doctors and nurses record results; the PDF and file link are clinical reads.
        ["POST /api/care/prescriptions"] = Doctors,
        ["GET /api/care/prescriptions/{id:guid}/pdf"] = Clinical,
        ["POST /api/care/tests"] = Doctors,
        ["POST /api/care/tests/{id:guid}/result"] = Clinical,
        ["GET /api/care/tests/{id:guid}/file"] = Clinical,
        // Bills (Tasks 2 and 11): everyone reads (no clinical references for non-clinical callers); the cashier or leadership changes lines.
        ["GET /api/care/bills"] = Everyone,
        ["GET /api/care/bills/{id:guid}"] = Everyone,
        ["POST /api/care/bills/{id:guid}/lines"] = Money,
        ["POST /api/care/bills/{id:guid}/lines/{lineId:guid}/remove"] = Money,
        // Wards and beds (Task 8): everyone reads; leadership sets up; front desk and clinical staff mark a bed ready.
        ["GET /api/care/wards"] = Everyone,
        ["POST /api/care/wards"] = LeadersWrite,
        ["PUT /api/care/wards/{id:guid}"] = LeadersWrite,
        ["DELETE /api/care/wards/{id:guid}"] = LeadersWrite,
        ["GET /api/care/wards/{id:guid}/beds"] = Everyone,
        ["POST /api/care/wards/{id:guid}/beds"] = LeadersWrite,
        ["PUT /api/care/wards/{id:guid}/beds/{bedId:guid}"] = LeadersWrite,
        ["DELETE /api/care/wards/{id:guid}/beds/{bedId:guid}"] = LeadersWrite,
        ["POST /api/care/beds/{id:guid}/ready"] = DeskWrites,
        ["GET /api/care/bed-board"] = Everyone,
        ["GET /api/care/wards/{id:guid}/patients"] = Everyone,
        // Admissions (Task 8): doctors admit; any member assigns the bed and takes the deposit (ruling); front desk, clinical staff and
        // leadership transfer; one admission is a clinical read. Task 16: the admitting doctor or the director changes the care team.
        ["POST /api/care/admissions"] = Doctors,
        ["GET /api/care/admissions/{id:guid}"] = Clinical,
        ["POST /api/care/admissions/{id:guid}/bed"] = EveryoneWrites,
        ["POST /api/care/admissions/{id:guid}/transfer"] = DeskWrites,
        ["POST /api/care/admissions/{id:guid}/care-team"] = DoctorsS,
        ["DELETE /api/care/admissions/{id:guid}/care-team/{userId:guid}"] = DoctorsS,
        // Ward care (Task 9): clinical staff on the care team write nursing notes, fluids and doses; doctors write ward rounds.
        ["POST /api/care/admissions/{id:guid}/nursing-notes"] = ClinicalS,
        ["GET /api/care/admissions/{id:guid}/nursing-notes"] = Clinical,
        ["POST /api/care/admissions/{id:guid}/fluids"] = ClinicalS,
        ["GET /api/care/admissions/{id:guid}/fluids"] = Clinical,
        ["POST /api/care/admissions/{id:guid}/ward-rounds"] = DoctorsS,
        ["GET /api/care/admissions/{id:guid}/ward-rounds"] = Clinical,
        ["PUT /api/care/ward-rounds/{id:guid}"] = DoctorsS,
        ["POST /api/care/ward-rounds/{id:guid}/sign"] = DoctorsS,
        ["POST /api/care/ward-rounds/{id:guid}/discard"] = DoctorsS,
        ["GET /api/care/admissions/{id:guid}/medication-chart"] = Clinical,
        ["POST /api/care/doses/{id:guid}"] = ClinicalS,
        // Discharge (Task 10): the admissions list is leadership, clinical staff and the front desk (ruling I3); the summary and the
        // discharge are the care team's doctors; the director takes over a draft.
        ["GET /api/care/admissions"] = DeskReads,
        ["GET /api/care/admissions/{id:guid}/discharge-summary"] = Clinical,
        ["PUT /api/care/admissions/{id:guid}/discharge-summary"] = DoctorsS,
        ["POST /api/care/admissions/{id:guid}/discharge-summary/sign"] = DoctorsS,
        ["POST /api/care/admissions/{id:guid}/discharge-summary/take-over"] = OwnerWrites,
        ["POST /api/care/admissions/{id:guid}/discharge-summary/discard"] = DoctorsS,
        ["POST /api/care/admissions/{id:guid}/discharge"] = DoctorsS,
        ["GET /api/care/admissions/{id:guid}/discharge.pdf"] = Clinical,
        // Payments (Task 11): the cashier and leadership; adjustments are leadership's; every member prints a receipt.
        ["POST /api/care/bills/{id:guid}/payments"] = Money,
        ["POST /api/care/payments/{id:guid}/reverse"] = Money,
        ["GET /api/care/payments/{id:guid}/receipt.pdf"] = Everyone,
        ["POST /api/care/bills/{id:guid}/adjustments"] = LeadersWrite,
        ["POST /api/care/bills/{id:guid}/reviewed"] = Money,
        ["POST /api/care/payments/online/{reference}/verify"] = Money,
        // The clinic's Paystack key (Task 11 ruling): the director only.
        ["GET /api/care/settings/paystack"] = OwnerReads,
        ["PUT /api/care/settings/paystack"] = OwnerWrites,
        ["DELETE /api/care/settings/paystack"] = OwnerWrites,
        // Reports (Task 12): the cashier and leadership. Task 16: the director's unsigned-drafts list.
        ["GET /api/care/reports/takings"] = MoneyReads,
        ["GET /api/care/reports/takings.csv"] = MoneyReads,
        ["GET /api/care/reports/debtors"] = MoneyReads,
        ["GET /api/care/reports/debtors.csv"] = MoneyReads,
        ["GET /api/care/reports/revenue"] = MoneyReads,
        ["GET /api/care/reports/revenue.csv"] = MoneyReads,
        ["GET /api/care/reports/deposits"] = MoneyReads,
        ["GET /api/care/reports/deposits.csv"] = MoneyReads,
        ["GET /api/care/reports/unsigned-drafts"] = OwnerReads,
        // Imports (Task 12): each kind's screen (here care-patients: front desk and above). The confirm token belongs to its uploader,
        // so another member's confirm is a 404 whatever the role.
        ["GET /api/care/imports/{kind}/template.xlsx"] = DeskReads,
        ["GET /api/care/imports/{kind}/template.csv"] = DeskReads,
        ["POST /api/care/imports/{kind}/preview"] = DeskWrites,
        ["POST /api/care/imports/{token}/confirm"] = EveryoneWrites,
    };

    /// <summary>Clinical GETs that open no patient's record, so write no access-log row, each with its reason.</summary>
    private static readonly Dictionary<string, string> ClinicalReadExemptions = new()
    {
        ["GET /api/care/diagnosis-codes"] = "the built-in list of condition names and codes: a clinical tool with no patient data",
    };

    /// <summary>Routes under /api/care outside the signed-in group: the signed result-file link carries its own proof.</summary>
    private static readonly HashSet<string> Exempt = ["GET /api/care/result-files/{id:guid}/{expires:long}/{signature}"];

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Parameter();

    private List<(string Method, string Pattern, bool Form)> CareRoutes(bool withExempt = false)
    {
        _ = api.CreateClient();
        return api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } p && p.StartsWith("/api/care/", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (m, e.RoutePattern.RawText!,
                e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true)))
            .Where(r => withExempt || !Exempt.Contains($"{r.m} {r.Item2}")).Distinct().ToList();
    }

    private static HttpRequestMessage Request(string method, string path, bool form) => new(new HttpMethod(method), path)
    {
        Content = form ? new MultipartFormDataContent { { new StringContent("x"), "body" } }
            : method is "POST" or "PUT" ? JsonContent.Create(new { }) : null,
    };

    [Fact]
    public async Task Care_role_table()
    {
        var routes = CareRoutes();
        Assert.All(Exempt, e => Assert.Contains(e, CareRoutes(withExempt: true).Select(r => $"{r.Method} {r.Pattern}")));
        var missing = routes.Select(r => $"{r.Method} {r.Pattern}").Where(k => !Table.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Decide who may use: " + string.Join(", ", missing));
        var stale = Table.Keys.Except(routes.Select(r => $"{r.Method} {r.Pattern}")).ToList();
        Assert.True(stale.Count == 0, "No longer routes: " + string.Join(", ", stale));
        Assert.All(Table, t => Assert.Equal(RoleNames.Length, t.Value.Length));

        // Review Focus 5 (fix round 1, M1): every clinical GET is one the access-log test opens (CareAccessTests.ClinicalReads), or an
        // exemption with its reason, so a clinical read that forgot RecordGuard.OpenAsync can't pass both tables unnoticed.
        var logged = CareAccessTests.ClinicalReadRoutes;
        Assert.All(ClinicalReadExemptions.Keys, e => Assert.Equal(Clinical, Table[e]));
        var unlogged = Table.Where(t => t.Key.StartsWith("GET ", StringComparison.Ordinal) && t.Value == Clinical)
            .Select(t => t.Key).Where(k => !logged.Contains(k) && !ClinicalReadExemptions.ContainsKey(k)).ToList();
        Assert.True(unlogged.Count == 0, "Clinical GETs not in CareAccessTests.ClinicalReads (the access-log test): " + string.Join(", ", unlogged));

        var world = await new CareAccessTests(api).SeedWalkAsync();
        var clinic = world.Clinic;
        var roles = new (string Name, HttpClient Client)[]
        {
            ("Owner", clinic.Owner),
            ("Admin", (await kit.StaffAsync(clinic, "Admin")).Client),
            ("Admin+Doctor", await AdminDoctorAsync(clinic)),
            ("Doctor", (await kit.StaffAsync(clinic, "Doctor")).Client),
            ("Nurse", (await kit.StaffAsync(clinic, "Nurse")).Client),
            ("FrontDesk", (await kit.StaffAsync(clinic, "FrontDesk")).Client),
            ("Cashier", (await kit.StaffAsync(clinic, "Cashier")).Client),
            ("portal token", await PortalAsync(clinic, world.Ids["patient"])),
            ("support view", await SupportViewAsync(clinic)),
        };

        // Reads get the walk's real records (so a role refusal is a real 403, not a 404); writes keep made-up ids and empty bodies, so
        // the clinic survives the run (each allowed role gets a 400 or 404 instead of a change).
        string Fill(string method, string pattern) => Parameter().Replace(pattern, m =>
        {
            var before = pattern[..m.Index];
            string Real(string key) => world.Ids[key].ToString();
            return m.Value switch
            {
                "{kind}" when before.StartsWith("/api/care/notes/") => "consultation",
                "{kind}" => "care-patients",
                "{reference}" => "care_rolecheck",
                "{token}" => "rolecheck",
                "{userId:guid}" when before.StartsWith("/api/care/doctors/") => Real("doctor"),
                "{id:guid}" when method == "GET" && before == "/api/care/patients/" => Real("patient"),
                "{id:guid}" when method == "GET" && before == "/api/care/visits/" => Real("visit"),
                "{id:guid}" when method == "GET" && before == "/api/care/admissions/" => Real("admission"),
                "{id:guid}" when method == "GET" && before == "/api/care/wards/" => Real("ward"),
                "{id:guid}" when method == "GET" && before == "/api/care/bills/" => Real("bill"),
                "{id:guid}" when method == "GET" && before == "/api/care/prescriptions/" => Real("prescription"),
                "{id:guid}" when method == "GET" && before == "/api/care/tests/" => Real("test"),
                "{id:guid}" when method == "GET" && before == "/api/care/payments/" => Real("payment"),
                var v when v.Contains(":guid") => Guid.NewGuid().ToString(),
                _ => "x",
            };
        });
        var queries = new Dictionary<string, string>
        {
            ["GET /api/care/doctors/{userId:guid}/slots"] = $"?date={CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(14):yyyy-MM-dd}",
            ["GET /api/care/diagnosis-codes"] = "?q=malaria",
        };
        var failures = new List<string>();

        foreach (var (method, pattern, form) in routes.OrderBy(r => r.Pattern).ThenBy(r => r.Method))
        {
            var key = $"{method} {pattern}";
            var expected = Table[key];
            var path = Fill(method, pattern) + queries.GetValueOrDefault(key, "");
            for (var i = 0; i < roles.Length; i++)
            {
                var (role, client) = roles[i];
                using var request = Request(method, path, form);
                var response = await client.SendAsync(request, Ct);
                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(Ct);
                var served = status is (>= 200 and < 300) or 400 or 402 or 404 or 409 or 422;
                var problem = expected[i] switch
                {
                    'Y' when !served => "expected access (2xx, or 400/402/404/409/422 for the made-up input)",
                    'S' when !(served || status == 403) => "expected the route to let it through (a record check may still refuse)",
                    'N' when status != 403 => "expected 403",
                    'N' when role == "support view" && method != "GET" && !body.Contains("read only") => "expected the read-only refusal",
                    _ => null,
                };
                if (problem is not null) failures.Add($"{role} {key} → {status}, {problem} {(status >= 400 ? body[..Math.Min(body.Length, 160)] : "")}");
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n" + string.Join("\n", failures));
    }

    [Fact]
    public async Task Result_file_link_outside_the_group_refuses_a_forged_signature()
    {
        var anon = api.CreateClient();
        var expires = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
        var response = await anon.GetAsync($"/api/care/result-files/{Guid.NewGuid()}/{expires}/forged-signature", Ct);
        Assert.False(response.IsSuccessStatusCode, $"{(int)response.StatusCode}");
        Assert.True((int)response.StatusCode < 500);
    }

    // ---------- Check-in against a death (Task 14 carry) ----------

    /// <summary>
    /// Holds the patient's admission lock (as a Died discharge does), starts <paramref name="request"/>, waits until it waits on that
    /// lock (counting only waiters on this key), marks the patient Deceased under the lock, commits and returns the answer.
    /// </summary>
    private async Task<HttpResponseMessage> UnderHeldDeathAsync(Guid tenantId, Guid patientId, Func<Task<HttpResponseMessage>> request)
    {
        var key = AdmissionService.PatientLockKey(tenantId, patientId);
        var high = (long)((ulong)key >> 32);
        var low = key & 0xFFFFFFFFL;
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", Ct);
        var pending = request();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var waiting = 0;
        while (!pending.IsCompleted && DateTime.UtcNow < deadline && waiting == 0)
        {
            waiting = await api.WithDbAsync(d => d.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND classid::bigint = {high} AND objid::bigint = {low} AND objsubid = 1").FirstAsync(Ct));
            if (waiting == 0) await Task.Delay(50, Ct);
        }
        await db.Database.ExecuteSqlAsync($"UPDATE patients SET status = 'Deceased' WHERE id = {patientId}", Ct);
        await tx.CommitAsync(Ct);
        var response = await pending;
        Assert.True(waiting > 0, $"The request didn't wait on the patient lock: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return response;
    }

    [Fact]
    public async Task Check_in_racing_a_death_is_refused_and_opens_no_visit()
    {
        var clinic = await kit.ClinicAsync("Check-in Race Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        await kit.PriceItemAsync(clinic, "Race consultation", "Consultation", 500_000);

        // A walk-in that passed its first check waits for the death to commit, then sees it.
        var walkIn = Id(await kit.PatientAsync(clinic, "Walk", "Racer"));
        await AssertProblemAsync(await UnderHeldDeathAsync(clinic.TenantId, walkIn, () => desk.PostAsJsonAsync("/api/care/visits", new { patientId = walkIn }, Ct)),
            HttpStatusCode.Conflict, AppointmentService.PatientDeceased);
        Assert.Equal(0, await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().CountAsync(v => v.PatientId == walkIn, Ct)));
        Assert.Equal(0, await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().CountAsync(b => b.PatientId == walkIn, Ct)));

        // An appointment check-in (doctor lock, then check-in lock, then the patient lock) does the same and leaves the booking as it was.
        var day = CareCalendar.Today(DateTimeOffset.UtcNow);
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new
        {
            blocks = Enum.GetValues<DayOfWeek>().Select(d => new { weekday = d.ToString(), start = "00:00", end = "23:45", slotMinutes = 15 }).ToArray(),
        }, Ct));
        var booked = Id(await kit.PatientAsync(clinic, "Booked", "Racer", "1975-05-05"));
        var slots = (await Json(await desk.GetAsync($"/api/care/doctors/{doctorId}/slots?date={day.AddDays(1):yyyy-MM-dd}", Ct))).EnumerateArray()
            .Select(s => s.GetProperty("start").GetDateTimeOffset()).ToList();
        var appointmentId = Id(await Json(await desk.PostAsJsonAsync("/api/care/appointments",
            new { patientId = booked, doctorUserId = doctorId, start = slots[0], reason = "Review" }, Ct)));
        await AssertProblemAsync(await UnderHeldDeathAsync(clinic.TenantId, booked, () => desk.PostAsJsonAsync("/api/care/visits", new { patientId = booked, appointmentId }, Ct)),
            HttpStatusCode.Conflict, AppointmentService.PatientDeceased);
        Assert.Equal(0, await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().CountAsync(v => v.PatientId == booked, Ct)));
        Assert.Equal(AppointmentStatus.Booked, await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.Id == appointmentId).Select(a => a.Status).FirstAsync(Ct)));

        // The control: a living patient checks in as before.
        var alive = Id(await kit.PatientAsync(clinic, "Alive", "Racer", "1980-08-08"));
        Assert.Equal("WaitingVitals", (await kit.CheckInAsync(desk, alive)).GetProperty("stage").GetString());
    }

    // ---------- Covering doctor (Task 10 ruling M6) ----------

    private async Task<(CareKit.Clinic Clinic, HttpClient Admitting, Guid AdmittingId, Guid PatientId, Guid AdmissionId)> AdmittedAsync(string name)
    {
        var clinic = await kit.ClinicAsync(name);
        var (admitting, admittingId) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = Id(await kit.PatientAsync(clinic, "Covered", "Patient"));
        var admissionId = await kit.AdmitAsync(admitting, patientId);
        return (clinic, admitting, admittingId, patientId, admissionId);
    }

    private static string Team(Guid admissionId) => $"/api/care/admissions/{admissionId}/care-team";

    private Task<List<CareAssignment>> CoveringRowsAsync(Guid admissionId, Guid doctorId) => api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters()
        .Where(a => a.AdmissionId == admissionId && a.StaffUserId == doctorId && a.Kind == CareAssignmentKind.CoveringDoctor).OrderBy(a => a.CreatedAt).ToListAsync(Ct));

    [Fact]
    public async Task Covering_doctor_joins_and_leaves_the_care_team()
    {
        var (clinic, admitting, admittingId, patientId, admissionId) = await AdmittedAsync("Covering Clinic");
        var (covering, coveringId) = await kit.StaffAsync(clinic, "Doctor");
        var admissionPath = $"/api/care/admissions/{admissionId}";

        // Before: the covering doctor needs emergency access to read and can't write ward care.
        await AssertProblemAsync(await covering.GetAsync(admissionPath, Ct), HttpStatusCode.Conflict, RecordGuard.ReasonRequired);
        await AssertProblemAsync(await covering.PostAsJsonAsync($"{admissionPath}/ward-rounds", new { note = "Cover round" }, Ct), HttpStatusCode.Forbidden, WardCareService.NotOnWard);

        // The admitting doctor adds them: an active care relationship, audited with ids only. Adding again changes nothing.
        var team = await Json(await admitting.PostAsJsonAsync(Team(admissionId), new { userId = coveringId }, Ct));
        Assert.Contains(team.EnumerateArray(), m => m.GetProperty("userId").GetGuid() == coveringId && m.GetProperty("kind").GetString() == "CoveringDoctor");
        Assert.Contains(team.EnumerateArray(), m => m.GetProperty("userId").GetGuid() == admittingId && m.GetProperty("kind").GetString() == "AdmittingDoctor");
        await Json(await admitting.PostAsJsonAsync(Team(admissionId), new { userId = coveringId }, Ct));
        var row = Assert.Single(await CoveringRowsAsync(admissionId, coveringId));
        Assert.Equal((patientId, (DateTimeOffset?)null), (row.PatientId, row.EndedAt));
        var added = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == clinic.TenantId && a.Action == "care.care_team_added").ToListAsync(Ct)));
        Assert.Equal(admittingId, added.ActorId);
        using (var doc = JsonDocument.Parse(added.AfterJson!))
            Assert.Equal(["admissionId", "doctorUserId", "patientId"], doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());

        // Now they read without a reason (a related, non-emergency log row) and write ward care.
        Assert.Equal(HttpStatusCode.OK, (await covering.GetAsync(admissionPath, Ct)).StatusCode);
        var opened = await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId && a.StaffUserId == coveringId).ToListAsync(Ct));
        Assert.Contains(opened, a => !a.Emergency);
        var round = Id(await Json(await covering.PostAsJsonAsync($"{admissionPath}/ward-rounds", new { note = "Cover round, settling" }, Ct)));

        // The director can add someone too; a nurse, front desk or a doctor not admitting can't; only active doctors can be added.
        var (other, otherId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        await AssertProblemAsync(await other.PostAsJsonAsync(Team(admissionId), new { userId = otherId }, Ct), HttpStatusCode.Forbidden, AdmissionService.CareTeamManagers);
        await AssertProblemAsync(await covering.PostAsJsonAsync(Team(admissionId), new { userId = otherId }, Ct), HttpStatusCode.Forbidden, AdmissionService.CareTeamManagers);
        await AssertProblemAsync(await nurse.PostAsJsonAsync(Team(admissionId), new { userId = otherId }, Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await desk.PostAsJsonAsync(Team(admissionId), new { userId = otherId }, Ct), HttpStatusCode.Forbidden);
        foreach (var notADoctor in new Guid?[] { nurseId, Guid.NewGuid(), null })
            await AssertProblemAsync(await admitting.PostAsJsonAsync(Team(admissionId), new { userId = notADoctor }, Ct), HttpStatusCode.BadRequest);
        var (_, leftId) = await kit.StaffAsync(clinic, "Doctor");
        await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == clinic.TenantId && m.UserId == leftId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MembershipStatus.Disabled), Ct));
        await AssertProblemAsync(await admitting.PostAsJsonAsync(Team(admissionId), new { userId = leftId }, Ct), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await admitting.PostAsJsonAsync($"/api/care/admissions/{Guid.NewGuid()}/care-team", new { userId = otherId }, Ct), HttpStatusCode.NotFound);
        await Json(await clinic.Owner.PostAsJsonAsync(Team(admissionId), new { userId = otherId }, Ct));
        Assert.Single(await CoveringRowsAsync(admissionId, otherId));

        // Removing ends the row (audited); the admitting doctor can't be removed; a second removal is a 404.
        Assert.Equal(HttpStatusCode.NoContent, (await clinic.Owner.DeleteAsync($"{Team(admissionId)}/{otherId}", Ct)).StatusCode);
        Assert.NotNull(Assert.Single(await CoveringRowsAsync(admissionId, otherId)).EndedAt);
        await AssertProblemAsync(await clinic.Owner.DeleteAsync($"{Team(admissionId)}/{otherId}", Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await clinic.Owner.DeleteAsync($"{Team(admissionId)}/{admittingId}", Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await other.DeleteAsync($"{Team(admissionId)}/{coveringId}", Ct), HttpStatusCode.Forbidden, AdmissionService.CareTeamManagers);
        var removed = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == clinic.TenantId && a.Action == "care.care_team_removed").ToListAsync(Ct)));
        Assert.DoesNotContain("Doctor Staff", removed.AfterJson!);
        Assert.Equal(RecordGuard.ReasonRequired, JsonDocument.Parse(await (await other.GetAsync(admissionPath, Ct)).Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("detail").GetString());

        // At discharge the covering doctor's row ends like the admitting doctor's: discharge time + 30 days.
        (await covering.PostAsync($"/api/care/ward-rounds/{round}/sign", null, Ct)).EnsureSuccessStatusCode();
        // The covering doctor writes and signs the discharge summary (they are on the care team), the admitting doctor discharges.
        await Json(await covering.PutAsJsonAsync($"{admissionPath}/discharge-summary",
            new { diagnoses = "Recovered", treatment = "Fluids", condition = "Stable", takeHome = "None" }, Ct));
        (await covering.PostAsync($"{admissionPath}/discharge-summary/sign", null, Ct)).EnsureSuccessStatusCode();
        await Json(await admitting.PostAsJsonAsync($"{admissionPath}/discharge", new { type = "Home" }, Ct));
        var dischargedAt = await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().Where(a => a.Id == admissionId).Select(a => a.DischargedAt).FirstAsync(Ct));
        Assert.Equal(dischargedAt + TimeSpan.FromDays(30), Assert.Single(await CoveringRowsAsync(admissionId, coveringId)).EndedAt);
        Assert.Equal(HttpStatusCode.OK, (await covering.GetAsync(admissionPath, Ct)).StatusCode); // the follow-up window
        await AssertProblemAsync(await admitting.PostAsJsonAsync(Team(admissionId), new { userId = otherId }, Ct), HttpStatusCode.Conflict, AdmissionService.Discharged);
    }

    /// <summary>Fix round 1 (M4): the care team stays inside the clinic: another clinic's doctor is no doctor here, another clinic's admission isn't found.</summary>
    [Fact]
    public async Task Care_team_is_limited_to_the_clinic()
    {
        var (clinic, admitting, _, _, admissionId) = await AdmittedAsync("Team Home Clinic");
        var (otherClinic, otherAdmitting, _, _, otherAdmissionId) = await AdmittedAsync("Team Other Clinic");
        var (_, otherDoctorId) = await kit.StaffAsync(otherClinic, "Doctor");
        var (_, homeDoctorId) = await kit.StaffAsync(clinic, "Doctor");

        await AssertProblemAsync(await admitting.PostAsJsonAsync(Team(admissionId), new { userId = otherDoctorId }, Ct), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await clinic.Owner.PostAsJsonAsync(Team(admissionId), new { userId = otherDoctorId }, Ct), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await admitting.PostAsJsonAsync(Team(otherAdmissionId), new { userId = homeDoctorId }, Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await clinic.Owner.PostAsJsonAsync(Team(otherAdmissionId), new { userId = homeDoctorId }, Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await clinic.Owner.DeleteAsync($"{Team(otherAdmissionId)}/{otherDoctorId}", Ct), HttpStatusCode.NotFound);
        Assert.Empty(await CoveringRowsAsync(admissionId, otherDoctorId));
        Assert.Empty(await CoveringRowsAsync(otherAdmissionId, homeDoctorId));
        _ = otherAdmitting;
    }

    // ---------- Inactive vs death (fix round 1, M8) ----------

    /// <summary>
    /// Making a patient inactive (or active again) takes the patient lock and re-reads the status, so a request racing a Died discharge waits
    /// for it, then gets 409 "This patient is recorded as deceased." and the Deceased status stands.
    /// </summary>
    [Fact]
    public async Task Inactive_racing_a_death_is_refused_and_keeps_deceased()
    {
        var clinic = await kit.ClinicAsync("Inactive Race Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        foreach (var action in new[] { "inactive", "active" })
        {
            var patientId = Id(await kit.PatientAsync(clinic, "Race", action == "inactive" ? "Inactive" : "Active", action == "inactive" ? "1960-01-01" : "1961-01-01"));
            if (action == "active") await Json(await desk.PostAsync($"/api/care/patients/{patientId}/inactive", null, Ct));
            await AssertProblemAsync(await UnderHeldDeathAsync(clinic.TenantId, patientId, () => desk.PostAsync($"/api/care/patients/{patientId}/{action}", null, Ct)),
                HttpStatusCode.Conflict, AppointmentService.PatientDeceased);
            Assert.Equal(PatientStatus.Deceased, await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.Id == patientId).Select(p => p.Status).FirstAsync(Ct)));
        }
    }

    // ---------- Unsigned drafts (Task 6 carry) ----------

    [Fact]
    public async Task Director_lists_unsigned_drafts_without_note_text()
    {
        var clinic = await kit.ClinicAsync("Unsigned Drafts Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var others = new List<HttpClient>();
        foreach (var role in new[] { "Admin", "Nurse", "FrontDesk", "Cashier" }) others.Add((await kit.StaffAsync(clinic, role)).Client);
        others.Add(doctor);
        others.Add(await AdminDoctorAsync(clinic));

        async Task<(Guid Patient, Guid Visit, Guid Consultation)> ConsultedAsync(string first, string dob)
        {
            var patient = Id(await kit.PatientAsync(clinic, first, "Drafts", dob));
            var visit = Id(await kit.CheckInAsync(clinic.Owner, patient));
            await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visit}/triage",
                new { vitals = new { temperatureC = 37.0 }, urgency = "Routine", doctorUserId = doctorId }, Ct));
            var consultation = Id(await Json(await doctor.PostAsync($"/api/care/visits/{visit}/consultation", null, Ct)));
            await Json(await doctor.PutAsJsonAsync($"/api/care/consultations/{consultation}",
                new { complaint = "Draftmarker complaint", history = "", examination = "", plan = "Draftmarker plan", diagnoses = Array.Empty<object>() }, Ct));
            return (patient, visit, consultation);
        }

        // Listed: a draft on a Done visit (made three days ago). Not listed: a signed note on a Done visit, a draft on an open visit.
        var late = await ConsultedAsync("Late", "1990-01-01");
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{late.Visit}/stage", new { stage = "Done" }, Ct));
        await api.WithDbAsync(db => db.Consultations.IgnoreQueryFilters().Where(c => c.Id == late.Consultation)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, DateTimeOffset.UtcNow.AddDays(-3)), Ct));
        var signed = await ConsultedAsync("Signed", "1991-01-01");
        (await doctor.PostAsync($"/api/care/consultations/{signed.Consultation}/sign", null, Ct)).EnsureSuccessStatusCode();
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{signed.Visit}/stage", new { stage = "Done" }, Ct));
        var open = await ConsultedAsync("Open", "1992-01-01");

        // Listed: an unsigned ward round and an unsigned discharge summary on an admission.
        var admitted = Id(await kit.PatientAsync(clinic, "Ward", "Drafts", "1993-01-01"));
        var admissionId = await kit.AdmitAsync(doctor, admitted);
        await Json(await doctor.PostAsJsonAsync($"/api/care/admissions/{admissionId}/ward-rounds", new { note = "Draftmarker round" }, Ct));
        await Json(await doctor.PutAsJsonAsync($"/api/care/admissions/{admissionId}/discharge-summary",
            new { diagnoses = "Draftmarker diagnoses", treatment = "Draftmarker treatment", condition = "", takeHome = "" }, Ct));

        var response = await clinic.Owner.GetAsync("/api/care/reports/unsigned-drafts", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, text);
        Assert.DoesNotContain("Draftmarker", text);
        var rows = JsonDocument.Parse(text).RootElement.EnumerateArray().ToList();
        var numbers = await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.TenantId == clinic.TenantId).ToDictionaryAsync(p => p.Id, p => p.Number, Ct));
        Assert.Equal(3, rows.Count);
        var consultationRow = Assert.Single(rows, r => r.GetProperty("kind").GetString() == "consultation");
        Assert.Equal((late.Consultation, numbers[late.Patient], "Doctor Staff", 3), (Id(consultationRow), consultationRow.GetProperty("patientNumber").GetString(),
            consultationRow.GetProperty("doctorName").GetString(), consultationRow.GetProperty("ageDays").GetInt32()));
        Assert.Equal(late.Visit, consultationRow.GetProperty("visitId").GetGuid());
        Assert.Equal(CareCalendar.Today(DateTimeOffset.UtcNow), DateOnly.Parse(consultationRow.GetProperty("date").GetString()!));
        Assert.Equal(0, consultationRow.GetProperty("ageDays").GetInt32() - 3);
        foreach (var kind in new[] { "ward-round", "discharge-summary" })
        {
            var r = Assert.Single(rows, x => x.GetProperty("kind").GetString() == kind);
            Assert.Equal((numbers[admitted], admissionId, "Doctor Staff", 0), (r.GetProperty("patientNumber").GetString(), r.GetProperty("admissionId").GetGuid(),
                r.GetProperty("doctorName").GetString(), r.GetProperty("ageDays").GetInt32()));
        }
        Assert.DoesNotContain(rows, r => numbers[signed.Patient] == r.GetProperty("patientNumber").GetString() || numbers[open.Patient] == r.GetProperty("patientNumber").GetString());
        Assert.All(rows.SelectMany(r => r.EnumerateObject()).Select(p => p.Name).Distinct(), name => Assert.False(CareAccessTests.IsClinicalName(name), name));

        // The medical director's list: everyone else gets 403.
        foreach (var client in others) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/care/reports/unsigned-drafts", Ct)).StatusCode);
    }

    // ---------- Access-log area labels (Task 5 carry) ----------

    [Fact]
    public void Every_access_log_area_has_a_staff_and_a_portal_label()
    {
        foreach (var area in RecordGuard.Areas.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(RecordGuard.Areas.Label(area)), area);
            Assert.True(RecordGuard.Areas.Labels.ContainsKey(area), $"staff label for {area}");
            Assert.True(PortalService.AreaLabels.ContainsKey(area), $"portal label for {area}");
        }
        Assert.Equal(RecordGuard.Areas.All.Order(), RecordGuard.Areas.Labels.Keys.Order());
    }

    [Fact]
    public async Task Staff_access_log_labels_triage_and_every_other_area()
    {
        var clinic = await kit.ClinicAsync("Access Label Clinic");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = Id(await kit.PatientAsync(clinic, "Label", "Patient"));
        var visit = Id(await kit.CheckInAsync(clinic.Owner, patientId));
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{visit}/triage", new { vitals = new { pulse = 80 }, urgency = "Routine", doctorUserId = doctorId }, Ct));
        await Json(await nurse.GetAsync($"/api/care/patients/{patientId}", Ct));

        var log = (await Json(await clinic.Owner.GetAsync($"/api/care/patients/{patientId}/access-log", Ct))).EnumerateArray().ToList();
        var triage = Assert.Single(log, e => e.GetProperty("area").GetString() == RecordGuard.Areas.Triage);
        Assert.Equal((nurseId, RecordGuard.Areas.Label(RecordGuard.Areas.Triage)), (triage.GetProperty("staffUserId").GetGuid(), triage.GetProperty("areaLabel").GetString()));
        var record = Assert.Single(log, e => e.GetProperty("area").GetString() == RecordGuard.Areas.Record);
        Assert.Equal(RecordGuard.Areas.Label(RecordGuard.Areas.Record), record.GetProperty("areaLabel").GetString());
        Assert.All(log, e => Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("areaLabel").GetString())));
    }
}
