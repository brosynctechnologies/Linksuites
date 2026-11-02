using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Saas.IntegrationTests;

/// <summary>CareLink Task 1: signup, plans, clinic roles and staff profiles.</summary>
[Collection(nameof(ApiCollection))]
public sealed class CareSetupTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    [Fact]
    public async Task Signup_creates_the_clinic_with_free_plan()
    {
        var clinic = await kit.ClinicAsync("Setup Clinic");

        var setup = await Json(await clinic.Owner.GetAsync("/api/care/setup", Ct));
        Assert.Equal("Setup Clinic", setup.GetProperty("clinic").GetProperty("name").GetString());
        Assert.Equal(clinic.Slug, setup.GetProperty("clinic").GetProperty("slug").GetString());
        Assert.Equal("CL", setup.GetProperty("clinic").GetProperty("patientPrefix").GetString());
        var checklist = setup.GetProperty("checklist").EnumerateArray().ToList();
        Assert.Equal(4, checklist.Count);
        Assert.Equal(["profile", "price-list", "wards", "staff"], checklist.Select(c => c.GetProperty("key").GetString()));
        Assert.All(checklist, c => Assert.False(c.GetProperty("done").GetBoolean()));
        Assert.Equal("Doctor", setup.GetProperty("caller").GetProperty("clinicalRole").GetString());
        Assert.True(setup.GetProperty("caller").GetProperty("isClinical").GetBoolean());
        Assert.True(setup.GetProperty("caller").GetProperty("isLeadership").GetBoolean());

        var billing = await Json(await clinic.Owner.GetAsync("/api/billing", Ct));
        var plan = billing.GetProperty("currentPlan");
        Assert.Equal("free", plan.GetProperty("code").GetString());
        Assert.Equal(0, plan.GetProperty("limits").GetProperty("beds").GetInt64());
        Assert.Equal(3, plan.GetProperty("limits").GetProperty("team_seats").GetInt64());
        Assert.Equal(0, plan.GetProperty("limits").GetProperty("online_payments").GetInt64());
        var plans = billing.GetProperty("plans").EnumerateArray().ToList();
        Assert.Equal(["free", "clinic", "hospital"], plans.Select(p => p.GetProperty("code").GetString()));
        Assert.Equal([0L, 2_500_000L, 7_500_000L], plans.Select(p => p.GetProperty("priceMinor").GetInt64()));
        Assert.All(plans, p => Assert.Equal("Month", p.GetProperty("interval").GetString()));
        var usage = billing.GetProperty("usage").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()).ToList();
        Assert.Contains("team_seats", usage);
        Assert.Contains("beds", usage);

        var me = await Json(await clinic.Owner.GetAsync("/api/care/staff/me", Ct));
        Assert.Equal("Doctor", me.GetProperty("clinicalRole").GetString());
        Assert.Equal("Owner", me.GetProperty("memberRole").GetString());
    }

    /// <summary>Task 8: the wards checklist item is done once a ward has an active bed, and usage counts active beds only.</summary>
    [Fact]
    public async Task Wards_checklist_and_bed_usage_count_active_beds()
    {
        var clinic = await kit.ClinicAsync("Checklist Beds Clinic");
        await api.MakeProAsync(clinic.TenantId, "care", "clinic");
        bool WardsDone(JsonElement setup) =>
            setup.GetProperty("checklist").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "wards").GetProperty("done").GetBoolean();
        long BedsUsed(JsonElement billing) => billing.GetProperty("usage").GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("key").GetString() == "beds").GetProperty("used").GetInt64();

        var wardId = await kit.WardAsync(clinic, "Checklist ward");
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/wards/{wardId}/beds", new { label = "Spare", active = false }, Ct));
        Assert.False(WardsDone(await Json(await clinic.Owner.GetAsync("/api/care/setup", Ct))));
        Assert.Equal(0, BedsUsed(await Json(await clinic.Owner.GetAsync("/api/billing", Ct))));

        await kit.BedAsync(clinic, wardId, "C1");
        await kit.BedAsync(clinic, wardId, "C2");
        Assert.True(WardsDone(await Json(await clinic.Owner.GetAsync("/api/care/setup", Ct))));
        Assert.Equal(2, BedsUsed(await Json(await clinic.Owner.GetAsync("/api/billing", Ct))));
    }

    [Fact]
    public async Task Invited_staff_get_clinic_roles()
    {
        var clinic = await kit.ClinicAsync("Roles Clinic");
        var expected = new (string ClinicRole, string MemberRole, string ClinicalRole, bool Clinical)[]
        {
            ("Doctor", "Staff", "Doctor", true),
            ("Nurse", "Staff", "Nurse", true),
            ("FrontDesk", "Staff", "FrontDesk", false),
            ("Cashier", "Accountant", "None", false),
            ("Admin", "Admin", "None", false),
        };
        foreach (var (clinicRole, memberRole, clinicalRole, clinical) in expected)
        {
            var (client, userId) = await kit.StaffAsync(clinic, clinicRole);
            var me = await Json(await client.GetAsync("/api/care/staff/me", Ct));
            Assert.Equal(memberRole, me.GetProperty("memberRole").GetString());
            Assert.Equal(clinicalRole, me.GetProperty("clinicalRole").GetString());
            Assert.Equal(clinical, me.GetProperty("isClinical").GetBoolean());
            Assert.Equal(userId, me.GetProperty("userId").GetGuid());
        }

        // Leadership sees every member with their clinical role.
        var staff = (await Json(await clinic.Owner.GetAsync("/api/care/staff", Ct))).EnumerateArray().ToList();
        Assert.Equal(6, staff.Count);
        Assert.Equal(["Doctor", "Doctor", "Nurse", "FrontDesk", "None", "None"], staff.Select(s => s.GetProperty("clinicalRole").GetString()));

        // Staff is done on the checklist once someone besides the director has joined.
        var setup = await Json(await clinic.Owner.GetAsync("/api/care/setup", Ct));
        Assert.True(setup.GetProperty("checklist").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "staff").GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task Only_leadership_sets_clinical_roles()
    {
        var clinic = await kit.ClinicAsync("Leadership Clinic");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (frontDesk, frontDeskId) = await kit.StaffAsync(clinic, "FrontDesk");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var body = new { clinicalRole = "Nurse", title = "Senior nurse", licenceNumber = "NMCN-123" };

        var refused = await doctor.PutAsJsonAsync($"/api/care/staff/{frontDeskId}", body, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/api/care/staff", Ct)).StatusCode);

        var saved = await Json(await admin.PutAsJsonAsync($"/api/care/staff/{frontDeskId}", body, Ct));
        Assert.Equal("Nurse", saved.GetProperty("clinicalRole").GetString());
        Assert.Equal("Senior nurse", saved.GetProperty("title").GetString());
        Assert.Equal("NMCN-123", saved.GetProperty("licenceNumber").GetString());
        var me = await Json(await frontDesk.GetAsync("/api/care/staff/me", Ct));
        Assert.Equal("Nurse", me.GetProperty("clinicalRole").GetString());
        Assert.True(me.GetProperty("isClinical").GetBoolean());
    }

    private const string AdminRoleRefused = "Only the medical director can change an admin's clinical role.";
    private const string CashierRefused = "A cashier can't have a clinical role.";
    private const string SelfRoleRefused = "Ask the medical director to change your role.";

    [Fact]
    public async Task Non_owner_cannot_change_their_own_clinical_profile()
    {
        var clinic = await kit.ClinicAsync("Self Profile Clinic");
        var (admin, adminId) = await kit.StaffAsync(clinic, "Admin");
        var (_, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var director = await StaffRowAsync(clinic, (await Json(await clinic.Owner.GetAsync("/api/care/staff/me", Ct))).GetProperty("userId").GetGuid());

        // Even keeping the role the same (a title edit) goes through the director.
        await AssertProblemAsync(await admin.PutAsJsonAsync($"/api/care/staff/{adminId}",
            new { clinicalRole = "None", title = "Practice manager", licenceNumber = (string?)null }, Ct), HttpStatusCode.Forbidden, SelfRoleRefused);
        Assert.Equal(JsonValueKind.Null, (await StaffRowAsync(clinic, adminId)).GetProperty("title").ValueKind);

        // The admin still edits others; the director edits anyone, including themselves.
        (await admin.PutAsJsonAsync($"/api/care/staff/{nurseId}", new { clinicalRole = "Nurse", title = "Ward nurse", licenceNumber = (string?)null }, Ct))
            .EnsureSuccessStatusCode();
        Assert.Equal("Practice manager", (await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/staff/{adminId}",
            new { clinicalRole = "None", title = "Practice manager", licenceNumber = (string?)null }, Ct))).GetProperty("title").GetString());
        Assert.Equal("Consultant", (await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/staff/{director.GetProperty("userId").GetGuid()}",
            new { clinicalRole = "Doctor", title = "Consultant", licenceNumber = (string?)null }, Ct))).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Clinic_profile_takes_registration_and_consultation_items()
    {
        var clinic = await kit.ClinicAsync("Profile Items Clinic");
        var other = await kit.ClinicAsync("Profile Items Other Clinic");
        var registration = await kit.PriceItemAsync(clinic, "Registration", "Registration", 200_000);
        var consultation = await kit.PriceItemAsync(clinic, "General consultation", "Consultation", 500_000);
        var drug = await kit.PriceItemAsync(clinic, "Paracetamol", "Drug", 20_000);
        var otherClinics = await kit.PriceItemAsync(other, "Registration", "Registration", 100_000);

        Task<HttpResponseMessage> Save(Guid? registrationPriceItemId, Guid? consultationPriceItemId) =>
            clinic.Owner.PutAsJsonAsync("/api/care/settings/profile", new { name = "Profile Items Clinic", registrationPriceItemId, consultationPriceItemId }, Ct);

        var saved = await Json(await Save(registration, consultation));
        Assert.Equal(registration, saved.GetProperty("registrationPriceItemId").GetGuid());
        Assert.Equal(consultation, saved.GetProperty("consultationPriceItemId").GetGuid());

        // Wrong category, another clinic's item, an unknown id or an inactive item: 400 on the field.
        async Task Refused(HttpResponseMessage r, string field)
        {
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
            Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("errors").TryGetProperty(field, out _));
        }
        await Refused(await Save(drug, consultation), "registrationPriceItemId");
        await Refused(await Save(registration, registration), "consultationPriceItemId");
        await Refused(await Save(otherClinics, consultation), "registrationPriceItemId");
        await Refused(await Save(registration, Guid.NewGuid()), "consultationPriceItemId");
        (await clinic.Owner.PutAsJsonAsync($"/api/care/price-list/{consultation}",
            new { name = "General consultation", category = "Consultation", priceMinor = 500_000, active = false }, Ct)).EnsureSuccessStatusCode();

        // Null clears them; choosing the now-inactive item again is refused.
        var cleared = await Json(await Save(null, null));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("registrationPriceItemId").ValueKind);
        await Refused(await Save(registration, consultation), "consultationPriceItemId");
    }

    [Fact]
    public async Task Profile_still_saves_after_its_configured_item_is_deactivated()
    {
        var clinic = await kit.ClinicAsync("Profile Stale Item Clinic");
        var registration = await kit.PriceItemAsync(clinic, "Registration", "Registration", 200_000);
        var consultation = await kit.PriceItemAsync(clinic, "General consultation", "Consultation", 500_000);
        (await clinic.Owner.PutAsJsonAsync("/api/care/settings/profile",
            new { name = "Profile Stale Item Clinic", registrationPriceItemId = registration, consultationPriceItemId = consultation }, Ct)).EnsureSuccessStatusCode();
        (await clinic.Owner.PutAsJsonAsync($"/api/care/price-list/{consultation}",
            new { name = "General consultation", category = "Consultation", priceMinor = 500_000, active = false }, Ct)).EnsureSuccessStatusCode();

        // Unchanged ids aren't re-validated, so an address edit still saves.
        var saved = await Json(await clinic.Owner.PutAsJsonAsync("/api/care/settings/profile", new
        {
            name = "Profile Stale Item Clinic", address = "1 Marina, Lagos", registrationPriceItemId = registration, consultationPriceItemId = consultation,
        }, Ct));
        Assert.Equal("1 Marina, Lagos", saved.GetProperty("address").GetString());
        Assert.Equal(consultation, saved.GetProperty("consultationPriceItemId").GetGuid());
    }

    [Fact]
    public async Task Price_list_checklist_needs_an_active_item()
    {
        var clinic = await kit.ClinicAsync("Checklist Prices Clinic");
        async Task<bool> Done() => (await Json(await clinic.Owner.GetAsync("/api/care/setup", Ct))).GetProperty("checklist").EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == "price-list").GetProperty("done").GetBoolean();

        Assert.False(await Done());
        var id = await kit.PriceItemAsync(clinic, "General consultation", "Consultation", 500_000);
        Assert.True(await Done());
        (await clinic.Owner.PutAsJsonAsync($"/api/care/price-list/{id}",
            new { name = "General consultation", category = "Consultation", priceMinor = 500_000, active = false }, Ct)).EnsureSuccessStatusCode();
        Assert.False(await Done());
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string detail)
    {
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        var body = await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        Assert.Equal(detail, body.GetProperty("detail").GetString());
    }

    private static async Task<JsonElement> StaffRowAsync(CareKit.Clinic clinic, Guid userId) =>
        (await Json(await clinic.Owner.GetAsync("/api/care/staff", Ct))).EnumerateArray().Single(s => s.GetProperty("userId").GetGuid() == userId);

    [Fact]
    public async Task Only_the_director_changes_an_admins_clinical_role()
    {
        var clinic = await kit.ClinicAsync("Admin Role Clinic");
        var (admin, adminId) = await kit.StaffAsync(clinic, "Admin");
        var (_, otherAdminId) = await kit.StaffAsync(clinic, "Admin");
        var (_, frontDeskId) = await kit.StaffAsync(clinic, "FrontDesk");
        var doctor = new { clinicalRole = "Doctor", title = (string?)null, licenceNumber = (string?)null };

        // Their own row is refused first by the self rule (Task 2 carry); another admin's role by the director-only rule.
        await AssertProblemAsync(await admin.PutAsJsonAsync($"/api/care/staff/{adminId}", doctor, Ct), HttpStatusCode.Forbidden, SelfRoleRefused);
        await AssertProblemAsync(await admin.PutAsJsonAsync($"/api/care/staff/{otherAdminId}", doctor, Ct), HttpStatusCode.Forbidden, AdminRoleRefused);
        Assert.Equal("None", (await StaffRowAsync(clinic, adminId)).GetProperty("clinicalRole").GetString());

        // An admin may still set a Staff member's clinical role; the director may set an admin's.
        Assert.Equal("Nurse", (await Json(await admin.PutAsJsonAsync($"/api/care/staff/{frontDeskId}",
            new { clinicalRole = "Nurse", title = (string?)null, licenceNumber = (string?)null }, Ct))).GetProperty("clinicalRole").GetString());
        Assert.Equal("Doctor", (await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/staff/{adminId}", doctor, Ct))).GetProperty("clinicalRole").GetString());

        // The same rule at invite: an admin can't invite an admin with a clinical role; the director can.
        await AssertProblemAsync(await admin.PostAsJsonAsync("/api/team/invites",
            new { email = $"adm-{Guid.NewGuid():N}@example.com", role = "Admin", clinicalRole = "Doctor" }, Ct), HttpStatusCode.BadRequest, AdminRoleRefused);
        (await clinic.Owner.PostAsJsonAsync("/api/team/invites",
            new { email = $"adm-{Guid.NewGuid():N}@example.com", role = "Admin", clinicalRole = "Doctor" }, Ct)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/team/invites",
            new { email = $"doc-{Guid.NewGuid():N}@example.com", role = "Staff", clinicalRole = "Doctor" }, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Moving_a_member_to_cashier_clears_their_clinical_role()
    {
        var clinic = await kit.ClinicAsync("Role Change Clinic");
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var membershipId = (await StaffRowAsync(clinic, doctorId)).GetProperty("membershipId").GetGuid();

        (await clinic.Owner.PatchAsJsonAsync($"/api/team/members/{membershipId}", new { role = "Accountant" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal("None", (await StaffRowAsync(clinic, doctorId)).GetProperty("clinicalRole").GetString());

        (await clinic.Owner.PatchAsJsonAsync($"/api/team/members/{membershipId}", new { role = "Staff" }, Ct)).EnsureSuccessStatusCode();
        var row = await StaffRowAsync(clinic, doctorId);
        Assert.Equal("None", row.GetProperty("clinicalRole").GetString());
        Assert.False(row.GetProperty("isClinical").GetBoolean());
    }

    private static async Task<Guid> MembershipIdAsync(CareKit.Clinic clinic, Guid userId) =>
        (await StaffRowAsync(clinic, userId)).GetProperty("membershipId").GetGuid();

    private static Task<HttpResponseMessage> ChangeRoleAsync(HttpClient client, Guid membershipId, string role) =>
        client.PatchAsJsonAsync($"/api/team/members/{membershipId}", new { role }, Ct);

    [Fact]
    public async Task Admin_cannot_change_their_own_role()
    {
        var clinic = await kit.ClinicAsync("Self Role Clinic");
        var (admin, adminId) = await kit.StaffAsync(clinic, "Admin");
        var membershipId = await MembershipIdAsync(clinic, adminId);

        await AssertProblemAsync(await ChangeRoleAsync(admin, membershipId, "Staff"), HttpStatusCode.Forbidden, "Ask the medical director to change your role.");
        var row = await StaffRowAsync(clinic, adminId);
        Assert.Equal("Admin", row.GetProperty("memberRole").GetString());
        Assert.Equal("None", row.GetProperty("clinicalRole").GetString());
    }

    [Fact]
    public async Task Admin_promoting_a_member_to_admin_clears_their_clinical_role()
    {
        var clinic = await kit.ClinicAsync("Promote Chain Clinic");
        var (adminA, _) = await kit.StaffAsync(clinic, "Admin");
        var (_, adminBId) = await kit.StaffAsync(clinic, "Admin");
        var bMembership = await MembershipIdAsync(clinic, adminBId);

        (await ChangeRoleAsync(adminA, bMembership, "Staff")).EnsureSuccessStatusCode();
        Assert.Equal("Doctor", (await Json(await adminA.PutAsJsonAsync($"/api/care/staff/{adminBId}",
            new { clinicalRole = "Doctor", title = (string?)null, licenceNumber = (string?)null }, Ct))).GetProperty("clinicalRole").GetString());
        (await ChangeRoleAsync(adminA, bMembership, "Admin")).EnsureSuccessStatusCode();

        var row = await StaffRowAsync(clinic, adminBId);
        Assert.Equal("Admin", row.GetProperty("memberRole").GetString());
        Assert.Equal("None", row.GetProperty("clinicalRole").GetString());
        Assert.False(row.GetProperty("isClinical").GetBoolean());
    }

    [Fact]
    public async Task Director_can_promote_a_doctor_to_admin_and_keep_the_role()
    {
        var clinic = await kit.ClinicAsync("Director Promote Clinic");
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (_, nurseId) = await kit.StaffAsync(clinic, "Nurse");

        (await ChangeRoleAsync(clinic.Owner, await MembershipIdAsync(clinic, doctorId), "Admin")).EnsureSuccessStatusCode();
        var row = await StaffRowAsync(clinic, doctorId);
        Assert.Equal("Admin", row.GetProperty("memberRole").GetString());
        Assert.Equal("Doctor", row.GetProperty("clinicalRole").GetString());
        Assert.True(row.GetProperty("isClinical").GetBoolean());

        // Or promote first and set the clinical role afterwards.
        (await ChangeRoleAsync(clinic.Owner, await MembershipIdAsync(clinic, nurseId), "Admin")).EnsureSuccessStatusCode();
        Assert.Equal("Doctor", (await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/staff/{nurseId}",
            new { clinicalRole = "Doctor", title = (string?)null, licenceNumber = (string?)null }, Ct))).GetProperty("clinicalRole").GetString());
    }

    [Fact]
    public async Task Cashier_cannot_hold_a_clinical_role()
    {
        var clinic = await kit.ClinicAsync("Cashier Clinic");
        var (_, cashierId) = await kit.StaffAsync(clinic, "Cashier");

        await AssertProblemAsync(await clinic.Owner.PostAsJsonAsync("/api/team/invites",
            new { email = $"cash-{Guid.NewGuid():N}@example.com", role = "Accountant", clinicalRole = "Doctor" }, Ct), HttpStatusCode.BadRequest, CashierRefused);
        await AssertProblemAsync(await clinic.Owner.PutAsJsonAsync($"/api/care/staff/{cashierId}",
            new { clinicalRole = "Nurse", title = (string?)null, licenceNumber = (string?)null }, Ct), HttpStatusCode.BadRequest, CashierRefused);
        Assert.Equal("None", (await StaffRowAsync(clinic, cashierId)).GetProperty("clinicalRole").GetString());
    }

    [Fact]
    public async Task Paid_plans_carry_their_limits()
    {
        var clinic = await kit.ClinicAsync("Plans Clinic");
        var plans = (await Json(await clinic.Owner.GetAsync("/api/billing", Ct))).GetProperty("plans").EnumerateArray()
            .ToDictionary(p => p.GetProperty("code").GetString()!, p => p.GetProperty("limits"));
        static long[] Limits(JsonElement l) =>
            [l.GetProperty("team_seats").GetInt64(), l.GetProperty("beds").GetInt64(), l.GetProperty("online_payments").GetInt64(), l.GetProperty("remove_branding").GetInt64()];
        Assert.Equal([3L, 0, 0, 0], Limits(plans["free"]));
        Assert.Equal([15L, 20, 1, 0], Limits(plans["clinic"]));
        Assert.Equal([60L, 150, 1, 1], Limits(plans["hospital"]));
    }

    [Fact]
    public async Task Clinical_role_on_an_invite_is_ignored_outside_care()
    {
        var school = await new SchoolKit(api).SchoolAsync("Invite Role School");
        var email = $"teacher-{Guid.NewGuid():N}@example.com";
        var invite = await Json(await school.Owner.PostAsJsonAsync("/api/team/invites", new { email, role = "Staff", clinicalRole = "Doctor" }, Ct));
        Assert.Equal(JsonValueKind.Null, invite.GetProperty("clinicalRole").ValueKind);
        var stored = await api.WithDbAsync(db => db.Invitations.IgnoreQueryFilters().FirstAsync(i => i.Email == email, Ct));
        Assert.Null(stored.ClinicalRole);
    }

    [Fact]
    public async Task Care_routes_need_a_care_business()
    {
        var (invoices, _) = await api.SignupInvoicesAsync("NotAClinic");
        Assert.Equal(HttpStatusCode.Forbidden, (await invoices.GetAsync("/api/care/setup", Ct)).StatusCode);
    }
}
