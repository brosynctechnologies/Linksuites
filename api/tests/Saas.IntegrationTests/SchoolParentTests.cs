using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>
/// SchoolLink guardian invites and parent accounts. A parent account reaches only the children of the guardian records it is linked to,
/// across schools, and nothing in a suspended school.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolParentTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task<string> ProblemAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == r.StatusCode, $"expected {(int)expected}, got {(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
    }

    private static string Phone() => "080" + Random.Shared.Next(10_000_000, 99_999_999);

    private static List<JsonElement> Children(JsonElement list) => list.EnumerateArray().ToList();

    private Task SetStatusAsync(Guid tenantId, TenantStatus status) =>
        api.WithDbAsync(db => db.Tenants.Where(t => t.Id == tenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status), Ct));

    [Fact]
    public async Task Guardian_invite_links_a_parent_account_to_their_children()
    {
        var school = await kit.SchoolAsync("Invite Academy");
        var classId = await kit.ClassAsync(school);
        var ada = await kit.StudentAsync(school, classId, "Ada");
        var tolu = await kit.StudentAsync(school, classId, "Tolu");
        var email = $"ngozi-{Guid.NewGuid():N}@example.com";
        var guardianId = await kit.GuardianAsync(school, ada, "Ngozi Obi", "0803 123 4567", email);
        Assert.Equal(guardianId, await kit.GuardianAsync(school, tolu, "Ngozi Obi", "08031234567", null)); // siblings share the guardian

        // Only leadership sends invites.
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsync($"/api/school/guardians/{guardianId}/invite", null, Ct)).StatusCode);

        var invite = await Json(await school.Owner.PostAsync($"/api/school/guardians/{guardianId}/invite", null, Ct));
        var link = invite.GetProperty("link").GetString()!;
        Assert.StartsWith("http://localhost:4200/invite/", link);
        var token = SchoolKit.TokenOf(link);
        Assert.True(token.Length >= 43, token); // 32 random bytes, URL-safe
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        var text = $"Hello Ngozi Obi, Invite Academy has set up SchoolLink for Ada Obi and Tolu Obi. Join here: {link}";
        Assert.Equal("https://wa.me/2348031234567?text=" + Uri.EscapeDataString(text), invite.GetProperty("whatsAppUrl").GetString());

        // Only the hash is stored; the guardian is emailed the link.
        var stored = await api.WithDbAsync(db => db.GuardianInvites.IgnoreQueryFilters().SingleAsync(i => i.GuardianId == guardianId, Ct));
        Assert.NotEqual(token, stored.TokenHash);
        Assert.DoesNotContain(token, stored.TokenHash);
        var mail = await api.WithDbAsync(db => db.Notifications.SingleAsync(n => n.TemplateKey == "school.guardian_invite" && n.Recipient == email, Ct));
        Assert.Equal(link, mail.Payload["link"]);
        Assert.Equal(school.TenantId, mail.TenantId);

        // The public invite page shows who it is for.
        var anonymous = api.CreateClient();
        var preview = await Json(await anonymous.GetAsync($"/api/school-invites/{token}", Ct));
        Assert.Equal("Invite Academy", preview.GetProperty("schoolName").GetString());
        Assert.Equal("Ngozi Obi", preview.GetProperty("guardianName").GetString());
        Assert.Equal(["Ada Obi", "Tolu Obi"], preview.GetProperty("children").EnumerateArray().Select(c => c.GetString()!).Order().ToArray());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/school-invites/not-a-real-token", Ct)).StatusCode);

        // Accepting needs a signed-in parent account.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/school-invites/{token}/accept", null, Ct)).StatusCode);
        var (parent, userId, parentEmail) = await kit.ParentAccountAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync("/api/school/students", Ct)).StatusCode); // no staff access
        await Json(await parent.PostAsync($"/api/school-invites/{token}/accept", null, Ct));
        Assert.Equal(userId, await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().Where(g => g.Id == guardianId).Select(g => g.UserId).SingleAsync(Ct)));

        var children = Children(await Json(await parent.GetAsync("/api/parent/children", Ct)));
        Assert.Equal(2, children.Count);
        Assert.All(children, c =>
        {
            Assert.Equal(school.TenantId, c.GetProperty("tenantId").GetGuid());
            Assert.Equal("Invite Academy", c.GetProperty("schoolName").GetString());
            Assert.Equal(school.Slug, c.GetProperty("schoolSlug").GetString());
            Assert.Equal("JSS 1A", c.GetProperty("className").GetString());
        });
        Assert.Equal(new[] { ada, tolu }.Order(), children.Select(c => c.GetProperty("studentId").GetGuid()).Order());
        var one = await Json(await parent.GetAsync($"/api/parent/children/{ada}", Ct));
        Assert.Equal("Ada Obi", one.GetProperty("name").GetString());
        Assert.Equal("JSS 1A", one.GetProperty("className").GetString());

        // The school sees the guardian has an account.
        var detail = await Json(await school.Owner.GetAsync($"/api/school/students/{ada}", Ct));
        Assert.True(detail.GetProperty("guardians")[0].GetProperty("hasAccount").GetBoolean());

        // The parent can come back with an emailed code instead of the password.
        (await anonymous.PostAsJsonAsync("/api/auth/otp/request", new { email = parentEmail }, Ct)).EnsureSuccessStatusCode();
        var code = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "otp" && n.Recipient == parentEmail).OrderByDescending(n => n.CreatedAt).FirstAsync(Ct))).Payload["code"];
        var signedIn = await Json(await anonymous.PostAsJsonAsync("/api/auth/parent/otp/verify", new { email = parentEmail, code }, Ct));
        Assert.Equal(JsonValueKind.Null, signedIn.GetProperty("tenant").ValueKind);
        var byCode = api.CreateClient();
        byCode.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        Assert.Equal(2, (await Json(await byCode.GetAsync("/api/parent/children", Ct))).GetArrayLength());

        // Final fix pass B (user decision I3, superseding the Task 15 R1 scope): the parent confirmed the address with the code at sign-up, so
        // a later code sign-in is no takeover. The password still signs in and the earlier session still works.
        var login = await Json(await anonymous.PostAsJsonAsync("/api/auth/parent/login", new { email = parentEmail, password = "password123" }, Ct));
        Assert.Equal(JsonValueKind.Null, login.GetProperty("tenant").ValueKind);
        Assert.Equal(2, (await Json(await byCode.GetAsync("/api/parent/children", Ct))).GetArrayLength()); // the code session works
        Assert.Equal(2, (await Json(await parent.GetAsync("/api/parent/children", Ct))).GetArrayLength()); // and so does the sign-up's
    }

    [Fact]
    public async Task Invite_is_single_use_and_expires_after_14_days()
    {
        var school = await kit.SchoolAsync("Single Use School");
        var classId = await kit.ClassAsync(school);
        var student = await kit.StudentAsync(school, classId, "Bisi");
        var guardianId = await kit.GuardianAsync(school, student, "Bisi Mother", Phone(), null); // no email: WhatsApp only

        var before = DateTimeOffset.UtcNow;
        var invite = await Json(await school.Owner.PostAsync($"/api/school/guardians/{guardianId}/invite", null, Ct));
        Assert.StartsWith("https://wa.me/234", invite.GetProperty("whatsAppUrl").GetString());
        var token = SchoolKit.TokenOf(invite.GetProperty("link").GetString()!);
        var expires = await api.WithDbAsync(db => db.GuardianInvites.IgnoreQueryFilters().Where(i => i.GuardianId == guardianId).Select(i => i.ExpiresAt).SingleAsync(Ct));
        Assert.InRange(expires, before.AddDays(14).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(14).AddMinutes(1));
        Assert.False(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "school.guardian_invite" && n.TenantId == school.TenantId, Ct)));

        // Single use: the same parent again, or anyone else, is refused.
        var (parent, userId, _) = await kit.ParentAccountAsync();
        await Json(await parent.PostAsync($"/api/school-invites/{token}/accept", null, Ct));
        Assert.Equal("This invitation was already used.", await ProblemAsync(await parent.PostAsync($"/api/school-invites/{token}/accept", null, Ct), HttpStatusCode.Conflict));
        var (other, _, _) = await kit.ParentAccountAsync();
        Assert.Equal("This invitation was already used.", await ProblemAsync(await other.PostAsync($"/api/school-invites/{token}/accept", null, Ct), HttpStatusCode.Conflict));
        await ProblemAsync(await api.CreateClient().GetAsync($"/api/school-invites/{token}", Ct), HttpStatusCode.Conflict);

        // A fresh invite for a guardian already linked to someone else can't move the children to another account.
        var again = await kit.InviteAsync(school, guardianId);
        Assert.Equal("This invitation was already used.", await ProblemAsync(await other.PostAsync($"/api/school-invites/{again}/accept", null, Ct), HttpStatusCode.Conflict));
        Assert.Equal(userId, await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().Where(g => g.Id == guardianId).Select(g => g.UserId).SingleAsync(Ct)));
        Assert.Empty(Children(await Json(await other.GetAsync("/api/parent/children", Ct))));

        // Expiry: 14 days.
        var second = await kit.StudentAsync(school, classId, "Chidi");
        var secondGuardian = await kit.GuardianAsync(school, second, "Chidi Father", Phone(), null);
        var expiring = await kit.InviteAsync(school, secondGuardian);
        await api.WithDbAsync(db => db.GuardianInvites.IgnoreQueryFilters().Where(i => i.GuardianId == secondGuardian)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)), Ct));
        var (late, _, _) = await kit.ParentAccountAsync();
        Assert.Contains("expired", await ProblemAsync(await late.PostAsync($"/api/school-invites/{expiring}/accept", null, Ct), HttpStatusCode.Conflict));
        Assert.Contains("expired", await ProblemAsync(await api.CreateClient().GetAsync($"/api/school-invites/{expiring}", Ct), HttpStatusCode.Conflict));

        // Re-inviting replaces earlier unused links.
        var first = await kit.InviteAsync(school, secondGuardian);
        var latest = await kit.InviteAsync(school, secondGuardian);
        Assert.Contains("expired", await ProblemAsync(await late.PostAsync($"/api/school-invites/{first}/accept", null, Ct), HttpStatusCode.Conflict));
        await Json(await late.PostAsync($"/api/school-invites/{latest}/accept", null, Ct));
        Assert.Single(Children(await Json(await late.GetAsync("/api/parent/children", Ct))));
    }

    [Fact]
    public async Task Invite_all_emails_every_guardian_without_an_account()
    {
        var school = await kit.SchoolAsync("Invite All School");
        var classId = await kit.ClassAsync(school);
        var a = await kit.StudentAsync(school, classId, "Efe");
        var b = await kit.StudentAsync(school, classId, "Femi");
        var c = await kit.StudentAsync(school, classId, "Gbenga");
        var withEmail = await kit.GuardianAsync(school, a, "Efe Mother", Phone(), $"efe-{Guid.NewGuid():N}@example.com");
        var linked = await kit.GuardianAsync(school, b, "Femi Mother", Phone(), $"femi-{Guid.NewGuid():N}@example.com");
        await kit.GuardianAsync(school, c, "Gbenga Mother", Phone(), null); // no email: invited one by one on WhatsApp
        await kit.ParentAsync(school, linked);

        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync("/api/school/guardians/invite-all", null, Ct)).StatusCode);

        var result = await Json(await school.Owner.PostAsync("/api/school/guardians/invite-all", null, Ct));
        Assert.Equal(1, result.GetProperty("invited").GetInt32());
        Assert.Equal(1, await api.WithDbAsync(db => db.GuardianInvites.IgnoreQueryFilters().CountAsync(i => i.GuardianId == withEmail, Ct)));

        // Running it again doesn't send a second invite to someone with a link that still works.
        Assert.Equal(0, (await Json(await school.Owner.PostAsync("/api/school/guardians/invite-all", null, Ct))).GetProperty("invited").GetInt32());
    }

    [Fact]
    public async Task Parents_only_see_their_own_children()
    {
        var schoolA = await kit.SchoolAsync("Privacy School A");
        var classA = await kit.ClassAsync(schoolA);
        var mine = await kit.StudentAsync(schoolA, classA, "Mine");
        var theirs = await kit.StudentAsync(schoolA, classA, "Theirs", "Ade");
        var (parentA, _) = await kit.ParentAsync(schoolA, await kit.GuardianAsync(schoolA, mine, "Parent A", Phone(), null));
        var (parentB, _) = await kit.ParentAsync(schoolA, await kit.GuardianAsync(schoolA, theirs, "Parent B", Phone(), null));

        var schoolB = await kit.SchoolAsync("Privacy School B");
        var classB = await kit.ClassAsync(schoolB);
        var elsewhere = await kit.StudentAsync(schoolB, classB, "Elsewhere");
        await kit.ParentAsync(schoolB, await kit.GuardianAsync(schoolB, elsewhere, "Parent C", Phone(), null));

        var children = Children(await Json(await parentA.GetAsync("/api/parent/children", Ct)));
        Assert.Equal(mine, Assert.Single(children).GetProperty("studentId").GetGuid());
        Assert.Equal(theirs, Assert.Single(Children(await Json(await parentB.GetAsync("/api/parent/children", Ct)))).GetProperty("studentId").GetGuid());

        // Another family's child, another school's child and an unknown id look the same: not found.
        await Json(await parentA.GetAsync($"/api/parent/children/{mine}", Ct));
        var unknown = await ProblemAsync(await parentA.GetAsync($"/api/parent/children/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound);
        Assert.Equal(unknown, await ProblemAsync(await parentA.GetAsync($"/api/parent/children/{theirs}", Ct), HttpStatusCode.NotFound));
        Assert.Equal(unknown, await ProblemAsync(await parentA.GetAsync($"/api/parent/children/{elsewhere}", Ct), HttpStatusCode.NotFound));

        // The parent portal is for parent accounts: not anonymous callers, staff business tokens or support's view-as.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/parent/children", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await schoolA.Owner.GetAsync("/api/parent/children", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await schoolA.Owner.GetAsync($"/api/parent/children/{mine}", Ct)).StatusCode);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{schoolA.TenantId}/view-as", new { reason = "Checking the parent portal report." }, Ct));
        var viewer = api.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewAs.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/parent/children", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/parent/children/{mine}", Ct)).StatusCode);

        // Staff tokens can't accept invites either: a teacher who is a parent signs in to their parent account.
        var school = schoolA;
        var staffChild = await kit.StudentAsync(school, classA, "Staffchild");
        var staffGuardian = await kit.GuardianAsync(school, staffChild, "Teacher Parent", Phone(), null);
        var staffToken = await kit.InviteAsync(school, staffGuardian);
        Assert.Equal(HttpStatusCode.Forbidden, (await school.Owner.PostAsync($"/api/school-invites/{staffToken}/accept", null, Ct)).StatusCode);

        // A child who leaves the school leaves the portal.
        (await schoolA.Owner.PostAsJsonAsync($"/api/school/students/{mine}/status", new { status = "Withdrawn", reason = "Moved away" }, Ct)).EnsureSuccessStatusCode();
        Assert.Empty(Children(await Json(await parentA.GetAsync("/api/parent/children", Ct))));
        Assert.Equal(unknown, await ProblemAsync(await parentA.GetAsync($"/api/parent/children/{mine}", Ct), HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task A_teacher_can_also_be_a_parent()
    {
        var school = await kit.SchoolAsync("Teacher Parent School");
        var classId = await kit.ClassAsync(school);
        var child = await kit.StudentAsync(school, classId, "Kemi");
        var (_, teacherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var teacherEmail = await api.WithDbAsync(db => db.Users.Where(u => u.Id == teacherUserId).Select(u => u.Email!).SingleAsync(Ct));
        var guardianId = await kit.GuardianAsync(school, child, "Kemi Teacher", Phone(), teacherEmail);
        var token = await kit.InviteAsync(school, guardianId);

        // The teacher signs in to the same account as a parent (no business attached) and accepts.
        var auth = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/parent/login", new { email = teacherEmail, password = "password123" }, Ct));
        var parent = api.CreateClient();
        parent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        await Json(await parent.PostAsync($"/api/school-invites/{token}/accept", null, Ct));
        Assert.Equal(child, Assert.Single(Children(await Json(await parent.GetAsync("/api/parent/children", Ct)))).GetProperty("studentId").GetGuid());
    }

    [Fact]
    public async Task One_parent_account_sees_children_in_two_schools()
    {
        var (parent, userId, email) = await kit.ParentAccountAsync();

        var first = await kit.SchoolAsync("Two Schools First");
        var firstChild = await kit.StudentAsync(first, await kit.ClassAsync(first), "Ife");
        var firstToken = await kit.InviteAsync(first, await kit.GuardianAsync(first, firstChild, "Same Parent", Phone(), email));
        await Json(await parent.PostAsync($"/api/school-invites/{firstToken}/accept", null, Ct));

        var second = await kit.SchoolAsync("Two Schools Second");
        var secondChild = await kit.StudentAsync(second, await kit.ClassAsync(second, "B"), "Jide");
        var secondToken = await kit.InviteAsync(second, await kit.GuardianAsync(second, secondChild, "Same Parent", Phone(), email));
        await Json(await parent.PostAsync($"/api/school-invites/{secondToken}/accept", null, Ct));

        var children = Children(await Json(await parent.GetAsync("/api/parent/children", Ct)));
        Assert.Equal(2, children.Count);
        var a = children.Single(c => c.GetProperty("studentId").GetGuid() == firstChild);
        var b = children.Single(c => c.GetProperty("studentId").GetGuid() == secondChild);
        Assert.Equal((first.TenantId, "Two Schools First", "JSS 1A"), (a.GetProperty("tenantId").GetGuid(), a.GetProperty("schoolName").GetString(), a.GetProperty("className").GetString()));
        Assert.Equal((second.TenantId, "Two Schools Second", "JSS 1B"), (b.GetProperty("tenantId").GetGuid(), b.GetProperty("schoolName").GetString(), b.GetProperty("className").GetString()));

        // Each child opens in its own school.
        Assert.Equal(second.TenantId, (await Json(await parent.GetAsync($"/api/parent/children/{secondChild}", Ct))).GetProperty("tenantId").GetGuid());
        Assert.Equal(first.TenantId, (await Json(await parent.GetAsync($"/api/parent/children/{firstChild}", Ct))).GetProperty("tenantId").GetGuid());
        Assert.Equal(2, await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().CountAsync(g => g.UserId == userId, Ct)));
    }

    [Fact]
    public async Task A_suspended_school_hides_its_children_from_the_portal()
    {
        var (parent, _, email) = await kit.ParentAccountAsync();
        var open = await kit.SchoolAsync("Open School");
        var openChild = await kit.StudentAsync(open, await kit.ClassAsync(open), "Open");
        await Json(await parent.PostAsync($"/api/school-invites/{await kit.InviteAsync(open, await kit.GuardianAsync(open, openChild, "Parent", Phone(), email))}/accept", null, Ct));
        var closed = await kit.SchoolAsync("Suspended School");
        var closedClass = await kit.ClassAsync(closed);
        var closedChild = await kit.StudentAsync(closed, closedClass, "Closed");
        await Json(await parent.PostAsync($"/api/school-invites/{await kit.InviteAsync(closed, await kit.GuardianAsync(closed, closedChild, "Parent", Phone(), email))}/accept", null, Ct));
        var sibling = await kit.StudentAsync(closed, closedClass, "Pending");
        var pendingToken = await kit.InviteAsync(closed, await kit.GuardianAsync(closed, sibling, "Other Parent", Phone(), null));
        Assert.Equal(2, (await Json(await parent.GetAsync("/api/parent/children", Ct))).GetArrayLength());

        await SetStatusAsync(closed.TenantId, TenantStatus.Suspended);
        try
        {
            Assert.Equal(openChild, Assert.Single(Children(await Json(await parent.GetAsync("/api/parent/children", Ct)))).GetProperty("studentId").GetGuid());
            Assert.Equal("This school's portal is unavailable.", await ProblemAsync(await parent.GetAsync($"/api/parent/children/{closedChild}", Ct), HttpStatusCode.Forbidden));
            await Json(await parent.GetAsync($"/api/parent/children/{openChild}", Ct));

            // Its invites can't be opened or accepted either.
            Assert.Equal("This school's portal is unavailable.", await ProblemAsync(await api.CreateClient().GetAsync($"/api/school-invites/{pendingToken}", Ct), HttpStatusCode.Forbidden));
            var (newcomer, _, _) = await kit.ParentAccountAsync();
            Assert.Equal("This school's portal is unavailable.", await ProblemAsync(await newcomer.PostAsync($"/api/school-invites/{pendingToken}/accept", null, Ct), HttpStatusCode.Forbidden));
        }
        finally
        {
            await SetStatusAsync(closed.TenantId, TenantStatus.Active);
        }
        Assert.Equal(2, (await Json(await parent.GetAsync("/api/parent/children", Ct))).GetArrayLength());
    }
}
