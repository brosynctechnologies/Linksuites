using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using static Saas.IntegrationTests.SchoolPortalKit;

namespace Saas.IntegrationTests;

/// <summary>
/// Privacy of the parent and student portals: a parent reaches only their own children, only published results, and nothing of another
/// school; a student reaches only themselves. Two schools, two families in school A and one in school B.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolPortalIsolationTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolPortalKit portal = new(api);

    private sealed record World(Setup A, Setup B, Family Ada, Family Xena, Family Bola);

    /// <summary>
    /// School A: Ada (parent A) and Xena (another family). School B: Bola (parent B). Every child has published results, an invoice,
    /// attendance, a timetable and a class announcement.
    /// </summary>
    private async Task<World> WorldAsync(string name)
    {
        var a = await portal.SetupAsync($"{name} A");
        var b = await portal.SetupAsync($"{name} B");
        var ada = await portal.FamilyAsync(a, "Ada", "Zed");
        var xena = await portal.FamilyAsync(a, "Xena", "Quill");
        var bola = await portal.FamilyAsync(b, "Bola", "Ige");
        foreach (var (s, kids) in new[] { (a, new[] { ada, xena }), (b, new[] { bola }) })
        {
            foreach (var kid in kids)
            {
                await portal.ScoreAsync(s, s.MathId, kid.EnrolmentId, 15, 15, 40);
                await portal.ScoreAsync(s, s.EnglishId, kid.EnrolmentId, 15, 15, 40);
            }
            await RegisterAsync(s, s.ClassId, Today.AddDays(-1), kids.Select(k => (k.EnrolmentId, "Present")).ToArray());
            await LockAsync(s);
            await PublishAsync(s);
            await BillAsync(s);
            await TimetableAsync(s.School.Owner, s.ClassId, Slot("Mon", 1, "08:00", "08:40", s.MathId));
            await Json(await s.School.Owner.PostAsJsonAsync("/api/school/announcements",
                new { title = $"Class news {s.School.Slug}", body = "Bring your books.", audience = "Classes", classIds = new[] { s.ClassId } }, Ct));
        }
        return new World(a, b, ada, xena, bola);
    }

    /// <summary>Every child-scoped GET of the parent portal for the child and term.</summary>
    private static string[] ChildRoutes(Guid studentId, Guid termId, Guid classId) =>
    [
        $"/api/parent/children/{studentId}",
        $"/api/parent/children/{studentId}/overview",
        $"/api/parent/children/{studentId}/results/{termId}",
        $"/api/parent/children/{studentId}/report-card/{termId}.pdf",
        $"/api/parent/children/{studentId}/report-card/{termId}.pdf?classId={classId}",
        $"/api/parent/children/{studentId}/attendance",
        $"/api/parent/children/{studentId}/invoices",
        $"/api/parent/children/{studentId}/timetable",
        $"/api/parent/children/{studentId}/announcements",
        $"/api/parent/children/{studentId}/assignments",
        $"/api/parent/children/{studentId}/threads",
    ];

    [Fact]
    public async Task Parents_only_see_their_own_children_through_every_portal_route()
    {
        var w = await WorldAsync("Isolation");

        // Their own child: every route answers.
        foreach (var url in ChildRoutes(w.Ada.StudentId, w.A.TermId, w.A.ClassId))
            Assert.True((await w.Ada.Parent.GetAsync(url, Ct)).IsSuccessStatusCode, url);
        foreach (var url in ChildRoutes(w.Bola.StudentId, w.B.TermId, w.B.ClassId))
            Assert.True((await w.Bola.Parent.GetAsync(url, Ct)).IsSuccessStatusCode, url);

        // Another family's child in the same school, a child of another school, and an unknown id: 404 everywhere, for both parents.
        var cases = new[]
        {
            (w.Ada.Parent, w.Xena.StudentId, w.A.TermId, w.A.ClassId),
            (w.Ada.Parent, w.Bola.StudentId, w.B.TermId, w.B.ClassId),
            (w.Ada.Parent, w.Bola.StudentId, w.A.TermId, w.A.ClassId),
            (w.Bola.Parent, w.Ada.StudentId, w.A.TermId, w.A.ClassId),
            (w.Bola.Parent, w.Xena.StudentId, w.A.TermId, w.A.ClassId),
            (w.Ada.Parent, Guid.NewGuid(), w.A.TermId, w.A.ClassId),
        };
        foreach (var (parent, studentId, termId, classId) in cases)
            foreach (var url in ChildRoutes(studentId, termId, classId))
                await ProblemAsync(await parent.GetAsync(url, Ct), HttpStatusCode.NotFound);

        // The parent's own child with another school's term or class: unknown, not another school's records.
        await ProblemAsync(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/results/{w.B.TermId}", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/report-card/{w.B.TermId}.pdf", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/report-card/{w.A.TermId}.pdf?classId={w.B.ClassId}", Ct), HttpStatusCode.NotFound);

        // Paying another family's invoice is refused the same way.
        var xenaInvoice = (await InvoiceOfAsync(w.A, w.Xena.StudentId)).GetProperty("id").GetGuid();
        await ProblemAsync(await w.Ada.Parent.PostAsJsonAsync($"/api/parent/children/{w.Xena.StudentId}/invoices/{xenaInvoice}/pay", new { amountMinor = 100_000 }, Ct), HttpStatusCode.NotFound);

        // What the own-child routes return never names another child.
        foreach (var url in ChildRoutes(w.Ada.StudentId, w.A.TermId, w.A.ClassId).Where(u => !u.Contains(".pdf")))
        {
            var body = await (await w.Ada.Parent.GetAsync(url, Ct)).Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain("Xena", body);
            Assert.DoesNotContain("Bola", body);
            Assert.DoesNotContain(w.Xena.StudentId.ToString(), body);
            Assert.DoesNotContain(w.Xena.EnrolmentId.ToString(), body);
            Assert.DoesNotContain(w.B.School.Slug, body);
        }
        var children = await Json(await w.Ada.Parent.GetAsync("/api/parent/children", Ct));
        Assert.Equal(w.Ada.StudentId, Assert.Single(children.EnumerateArray()).GetProperty("studentId").GetGuid());
    }

    [Fact]
    public async Task Students_only_see_themselves_and_nothing_of_another_school()
    {
        var w = await WorldAsync("Student Isolation");
        var ada = await portal.Kit.StudentLoginAsync(w.A.School, w.Ada.StudentId);
        var bola = await portal.Kit.StudentLoginAsync(w.B.School, w.Bola.StudentId);

        foreach (var (client, own, ownTerm, otherTerm, others) in new[]
                 {
                     (ada, w.Ada, w.A.TermId, w.B.TermId, new[] { "Xena", "Bola" }),
                     (bola, w.Bola, w.B.TermId, w.A.TermId, new[] { "Ada", "Xena" }),
                 })
        {
            foreach (var url in new[] { "/api/student/me", "/api/student/overview", $"/api/student/results/{ownTerm}", "/api/student/timetable", "/api/student/announcements", "/api/student/assignments", "/api/student/cbt/papers", "/api/student/play/word-search", "/api/student/play/teasers" })
            {
                var body = await (await client.GetAsync(url, Ct)).Content.ReadAsStringAsync(Ct);
                foreach (var name in others) Assert.DoesNotContain(name, body);
            }
            Assert.Contains(own.Name, (await Json(await client.GetAsync("/api/student/overview", Ct))).GetProperty("child").GetRawText());
            await ProblemAsync(await client.GetAsync($"/api/student/results/{otherTerm}", Ct), HttpStatusCode.NotFound);
            await ProblemAsync(await client.GetAsync($"/api/student/report-card/{otherTerm}.pdf", Ct), HttpStatusCode.NotFound);
            Assert.True((await client.GetAsync($"/api/student/report-card/{ownTerm}.pdf", Ct)).IsSuccessStatusCode);
        }

        // Student accounts are not parents: the parent portal does not open a child for them, even themselves.
        await ProblemAsync(await ada.GetAsync($"/api/parent/children/{w.Ada.StudentId}/overview", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await ada.GetAsync($"/api/parent/children/{w.Xena.StudentId}/results/{w.A.TermId}", Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unpublished_results_are_never_visible_even_after_publish_then_unpublish()
    {
        var w = await WorldAsync("Unpublish Isolation");
        var student = await portal.Kit.StudentLoginAsync(w.A.School, w.Ada.StudentId);
        await UnpublishAsync(w.A);
        // A staff view recomputes the (unpublished) rows; they still exist in the database.
        await Json(await w.A.School.Owner.GetAsync($"/api/school/results/{w.A.ClassId}/{w.A.TermId}", Ct));
        Assert.True(await api.WithDbAsync(db => db.TermResults.IgnoreQueryFilters().AnyAsync(r => r.EnrolmentId == w.Ada.EnrolmentId, Ct)));

        var overview = await Json(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/overview", Ct));
        var term = overview.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("termId").GetGuid() == w.A.TermId);
        Assert.False(term.GetProperty("published").GetBoolean());
        Assert.Empty(term.GetProperty("classes").EnumerateArray());
        Assert.Empty((await Json(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/results/{w.A.TermId}", Ct))).GetProperty("entries").EnumerateArray());
        await ProblemAsync(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/report-card/{w.A.TermId}.pdf", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/report-card/{w.A.TermId}.pdf?classId={w.A.ClassId}", Ct), HttpStatusCode.NotFound);
        Assert.Empty((await Json(await student.GetAsync($"/api/student/results/{w.A.TermId}", Ct))).GetProperty("entries").EnumerateArray());
        await ProblemAsync(await student.GetAsync($"/api/student/report-card/{w.A.TermId}.pdf", Ct), HttpStatusCode.NotFound);

        // School B is untouched by A's unpublish.
        Assert.Single((await Json(await w.Bola.Parent.GetAsync($"/api/parent/children/{w.Bola.StudentId}/results/{w.B.TermId}", Ct))).GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task A_suspended_school_closes_its_portal_but_not_another_schools()
    {
        var w = await WorldAsync("Suspended Isolation");
        await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == w.B.School.TenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenantStatus.Suspended), Ct));
        try
        {
            foreach (var url in ChildRoutes(w.Bola.StudentId, w.B.TermId, w.B.ClassId))
                Assert.Equal("This school's portal is unavailable.", await ProblemAsync(await w.Bola.Parent.GetAsync(url, Ct), HttpStatusCode.Forbidden));
            // Another parent still gets 404 for B's child: suspension reveals nothing about other families.
            await ProblemAsync(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Bola.StudentId}/overview", Ct), HttpStatusCode.NotFound);
            Assert.True((await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/overview", Ct)).IsSuccessStatusCode);
        }
        finally
        {
            await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == w.B.School.TenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenantStatus.Active), Ct));
        }
    }
}
