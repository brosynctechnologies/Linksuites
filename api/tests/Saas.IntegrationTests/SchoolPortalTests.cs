using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using UglyToad.PdfPig;
using static Saas.IntegrationTests.SchoolPortalKit;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink parent and student portals (overview, results, report cards, attendance, fees, timetable) and the timetable itself.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolPortalTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolPortalKit portal = new(api);
    private SchoolKit Kit => portal.Kit;
    public const string Held = "Results are held. Please see the school office.";

    private static string PdfText(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    private static async Task<string> CardTextAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "report cards must not be cached");
        return PdfText(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    private static JsonElement TermOf(JsonElement overview, Guid termId) =>
        overview.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("termId").GetGuid() == termId);

    [Fact]
    public async Task Parent_overview_shows_results_attendance_fees_and_timetable()
    {
        var s = await portal.SetupAsync("Overview Academy");
        var ada = await portal.FamilyAsync(s, "Ada", "Zed");
        var bayo = await portal.FamilyAsync(s, "Bayo", "Yusuf");
        await portal.ScoreAsync(s, s.MathId, ada.EnrolmentId, 20, 20, 50);
        await portal.ScoreAsync(s, s.EnglishId, ada.EnrolmentId, 10, 10, 50);
        await portal.ScoreAsync(s, s.MathId, bayo.EnrolmentId, 10, 10, 30);
        await portal.ScoreAsync(s, s.EnglishId, bayo.EnrolmentId, 10, 10, 30);
        await RegisterAsync(s, s.ClassId, Today.AddDays(-3), (ada.EnrolmentId, "Present"), (bayo.EnrolmentId, "Present"));
        await RegisterAsync(s, s.ClassId, Today.AddDays(-2), (ada.EnrolmentId, "Late"), (bayo.EnrolmentId, "Present"));
        await RegisterAsync(s, s.ClassId, Today.AddDays(-1), (ada.EnrolmentId, "Absent"), (bayo.EnrolmentId, "Present"));
        await LockAsync(s);
        await PublishAsync(s);
        await BillAsync(s, 7_500_000);
        await TimetableAsync(s.School.Owner, s.ClassId,
            Slot("Mon", 1, "08:00", "08:40", s.MathId), Slot("Mon", 2, "08:40", "09:20", s.EnglishId), Slot("Tue", 1, "08:00", "08:40", s.EnglishId));

        var overview = await Json(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/overview", Ct));
        var child = overview.GetProperty("child");
        Assert.Equal(ada.StudentId, child.GetProperty("studentId").GetGuid());
        Assert.Equal("Ada Zed", child.GetProperty("name").GetString());
        Assert.Equal("JSS 1A", child.GetProperty("className").GetString());
        Assert.Equal("Overview Academy", child.GetProperty("schoolName").GetString());

        // Every term of the session is listed; the current one is published and not held.
        var terms = overview.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(3, terms.Count);
        var current = TermOf(overview, s.TermId);
        Assert.True(current.GetProperty("published").GetBoolean());
        Assert.False(current.GetProperty("held").GetBoolean());
        var summary = Assert.Single(current.GetProperty("classes").EnumerateArray());
        Assert.Equal("JSS 1A", summary.GetProperty("className").GetString());
        Assert.Equal(1, summary.GetProperty("position").GetInt32());
        Assert.Equal(2, summary.GetProperty("classSize").GetInt32());
        Assert.Equal(80m, summary.GetProperty("average").GetDecimal());
        foreach (var other in terms.Where(t => t.GetProperty("termId").GetGuid() != s.TermId))
        {
            Assert.False(other.GetProperty("published").GetBoolean());
            Assert.Empty(other.GetProperty("classes").EnumerateArray());
        }

        // Attendance this term: present is present marks only. Late, absent and excused are their own counts.
        var attendance = overview.GetProperty("attendance");
        Assert.Equal(s.TermId, attendance.GetProperty("termId").GetGuid());
        Assert.Equal(1, attendance.GetProperty("present").GetInt32());
        Assert.Equal(1, attendance.GetProperty("absent").GetInt32());
        Assert.Equal(1, attendance.GetProperty("late").GetInt32());
        Assert.Equal(0, attendance.GetProperty("excused").GetInt32());
        Assert.Equal(3, attendance.GetProperty("open").GetInt32());

        var fees = overview.GetProperty("fees");
        Assert.Equal(7_500_000, fees.GetProperty("balance").GetInt64());
        var invoice = Assert.Single(fees.GetProperty("invoices").EnumerateArray());
        Assert.Equal(7_500_000, invoice.GetProperty("balance").GetInt64());

        var timetable = overview.GetProperty("timetable");
        Assert.Equal("JSS 1A", timetable.GetProperty("className").GetString());
        var slots = timetable.GetProperty("slots").EnumerateArray().ToList();
        Assert.Equal(3, slots.Count);
        Assert.Equal("Mon", slots[0].GetProperty("day").GetString());
        Assert.Equal(1, slots[0].GetProperty("period").GetInt32());
        Assert.Equal("08:00", slots[0].GetProperty("start").GetString());
        Assert.Equal("08:40", slots[0].GetProperty("end").GetString());
        Assert.Equal("Mathematics", slots[0].GetProperty("subject").GetString());

        // The results page of the term: the child's own figures and subjects only.
        var results = await Json(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/results/{s.TermId}", Ct));
        Assert.True(results.GetProperty("published").GetBoolean());
        var entry = Assert.Single(results.GetProperty("entries").EnumerateArray());
        Assert.Equal(160m, entry.GetProperty("total").GetDecimal());
        Assert.Equal(1, entry.GetProperty("position").GetInt32());
        Assert.Equal(1, entry.GetProperty("daysPresent").GetInt32());
        Assert.Equal(1, entry.GetProperty("daysAbsent").GetInt32());
        Assert.Equal(1, entry.GetProperty("daysLate").GetInt32());
        Assert.Equal(0, entry.GetProperty("daysExcused").GetInt32());
        Assert.Equal(3, entry.GetProperty("daysOpen").GetInt32());
        var subjects = entry.GetProperty("subjects").EnumerateArray().Select(x => x.GetProperty("subject").GetString()).ToList();
        Assert.Equal(["English", "Mathematics"], subjects);
        Assert.DoesNotContain("Bayo", results.GetRawText());

        var card = await CardTextAsync(ada.Parent, $"/api/parent/children/{ada.StudentId}/report-card/{s.TermId}.pdf");
        Assert.Contains("Ada Zed", card);
        Assert.DoesNotContain("Draft", card);
        Assert.DoesNotContain("Bayo", card);

        var direct = await Json(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/timetable", Ct));
        Assert.Equal(3, direct.GetProperty("slots").GetArrayLength());
        var att = await Json(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/attendance", Ct));
        Assert.Equal(1, att.GetProperty("present").GetInt32());
        Assert.Equal(1, att.GetProperty("absent").GetInt32());
        Assert.Equal(1, att.GetProperty("late").GetInt32());
        Assert.Equal(0, att.GetProperty("excused").GetInt32());
    }

    [Fact]
    public async Task Unpublished_results_are_invisible_to_parents_and_students()
    {
        var s = await portal.SetupAsync("Draft Academy");
        var ada = await portal.FamilyAsync(s, "Ada", "Zed");
        var student = await Kit.StudentLoginAsync(s.School, ada.StudentId);
        await portal.ScoreAsync(s, s.MathId, ada.EnrolmentId, 20, 20, 50);
        await portal.ScoreAsync(s, s.EnglishId, ada.EnrolmentId, 10, 10, 50);
        await LockAsync(s);
        // The staff side has computed (draft) results for the class.
        await Json(await s.School.Owner.GetAsync($"/api/school/results/{s.ClassId}/{s.TermId}", Ct));

        var parentResults = $"/api/parent/children/{ada.StudentId}/results/{s.TermId}";
        var parentCard = $"/api/parent/children/{ada.StudentId}/report-card/{s.TermId}.pdf";
        var studentResults = $"/api/student/results/{s.TermId}";
        var studentCard = $"/api/student/report-card/{s.TermId}.pdf";

        async Task AssertInvisibleAsync()
        {
            var overview = await Json(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/overview", Ct));
            Assert.False(TermOf(overview, s.TermId).GetProperty("published").GetBoolean());
            Assert.Empty(TermOf(overview, s.TermId).GetProperty("classes").EnumerateArray());
            var results = await Json(await ada.Parent.GetAsync(parentResults, Ct));
            Assert.False(results.GetProperty("published").GetBoolean());
            Assert.Empty(results.GetProperty("entries").EnumerateArray());
            await ProblemAsync(await ada.Parent.GetAsync(parentCard, Ct), HttpStatusCode.NotFound);

            var mine = await Json(await student.GetAsync("/api/student/overview", Ct));
            Assert.False(TermOf(mine, s.TermId).GetProperty("published").GetBoolean());
            Assert.Empty(TermOf(mine, s.TermId).GetProperty("classes").EnumerateArray());
            var own = await Json(await student.GetAsync(studentResults, Ct));
            Assert.Empty(own.GetProperty("entries").EnumerateArray());
            await ProblemAsync(await student.GetAsync(studentCard, Ct), HttpStatusCode.NotFound);
        }

        await AssertInvisibleAsync();

        await PublishAsync(s);
        var published = await Json(await ada.Parent.GetAsync(parentResults, Ct));
        Assert.Single(published.GetProperty("entries").EnumerateArray());
        Assert.Contains("Ada Zed", await CardTextAsync(ada.Parent, parentCard));
        Assert.Single((await Json(await student.GetAsync(studentResults, Ct))).GetProperty("entries").EnumerateArray());
        Assert.Contains("Ada Zed", await CardTextAsync(student, studentCard));

        // Unpublishing hides them again, even though the rows are still stored.
        await UnpublishAsync(s);
        await AssertInvisibleAsync();
    }

    [Fact]
    public async Task Results_hold_blocks_results_until_fees_are_paid()
    {
        var s = await portal.SetupAsync("Hold Portal Academy");
        var ada = await portal.FamilyAsync(s, "Ada", "Zed");
        var student = await Kit.StudentLoginAsync(s.School, ada.StudentId);
        await portal.ScoreAsync(s, s.MathId, ada.EnrolmentId, 20, 20, 50);
        await portal.ScoreAsync(s, s.EnglishId, ada.EnrolmentId, 10, 10, 50);
        await LockAsync(s);
        await PublishAsync(s);
        await BillAsync(s);

        // A balance, but the hold is off: visible.
        await Json(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/results/{s.TermId}", Ct));

        await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/finance", new { resultsHoldForUnpaidFees = true }, Ct));
        Assert.Equal(Held, await ProblemAsync(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/results/{s.TermId}", Ct), HttpStatusCode.Forbidden));
        Assert.Equal(Held, await ProblemAsync(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/report-card/{s.TermId}.pdf", Ct), HttpStatusCode.Forbidden));
        Assert.Equal(Held, await ProblemAsync(await student.GetAsync($"/api/student/results/{s.TermId}", Ct), HttpStatusCode.Forbidden));
        Assert.Equal(Held, await ProblemAsync(await student.GetAsync($"/api/student/report-card/{s.TermId}.pdf", Ct), HttpStatusCode.Forbidden));

        var overview = await Json(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/overview", Ct));
        var term = TermOf(overview, s.TermId);
        Assert.True(term.GetProperty("published").GetBoolean());
        Assert.True(term.GetProperty("held").GetBoolean());
        Assert.Empty(term.GetProperty("classes").EnumerateArray()); // no figures while held
        Assert.True(TermOf(await Json(await student.GetAsync("/api/student/overview", Ct)), s.TermId).GetProperty("held").GetBoolean());

        await PayInFullAsync(s, ada.StudentId);
        var results = await Json(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/results/{s.TermId}", Ct));
        Assert.Single(results.GetProperty("entries").EnumerateArray());
        Assert.Contains("Ada Zed", await CardTextAsync(ada.Parent, $"/api/parent/children/{ada.StudentId}/report-card/{s.TermId}.pdf"));
        Assert.Single((await Json(await student.GetAsync($"/api/student/results/{s.TermId}", Ct))).GetProperty("entries").EnumerateArray());
        var after = TermOf(await Json(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/overview", Ct)), s.TermId);
        Assert.False(after.GetProperty("held").GetBoolean());
        Assert.Single(after.GetProperty("classes").EnumerateArray());
    }

    [Fact]
    public async Task Student_sees_only_their_own_results()
    {
        var s = await portal.SetupAsync("Own Results Academy");
        var ada = await portal.FamilyAsync(s, "Ada", "Zed");
        var bayo = await portal.FamilyAsync(s, "Bayo", "Yusuf");
        await portal.ScoreAsync(s, s.MathId, ada.EnrolmentId, 20, 20, 50);
        await portal.ScoreAsync(s, s.MathId, bayo.EnrolmentId, 10, 10, 30);
        await LockAsync(s);
        await PublishAsync(s);
        await TimetableAsync(s.School.Owner, s.ClassId, Slot("Wed", 3, "10:00", "10:40", s.MathId));
        var student = await Kit.StudentLoginAsync(s.School, bayo.StudentId);

        var overview = await Json(await student.GetAsync("/api/student/overview", Ct));
        Assert.Equal("Bayo Yusuf", overview.GetProperty("child").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("fees").ValueKind); // fees are for parents
        Assert.Equal(2, Assert.Single(TermOf(overview, s.TermId).GetProperty("classes").EnumerateArray()).GetProperty("position").GetInt32());

        var results = await Json(await student.GetAsync($"/api/student/results/{s.TermId}", Ct));
        var entry = Assert.Single(results.GetProperty("entries").EnumerateArray());
        Assert.Equal(50m, entry.GetProperty("total").GetDecimal());
        Assert.DoesNotContain("Ada", results.GetRawText());
        Assert.DoesNotContain(ada.EnrolmentId.ToString(), results.GetRawText());

        var card = await CardTextAsync(student, $"/api/student/report-card/{s.TermId}.pdf");
        Assert.Contains("Bayo Yusuf", card);
        Assert.DoesNotContain("Ada Zed", card);

        var timetable = await Json(await student.GetAsync("/api/student/timetable", Ct));
        var slot = Assert.Single(timetable.GetProperty("slots").EnumerateArray());
        Assert.Equal("Wed", slot.GetProperty("day").GetString());

        // A term of another school is unknown here.
        var other = await portal.SetupAsync("Other Own Academy");
        await ProblemAsync(await student.GetAsync($"/api/student/results/{other.TermId}", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await student.GetAsync($"/api/student/report-card/{other.TermId}.pdf", Ct), HttpStatusCode.NotFound);

        // Staff and parent tokens can't use the student portal.
        await ProblemAsync(await s.School.Owner.GetAsync("/api/student/overview", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await ada.Parent.GetAsync("/api/student/overview", Ct), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Report_card_urls_for_another_child_are_404()
    {
        var s = await portal.SetupAsync("Card Url Academy");
        var ada = await portal.FamilyAsync(s, "Ada", "Zed");
        var bayo = await portal.FamilyAsync(s, "Bayo", "Yusuf");
        await portal.ScoreAsync(s, s.MathId, ada.EnrolmentId, 20, 20, 50);
        await portal.ScoreAsync(s, s.MathId, bayo.EnrolmentId, 10, 10, 30);
        await LockAsync(s);
        await PublishAsync(s);

        Assert.Contains("Ada Zed", await CardTextAsync(ada.Parent, $"/api/parent/children/{ada.StudentId}/report-card/{s.TermId}.pdf"));
        // Another family's child (by student id, or by enrolment id used as a student id), and an unknown id.
        foreach (var id in new[] { bayo.StudentId, bayo.EnrolmentId, Guid.NewGuid() })
        {
            await ProblemAsync(await ada.Parent.GetAsync($"/api/parent/children/{id}/report-card/{s.TermId}.pdf", Ct), HttpStatusCode.NotFound);
            await ProblemAsync(await ada.Parent.GetAsync($"/api/parent/children/{id}/report-card/{s.TermId}.pdf?classId={s.ClassId}", Ct), HttpStatusCode.NotFound);
            await ProblemAsync(await ada.Parent.GetAsync($"/api/parent/children/{id}/results/{s.TermId}", Ct), HttpStatusCode.NotFound);
        }
        // The staff report card route is not open to parents.
        Assert.NotEqual(HttpStatusCode.OK, (await ada.Parent.GetAsync($"/api/school/report-cards/{bayo.EnrolmentId}/{s.TermId}.pdf", Ct)).StatusCode);
        // A class the child never had a published row in.
        var classB = await Kit.ClassAsync(s.School, "B");
        await ProblemAsync(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/report-card/{s.TermId}.pdf?classId={classB}", Ct), HttpStatusCode.NotFound);
        // An unknown term.
        await ProblemAsync(await ada.Parent.GetAsync($"/api/parent/children/{ada.StudentId}/report-card/{Guid.NewGuid()}.pdf", Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Transferred_child_has_one_entry_and_one_card_per_class()
    {
        var s = await portal.SetupAsync("Transfer Portal Academy");
        var classB = await Kit.ClassAsync(s.School, "B");
        var physics = await portal.ClassSubjectAsync(s.School, classB, "Physics", "PHY");
        var moe = await portal.FamilyAsync(s, "Moe", "Mover");
        await portal.ScoreAsync(s, s.MathId, moe.EnrolmentId, 10, 10, 30);
        await Json(await s.School.Owner.GetAsync($"/api/school/results/{s.ClassId}/{s.TermId}", Ct));
        await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/students/{moe.StudentId}/transfer", new { classId = classB }, Ct));
        await portal.ScoreAsync(s, physics, moe.EnrolmentId, 20, 20, 55);
        await LockAsync(s);
        await PublishAsync(s);
        await LockAsync(s, classB);
        await PublishAsync(s, classB);

        var results = await Json(await moe.Parent.GetAsync($"/api/parent/children/{moe.StudentId}/results/{s.TermId}", Ct));
        var names = results.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("className").GetString()).OrderBy(n => n).ToList();
        Assert.Equal(["JSS 1A", "JSS 1B"], names);
        var overview = await Json(await moe.Parent.GetAsync($"/api/parent/children/{moe.StudentId}/overview", Ct));
        Assert.Equal(2, TermOf(overview, s.TermId).GetProperty("classes").GetArrayLength());

        var url = $"/api/parent/children/{moe.StudentId}/report-card/{s.TermId}.pdf";
        var ambiguous = await moe.Parent.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        Assert.Contains("Choose the class.", await ambiguous.Content.ReadAsStringAsync(Ct));
        var a = await CardTextAsync(moe.Parent, $"{url}?classId={s.ClassId}");
        Assert.Contains("Mathematics", a);
        Assert.DoesNotContain("Physics", a);
        var b = await CardTextAsync(moe.Parent, $"{url}?classId={classB}");
        Assert.Contains("Physics", b);

        // Only B's results unpublished: A's card stays, B's goes.
        await UnpublishAsync(s, classB);
        Assert.Contains("Mathematics", await CardTextAsync(moe.Parent, url));
        await ProblemAsync(await moe.Parent.GetAsync($"{url}?classId={classB}", Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Timetable_warns_on_teacher_clash()
    {
        var s = await portal.SetupAsync("Timetable Academy");
        var classB = await Kit.ClassAsync(s.School, "B");
        var mathB = await portal.ClassSubjectAsync(s.School, classB, "Mathematics", "MTH");
        var (teacher, teacherUser) = await Kit.StaffAsync(s.School, MemberRole.Staff, "Maths Teacher");
        var staffId = (await Json(await s.School.Owner.GetAsync("/api/school/staff", Ct))).EnumerateArray()
            .Single(x => x.GetProperty("userId").GetGuid() == teacherUser).GetProperty("staffId").GetGuid();
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/class-subjects/{s.MathId}/teacher", new { staffId }, Ct));
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/class-subjects/{mathB}/teacher", new { staffId }, Ct));

        var first = await TimetableAsync(s.School.Owner, s.ClassId, Slot("Mon", 1, "08:00", "08:40", s.MathId), Slot("Mon", 2, "08:40", "09:20", s.EnglishId));
        Assert.Empty(first.GetProperty("warnings").EnumerateArray());
        Assert.Equal(2, first.GetProperty("slots").GetArrayLength());

        // The same teacher in B at the same day and period: saved, with a warning.
        var second = await TimetableAsync(s.School.Owner, classB, Slot("Mon", 1, "08:00", "08:40", mathB));
        Assert.Single(second.GetProperty("slots").EnumerateArray());
        var warning = Assert.Single(second.GetProperty("warnings").EnumerateArray());
        Assert.Equal("Maths Teacher Staff", warning.GetProperty("teacher").GetString());
        Assert.Equal("Mon", warning.GetProperty("day").GetString());
        Assert.Equal(1, warning.GetProperty("period").GetInt32());
        Assert.Equal("JSS 1A", warning.GetProperty("otherClass").GetString());
        // Reading A's timetable shows the clash too.
        var readA = await Json(await s.School.Owner.GetAsync($"/api/school/timetable/{s.ClassId}", Ct));
        Assert.Equal("JSS 1B", Assert.Single(readA.GetProperty("warnings").EnumerateArray()).GetProperty("otherClass").GetString());

        // Saving replaces the class's slots.
        var replaced = await TimetableAsync(s.School.Owner, s.ClassId, Slot("Fri", 4, "11:00", "11:40", s.EnglishId));
        Assert.Equal("Fri", Assert.Single(replaced.GetProperty("slots").EnumerateArray()).GetProperty("day").GetString());
        Assert.Empty(replaced.GetProperty("warnings").EnumerateArray());

        // Validation.
        async Task BadAsync(params object[] slots) =>
            await ProblemAsync(await s.School.Owner.PutAsJsonAsync($"/api/school/timetable/{s.ClassId}", slots, Ct), HttpStatusCode.BadRequest);
        await BadAsync(Slot("Mon", 1, "08:00", "08:40", mathB)); // B's subject in A
        await BadAsync(Slot("Mon", 1, "08:40", "08:00", s.MathId)); // start after end
        await BadAsync(Slot("Mon", 1, "08:00", "08:00", s.MathId)); // start equals end
        await BadAsync(Slot("Mon", 1, "08:00", "08:40", s.MathId), Slot("Mon", 1, "08:00", "08:40", s.EnglishId)); // same day and period
        await BadAsync(Slot("Sun", 1, "08:00", "08:40", s.MathId));
        await BadAsync(Slot("Mon", 0, "08:00", "08:40", s.MathId));
        await BadAsync(Slot("Mon", 13, "08:00", "08:40", s.MathId));
        await BadAsync(Slot("Mon", 1, "8am", "08:40", s.MathId));
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/timetable/{s.ClassId}", new[] { Slot("Sat", 12, "13:00", "13:40", s.MathId) }, Ct));
        // A failed save changed nothing; the last good one stands.
        Assert.Equal("Sat", Assert.Single((await Json(await s.School.Owner.GetAsync($"/api/school/timetable/{s.ClassId}", Ct))).GetProperty("slots").EnumerateArray()).GetProperty("day").GetString());

        // Only leadership saves; a teacher reads the classes they teach.
        await ProblemAsync(await teacher.PutAsJsonAsync($"/api/school/timetable/{s.ClassId}", Array.Empty<object>(), Ct), HttpStatusCode.Forbidden);
        await Json(await teacher.GetAsync($"/api/school/timetable/{s.ClassId}", Ct));
        var classC = await Kit.ClassAsync(s.School, "C");
        await ProblemAsync(await teacher.GetAsync($"/api/school/timetable/{classC}", Ct), HttpStatusCode.Forbidden);
        // Another school's class is unknown.
        var other = await portal.SetupAsync("Other Timetable Academy");
        await ProblemAsync(await s.School.Owner.GetAsync($"/api/school/timetable/{other.ClassId}", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await s.School.Owner.PutAsJsonAsync($"/api/school/timetable/{other.ClassId}", Array.Empty<object>(), Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Timetable_save_needs_a_body_and_the_bursar_cannot_read_it()
    {
        var s = await portal.SetupAsync("Timetable Body Academy");
        var url = $"/api/school/timetable/{s.ClassId}";
        await TimetableAsync(s.School.Owner, s.ClassId, Slot("Mon", 1, "08:00", "08:40", s.MathId));

        // A missing or null body is a mistake, not "clear the week": 400 and nothing deleted.
        var missing = await s.School.Owner.PutAsync(url, null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("Send the timetable slots.", await missing.Content.ReadAsStringAsync(Ct));
        var nullBody = await s.School.Owner.PutAsync(url, new StringContent("null", System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, nullBody.StatusCode);
        Assert.Contains("Send the timetable slots.", await nullBody.Content.ReadAsStringAsync(Ct));
        Assert.Single((await Json(await s.School.Owner.GetAsync(url, Ct))).GetProperty("slots").EnumerateArray());

        // An explicit empty list clears it.
        var cleared = await TimetableAsync(s.School.Owner, s.ClassId);
        Assert.Empty(cleared.GetProperty("slots").EnumerateArray());
        Assert.Empty((await Json(await s.School.Owner.GetAsync(url, Ct))).GetProperty("slots").EnumerateArray());

        // The bursar reads students, not timetables.
        var (bursar, _) = await Kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        await ProblemAsync(await bursar.GetAsync(url, Ct), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_screens_get_login_flags_guardians_without_email_and_their_own_role()
    {
        var s = await portal.SetupAsync("Helpers Academy");
        var withLogin = await Kit.StudentAsync(s.School, s.ClassId, "Lola", "Login");
        var withoutLogin = await Kit.StudentAsync(s.School, s.ClassId, "Nora", "None");
        await Kit.StudentLoginAsync(s.School, withLogin);

        var list = (await Json(await s.School.Owner.GetAsync($"/api/school/students?classId={s.ClassId}", Ct))).EnumerateArray().ToList();
        Assert.True(list.Single(x => x.GetProperty("id").GetGuid() == withLogin).GetProperty("hasLogin").GetBoolean());
        Assert.False(list.Single(x => x.GetProperty("id").GetGuid() == withoutLogin).GetProperty("hasLogin").GetBoolean());
        Assert.True((await Json(await s.School.Owner.GetAsync($"/api/school/students/{withLogin}", Ct))).GetProperty("hasLogin").GetBoolean());

        // Guardians with no email in the class: phone and children in one call.
        var phoneOnly = await Kit.GuardianAsync(s.School, withLogin, "Phone Only", "08031234567", null);
        await Kit.GuardianAsync(s.School, withoutLogin, "Phone Only", "08031234567", null); // the same guardian, a sibling
        await Kit.GuardianAsync(s.School, withoutLogin, "Has Email", null, $"has-email-{Guid.NewGuid():N}@example.com");
        var classB = await Kit.ClassAsync(s.School, "B");
        var elsewhere = await Kit.StudentAsync(s.School, classB, "Else", "Where");
        await Kit.GuardianAsync(s.School, elsewhere, "Other Class", "08039999999", null);

        var guardians = (await Json(await s.School.Owner.GetAsync($"/api/school/guardians?withoutEmail=true&classId={s.ClassId}", Ct))).EnumerateArray().ToList();
        var g = Assert.Single(guardians);
        Assert.Equal(phoneOnly, g.GetProperty("guardianId").GetGuid());
        Assert.Equal("Phone Only", g.GetProperty("fullName").GetString());
        Assert.Equal("+2348031234567", g.GetProperty("phone").GetString());
        Assert.False(g.GetProperty("hasAccount").GetBoolean());
        var children = g.GetProperty("children").EnumerateArray().Select(c => c.GetProperty("name").GetString()).OrderBy(n => n).ToList();
        Assert.Equal(["Lola Login", "Nora None"], children);
        Assert.Equal("JSS 1A", g.GetProperty("children")[0].GetProperty("className").GetString());
        // Without the email filter, both of the class's guardians.
        Assert.Equal(2, (await Json(await s.School.Owner.GetAsync($"/api/school/guardians?classId={s.ClassId}", Ct))).GetArrayLength());

        var (bursar, _) = await Kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        await Json(await bursar.GetAsync($"/api/school/guardians?withoutEmail=true&classId={s.ClassId}", Ct));
        var (teacher, _) = await Kit.StaffAsync(s.School, MemberRole.Staff, "Teacher");
        await ProblemAsync(await teacher.GetAsync($"/api/school/guardians?withoutEmail=true&classId={s.ClassId}", Ct), HttpStatusCode.Forbidden);
        var other = await portal.SetupAsync("Other Helpers Academy");
        Assert.Empty((await Json(await s.School.Owner.GetAsync($"/api/school/guardians?withoutEmail=true&classId={other.ClassId}", Ct))).EnumerateArray());

        // The signed-in staff member.
        var me = await Json(await teacher.GetAsync("/api/school/staff/me", Ct));
        Assert.Equal("Staff", me.GetProperty("role").GetString());
        Assert.Equal("Teacher", me.GetProperty("jobTitle").GetString());
        var teacherStaffId = (await Json(await s.School.Owner.GetAsync("/api/school/staff", Ct))).EnumerateArray()
            .Single(x => x.GetProperty("role").GetString() == "Staff").GetProperty("staffId").GetGuid();
        Assert.Equal(teacherStaffId, me.GetProperty("staffId").GetGuid());
        var owner = await Json(await s.School.Owner.GetAsync("/api/school/staff/me", Ct));
        Assert.Equal("Owner", owner.GetProperty("role").GetString());
        Assert.Equal("Proprietor", owner.GetProperty("jobTitle").GetString());
        Assert.Equal("Accountant", (await Json(await bursar.GetAsync("/api/school/staff/me", Ct))).GetProperty("role").GetString());
    }
}
