using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Domain.Core;
using Saas.Domain.School;
using Saas.Infrastructure.Schools;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink background jobs: reminders, report card bundles, broadcasts, stranded payments, key retirement and cleanup.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolJobTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>The Lagos wall-clock time on the given day (Lagos is UTC+1 all year).</summary>
    private static DateTimeOffset Lagos(DateOnly day, int hour, int minute = 0) =>
        new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(1));

    private async Task RunAsync(DateTimeOffset now)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SchoolJobs>().RunOnceAsync(now, Ct);
    }

    private sealed record Family(SchoolKit.School School, Guid ClassId, Guid StudentId, Guid InvoiceId, HttpClient Parent, string GuardianEmail, Guid TermId);

    /// <summary>A school with one student invoiced for tuition, a guardian with email and phone, and their parent account.</summary>
    private async Task<Family> FamilyAsync(string name, bool online = false, long tuition = 10_000_000)
    {
        var school = await kit.SchoolAsync(name);
        if (online)
        {
            await api.MakeProAsync(school.TenantId, "school", "standard");
            await kit.SetPaystackKeyAsync(school);
        }
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var email = $"guardian-{Guid.NewGuid():N}@example.com";
        var guardianId = await kit.GuardianAsync(school, studentId, "Ngozi Obi", "08031234567", email);
        var (parent, _) = await kit.ParentAsync(school, guardianId);
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var item = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Tuition", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}",
            new { lines = new[] { new { feeItemId = item, amountMinor = tuition } } }, Ct));
        await Json(await school.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId }, Ct));
        var invoiceId = (await Json(await school.Owner.GetAsync($"/api/school/invoices?termId={termId}", Ct))).EnumerateArray().Single().GetProperty("id").GetGuid();
        return new Family(school, classId, studentId, invoiceId, parent, email, termId);
    }

    private Task SetDueAsync(Guid invoiceId, DateOnly due) =>
        api.WithDbAsync(db => db.StudentInvoices.IgnoreQueryFilters().Where(i => i.Id == invoiceId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.DueDate, due), Ct));

    private Task<int> EmailsAsync(Guid tenantId, string template) =>
        api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.TenantId == tenantId && n.TemplateKey == template, Ct));

    private Task SuspendAsync(Guid tenantId, TenantStatus status) =>
        api.WithDbAsync(db => db.Tenants.Where(t => t.Id == tenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status), Ct));

    // ---------- Reminders ----------

    [Fact]
    public async Task Reminders_send_once_per_day_per_invoice()
    {
        var f = await FamilyAsync("Reminder Job School");
        await SetDueAsync(f.InvoiceId, Today.AddDays(3));

        await RunAsync(Lagos(Today, 7, 59)); // before 08:00 in Lagos: nothing
        Assert.Equal(0, await EmailsAsync(f.School.TenantId, "school.fee_reminder"));

        await RunAsync(Lagos(Today, 9));
        Assert.Equal(1, await EmailsAsync(f.School.TenantId, "school.fee_reminder"));
        Assert.Equal(Today, await api.WithDbAsync(db => db.SchoolProfiles.IgnoreQueryFilters().Where(p => p.TenantId == f.School.TenantId).Select(p => p.LastRemindersOn).SingleAsync(Ct)));

        // The per-invoice history would also stop a second email, so forget it: only the school's day marker is left to stop the run.
        await api.WithDbAsync(db => db.FeeReminders.IgnoreQueryFilters().Where(r => r.InvoiceId == f.InvoiceId).ExecuteDeleteAsync(Ct));
        await RunAsync(Lagos(Today, 9, 30)); // the same day again
        await RunAsync(Lagos(Today, 23));
        Assert.Equal(1, await EmailsAsync(f.School.TenantId, "school.fee_reminder"));

        // The next Lagos day the school's run goes ahead again.
        await RunAsync(Lagos(Today.AddDays(1), 9));
        Assert.Equal(2, await EmailsAsync(f.School.TenantId, "school.fee_reminder"));
    }

    [Fact]
    public async Task Suspended_schools_get_no_reminders_or_broadcasts()
    {
        var f = await FamilyAsync("Suspended Job School");
        await SetDueAsync(f.InvoiceId, Today.AddDays(3));
        await Json(await f.School.Owner.PostAsJsonAsync("/api/school/announcements", new
        {
            title = "Closing early", body = "School closes at noon.", audience = "School", classIds = Array.Empty<Guid>(), levelIds = Array.Empty<Guid>(), emailGuardians = true,
        }, Ct));
        await SuspendAsync(f.School.TenantId, TenantStatus.Suspended);

        await RunAsync(Lagos(Today, 9));
        Assert.Equal(0, await EmailsAsync(f.School.TenantId, "school.fee_reminder"));
        Assert.Equal(0, await EmailsAsync(f.School.TenantId, "school.announcement"));
        Assert.Equal(1, await api.WithDbAsync(db => db.AnnouncementDeliveries.IgnoreQueryFilters()
            .CountAsync(d => d.TenantId == f.School.TenantId && d.Status == AnnouncementDeliveryStatus.Pending, Ct)));

        await SuspendAsync(f.School.TenantId, TenantStatus.Active); // back to normal: the same run now does the work
        await RunAsync(Lagos(Today, 9, 5));
        Assert.Equal(1, await EmailsAsync(f.School.TenantId, "school.fee_reminder"));
        Assert.Equal(1, await EmailsAsync(f.School.TenantId, "school.announcement"));
    }

    // ---------- Broadcasts ----------

    [Fact]
    public async Task Broadcast_emails_are_sent_in_batches()
    {
        var f = await FamilyAsync("Broadcast Job School");
        var posted = await Json(await f.School.Owner.PostAsJsonAsync("/api/school/announcements", new
        {
            title = "Fees notice", body = "Please read.", audience = "School", classIds = Array.Empty<Guid>(), levelIds = Array.Empty<Guid>(), emailGuardians = true,
        }, Ct));
        var announcementId = posted.GetProperty("id").GetGuid();
        // 1,049 more guardians, so 1,050 emails wait: a run sends 10 batches of 100 and leaves the rest for the next minute.
        await api.WithDbAsync(async db =>
        {
            for (var i = 0; i < 1049; i++)
            {
                var guardian = new Guardian { TenantId = f.School.TenantId, FullName = $"Bulk Guardian {i}", Email = $"bulk-{i}-{Guid.NewGuid():N}@example.com" };
                db.Guardians.Add(guardian);
                db.AnnouncementDeliveries.Add(new AnnouncementDelivery { TenantId = f.School.TenantId, AnnouncementId = announcementId, GuardianId = guardian.Id, Email = guardian.Email! });
            }
            return await db.SaveChangesAsync(Ct);
        });

        await RunAsync(DateTimeOffset.UtcNow); 
        Assert.Equal(1000, await EmailsAsync(f.School.TenantId, "school.announcement"));
        Assert.Equal(50, await api.WithDbAsync(db => db.AnnouncementDeliveries.IgnoreQueryFilters()
            .CountAsync(d => d.TenantId == f.School.TenantId && d.Status == AnnouncementDeliveryStatus.Pending, Ct)));
        await RunAsync(DateTimeOffset.UtcNow);
        Assert.Equal(1050, await EmailsAsync(f.School.TenantId, "school.announcement"));
    }

    // ---------- Report card bundles ----------

    [Fact]
    public async Task Report_card_bundles_are_built()
    {
        var school = await kit.SchoolAsync("Bundle Job School");
        var classId = await kit.ClassAsync(school);
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var classSubject = (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = subject.GetProperty("id").GetGuid() }, Ct))).GetProperty("id").GetGuid();
        var termId = await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct);
            return await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
        });
        var components = (await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={classSubject}&termId={termId}", Ct)))
            .GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        var studentId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var enrolmentId = await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));
        var entries = components.Zip(new[] { 18m, 17m, 50m }, (c, v) => new { enrolmentId, componentId = c, value = (decimal?)v }).ToArray();
        (await school.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId = classSubject, termId, entries }, Ct)).EnsureSuccessStatusCode();

        var (teacher, teacherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Bundle teacher");
        var staffId = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == teacherUser).Select(p => p.Id).FirstAsync(Ct));
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();
        var id = (await Json(await teacher.PostAsync($"/api/school/report-cards/{classId}/{termId}/bundle", null, Ct))).GetProperty("id").GetGuid();

        // An old finished bundle and its file, and a recent one, to be cleaned up by the same run.
        var storage = api.Services.GetRequiredService<IFileStorage>();
        var oldKey = $"private/school/{school.TenantId}/report-cards/{Guid.NewGuid():N}.pdf";
        var recentKey = $"private/school/{school.TenantId}/report-cards/{Guid.NewGuid():N}.pdf";
        foreach (var key in new[] { oldKey, recentKey })
            await storage.SaveAsync(key, new MemoryStream([1, 2, 3]), "application/pdf", Ct);
        var (oldId, recentId) = await api.WithDbAsync(async db =>
        {
            var old = new ReportCardBundle { TenantId = school.TenantId, ClassId = classId, TermId = termId, RequestedBy = teacherUser, Status = ReportCardBundleStatus.Ready, FileKey = oldKey };
            var recent = new ReportCardBundle { TenantId = school.TenantId, ClassId = classId, TermId = termId, RequestedBy = teacherUser, Status = ReportCardBundleStatus.Ready, FileKey = recentKey };
            db.ReportCardBundles.AddRange(old, recent);
            await db.SaveChangesAsync(Ct);
            await db.ReportCardBundles.IgnoreQueryFilters().Where(b => b.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.CreatedAt, DateTimeOffset.UtcNow.AddDays(-8)), Ct);
            return (old.Id, recent.Id);
        });

        await RunAsync(DateTimeOffset.UtcNow);

        Assert.Equal(ReportCardBundleStatus.Ready, await api.WithDbAsync(db => db.ReportCardBundles.IgnoreQueryFilters().Where(b => b.Id == id).Select(b => b.Status).SingleAsync(Ct)));
        // Built inside the school's own tenant: the requester's business token sees the "ready" notification.
        var feed = await Json(await teacher.GetAsync("/api/me/notifications", Ct));
        Assert.Contains(feed.GetProperty("items").EnumerateArray(), n => n.GetProperty("kind").GetString() == "school.report_cards_ready");
        Assert.False(await api.WithDbAsync(db => db.ReportCardBundles.IgnoreQueryFilters().AnyAsync(b => b.Id == oldId, Ct)));
        Assert.True(await api.WithDbAsync(db => db.ReportCardBundles.IgnoreQueryFilters().AnyAsync(b => b.Id == recentId, Ct)));
        Assert.Null(await storage.OpenReadAsync(oldKey, Ct));
        Assert.NotNull(await storage.OpenReadAsync(recentKey, Ct));
    }

    // ---------- Stranded payments ----------

    private async Task<(Guid PaymentId, string Reference)> PendingPaymentAsync(Family f, long amount = 2_000_000)
    {
        var response = await f.Parent.PostAsJsonAsync($"/api/parent/children/{f.StudentId}/invoices/{f.InvoiceId}/pay", new { amountMinor = amount }, Ct);
        var reference = (await Json(response)).GetProperty("reference").GetString()!;
        var id = await api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference).Select(p => p.Id).SingleAsync(Ct));
        return (id, reference);
    }

    private Task AgeAsync(Guid paymentId, TimeSpan age) =>
        api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.Id == paymentId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, DateTimeOffset.UtcNow - age), Ct));

    private Task<FeePaymentStatus> StatusAsync(Guid paymentId) =>
        api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync(Ct));

    [Fact]
    public async Task A_stranded_payment_is_confirmed_when_paystack_has_the_charge()
    {
        var f = await FamilyAsync("Stranded Job School", online: true);
        var (stranded, reference) = await PendingPaymentAsync(f);
        var (fresh, freshReference) = await PendingPaymentAsync(f);
        kit.Charge(reference, 2_000_000);
        kit.Charge(freshReference, 2_000_000);
        await AgeAsync(stranded, TimeSpan.FromHours(2));

        await RunAsync(DateTimeOffset.UtcNow);

        Assert.Equal(FeePaymentStatus.Succeeded, await StatusAsync(stranded));
        Assert.Equal(FeePaymentStatus.Pending, await StatusAsync(fresh)); // younger than an hour: the parent may still be paying
        Assert.Equal(8_000_000, await api.WithDbAsync(db => db.StudentInvoices.IgnoreQueryFilters().Where(i => i.Id == f.InvoiceId).Select(i => i.Balance).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_stranded_payment_is_confirmed_with_the_previous_key()
    {
        var f = await FamilyAsync("Previous Key Job School", online: true);
        var (payment, reference) = await PendingPaymentAsync(f);
        await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/paystack",
            new { secretKey = "sk_test_integration_second_key_0001", force = true }, Ct));
        kit.Charge(reference, 2_000_000, inAccountOfKey: SchoolKit.PaystackKey);
        await AgeAsync(payment, TimeSpan.FromHours(2));

        await RunAsync(DateTimeOffset.UtcNow);

        Assert.Equal(FeePaymentStatus.Succeeded, await StatusAsync(payment));
    }

    [Fact]
    public async Task An_unverifiable_payment_alerts_once_and_is_abandoned_after_a_day()
    {
        var f = await FamilyAsync("Abandoned Job School", online: true);
        var (payment, _) = await PendingPaymentAsync(f);
        await AgeAsync(payment, TimeSpan.FromHours(3));

        await RunAsync(DateTimeOffset.UtcNow); // Paystack has no charge yet: the parent may still pay
        Assert.Equal(FeePaymentStatus.Pending, await StatusAsync(payment));
        Assert.Equal(0, await EmailsAsync(f.School.TenantId, "school.payment_unverified"));

        await AgeAsync(payment, TimeSpan.FromHours(25));
        await RunAsync(DateTimeOffset.UtcNow);
        Assert.Equal(FeePaymentStatus.Abandoned, await StatusAsync(payment));
        Assert.Equal(1, await EmailsAsync(f.School.TenantId, "school.payment_unverified"));
        Assert.NotNull(await api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.Id == payment).Select(p => p.UnverifiedAlertedAt).SingleAsync(Ct)));

        var calls = api.Merchant.VerifyCalls;
        await RunAsync(DateTimeOffset.UtcNow); // no longer checked, no second alert
        Assert.Equal(1, await EmailsAsync(f.School.TenantId, "school.payment_unverified"));
        Assert.Equal(calls, api.Merchant.VerifyCalls);
    }

    // ---------- Late confirmation of an abandoned payment ----------

    /// <summary>A pending payment that no Paystack account knows yet, left for 25 hours and abandoned by the job.</summary>
    private async Task<(Guid PaymentId, string Reference)> AbandonedAsync(Family f)
    {
        var (id, reference) = await PendingPaymentAsync(f);
        await AgeAsync(id, TimeSpan.FromHours(25));
        await RunAsync(DateTimeOffset.UtcNow);
        Assert.Equal(FeePaymentStatus.Abandoned, await StatusAsync(id));
        return (id, reference);
    }

    private Task<long> BalanceAsync(Guid invoiceId) =>
        api.WithDbAsync(db => db.StudentInvoices.IgnoreQueryFilters().Where(i => i.Id == invoiceId).Select(i => i.Balance).SingleAsync(Ct));

    private Task<string?> ReceiptAsync(Guid paymentId) =>
        api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.Id == paymentId).Select(p => p.ReceiptNumber).SingleAsync(Ct));

    [Fact]
    public async Task A_late_webhook_confirms_an_abandoned_payment_once()
    {
        var f = await FamilyAsync("Late Webhook School", online: true);
        var (id, reference) = await AbandonedAsync(f);
        kit.Charge(reference, 2_000_000);
        var body = SchoolKit.ChargeSuccessBody(reference, 2_000_000);

        Assert.True((await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body))).IsSuccessStatusCode);
        Assert.Equal(FeePaymentStatus.Succeeded, await StatusAsync(id));
        Assert.Equal(8_000_000, await BalanceAsync(f.InvoiceId));
        var receipt = await ReceiptAsync(id);
        Assert.False(string.IsNullOrEmpty(receipt));

        // The same webhook again changes nothing.
        Assert.True((await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body))).IsSuccessStatusCode);
        Assert.Equal(8_000_000, await BalanceAsync(f.InvoiceId));
        Assert.Equal(receipt, await ReceiptAsync(id));
        Assert.Equal(1, await api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().CountAsync(p => p.InvoiceId == f.InvoiceId && p.Status == FeePaymentStatus.Succeeded, Ct)));
    }

    [Fact]
    public async Task A_late_return_verify_confirms_an_abandoned_payment()
    {
        var f = await FamilyAsync("Late Verify School", online: true);
        var (id, reference) = await AbandonedAsync(f);
        kit.Charge(reference, 2_000_000);

        var status = await Json(await f.Parent.PostAsync($"/api/parent/payments/{reference}/verify", null, Ct));
        Assert.Equal("Succeeded", status.GetProperty("status").GetString());
        Assert.Equal(FeePaymentStatus.Succeeded, await StatusAsync(id));
        Assert.Equal(8_000_000, await BalanceAsync(f.InvoiceId));
        Assert.False(string.IsNullOrEmpty(await ReceiptAsync(id)));
    }

    [Fact]
    public async Task A_late_webhook_signed_with_the_previous_key_confirms_an_abandoned_payment()
    {
        var f = await FamilyAsync("Late Previous Key School", online: true);
        var (id, reference) = await PendingPaymentAsync(f);
        await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = "sk_test_integration_second_key_0002", force = true }, Ct));
        await AgeAsync(id, TimeSpan.FromHours(25));
        await RunAsync(DateTimeOffset.UtcNow);
        Assert.Equal(FeePaymentStatus.Abandoned, await StatusAsync(id));

        kit.Charge(reference, 2_000_000, inAccountOfKey: SchoolKit.PaystackKey);
        var body = SchoolKit.ChargeSuccessBody(reference, 2_000_000);
        Assert.True((await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body, SchoolKit.PaystackKey))).IsSuccessStatusCode);

        Assert.Equal(FeePaymentStatus.Succeeded, await StatusAsync(id));
        Assert.Equal(8_000_000, await BalanceAsync(f.InvoiceId));
    }

    [Fact]
    public async Task A_late_charge_of_another_amount_on_an_abandoned_payment_is_a_mismatch()
    {
        var f = await FamilyAsync("Late Mismatch School", online: true);
        var (id, reference) = await AbandonedAsync(f);
        kit.Charge(reference, 1_500_000);
        var body = SchoolKit.ChargeSuccessBody(reference, 1_500_000);
        Assert.True((await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body))).IsSuccessStatusCode);
        Assert.Equal(FeePaymentStatus.Mismatch, await StatusAsync(id));
        Assert.Equal(10_000_000, await BalanceAsync(f.InvoiceId));
    }

    // ---------- Paystack unavailable ----------

    [Fact]
    public async Task A_payment_stays_pending_while_paystack_cannot_answer_and_is_abandoned_once_it_says_no()
    {
        var f = await FamilyAsync("Paystack Down School", online: true);
        var (id, reference) = await PendingPaymentAsync(f);
        await AgeAsync(id, TimeSpan.FromHours(25));
        api.Merchant.ThrowFor.Add(reference);
        try
        {
            await RunAsync(DateTimeOffset.UtcNow);
            Assert.Equal(FeePaymentStatus.Pending, await StatusAsync(id));
            Assert.Equal(0, await EmailsAsync(f.School.TenantId, "school.payment_unverified"));
        }
        finally { api.Merchant.ThrowFor.Remove(reference); }

        await RunAsync(DateTimeOffset.UtcNow);
        Assert.Equal(FeePaymentStatus.Abandoned, await StatusAsync(id));
        Assert.Equal(1, await EmailsAsync(f.School.TenantId, "school.payment_unverified"));
    }

    private sealed class StatusHandler(System.Net.HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("{\"status\":false,\"message\":\"x\"}") });
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError, true)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests, true)]
    [InlineData(System.Net.HttpStatusCode.NotFound, false)]
    [InlineData(System.Net.HttpStatusCode.BadRequest, false)]
    public async Task Paystack_verify_throws_when_unavailable_and_returns_null_when_not_found(System.Net.HttpStatusCode code, bool throws)
    {
        var client = new Saas.Infrastructure.Payments.PaystackClient(new HttpClient(new StatusHandler(code)));
        if (throws) await Assert.ThrowsAsync<HttpRequestException>(() => client.VerifyAsync("sk_test_x", "sfee_x", Ct));
        else Assert.Null(await client.VerifyAsync("sk_test_x", "sfee_x", Ct));
    }

    [Fact]
    public async Task Local_fixture_paystack_keys_confirm_without_calling_the_network()
    {
        // Development turns local fixture keys on (appsettings.Development.json); this client is built the same way.
        var client = new Saas.Infrastructure.Payments.PaystackClient(new HttpClient(new StatusHandler(System.Net.HttpStatusCode.ServiceUnavailable)),
            Microsoft.Extensions.Options.Options.Create(new Saas.Infrastructure.Payments.PaystackOptions { AllowLocalFixtureKeys = true }));
        var init = new PaymentInit("ada-mum-walk@example.com", 1_500_000, "NGN", "sfee_walkthrough_part", "http://localhost:4200/parent/pay/return?reference=sfee_walkthrough_part", []);
        var url = await client.InitializeAsync("sk_test_schoollink_walkthrough1", init, Ct);
        Assert.Equal(init.CallbackUrl, url);
        var charge = await client.VerifyAsync("sk_test_schoollink_walkthrough1", init.Reference, Ct);
        Assert.NotNull(charge);
        Assert.Equal(init.AmountMinor, charge.AmountMinor);
        Assert.Null(await client.VerifyAsync("sk_test_schoollink_walkthrough1", "sfee_unknown", Ct));
    }

    // ---------- Final fix pass: fixture keys (I1), abandoned checkouts (I2), Paystack timeouts (I4) ----------

    private sealed class RecordingHandler(System.Net.HttpStatusCode code) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("{\"status\":false,\"message\":\"x\"}") });
        }
    }

    [Fact]
    public async Task Fixture_prefixed_paystack_keys_call_paystack_when_local_fixture_keys_are_off()
    {
        var handler = new RecordingHandler(System.Net.HttpStatusCode.ServiceUnavailable);
        var client = new Saas.Infrastructure.Payments.PaystackClient(new HttpClient(handler));
        var init = new PaymentInit("ada-mum-off@example.com", 1_500_000, "NGN", "sfee_fixture_off", "http://localhost:4200/parent/pay/return?reference=sfee_fixture_off", []);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => client.InitializeAsync("sk_test_schoollink_walkthrough1", init, Ct));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.VerifyAsync("sk_test_schoollink_walkthrough1", init.Reference, Ct));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Fixture_prefixed_paystack_keys_are_refused_at_save_when_local_fixture_keys_are_off()
    {
        var school = await kit.SchoolAsync("Fixture Key School");
        var response = await school.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = "sk_test_schoollink_attempt_0001" }, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.BadRequest, $"{(int)response.StatusCode} {body}");
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("errors").TryGetProperty("secretKey", out _), body);

        var (owner, _) = await api.SignupInvoicesAsync("FixtureKey");
        var invoices = await owner.PutAsJsonAsync("/api/business/paystack", new { secretKey = "sk_test_schoollink_attempt_0002" }, Ct);
        var invoicesBody = await invoices.Content.ReadAsStringAsync(Ct);
        Assert.True(invoices.StatusCode == System.Net.HttpStatusCode.BadRequest, $"{(int)invoices.StatusCode} {invoicesBody}");
        Assert.True(JsonDocument.Parse(invoicesBody).RootElement.GetProperty("errors").TryGetProperty("secretKey", out _), invoicesBody);
    }

    private Task<int> FeedCountAsync(Guid tenantId, string kind) =>
        api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.TenantId == tenantId && n.Kind == kind, Ct));

    [Fact]
    public async Task A_checkout_paystack_reports_abandoned_is_marked_abandoned_without_an_alert()
    {
        var f = await FamilyAsync("Abandoned At Paystack School", online: true);
        var (payment, reference) = await PendingPaymentAsync(f);
        api.Merchant.Unpaid[reference] = "abandoned";
        await AgeAsync(payment, TimeSpan.FromHours(25));

        await RunAsync(DateTimeOffset.UtcNow);

        Assert.Equal(FeePaymentStatus.Abandoned, await StatusAsync(payment));
        Assert.Equal(0, await EmailsAsync(f.School.TenantId, "school.payment_unverified"));
        Assert.Equal(0, await FeedCountAsync(f.School.TenantId, "school.payment_unverified"));
        Assert.Null(await api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.Id == payment).Select(p => p.UnverifiedAlertedAt).SingleAsync(Ct)));

        // The parent's return verify of the same checkout raises nothing either.
        var status = await Json(await f.Parent.PostAsync($"/api/parent/payments/{reference}/verify", null, Ct));
        Assert.Equal("Abandoned", status.GetProperty("status").GetString());
        Assert.Equal(0, await EmailsAsync(f.School.TenantId, "school.payment_unverified"));
    }

    [Fact]
    public async Task A_paystack_timeout_does_not_stop_the_job_for_later_schools_or_fail_the_return_verify()
    {
        var slow = await FamilyAsync("Slow Paystack School", online: true);
        var (slowPayment, slowReference) = await PendingPaymentAsync(slow);
        await AgeAsync(slowPayment, TimeSpan.FromHours(25));
        var later = await FamilyAsync("After Slow Paystack School", online: true);
        var (laterPayment, laterReference) = await PendingPaymentAsync(later);
        await AgeAsync(laterPayment, TimeSpan.FromHours(2));
        kit.Charge(laterReference, 2_000_000);
        api.Merchant.TimeoutFor.Add(slowReference);
        try
        {
            await RunAsync(DateTimeOffset.UtcNow);

            Assert.Equal(FeePaymentStatus.Pending, await StatusAsync(slowPayment));
            Assert.Equal(0, await EmailsAsync(slow.School.TenantId, "school.payment_unverified"));
            Assert.Equal(FeePaymentStatus.Succeeded, await StatusAsync(laterPayment)); // the later school's step still ran

            var response = await slow.Parent.PostAsync($"/api/parent/payments/{slowReference}/verify", null, Ct);
            var status = await Json(response);
            Assert.Equal("Pending", status.GetProperty("status").GetString());
        }
        finally { api.Merchant.TimeoutFor.Remove(slowReference); }
    }

    // ---------- Key retirement and cleanup ----------

    [Fact]
    public async Task The_previous_paystack_key_is_retired_after_seven_days()
    {
        var old = await kit.SchoolAsync("Old Key School");
        var recent = await kit.SchoolAsync("Recent Key School");
        await api.WithDbAsync(async db =>
        {
            await db.SchoolProfiles.IgnoreQueryFilters().Where(p => p.TenantId == old.TenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.PreviousPaystackSecretKeyProtected, "protected-key").SetProperty(p => p.PreviousKeyRetiredAt, DateTimeOffset.UtcNow.AddDays(-8)), Ct);
            await db.SchoolProfiles.IgnoreQueryFilters().Where(p => p.TenantId == recent.TenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.PreviousPaystackSecretKeyProtected, "protected-key").SetProperty(p => p.PreviousKeyRetiredAt, DateTimeOffset.UtcNow.AddDays(-6)), Ct);
            return 0;
        });

        await RunAsync(DateTimeOffset.UtcNow);

        var oldProfile = await api.WithDbAsync(db => db.SchoolProfiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.TenantId == old.TenantId, Ct));
        Assert.Null(oldProfile.PreviousPaystackSecretKeyProtected);
        Assert.Null(oldProfile.PreviousKeyRetiredAt);
        var recentProfile = await api.WithDbAsync(db => db.SchoolProfiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.TenantId == recent.TenantId, Ct));
        Assert.Equal("protected-key", recentProfile.PreviousPaystackSecretKeyProtected);
    }

    [Fact]
    public async Task Expired_import_batches_are_purged()
    {
        var school = await kit.SchoolAsync("Import Purge School");
        var (expired, live) = await api.WithDbAsync(async db =>
        {
            var a = new ImportBatch { TenantId = school.TenantId, Token = Guid.NewGuid().ToString("N"), Payload = "[]", ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1), CreatedBy = Guid.NewGuid() };
            var b = new ImportBatch { TenantId = school.TenantId, Token = Guid.NewGuid().ToString("N"), Payload = "[]", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), CreatedBy = Guid.NewGuid() };
            db.ImportBatches.AddRange(a, b);
            await db.SaveChangesAsync(Ct);
            return (a.Id, b.Id);
        });

        await RunAsync(DateTimeOffset.UtcNow);

        Assert.False(await api.WithDbAsync(db => db.ImportBatches.IgnoreQueryFilters().AnyAsync(b => b.Id == expired, Ct)));
        Assert.True(await api.WithDbAsync(db => db.ImportBatches.IgnoreQueryFilters().AnyAsync(b => b.Id == live, Ct)));
    }

    [Fact]
    public async Task Job_run_is_recorded_for_the_health_page()
    {
        var service = new SchoolJobsHostedService(api.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SchoolJobsHostedService>.Instance);
        await service.TickAsync(Ct);
        var run = await api.WithDbAsync(db => db.JobRuns.SingleAsync(j => j.Name == "school", Ct));
        Assert.NotNull(run.LastStartedAt);
        Assert.NotNull(run.LastSucceededAt);
        Assert.Null(run.LastError);
    }

    // ---------- WhatsApp debtors ----------

    [Fact]
    public async Task Debtors_whatsapp_lists_guardians_with_a_phone()
    {
        var f = await FamilyAsync("WhatsApp Debtors School");
        var noPhone = await kit.StudentAsync(f.School, f.ClassId, "Chi", "Eze");
        await kit.GuardianAsync(f.School, noPhone, "Email Only", null, $"eo-{Guid.NewGuid():N}@example.com");
        await Json(await f.School.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId = f.TermId }, Ct));

        var rows = (await Json(await f.School.Owner.GetAsync($"/api/school/debtors/whatsapp?termId={f.TermId}&minDaysOverdue=0", Ct))).EnumerateArray().ToList();
        var row = Assert.Single(rows);
        Assert.Equal("Ada Obi", row.GetProperty("studentName").GetString());
        Assert.Equal("Ngozi Obi", row.GetProperty("guardianName").GetString());
        Assert.Equal(10_000_000, row.GetProperty("balance").GetInt64());
        var url = row.GetProperty("whatsAppUrl").GetString()!;
        Assert.StartsWith("https://wa.me/2348031234567?text=", url);
        Assert.Equal("Hello Ngozi Obi, this is a reminder from WhatsApp Debtors School that Ada Obi's school fees balance of ₦100,000 is outstanding. Thank you.",
            Uri.UnescapeDataString(url[(url.IndexOf("text=", StringComparison.Ordinal) + 5)..]));
    }
}
