using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Exam sittings. Leadership writes a period and its sittings, then publishes it. A class cannot sit two exams at once, and a venue
/// cannot be booked twice; those are refused. An invigilator in two rooms at once is saved and warned about. Teachers, students and
/// parents see a period only after it is published. The bursar is refused before any period is looked up.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolExamTimetableTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);
    private static readonly DateOnly ExamDay = new(2026, 11, 16);
    private static readonly DateOnly NextDay = new(2026, 11, 17);

    private const string ClassOverlap = "This sitting overlaps another exam for the class.";
    private const string VenueOverlap = "This venue is already booked at that time.";
    private const string InvigilatorWarning = "This teacher is already invigilating another exam at that time.";
    private const string TimetableMissing = "This timetable was not found.";
    private const string LeadershipOnly = "You need the Owner or Admin role for this.";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == r.StatusCode, $"expected {(int)expected}, got {(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private sealed record Setup(
        SchoolKit.School School, Guid ClassId, Guid OtherClassId, Guid MathsId, Guid EnglishId, Guid OtherMathsId,
        Guid TermId, HttpClient Teacher, Guid TeacherStaffId, HttpClient OtherTeacher);

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var otherClassId = await kit.ClassAsync(school, "B");
        var (teacher, teacherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var (otherTeacher, otherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "English teacher");
        var teacherStaffId = await StaffIdAsync(school, teacherUserId);
        var otherStaffId = await StaffIdAsync(school, otherUserId);
        var maths = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var english = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "English", code = "ENG" }, Ct));
        var mathsLink = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = maths.GetProperty("id").GetGuid(), teacherStaffId }, Ct));
        var englishLink = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = english.GetProperty("id").GetGuid(), teacherStaffId = otherStaffId }, Ct));
        var otherLink = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{otherClassId}/subjects",
            new { subjectId = maths.GetProperty("id").GetGuid(), teacherStaffId }, Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        return new Setup(school, classId, otherClassId, mathsLink.GetProperty("id").GetGuid(), englishLink.GetProperty("id").GetGuid(),
            otherLink.GetProperty("id").GetGuid(), termId, teacher, teacherStaffId, otherTeacher);
    }

    private static object Period(Setup s, string name = "First term examination", string kind = "Examination") => new
    {
        termId = s.TermId,
        name,
        kind,
        start = ExamDay,
        end = NextDay,
    };

    private static object Sitting(Guid classSubjectId, string start, string end, int durationMinutes, string venue, Guid? invigilatorStaffId = null, DateOnly? date = null) => new
    {
        classSubjectId,
        date = date ?? ExamDay,
        start,
        end,
        durationMinutes,
        venue,
        instructions = "Answer every question. Show your working.",
        invigilatorStaffId,
    };

    private static string SittingsUrl(Guid periodId) => $"/api/school/exam-periods/{periodId}/sittings";

    [Fact]
    public async Task Class_clash_is_409()
    {
        var s = await SetupAsync("Class Clash School");
        var period = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", Period(s), Ct));
        var periodId = period.GetProperty("id").GetGuid();
        Assert.Equal("Examination", period.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, period.GetProperty("publishedAt").ValueKind);

        var first = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.MathsId, "09:00", "10:00", 60, "Hall A"), Ct));
        Assert.Equal(s.MathsId, first.GetProperty("classSubjectId").GetGuid());
        Assert.Empty(first.GetProperty("warnings").EnumerateArray());

        var boundary = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.EnglishId, "10:00", "11:00", 60, "Hall B"), Ct));
        Assert.Equal("10:00", boundary.GetProperty("start").GetString());

        var otherClass = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.OtherMathsId, "09:00", "10:00", 60, "Lab 1"), Ct));
        Assert.Equal(s.OtherMathsId, otherClass.GetProperty("classSubjectId").GetGuid());

        var nextDay = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.EnglishId, "09:00", "10:00", 60, "Hall C", date: NextDay), Ct));
        Assert.Equal("2026-11-17", nextDay.GetProperty("date").GetString());

        var clash = await ProblemAsync(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.EnglishId, "09:30", "10:30", 60, "Hall D"), Ct), HttpStatusCode.Conflict);
        Assert.Equal(ClassOverlap, clash.GetProperty("detail").GetString());

        var still = await Json(await s.School.Owner.GetAsync($"/api/school/exam-periods/{periodId}", Ct));
        Assert.Equal(4, still.GetProperty("sittings").GetArrayLength());
    }

    [Fact]
    public async Task Venue_clash_is_409()
    {
        var s = await SetupAsync("Venue Clash School");
        var periodId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", Period(s, "Continuous assessment", "Test"), Ct))).GetProperty("id").GetGuid();
        await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.MathsId, "09:00", "10:00", 60, "Hall A"), Ct));

        var boundary = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.OtherMathsId, "10:00", "11:00", 60, " hall a "), Ct));
        Assert.Equal("hall a", boundary.GetProperty("venue").GetString());

        var clash = await ProblemAsync(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.OtherMathsId, "09:00", "09:30", 30, "HALL A"), Ct), HttpStatusCode.Conflict);
        Assert.Equal(VenueOverlap, clash.GetProperty("detail").GetString());

        var otherRoom = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.OtherMathsId, "09:00", "09:30", 30, "Lab 2"), Ct));
        Assert.Equal("Lab 2", otherRoom.GetProperty("venue").GetString());
    }

    [Fact]
    public async Task Invigilator_overlap_is_a_warning()
    {
        var s = await SetupAsync("Invigilator Warning School");
        var periodId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", Period(s), Ct))).GetProperty("id").GetGuid();
        var first = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.MathsId, "09:00", "10:00", 60, "Hall A", s.TeacherStaffId), Ct));
        var firstId = first.GetProperty("id").GetGuid();
        Assert.Empty(first.GetProperty("warnings").EnumerateArray());

        var boundary = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.OtherMathsId, "10:00", "11:00", 60, "Hall B", s.TeacherStaffId), Ct));
        Assert.Empty(boundary.GetProperty("warnings").EnumerateArray());

        var response = await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.OtherMathsId, "09:15", "09:45", 30, "Hall C", s.TeacherStaffId), Ct);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var warning = Assert.Single(saved.GetProperty("warnings").EnumerateArray());
        Assert.Equal(firstId, warning.GetProperty("sittingId").GetGuid());
        Assert.Equal(InvigilatorWarning, warning.GetProperty("detail").GetString());

        var period = await Json(await s.School.Owner.GetAsync($"/api/school/exam-periods/{periodId}", Ct));
        Assert.Contains(period.GetProperty("sittings").EnumerateArray(), row => row.GetProperty("id").GetGuid() == saved.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Unpublished_timetable_is_hidden()
    {
        var s = await SetupAsync("Unpublished Timetable School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var otherStudentId = await kit.StudentAsync(s.School, s.OtherClassId, "Bola");
        var guardianId = await kit.GuardianAsync(s.School, studentId, "Ngozi Obi", "08031234567", $"guardian-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(s.School, guardianId);
        var student = await kit.StudentLoginAsync(s.School, studentId);
        var otherStudent = await kit.StudentLoginAsync(s.School, otherStudentId);

        var periodId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", Period(s), Ct))).GetProperty("id").GetGuid();
        var sitting = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.MathsId, "09:00", "11:00", 120, "Hall A", s.TeacherStaffId), Ct));

        var teacherHidden = await ProblemAsync(await s.Teacher.GetAsync($"/api/school/exam-periods/{periodId}", Ct), HttpStatusCode.NotFound);
        Assert.Equal(TimetableMissing, teacherHidden.GetProperty("detail").GetString());
        var sittingHidden = await ProblemAsync(await s.Teacher.GetAsync($"/api/school/exam-sittings/{sitting.GetProperty("id").GetGuid()}", Ct), HttpStatusCode.NotFound);
        Assert.Equal(TimetableMissing, sittingHidden.GetProperty("detail").GetString());
        Assert.Empty((await Json(await s.Teacher.GetAsync($"/api/school/exam-periods?termId={s.TermId}", Ct))).EnumerateArray());

        // Nothing published yet is an empty timetable, not an error.
        var studentHidden = await Json(await student.GetAsync("/api/student/exam-timetable", Ct));
        Assert.Empty(studentHidden.GetProperty("periods").EnumerateArray());
        var parentHidden = await Json(await parent.GetAsync($"/api/parent/children/{studentId}/exam-timetable", Ct));
        Assert.Empty(parentHidden.GetProperty("periods").EnumerateArray());

        var draft = await Json(await s.School.Owner.GetAsync($"/api/school/exam-periods/{periodId}", Ct));
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("publishedAt").ValueKind);
        Assert.Equal(sitting.GetProperty("id").GetGuid(), Assert.Single(draft.GetProperty("sittings").EnumerateArray()).GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/exam-periods/{periodId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/exam-periods/{Guid.NewGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/exam-periods?termId={s.TermId}", Ct)).StatusCode);

        var published = await Json(await s.School.Owner.PostAsync($"/api/school/exam-periods/{periodId}/publish", null, Ct));
        Assert.NotEqual(JsonValueKind.Null, published.GetProperty("publishedAt").ValueKind);
        Assert.Equal(periodId, published.GetProperty("id").GetGuid());

        var teacherView = await Json(await s.Teacher.GetAsync($"/api/school/exam-periods/{periodId}", Ct));
        Assert.Equal(sitting.GetProperty("id").GetGuid(), Assert.Single(teacherView.GetProperty("sittings").EnumerateArray()).GetProperty("id").GetGuid());
        var englishHidden = await ProblemAsync(await s.OtherTeacher.GetAsync($"/api/school/exam-periods/{periodId}", Ct), HttpStatusCode.NotFound);
        Assert.Equal(TimetableMissing, englishHidden.GetProperty("detail").GetString());

        var studentView = await Json(await student.GetAsync("/api/student/exam-timetable", Ct));
        var studentPeriod = Assert.Single(studentView.GetProperty("periods").EnumerateArray());
        Assert.Equal(periodId, studentPeriod.GetProperty("id").GetGuid());
        Assert.Equal("Mathematics", Assert.Single(studentPeriod.GetProperty("sittings").EnumerateArray()).GetProperty("subject").GetString());

        var parentView = await Json(await parent.GetAsync($"/api/parent/children/{studentId}/exam-timetable", Ct));
        Assert.Equal(periodId, Assert.Single(parentView.GetProperty("periods").EnumerateArray()).GetProperty("id").GetGuid());
        var otherHidden = await Json(await otherStudent.GetAsync("/api/student/exam-timetable", Ct));
        Assert.Empty(otherHidden.GetProperty("periods").EnumerateArray());

        var added = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.EnglishId, "12:00", "14:00", 120, "Hall B"), Ct));
        var after = await Json(await student.GetAsync("/api/student/exam-timetable", Ct));
        var same = Assert.Single(after.GetProperty("periods").EnumerateArray());
        Assert.Equal(periodId, same.GetProperty("id").GetGuid());
        Assert.Equal(2, same.GetProperty("sittings").GetArrayLength());
        Assert.Contains(same.GetProperty("sittings").EnumerateArray(), row => row.GetProperty("id").GetGuid() == added.GetProperty("id").GetGuid());

        var englishView = await Json(await s.OtherTeacher.GetAsync($"/api/school/exam-periods/{periodId}", Ct));
        Assert.Equal(added.GetProperty("id").GetGuid(), Assert.Single(englishView.GetProperty("sittings").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Teacher_cannot_post_a_sitting()
    {
        var s = await SetupAsync("Teacher Post School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var periodId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", Period(s), Ct))).GetProperty("id").GetGuid();

        foreach (var client in new[] { s.Teacher, bursar })
        {
            var period = await ProblemAsync(await client.PostAsJsonAsync("/api/school/exam-periods", Period(s, "Teacher period"), Ct), HttpStatusCode.Forbidden);
            Assert.Equal(LeadershipOnly, period.GetProperty("detail").GetString());
            var sitting = await ProblemAsync(await client.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.MathsId, "09:00", "10:00", 60, "Hall A"), Ct), HttpStatusCode.Forbidden);
            Assert.Equal(LeadershipOnly, sitting.GetProperty("detail").GetString());
            var publish = await ProblemAsync(await client.PostAsync($"/api/school/exam-periods/{periodId}/publish", null, Ct), HttpStatusCode.Forbidden);
            Assert.Equal(LeadershipOnly, publish.GetProperty("detail").GetString());
            var missing = await ProblemAsync(await client.PostAsJsonAsync(SittingsUrl(Guid.NewGuid()), Sitting(s.MathsId, "09:00", "10:00", 60, "Hall A"), Ct), HttpStatusCode.Forbidden);
            Assert.Equal(LeadershipOnly, missing.GetProperty("detail").GetString());
        }

        var untouched = await Json(await s.School.Owner.GetAsync($"/api/school/exam-periods/{periodId}", Ct));
        Assert.Equal(0, untouched.GetProperty("sittings").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, untouched.GetProperty("publishedAt").ValueKind);
    }

    [Fact]
    public async Task Edit_moves_a_sitting_and_frees_the_old_slot()
    {
        var s = await SetupAsync("Sitting Edit School");
        var periodId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", Period(s), Ct))).GetProperty("id").GetGuid();
        var wrong = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.MathsId, "09:00", "11:00", 120, "Hall A"), Ct));
        var wrongId = wrong.GetProperty("id").GetGuid();

        // Overlapping its own old time is not a clash.
        var moved = await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/exam-sittings/{wrongId}", Sitting(s.MathsId, "10:00", "12:00", 120, "Hall A"), Ct));
        Assert.Equal(wrongId, moved.GetProperty("id").GetGuid());
        Assert.Equal("10:00", moved.GetProperty("start").GetString());
        Assert.Equal("12:00", moved.GetProperty("end").GetString());

        // The old 09:00 slot is free for the class and for the venue.
        var english = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.EnglishId, "08:00", "10:00", 120, "Hall A"), Ct));
        Assert.Equal("08:00", english.GetProperty("start").GetString());

        var view = await Json(await s.School.Owner.GetAsync($"/api/school/exam-periods/{periodId}", Ct));
        Assert.Equal(2, view.GetProperty("sittings").GetArrayLength());
        Assert.Equal("10:00", view.GetProperty("sittings").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == wrongId).GetProperty("start").GetString());
    }

    [Fact]
    public async Task Edit_into_a_clash_is_409()
    {
        var s = await SetupAsync("Sitting Edit Clash School");
        var periodId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", Period(s), Ct))).GetProperty("id").GetGuid();
        await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.MathsId, "09:00", "10:00", 60, "Hall A"), Ct));
        var english = await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.EnglishId, "12:00", "13:00", 60, "Hall B"), Ct));
        var englishId = english.GetProperty("id").GetGuid();

        var classClash = await ProblemAsync(await s.School.Owner.PutAsJsonAsync($"/api/school/exam-sittings/{englishId}",
            Sitting(s.EnglishId, "09:30", "10:30", 60, "Hall B"), Ct), HttpStatusCode.Conflict);
        Assert.Equal(ClassOverlap, classClash.GetProperty("detail").GetString());
        var venueClash = await ProblemAsync(await s.School.Owner.PutAsJsonAsync($"/api/school/exam-sittings/{englishId}",
            Sitting(s.OtherMathsId, "09:30", "10:30", 60, "hall a"), Ct), HttpStatusCode.Conflict);
        Assert.Equal(VenueOverlap, venueClash.GetProperty("detail").GetString());

        var unchanged = await Json(await s.School.Owner.GetAsync($"/api/school/exam-sittings/{englishId}", Ct));
        Assert.Equal("12:00", unchanged.GetProperty("start").GetString());
        Assert.Equal(s.EnglishId, unchanged.GetProperty("classSubjectId").GetGuid());
    }

    [Fact]
    public async Task Delete_removes_a_sitting()
    {
        var s = await SetupAsync("Sitting Delete School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var periodId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", Period(s), Ct))).GetProperty("id").GetGuid();
        var sittingId = (await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.MathsId, "09:00", "10:00", 60, "Hall A"), Ct))).GetProperty("id").GetGuid();

        foreach (var client in new[] { s.Teacher, bursar })
        {
            Assert.Equal(LeadershipOnly, (await ProblemAsync(await client.DeleteAsync($"/api/school/exam-sittings/{sittingId}", Ct), HttpStatusCode.Forbidden)).GetProperty("detail").GetString());
            Assert.Equal(LeadershipOnly, (await ProblemAsync(await client.PutAsJsonAsync($"/api/school/exam-sittings/{sittingId}",
                Sitting(s.MathsId, "11:00", "12:00", 60, "Hall A"), Ct), HttpStatusCode.Forbidden)).GetProperty("detail").GetString());
        }

        Assert.Equal(HttpStatusCode.NoContent, (await s.School.Owner.DeleteAsync($"/api/school/exam-sittings/{sittingId}", Ct)).StatusCode);
        var view = await Json(await s.School.Owner.GetAsync($"/api/school/exam-periods/{periodId}", Ct));
        Assert.Equal(0, view.GetProperty("sittings").GetArrayLength());
        Assert.Equal(TimetableMissing, (await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/exam-sittings/{sittingId}", Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());

        // The slot is free again.
        await Json(await s.School.Owner.PostAsJsonAsync(SittingsUrl(periodId), Sitting(s.MathsId, "09:00", "10:00", 60, "Hall A"), Ct));

        var elsewhere = await SetupAsync("Sitting Delete Other School");
        var otherPeriod = (await Json(await elsewhere.School.Owner.PostAsJsonAsync("/api/school/exam-periods", Period(elsewhere), Ct))).GetProperty("id").GetGuid();
        var foreignId = (await Json(await elsewhere.School.Owner.PostAsJsonAsync(SittingsUrl(otherPeriod), Sitting(elsewhere.MathsId, "09:00", "10:00", 60, "Hall A"), Ct))).GetProperty("id").GetGuid();
        Assert.Equal(TimetableMissing, (await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/exam-sittings/{foreignId}", Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        Assert.Equal(TimetableMissing, (await ProblemAsync(await s.School.Owner.PutAsJsonAsync($"/api/school/exam-sittings/{foreignId}",
            Sitting(s.MathsId, "11:00", "12:00", 60, "Hall A"), Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        Assert.Equal(1, (await Json(await elsewhere.School.Owner.GetAsync($"/api/school/exam-periods/{otherPeriod}", Ct))).GetProperty("sittings").GetArrayLength());
    }

    [Fact]
    public async Task Foreign_id_is_404()
    {
        var s = await SetupAsync("Foreign Exam School");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var strangerId = await kit.StudentAsync(s.School, s.OtherClassId, "Bola");
        var guardianId = await kit.GuardianAsync(s.School, studentId, "Ngozi Obi", "08031234567", $"guardian-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(s.School, guardianId);

        var period = await ProblemAsync(await s.School.Owner.GetAsync($"/api/school/exam-periods/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound);
        Assert.Equal(TimetableMissing, period.GetProperty("detail").GetString());
        var sitting = await ProblemAsync(await s.School.Owner.PostAsJsonAsync(SittingsUrl(Guid.NewGuid()), Sitting(s.MathsId, "09:00", "10:00", 60, "Hall A"), Ct), HttpStatusCode.NotFound);
        Assert.Equal(TimetableMissing, sitting.GetProperty("detail").GetString());
        var publish = await ProblemAsync(await s.School.Owner.PostAsync($"/api/school/exam-periods/{Guid.NewGuid()}/publish", null, Ct), HttpStatusCode.NotFound);
        Assert.Equal(TimetableMissing, publish.GetProperty("detail").GetString());
        var one = await ProblemAsync(await s.School.Owner.GetAsync($"/api/school/exam-sittings/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound);
        Assert.Equal(TimetableMissing, one.GetProperty("detail").GetString());

        var child = await ProblemAsync(await parent.GetAsync($"/api/parent/children/{strangerId}/exam-timetable", Ct), HttpStatusCode.NotFound);
        Assert.Equal("Child was not found.", child.GetProperty("detail").GetString());
    }
}
