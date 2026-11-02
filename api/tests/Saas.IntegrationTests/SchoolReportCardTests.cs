using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Common;
using Saas.Application.Courses;
using Saas.Application.School;
using Saas.Domain.Core;
using SkiaSharp;
using UglyToad.PdfPig;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink report cards: the PDF for one student, the class bundle, branding uploads and the public logo link.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolReportCardTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid TermId, Guid MathId, Guid EnglishId, Guid[] Components);

    private async Task<Guid> ClassSubjectAsync(SchoolKit.School school, Guid classId, string name, string code)
    {
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name, code }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = subject.GetProperty("id").GetGuid() }, Ct));
        return link.GetProperty("id").GetGuid();
    }

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var math = await ClassSubjectAsync(school, classId, "Mathematics", "MTH");
        var english = await ClassSubjectAsync(school, classId, "English", "ENG");
        var termId = await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60))
                    .SetProperty(t => t.NextTermStarts, new DateOnly(2027, 1, 11)), Ct);
            return await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
        });
        var grid = await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={math}&termId={termId}", Ct));
        var components = grid.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        return new Setup(school, classId, termId, math, english, components);
    }

    private async Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    private async Task ScoreAsync(Setup s, Guid classSubjectId, Guid enrolmentId, decimal ca1, decimal ca2, decimal exam)
    {
        var entries = new[] { (s.Components[0], ca1), (s.Components[1], ca2), (s.Components[2], exam) }
            .Select(c => new { enrolmentId, componentId = c.Item1, value = (decimal?)c.Item2 }).ToArray();
        (await s.School.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId, termId = s.TermId, entries }, Ct)).EnsureSuccessStatusCode();
    }

    private async Task TakeRegisterAsync(Setup s, DateOnly date, params (Guid Enrolment, string Status)[] marks) =>
        (await s.School.Owner.PutAsJsonAsync($"/api/school/attendance/{s.ClassId}/{date:yyyy-MM-dd}",
            new { clientId = Guid.NewGuid().ToString(), marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status }).ToArray() }, Ct)).EnsureSuccessStatusCode();

    /// <summary>Ada is 1st (160), Bayo 2nd (150), Chi 3rd (50, Mathematics only).</summary>
    private async Task<(Guid Ada, Guid Bayo, Guid Chi)> ScoredClassAsync(Setup s)
    {
        var ada = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Ada", "Zed"));
        var bayo = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Bayo", "Yusuf"));
        var chi = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Chi", "Xu"));
        await ScoreAsync(s, s.MathId, ada, 20, 20, 50);
        await ScoreAsync(s, s.EnglishId, ada, 10, 10, 50);
        await ScoreAsync(s, s.MathId, bayo, 20, 20, 45);
        await ScoreAsync(s, s.EnglishId, bayo, 15, 10, 40);
        await ScoreAsync(s, s.MathId, chi, 10, 10, 30);
        return (ada, bayo, chi);
    }

    private static string CardUrl(Guid enrolmentId, Guid termId) => $"/api/school/report-cards/{enrolmentId}/{termId}.pdf";

    /// <summary>All the text of the PDF with words separated by single spaces, page after page.</summary>
    private static string PdfText(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    private static string Squash(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static async Task<byte[]> PdfAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "report cards must not be cached");
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        return bytes;
    }

    private static MultipartFormDataContent ImageForm(byte[] bytes, string contentType = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", "image" } };
    }

    private static byte[] PngOf(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public async Task Report_card_pdf_renders_with_results_and_attendance()
    {
        var s = await SetupAsync("Card Academy");
        var (ada, bayo, chi) = await ScoredClassAsync(s);
        await TakeRegisterAsync(s, Today.AddDays(-2), (ada, "Present"), (bayo, "Present"), (chi, "Present"));
        await TakeRegisterAsync(s, Today.AddDays(-1), (ada, "Absent"), (bayo, "Late"), (chi, "Present"));
        (await s.School.Owner.PutAsJsonAsync($"/api/school/results/{s.ClassId}/{s.TermId}/remarks",
            new { enrolmentId = bayo, teacherRemark = "Works steadily and asks good questions.", principalRemark = "Keep it up." }, Ct)).EnsureSuccessStatusCode();

        var draft = PdfText(await PdfAsync(s.School.Owner, CardUrl(bayo, s.TermId)));
        Assert.Contains("Card Academy", draft);
        Assert.Contains("Bayo Yusuf", draft);
        Assert.Contains("Mathematics", draft);
        Assert.Contains("English", draft);
        Assert.Contains("2nd of 3", draft);
        Assert.Contains("150", draft);
        Assert.Contains("75.00", draft); // average to 2 dp
        Assert.Contains("Present 1, late 1, absent 0, excused 0 of 2 days", draft);
        Assert.Contains("Works steadily and asks good questions.", draft);
        Assert.Contains("Keep it up.", draft);
        Assert.Contains("Next term begins 11 January 2027", draft);
        Assert.Contains("Draft — not published", draft);
        Assert.Contains("Powered by SchoolLink", draft); // Free plan

        // After lock and publish the card is the frozen published result, without the draft line.
        (await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/publish", null, Ct)).EnsureSuccessStatusCode();
        var published = PdfText(await PdfAsync(s.School.Owner, CardUrl(bayo, s.TermId)));
        Assert.DoesNotContain("Draft — not published", published);
        Assert.Contains("2nd of 3", published);
        Assert.Contains("Bayo Yusuf", published);
    }

    [Fact]
    public async Task Premium_report_cards_have_no_branding()
    {
        var s = await SetupAsync("Premium Cards");
        var (ada, _, _) = await ScoredClassAsync(s);
        await api.MakeProAsync(s.School.TenantId, "school", "premium");

        var text = PdfText(await PdfAsync(s.School.Owner, CardUrl(ada, s.TermId)));
        Assert.Contains("1st of 3", text);
        Assert.DoesNotContain("Powered by SchoolLink", text);
    }

    [Fact]
    public async Task Only_leadership_and_the_class_teacher_can_open_a_report_card_and_other_schools_get_404()
    {
        var s = await SetupAsync("Access Cards");
        var (ada, _, _) = await ScoredClassAsync(s);
        var (classTeacher, classTeacherUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Class teacher");
        var (other, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Other teacher");
        var staffId = (await Json(await s.School.Owner.GetAsync("/api/school/staff", Ct))).EnumerateArray()
            .Single(x => x.GetProperty("userId").GetGuid() == classTeacherUser).GetProperty("staffId").GetGuid();
        (await s.School.Owner.PutAsJsonAsync($"/api/school/classes/{s.ClassId}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();

        await PdfAsync(classTeacher, CardUrl(ada, s.TermId));
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(CardUrl(ada, s.TermId), Ct)).StatusCode);

        var stranger = await kit.SchoolAsync("Stranger Cards");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Owner.GetAsync(CardUrl(ada, s.TermId), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.School.Owner.GetAsync(CardUrl(Guid.NewGuid(), s.TermId), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_long_name_and_many_subjects_still_render()
    {
        var s = await SetupAsync("Crowded Cards");
        var longName = string.Join(' ', Enumerable.Repeat("Oluwadamilolatemitope", 3)); // 62 characters with spaces
        var student = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/students",
            new { admissionNo = $"ADM-{Guid.NewGuid().ToString("N")[..8]}", firstName = longName[..29], lastName = longName[30..], gender = "Male", classId = s.ClassId }, Ct));
        var enrolment = await EnrolmentAsync(s.School, student.GetProperty("id").GetGuid());
        await ScoreAsync(s, s.MathId, enrolment, 15, 15, 40);
        var names = new List<string> { "Mathematics" };
        for (var i = 1; i <= 15; i++)
        {
            var name = i == 15 ? "Entrepreneurship and vocational studies with a very long title" : $"Subject{i:00}";
            names.Add(name);
            var id = await ClassSubjectAsync(s.School, s.ClassId, name, $"S{i:00}");
            await ScoreAsync(s, id, enrolment, 10 + i % 5, 12, 30 + i);
        }

        var pdf = await PdfAsync(s.School.Owner, CardUrl(enrolment, s.TermId));
        var text = Squash(PdfText(pdf));
        Assert.Contains(Squash(longName[..29]), text);
        Assert.Contains(Squash(longName[30..]), text);
        // A wrapped cell interleaves with its neighbours in extracted text, so the long title is checked by its first word.
        foreach (var name in names) Assert.Contains(Squash(name.Split(' ')[0]), text);
        using var doc = PdfDocument.Open(pdf);
        Assert.True(doc.NumberOfPages >= 1);
    }

    [Fact]
    public async Task The_last_term_of_a_session_has_no_next_term_line()
    {
        var s = await SetupAsync("Last Term Cards");
        var (ada, _, _) = await ScoredClassAsync(s);
        var lastTerm = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters()
            .Where(t => t.TenantId == s.School.TenantId && t.Number == 3).Select(t => t.Id).FirstAsync(Ct));
        // Nobody has scores in the third term yet; the card still builds from the computed, empty term. The provisioned third term has no next start.
        var text = PdfText(await PdfAsync(s.School.Owner, CardUrl(ada, lastTerm)));
        Assert.Contains("Ada Zed", text);
        Assert.DoesNotContain("Next term begins", text);
    }

    [Fact]
    public async Task Class_bundle_is_generated_by_the_job_and_downloadable()
    {
        var s = await SetupAsync("Bundle Academy");
        var (ada, bayo, _) = await ScoredClassAsync(s);
        var (requester, requesterUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Bundle teacher");
        var (other, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Bystander");
        var staffId = (await Json(await s.School.Owner.GetAsync("/api/school/staff", Ct))).EnumerateArray()
            .Single(x => x.GetProperty("userId").GetGuid() == requesterUser).GetProperty("staffId").GetGuid();
        (await s.School.Owner.PutAsJsonAsync($"/api/school/classes/{s.ClassId}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();

        var bundleUrl = $"/api/school/report-cards/{s.ClassId}/{s.TermId}/bundle";
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync(bundleUrl, null, Ct)).StatusCode);
        var id = (await Json(await requester.PostAsync(bundleUrl, null, Ct))).GetProperty("id").GetGuid();

        var queued = await Json(await requester.GetAsync($"/api/school/report-cards/bundles/{id}", Ct));
        Assert.Equal("Queued", queued.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, queued.GetProperty("url").ValueKind);

        // The queue's job (Task 20) calls this; the test calls it directly.
        await api.AsTenantAsync<ReportCardService, bool>(s.School.TenantId, async svc => { await svc.BuildBundleAsync(id, Ct); return true; });

        var ready = await Json(await requester.GetAsync($"/api/school/report-cards/bundles/{id}", Ct));
        Assert.Equal("Ready", ready.GetProperty("status").GetString());
        var url = ready.GetProperty("url").GetString()!;
        Assert.Contains("/api/school/report-cards/bundles/", url);

        // The leadership reads it too; a teacher who did not request it cannot.
        Assert.Equal("Ready", (await Json(await s.School.Owner.GetAsync($"/api/school/report-cards/bundles/{id}", Ct))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/school/report-cards/bundles/{id}", Ct)).StatusCode);
        var stranger = await kit.SchoolAsync("Bundle Stranger");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Owner.GetAsync($"/api/school/report-cards/bundles/{id}", Ct)).StatusCode);

        // The signed link downloads the PDF without a sign-in; a tampered link does not.
        var download = await api.CreateClient().GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var text = PdfText(await download.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("Ada Zed", text);
        Assert.Contains("Bayo Yusuf", text);
        Assert.Contains("Chi Xu", text);
        Assert.Contains("1st of 3", text);
        Assert.Contains("3rd of 3", text);
        var tampered = await api.CreateClient().GetAsync(url[..^2] + (url[^1] == 'a' ? "bb" : "aa"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, tampered.StatusCode);
        Assert.NotEqual(Guid.Empty, ada);
        Assert.NotEqual(Guid.Empty, bayo);
    }

    [Fact]
    public async Task Branding_uploads_are_resized_private_and_shown_on_the_public_page_and_the_card()
    {
        var s = await SetupAsync("Brand Academy");
        var (ada, _, _) = await ScoredClassAsync(s);
        var publicUrl = $"/api/school/{s.School.Slug}/public";
        Assert.Equal(JsonValueKind.Null, (await Json(await api.CreateClient().GetAsync(publicUrl, Ct))).GetProperty("logoUrl").ValueKind);

        var profile = await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/profile",
            new { name = "Brand Academy College", motto = "Light and truth", address = "1 School Road, Ikeja" }, Ct));
        Assert.Equal("Brand Academy College", profile.GetProperty("name").GetString());

        var logo = await Json(await s.School.Owner.PutAsync("/api/school/settings/logo", ImageForm(PngOf(1600, 1200)), Ct));
        Assert.NotEqual(JsonValueKind.Null, logo.GetProperty("logoUrl").ValueKind);
        await Json(await s.School.Owner.PutAsync("/api/school/settings/signature", ImageForm(PngOf(1500, 900)), Ct));

        var (logoKey, signatureKey) = await api.WithDbAsync(async db =>
        {
            var p = await db.SchoolProfiles.IgnoreQueryFilters().FirstAsync(x => x.TenantId == s.School.TenantId, Ct);
            return (p.LogoKey!, p.SignatureKey!);
        });
        Assert.StartsWith("private/", logoKey);
        Assert.StartsWith("private/", signatureKey);
        using (var scope = api.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await using var logoStream = (await storage.OpenReadAsync(logoKey, Ct))!;
            using var logoBitmap = SKBitmap.Decode(logoStream);
            Assert.True(logoBitmap.Width <= 600 && logoBitmap.Height <= 600, $"{logoBitmap.Width}x{logoBitmap.Height}");
            Assert.Equal(600, Math.Max(logoBitmap.Width, logoBitmap.Height));
            await using var signatureStream = (await storage.OpenReadAsync(signatureKey, Ct))!;
            using var signatureBitmap = SKBitmap.Decode(signatureStream);
            Assert.True(signatureBitmap.Width <= 600 && signatureBitmap.Height <= 200, $"{signatureBitmap.Width}x{signatureBitmap.Height}");
        }

        var pub = await Json(await api.CreateClient().GetAsync(publicUrl, Ct));
        Assert.Equal("Brand Academy College", pub.GetProperty("name").GetString());
        var link = pub.GetProperty("logoUrl").GetString()!;
        Assert.Contains("/api/school/logos/", link);
        var image = await api.CreateClient().GetAsync(link, Ct);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.NotEmpty(await image.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync(link[..^2] + (link[^1] == 'a' ? "bb" : "aa"), Ct)).StatusCode);

        var text = PdfText(await PdfAsync(s.School.Owner, CardUrl(ada, s.TermId)));
        Assert.Contains("Brand Academy College", text);
        Assert.Contains("Light and truth", text);
        Assert.Contains("1 School Road, Ikeja", text);

        // Not an image, too large a type, or the wrong role.
        Assert.Equal(HttpStatusCode.BadRequest, (await s.School.Owner.PutAsync("/api/school/settings/logo", ImageForm("not an image"u8.ToArray()), Ct)).StatusCode);
        var (teacher, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Brand teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsync("/api/school/settings/logo", ImageForm(PngOf(10, 10)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync("/api/school/settings/profile", new { name = "Hijack" }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await s.School.Owner.DeleteAsync("/api/school/settings/logo", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.School.Owner.DeleteAsync("/api/school/settings/signature", Ct)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(await api.CreateClient().GetAsync(publicUrl, Ct))).GetProperty("logoUrl").ValueKind);
        await PdfAsync(s.School.Owner, CardUrl(ada, s.TermId)); // still renders without them
    }

    // ---------- Fix round 1 ----------

    private async Task<bool> StoredAsync(string key)
    {
        using var scope = api.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using var stream = await storage.OpenReadAsync(key, Ct);
        return stream is not null;
    }

    [Fact]
    public async Task A_student_who_changed_class_has_a_card_per_class()
    {
        var s = await SetupAsync("Mover Academy");
        var classB = await kit.ClassAsync(s.School, "B");
        var physics = await ClassSubjectAsync(s.School, classB, "Physics", "PHY");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Moe", "Mover");
        var enrolment = await EnrolmentAsync(s.School, studentId);
        await ScoreAsync(s, s.MathId, enrolment, 10, 10, 30);
        (await s.School.Owner.GetAsync($"/api/school/results/{s.ClassId}/{s.TermId}", Ct)).EnsureSuccessStatusCode(); // class A's row
        (await s.School.Owner.PostAsJsonAsync($"/api/school/students/{studentId}/transfer", new { classId = classB }, Ct)).EnsureSuccessStatusCode();
        await ScoreAsync(s, physics, enrolment, 20, 20, 55);
        (await s.School.Owner.GetAsync($"/api/school/results/{classB}/{s.TermId}", Ct)).EnsureSuccessStatusCode(); // class B's row

        var a = PdfText(await PdfAsync(s.School.Owner, $"{CardUrl(enrolment, s.TermId)}?classId={s.ClassId}"));
        Assert.Contains("JSS 1A", a);
        Assert.Contains("Mathematics", a);
        Assert.DoesNotContain("Physics", a);
        var b = PdfText(await PdfAsync(s.School.Owner, $"{CardUrl(enrolment, s.TermId)}?classId={classB}"));
        Assert.Contains("JSS 1B", b);
        Assert.Contains("Physics", b);
        Assert.DoesNotContain("Mathematics", b);

        var ambiguous = await s.School.Owner.GetAsync(CardUrl(enrolment, s.TermId), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        Assert.Contains("Choose the class.", await ambiguous.Content.ReadAsStringAsync(Ct));

        // Access follows the class used: B's class teacher reads B's card but not A's.
        var (teacherB, teacherUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Teacher of B");
        var staffId = (await Json(await s.School.Owner.GetAsync("/api/school/staff", Ct))).EnumerateArray()
            .Single(x => x.GetProperty("userId").GetGuid() == teacherUser).GetProperty("staffId").GetGuid();
        (await s.School.Owner.PutAsJsonAsync($"/api/school/classes/{classB}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();
        await PdfAsync(teacherB, $"{CardUrl(enrolment, s.TermId)}?classId={classB}");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacherB.GetAsync($"{CardUrl(enrolment, s.TermId)}?classId={s.ClassId}", Ct)).StatusCode);

        // A class the student never belonged to has no card.
        var classC = await kit.ClassAsync(s.School, "C");
        Assert.Equal(HttpStatusCode.NotFound, (await s.School.Owner.GetAsync($"{CardUrl(enrolment, s.TermId)}?classId={classC}", Ct)).StatusCode);

        // A's bundle still holds the student who moved out.
        var id = (await Json(await s.School.Owner.PostAsync($"/api/school/report-cards/{s.ClassId}/{s.TermId}/bundle", null, Ct))).GetProperty("id").GetGuid();
        await api.AsTenantAsync<ReportCardService, bool>(s.School.TenantId, async svc => { await svc.BuildBundleAsync(id, Ct); return true; });
        var url = (await Json(await s.School.Owner.GetAsync($"/api/school/report-cards/bundles/{id}", Ct))).GetProperty("url").GetString()!;
        var download = await api.CreateClient().GetAsync(url, Ct);
        Assert.True(download.Headers.CacheControl?.NoStore);
        Assert.Contains("Moe Mover", PdfText(await download.Content.ReadAsByteArrayAsync(Ct)));
    }

    [Fact]
    public async Task Signed_links_are_bound_to_their_purpose_id_and_expiry_and_other_schools_cannot_queue_bundles()
    {
        var s = await SetupAsync("Link Academy");
        await ScoredClassAsync(s);
        await Json(await s.School.Owner.PutAsync("/api/school/settings/logo", ImageForm(PngOf(100, 100)), Ct));
        var logoLink = (await Json(await api.CreateClient().GetAsync($"/api/school/{s.School.Slug}/public", Ct))).GetProperty("logoUrl").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().GetAsync(logoLink, Ct)).StatusCode);

        var id1 = (await Json(await s.School.Owner.PostAsync($"/api/school/report-cards/{s.ClassId}/{s.TermId}/bundle", null, Ct))).GetProperty("id").GetGuid();
        await api.AsTenantAsync<ReportCardService, bool>(s.School.TenantId, async svc => { await svc.BuildBundleAsync(id1, Ct); return true; });
        var bundleLink = (await Json(await s.School.Owner.GetAsync($"/api/school/report-cards/bundles/{id1}", Ct))).GetProperty("url").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().GetAsync(bundleLink, Ct)).StatusCode);

        // Same id, same expiry, same signature, but another kind of file: refused.
        var tenantPart = logoLink[(logoLink.IndexOf("/logos/", StringComparison.Ordinal) + 7)..];
        var bundlePart = bundleLink[(bundleLink.IndexOf("/bundles/", StringComparison.Ordinal) + 9)..];
        var root = logoLink[..logoLink.IndexOf("/api/school/", StringComparison.Ordinal)];
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync($"{root}/api/school/photos/{tenantPart}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync($"{root}/api/school/report-cards/bundles/{tenantPart}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync($"{root}/api/school/logos/{bundlePart}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync($"{root}/api/school/photos/{bundlePart}", Ct)).StatusCode);

        // The right signature for another bundle id.
        var parts = bundlePart.Split('/');
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync($"{root}/api/school/report-cards/bundles/{Guid.NewGuid()}/{parts[1]}/{parts[2]}", Ct)).StatusCode);

        // An expiry in the past, even with the otherwise valid signature.
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync($"{root}/api/school/report-cards/bundles/{parts[0]}/1/{parts[2]}", Ct)).StatusCode);
        var logoParts = tenantPart.Split('/');
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync($"{root}/api/school/logos/{logoParts[0]}/1/{logoParts[2]}", Ct)).StatusCode);

        // Another school cannot queue a bundle for this school's class.
        var stranger = await kit.SchoolAsync("Link Stranger");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Owner.PostAsync($"/api/school/report-cards/{s.ClassId}/{s.TermId}/bundle", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_bundle_with_nothing_to_print_fails_and_replaced_or_deleted_images_leave_storage()
    {
        var s = await SetupAsync("Empty Bundle School");
        var id = (await Json(await s.School.Owner.PostAsync($"/api/school/report-cards/{s.ClassId}/{s.TermId}/bundle", null, Ct))).GetProperty("id").GetGuid();
        await api.AsTenantAsync<ReportCardService, bool>(s.School.TenantId, async svc => { await svc.BuildBundleAsync(id, Ct); return true; });
        var status = await Json(await s.School.Owner.GetAsync($"/api/school/report-cards/bundles/{id}", Ct));
        Assert.Equal("Failed", status.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("url").ValueKind);
        var row = await api.WithDbAsync(db => db.ReportCardBundles.IgnoreQueryFilters().FirstAsync(b => b.Id == id, Ct));
        Assert.NotNull(row.CompletedAt);
        Assert.Null(row.FileKey);

        async Task<(string? Logo, string? Signature)> KeysAsync() => await api.WithDbAsync(async db =>
        {
            var p = await db.SchoolProfiles.IgnoreQueryFilters().FirstAsync(x => x.TenantId == s.School.TenantId, Ct);
            return (p.LogoKey, p.SignatureKey);
        });
        await Json(await s.School.Owner.PutAsync("/api/school/settings/logo", ImageForm(PngOf(80, 80)), Ct));
        await Json(await s.School.Owner.PutAsync("/api/school/settings/signature", ImageForm(PngOf(80, 40)), Ct));
        var first = await KeysAsync();
        Assert.True(await StoredAsync(first.Logo!));
        Assert.True(await StoredAsync(first.Signature!));

        await Json(await s.School.Owner.PutAsync("/api/school/settings/logo", ImageForm(PngOf(90, 90)), Ct));
        await Json(await s.School.Owner.PutAsync("/api/school/settings/signature", ImageForm(PngOf(90, 45)), Ct));
        var second = await KeysAsync();
        Assert.NotEqual(first.Logo, second.Logo);
        Assert.False(await StoredAsync(first.Logo!));
        Assert.False(await StoredAsync(first.Signature!));
        Assert.True(await StoredAsync(second.Logo!));

        (await s.School.Owner.DeleteAsync("/api/school/settings/logo", Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.DeleteAsync("/api/school/settings/signature", Ct)).EnsureSuccessStatusCode();
        Assert.False(await StoredAsync(second.Logo!));
        Assert.False(await StoredAsync(second.Signature!));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Validly_signed_links_past_their_expiry_are_refused_for_every_kind_of_file()
    {
        var s = await SetupAsync("Expiry Academy");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Eve", "Expiry");
        await ScoredClassAsync(s);
        (await s.School.Owner.PostAsync($"/api/school/students/{studentId}/photo", ImageForm(PngOf(60, 60)), Ct)).EnsureSuccessStatusCode();
        await Json(await s.School.Owner.PutAsync("/api/school/settings/logo", ImageForm(PngOf(60, 60)), Ct));
        var bundleId = (await Json(await s.School.Owner.PostAsync($"/api/school/report-cards/{s.ClassId}/{s.TermId}/bundle", null, Ct))).GetProperty("id").GetGuid();
        await api.AsTenantAsync<ReportCardService, bool>(s.School.TenantId, async svc => { await svc.BuildBundleAsync(bundleId, Ct); return true; });

        // Same key and URLs as the API, signed with the real signer; only the clock differs.
        var options = api.Services.GetRequiredService<MediaOptions>();
        var urls = api.Services.GetRequiredService<AppUrls>();
        var current = new SchoolPhotoSigner(options, urls, TimeProvider.System);
        var past = new SchoolPhotoSigner(options, urls, new FixedClock(DateTimeOffset.UtcNow.AddHours(-1))); // links ended 50 minutes ago

        var links = new (string Kind, string Fresh, string Expired)[]
        {
            ("photo", current.Url(studentId), past.Url(studentId)),
            ("logo", current.LogoUrl(s.School.TenantId), past.LogoUrl(s.School.TenantId)),
            ("bundle", current.BundleUrl(bundleId), past.BundleUrl(bundleId)),
        };
        foreach (var (kind, fresh, expired) in links)
        {
            var ok = await api.CreateClient().GetAsync(fresh, Ct);
            Assert.True(ok.StatusCode is HttpStatusCode.OK, $"{kind}: a current link should work, got {(int)ok.StatusCode}");
            Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync(expired, Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Nursery_card_has_no_numeric_total()
    {
        var school = await kit.SchoolAsync("Nursery Pdf School");
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));
        var levelId = level.GetProperty("id").GetGuid();
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var (classTeacher, classUser) = await kit.StaffAsync(school, MemberRole.Staff, "Nursery teacher");
        var staffId = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == classUser && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();
        var termId = await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60))
                    .SetProperty(t => t.NextTermStarts, new DateOnly(2027, 1, 11)), Ct);
            return await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
        });
        var studentId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var enrolmentId = await EnrolmentAsync(school, studentId);

        var areas = await Json(await classTeacher.GetAsync("/api/school/developmental/areas", Ct));
        var scale = await Json(await classTeacher.GetAsync("/api/school/developmental/scale", Ct));
        var excellent = scale.GetProperty("scale").EnumerateArray().Single(s => s.GetProperty("label").GetString() == "Excellent").GetProperty("id").GetGuid();
        var ratings = areas.GetProperty("areas").EnumerateArray().Select(a => new { areaId = a.GetProperty("id").GetGuid(), scaleId = excellent, comment = (string?)null }).ToArray();
        (await classTeacher.PutAsJsonAsync($"/api/school/students/{studentId}/developmental?termId={termId}", new { ratings }, Ct)).EnsureSuccessStatusCode();

        var traits = await Json(await classTeacher.GetAsync("/api/school/traits?kind=Psychomotor", Ct));
        var handwriting = traits.GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Handwriting").GetProperty("id").GetGuid();
        (await classTeacher.PutAsJsonAsync($"/api/school/students/{studentId}/traits?termId={termId}&kind=Psychomotor",
            new { ratings = new[] { new { criterionId = handwriting, value = 4 } } }, Ct)).EnsureSuccessStatusCode();

        await TakeRegisterAsync(new Setup(school, classId, termId, Guid.Empty, Guid.Empty, []), Today.AddDays(-1), (enrolmentId, "Present"));
        (await school.Owner.PutAsJsonAsync($"/api/school/results/{classId}/{termId}/remarks",
            new { enrolmentId, teacherRemark = "Settles well at nursery.", principalRemark = "A happy term." }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PostAsync($"/api/school/results/{classId}/{termId}/compute", null, Ct)).EnsureSuccessStatusCode();

        var text = PdfText(await PdfAsync(school.Owner, CardUrl(enrolmentId, termId)));
        Assert.Contains("Ada Zed", text);
        Assert.Contains("Language", text);
        Assert.Contains("Excellent", text);
        Assert.Contains("Developmental areas", text);
        Assert.Contains("Psychomotor", text);
        Assert.Contains("Handwriting", text);
        Assert.Contains("4 of 5", text);
        Assert.Contains("not exam marks", text);
        Assert.Contains("Present 1, late 0, absent 0, excused 0 of 1 days", text);
        Assert.Contains("Settles well at nursery.", text);
        Assert.Contains("A happy term.", text);
        Assert.DoesNotContain("A1", text);
        Assert.DoesNotContain("1st", text);
        Assert.DoesNotContain("No scores have been entered for this term yet.", text);
        Assert.DoesNotContain("Position in class", text);
    }
}
