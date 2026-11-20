using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Domain.Courses;
using Saas.Domain.Property;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 3 item 1: course sales, short-let stays and listing boosts go through the provider chosen in Payment settings (Paystack or
/// OPay). Each record keeps the provider that took it, and the return check, the OPay callback, the background re-check and refunds
/// always go to that provider.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class OpaySalesTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);
    private readonly CourseFlowTests courses = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private WebApplicationFactory<Program> Opay => api.OpayHost;

    private static string ReferenceOf(JsonElement checkout) => checkout.GetProperty("authorizationUrl").GetString()!.Split('/').Last();

    private Task<HttpResponseMessage> CallbackAsync(string reference, string status = "SUCCESS") =>
        api.CreateClient().PostAsJsonAsync("/api/webhooks/opay", new
        {
            payload = new { reference, status, amount = "1", currency = "NGN", country = "NG", transactionId = "241100002" },
            sha512 = "forged-signature",
            type = "transaction-status",
        }, Ct);

    // Courses

    /// <summary>A published ₦<paramref name="price"/>/100 course and a learner, both on <paramref name="host"/>.</summary>
    private async Task<(HttpClient Tutor, HttpClient Learner, Guid Course, Guid TenantId)> CourseAsync(string name, long price, WebApplicationFactory<Program>? host = null)
    {
        var (tutor, tenantId) = await courses.TutorAsync(name);
        var course = await courses.CreateCourseAsync(tutor, $"{name} course", price);
        await courses.BuildCurriculumAsync(tutor, course);
        await courses.PublishAsync(tutor, course);
        var learner = await courses.LearnerAsync($"{name}Learner");
        return host is null ? (tutor, learner, course, tenantId) : (ApiFixture.On(host, tutor), ApiFixture.On(host, learner), course, tenantId);
    }

    private static async Task<JsonElement> CourseCheckoutAsync(HttpClient learner, Guid course) =>
        await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct));

    private static async Task<bool> CourseVerifyAsync(HttpClient learner, Guid orderId, string reference) =>
        (await Json(await learner.PostAsJsonAsync($"/api/learn/orders/{orderId}/verify", new { reference }, Ct))).GetProperty("enrolled").GetBoolean();

    private Task<CourseOrder> OrderAsync(string reference) =>
        api.WithDbAsync(db => db.CourseOrders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Reference == reference, Ct));

    [Fact]
    public async Task Course_checkout_uses_opay_when_chosen_and_the_return_check_confirms_it()
    {
        var (_, learner, course, _) = await CourseAsync("OpayCourse", 800_000, Opay);

        var checkout = await CourseCheckoutAsync(learner, course);
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var reference = ReferenceOf(checkout);

        Assert.StartsWith($"{FakeOpay.BaseUrl}/cashier/crs_", checkout.GetProperty("authorizationUrl").GetString());
        Assert.Equal(("opay", OrderState.Pending), ((await OrderAsync(reference)).Provider, (await OrderAsync(reference)).Status));
        var created = api.Opay.Created[reference];
        Assert.Equal((800_000L, "NGN"), (created.Total, created.Currency));
        Assert.EndsWith($"/checkout/{orderId}?reference={reference}", created.ReturnUrl); // the return page reads the reference from here
        Assert.EndsWith($"/checkout/{orderId}?reference={reference}&cancelled=1", created.CancelUrl);
        Assert.EndsWith("/api/webhooks/opay", created.CallbackUrl);
        Assert.DoesNotContain(api.Gateway.Initialized, i => i.Reference == reference);

        Assert.False(await CourseVerifyAsync(learner, orderId, reference)); // OPay still says INITIAL
        api.Opay.Pay(reference);
        Assert.True(await CourseVerifyAsync(learner, orderId, reference));
        Assert.Equal(OrderState.Paid, (await OrderAsync(reference)).Status);
        Assert.Equal(0, api.Opay.Rejected);
    }

    [Fact]
    public async Task Course_opay_callback_confirms_only_after_asking_opay()
    {
        var (_, learner, course, tenantId) = await CourseAsync("CallbackCourse", 600_000, Opay);
        var reference = ReferenceOf(await CourseCheckoutAsync(learner, course));

        Assert.Equal(HttpStatusCode.OK, (await CallbackAsync(reference)).StatusCode); // the body says SUCCESS; OPay says INITIAL
        Assert.Equal(OrderState.Pending, (await OrderAsync(reference)).Status);
        Assert.Equal(1, api.Opay.StatusCalls[reference]);

        api.Opay.Pay(reference);
        await CallbackAsync(reference); // inside the per-order cooldown: OPay isn't asked again
        Assert.Equal(1, api.Opay.StatusCalls[reference]);
        Assert.Equal(OrderState.Pending, (await OrderAsync(reference)).Status);

        await api.ExpireStatusCooldownAsync(reference);
        await CallbackAsync(reference, status: "FAIL"); // the body's status is never read
        Assert.Equal(OrderState.Paid, (await OrderAsync(reference)).Status);
        Assert.Equal(1, await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().CountAsync(e => e.TenantId == tenantId, Ct)));
    }

    [Fact]
    public async Task Course_paid_the_wrong_amount_on_opay_does_not_enrol()
    {
        var (_, learner, course, _) = await CourseAsync("ShortCourse", 700_000, Opay);
        var checkout = await CourseCheckoutAsync(learner, course);
        var reference = ReferenceOf(checkout);

        api.Opay.Report(reference, "SUCCESS", total: 70_000);

        Assert.False(await CourseVerifyAsync(learner, checkout.GetProperty("orderId").GetGuid(), reference));
        Assert.Equal(OrderState.Failed, (await OrderAsync(reference)).Status);
    }

    [Fact]
    public async Task Course_refund_goes_through_opay_when_opay_took_the_payment()
    {
        var (tutor, learner, course, tenantId) = await CourseAsync("RefundOpayCourse", 500_000, Opay);
        var checkout = await CourseCheckoutAsync(learner, course);
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var reference = ReferenceOf(checkout);
        api.Opay.Pay(reference);
        Assert.True(await CourseVerifyAsync(learner, orderId, reference));

        (await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct)).EnsureSuccessStatusCode();

        var refund = Assert.Single(api.Opay.Refunds.Values, r => r.OriginalReference == reference);
        Assert.Equal((500_000L, "NGN", "NG"), (refund.Total, refund.Currency, refund.Country));
        Assert.DoesNotContain(api.Gateway.Refunds, r => r.Reference == reference);
        Assert.Equal(OrderState.Refunded, (await OrderAsync(reference)).Status);
        Assert.True(await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().Where(e => e.TenantId == tenantId).AllAsync(e => e.Reversed, Ct)));
        Assert.Equal(0, api.Opay.Rejected);
    }

    [Fact]
    public async Task A_refund_opay_refuses_leaves_the_course_order_paid()
    {
        var (tutor, learner, course, _) = await CourseAsync("RefusedRefund", 400_000, Opay);
        var checkout = await CourseCheckoutAsync(learner, course);
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var reference = ReferenceOf(checkout);
        api.Opay.Pay(reference);
        Assert.True(await CourseVerifyAsync(learner, orderId, reference));

        api.Opay.RefundStatus = "FAIL";
        try
        {
            var refused = await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.Contains("OPay", await refused.Content.ReadAsStringAsync(Ct));
        }
        finally { api.Opay.RefundStatus = "SUCCESS"; }
        Assert.Equal(OrderState.Paid, (await OrderAsync(reference)).Status);
    }

    // Stays

    private async Task<(PropertyKit.Agency Agency, Guid ListingId)> ShortLetAsync(string name)
    {
        var agency = await kit.AgencyAsync(name, verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"{name} flat", purpose: ListingPurpose.ShortLet, price: 5_000_000));
        return (agency, id);
    }

    private static async Task<JsonElement> BookAsync(HttpClient guest, Guid listingId, int daysAhead = 20)
    {
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead);
        return await Json(await guest.PostAsJsonAsync("/api/stays", new
        {
            listingId, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(2).ToString("yyyy-MM-dd"), guests = 2, phone = "+2348066666666",
        }, Ct));
    }

    private Task<Stay> StayAsync(string reference) =>
        api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Reference == reference, Ct));

    [Fact]
    public async Task Stay_paid_on_opay_confirms_by_callback_and_a_guest_cancellation_refunds_through_opay()
    {
        var (_, listing) = await ShortLetAsync("Opay Stay Homes");
        var guest = ApiFixture.On(Opay, await kit.SeekerAsync("OpayStayGuest"));
        var booking = await BookAsync(guest, listing);
        var stayId = booking.GetProperty("stayId").GetGuid();
        var reference = ReferenceOf(booking);
        var total = booking.GetProperty("totalMinor").GetInt64();

        Assert.Equal("opay", (await StayAsync(reference)).Provider);
        Assert.EndsWith($"/trips/{stayId}?reference={reference}", api.Opay.Created[reference].ReturnUrl);
        Assert.Equal(total, api.Opay.Created[reference].Total);

        api.Opay.Pay(reference);
        await CallbackAsync(reference);
        Assert.Equal(StayStatus.Confirmed, (await StayAsync(reference)).Status);

        var cancelled = await Json(await guest.PostAsync($"/api/stays/{stayId}/cancel", null, Ct));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        var refund = Assert.Single(api.Opay.Refunds.Values, r => r.OriginalReference == reference);
        Assert.Equal((total, "NGN"), (refund.Total, refund.Currency)); // more than 48 hours ahead: all of it
        Assert.DoesNotContain(api.Gateway.Refunds, r => r.Reference == reference);
        Assert.Equal(total, (await StayAsync(reference)).RefundMinor);
        Assert.Equal(0, api.Opay.Rejected);
    }

    [Fact]
    public async Task Stay_return_check_on_opay_confirms_and_a_host_cancellation_refunds_through_opay()
    {
        var (agency, listing) = await ShortLetAsync("Opay Host Cancels");
        var guest = ApiFixture.On(Opay, await kit.SeekerAsync("OpayHostGuest"));
        var booking = await BookAsync(guest, listing);
        var stayId = booking.GetProperty("stayId").GetGuid();
        var reference = ReferenceOf(booking);

        api.Opay.Pay(reference);
        var verified = await Json(await guest.PostAsJsonAsync($"/api/stays/{stayId}/verify", new { reference }, Ct));
        Assert.Equal("Confirmed", verified.GetProperty("status").GetString());

        (await agency.Client.PostAsync($"/api/property/stays/{stayId}/cancel", null, Ct)).EnsureSuccessStatusCode();
        Assert.Single(api.Opay.Refunds.Values, r => r.OriginalReference == reference);
        Assert.Equal(StayStatus.Cancelled, (await StayAsync(reference)).Status);
    }

    [Fact]
    public async Task Stay_paid_the_wrong_amount_on_opay_stays_unconfirmed()
    {
        var (_, listing) = await ShortLetAsync("Opay Short Pay");
        var guest = ApiFixture.On(Opay, await kit.SeekerAsync("OpayShortGuest"));
        var booking = await BookAsync(guest, listing);
        var reference = ReferenceOf(booking);

        api.Opay.Report(reference, "SUCCESS", total: 1_000);
        var verified = await Json(await guest.PostAsJsonAsync($"/api/stays/{booking.GetProperty("stayId").GetGuid()}/verify", new { reference }, Ct));

        Assert.Equal("Pending", verified.GetProperty("status").GetString());
        var alerts = await api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.TemplateKey == "stay.payment_mismatch").ToListAsync(Ct));
        Assert.Contains(alerts, n => n.Payload.GetValueOrDefault("reference") == reference);
    }

    [Fact]
    public async Task A_refund_opay_cannot_take_leaves_the_stay_confirmed()
    {
        var (_, listing) = await ShortLetAsync("Opay Busy Refund");
        var guest = ApiFixture.On(Opay, await kit.SeekerAsync("OpayBusyGuest"));
        var booking = await BookAsync(guest, listing);
        var stayId = booking.GetProperty("stayId").GetGuid();
        var reference = ReferenceOf(booking);
        api.Opay.Pay(reference);
        await Json(await guest.PostAsJsonAsync($"/api/stays/{stayId}/verify", new { reference }, Ct));

        api.Opay.RefundUnavailable = true;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await guest.PostAsync($"/api/stays/{stayId}/cancel", null, Ct)).StatusCode);
        }
        finally { api.Opay.RefundUnavailable = false; }

        var stay = await StayAsync(reference);
        Assert.Equal((StayStatus.Confirmed, 0L), (stay.Status, stay.RefundMinor));
        // Retried once OPay answers again: the same refund reference, accepted once.
        (await guest.PostAsync($"/api/stays/{stayId}/cancel", null, Ct)).EnsureSuccessStatusCode();
        Assert.Single(api.Opay.Refunds.Values, r => r.OriginalReference == reference);
    }

    // Fix round: retries after a failed OPay refund (I4) and refunds without a clear answer (I3)

    [Fact]
    public async Task A_retry_after_opay_failed_a_refund_is_a_new_refund_not_a_duplicate()
    {
        var (tutor, learner, course, _) = await CourseAsync("RetryRefund", 400_000, Opay);
        var checkout = await CourseCheckoutAsync(learner, course);
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var reference = ReferenceOf(checkout);
        api.Opay.Pay(reference);
        Assert.True(await CourseVerifyAsync(learner, orderId, reference));

        api.Opay.RefundStatus = "FAIL";
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct)).StatusCode);
        }
        finally { api.Opay.RefundStatus = "SUCCESS"; }
        Assert.Equal(OrderState.Paid, (await OrderAsync(reference)).Status);

        // The tutor tries again: a new refund reference, which OPay takes. (The old one would only be answered "duplicate".)
        (await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct)).EnsureSuccessStatusCode();
        Assert.True(api.Opay.Refunds.ContainsKey($"{reference}_rf2"));
        Assert.Equal("SUCCESS", api.Opay.RefundStatuses[$"{reference}_rf2"]);
        Assert.Equal(OrderState.Refunded, (await OrderAsync(reference)).Status);
    }

    [Fact]
    public async Task A_duplicate_refund_is_only_success_when_opay_says_so()
    {
        var (tutor, learner, course, _) = await CourseAsync("DuplicateRefund", 400_000, Opay);
        var checkout = await CourseCheckoutAsync(learner, course);
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var reference = ReferenceOf(checkout);
        api.Opay.Pay(reference);
        Assert.True(await CourseVerifyAsync(learner, orderId, reference));

        // OPay already has a failed refund under the first reference (sent before this fix): "duplicate" is not success.
        api.Opay.Refunds[$"{reference}_rf"] = new FakeOpay.RefundRequest($"{reference}_rf", reference, 400_000, "NGN", "NG");
        api.Opay.RefundStatuses[$"{reference}_rf"] = "FAIL";
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct)).StatusCode);
        Assert.Equal(OrderState.Paid, (await OrderAsync(reference)).Status);
        Assert.Equal(1, api.Opay.RefundQueries[$"{reference}_rf"]);

        (await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct)).EnsureSuccessStatusCode();
        Assert.True(api.Opay.Refunds.ContainsKey($"{reference}_rf2"));
    }

    [Fact]
    public async Task A_refund_without_a_clear_answer_stays_pending_until_the_recheck_settles_it()
    {
        // Paystack takes the refund but the answer times out.
        var (tutor, learner, course, tenantId) = await CourseAsync("PendingRefund", 300_000);
        var checkout = await CourseCheckoutAsync(learner, course);
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var reference = ReferenceOf(checkout);
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, 300_000, "NGN", DateTimeOffset.UtcNow);
        Assert.True(await CourseVerifyAsync(learner, orderId, reference));

        api.Gateway.RefundTimesOut[reference] = true;
        var answer = await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, answer.StatusCode);
        var order = await OrderAsync(reference);
        Assert.Equal(OrderState.Refunded, order.Status); // not put back: the money may already be on its way
        Assert.NotNull(order.RefundPendingSince);
        Assert.True(await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().Where(e => e.TenantId == tenantId).AllAsync(e => e.Reversed, Ct)));
        // A second click doesn't send it again.
        Assert.Equal(HttpStatusCode.Conflict, (await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct)).StatusCode);
        Assert.Single(api.Gateway.Refunds, r => r.Reference == reference);

        // The sales re-check asks Paystack, which has the refund.
        await SalesRecheckAsync(DateTimeOffset.UtcNow.AddMinutes(30));
        order = await OrderAsync(reference);
        Assert.Null(order.RefundPendingSince);
        Assert.Equal(OrderState.Refunded, order.Status);
        Assert.Single(api.Gateway.Refunds, r => r.Reference == reference);
    }

    [Fact]
    public async Task A_pending_refund_opay_reports_failed_or_cannot_confirm_in_a_day_alerts_Finance()
    {
        var (_, listing) = await ShortLetAsync("Opay Lost Refund");
        var guest = ApiFixture.On(Opay, await kit.SeekerAsync("OpayLostGuest"));
        var booking = await BookAsync(guest, listing, daysAhead: 30);
        var stayId = booking.GetProperty("stayId").GetGuid();
        var reference = ReferenceOf(booking);
        api.Opay.Pay(reference);
        await Json(await guest.PostAsJsonAsync($"/api/stays/{stayId}/verify", new { reference }, Ct));

        api.Opay.RefundAnswerLost = true;
        try
        {
            var cancelled = await Json(await guest.PostAsync($"/api/stays/{stayId}/cancel", null, Ct));
            Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
            Assert.True(cancelled.GetProperty("refundPending").GetBoolean());
        }
        finally { api.Opay.RefundAnswerLost = false; }
        var stay = await StayAsync(reference);
        Assert.Equal(StayStatus.Cancelled, stay.Status);
        Assert.NotNull(stay.RefundPendingSince);

        // OPay can't be asked yet: still pending, nobody alerted.
        api.Opay.RefundQueryUnavailable = true;
        try
        {
            await SalesRecheckAsync(DateTimeOffset.UtcNow.AddMinutes(30));
            Assert.NotNull((await StayAsync(reference)).RefundPendingSince);
            Assert.DoesNotContain(await RefundAlertsAsync(), n => n.Payload["reference"] == reference);

            // A day later and still no answer: Finance is told, once.
            await SalesRecheckAsync(DateTimeOffset.UtcNow.AddHours(25));
            await SalesRecheckAsync(DateTimeOffset.UtcNow.AddHours(26));
            Assert.Single(await RefundAlertsAsync(), n => n.Payload["reference"] == reference);
        }
        finally { api.Opay.RefundQueryUnavailable = false; }

        // OPay then says the refund failed: kept pending (Finance sends it by hand), never reported as refunded.
        api.Opay.RefundStatuses[$"{reference}_rf"] = "FAIL";
        await SalesRecheckAsync(DateTimeOffset.UtcNow.AddHours(27));
        Assert.NotNull((await StayAsync(reference)).RefundPendingSince);
    }

    private async Task SalesRecheckAsync(DateTimeOffset now)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SalesPaymentJobs>().RunOnceAsync(now, Ct);
    }

    private Task<List<Saas.Domain.Core.Notification>> RefundAlertsAsync() =>
        api.WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.TemplateKey == "refund.unconfirmed").ToListAsync(Ct));

    // Boosts

    [Fact]
    public async Task Boost_bought_on_opay_confirms_by_return_check_and_checks_the_amount()
    {
        var agency = await kit.AgencyAsync("Opay Booster", verified: true);
        var listing = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Opay boosted flat"));
        var owner = ApiFixture.On(Opay, agency.Client);
        var purchase = await Json(await owner.PostAsJsonAsync($"/api/property/listings/{listing}/boosts", new { code = "top-7" }, Ct));
        var boostId = purchase.GetProperty("boostId").GetGuid();
        var reference = ReferenceOf(purchase);

        var boost = await api.WithDbAsync(db => db.Boosts.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Reference == reference, Ct));
        Assert.Equal("opay", boost.Provider);
        Assert.EndsWith($"/studio/boosts/{boostId}?reference={reference}", api.Opay.Created[reference].ReturnUrl);

        api.Opay.Report(reference, "SUCCESS", total: 1_000);
        var wrong = await Json(await owner.PostAsJsonAsync($"/api/property/boosts/{boostId}/verify", new { reference }, Ct));
        Assert.Equal("Pending", wrong.GetProperty("status").GetString());

        api.Opay.Pay(reference);
        var paid = await Json(await owner.PostAsJsonAsync($"/api/property/boosts/{boostId}/verify", new { reference }, Ct));
        Assert.Equal("Active", paid.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Boost_opay_callback_activates_it()
    {
        var agency = await kit.AgencyAsync("Opay Callback Booster", verified: true);
        var listing = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Opay callback flat"));
        var purchase = await Json(await ApiFixture.On(Opay, agency.Client).PostAsJsonAsync($"/api/property/listings/{listing}/boosts", new { code = "featured-7" }, Ct));
        var reference = ReferenceOf(purchase);

        api.Opay.Pay(reference);
        await CallbackAsync(reference);

        var boost = await api.WithDbAsync(db => db.Boosts.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Reference == reference, Ct));
        Assert.Equal(BoostStatus.Active, boost.Status);

        using var scope = api.Services.CreateScope(); // the admin payments list shows who took it
        var rows = (await scope.ServiceProvider.GetRequiredService<Saas.Application.Platform.PlatformMoneyService>()
            .PaymentsAsync("property", null, DateTimeOffset.UtcNow.AddMinutes(-5), null, 1, 100, Ct)).Items;
        Assert.Equal("opay", rows.Single(r => r.Reference == reference).Provider);
    }

    // Re-check, switching, switch off

    [Fact]
    public async Task The_recheck_confirms_opay_payments_whose_callback_never_came()
    {
        var (_, listing) = await ShortLetAsync("Opay Recheck Homes");
        var guest = ApiFixture.On(Opay, await kit.SeekerAsync("OpayRecheckGuest"));
        var stayRef = ReferenceOf(await BookAsync(guest, listing));
        var (_, learner, course, _) = await CourseAsync("RecheckCourse", 300_000, Opay);
        var courseRef = ReferenceOf(await CourseCheckoutAsync(learner, course));
        api.Opay.Pay(stayRef);
        api.Opay.Pay(courseRef);

        var old = DateTimeOffset.UtcNow.AddMinutes(-10);
        await api.WithDbAsync(async db =>
            await db.Stays.IgnoreQueryFilters().Where(s => s.Reference == stayRef).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, old), Ct)
            + await db.CourseOrders.IgnoreQueryFilters().Where(o => o.Reference == courseRef).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, old), Ct));

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SalesPaymentJobs>().RunOnceAsync(DateTimeOffset.UtcNow, Ct);

        Assert.Equal(StayStatus.Confirmed, (await StayAsync(stayRef)).Status);
        Assert.Equal(OrderState.Paid, (await OrderAsync(courseRef)).Status);
        var asked = api.Opay.StatusCalls[stayRef];

        using (var scope = api.Services.CreateScope()) // a second run straight after asks nothing more
            await scope.ServiceProvider.GetRequiredService<SalesPaymentJobs>().RunOnceAsync(DateTimeOffset.UtcNow, Ct);
        Assert.Equal(asked, api.Opay.StatusCalls[stayRef]);
    }

    [Fact]
    public async Task Switching_to_opay_keeps_verifying_and_refunding_older_paystack_payments()
    {
        // Taken on Paystack (the suite's default provider)...
        var (_, listing) = await ShortLetAsync("Switch Stay Homes");
        var paystackGuest = await kit.SeekerAsync("SwitchGuest");
        var booking = await BookAsync(paystackGuest, listing);
        var stayId = booking.GetProperty("stayId").GetGuid();
        var stayRef = ReferenceOf(booking);
        var (_, learner, course, _) = await CourseAsync("SwitchCourse", 900_000);
        var checkout = await CourseCheckoutAsync(learner, course);
        var courseRef = ReferenceOf(checkout);
        Assert.Equal(("paystack", "paystack"), ((await StayAsync(stayRef)).Provider, (await OrderAsync(courseRef)).Provider));
        api.Gateway.Charges[stayRef] = new VerifiedCharge(stayRef, booking.GetProperty("totalMinor").GetInt64(), "NGN", DateTimeOffset.UtcNow);
        api.Gateway.Charges[courseRef] = new VerifiedCharge(courseRef, 900_000, "NGN", DateTimeOffset.UtcNow);

        // ...then the platform switches to OPay: the old payments are still checked and refunded with Paystack.
        var guestOnOpay = ApiFixture.On(Opay, paystackGuest);
        var stay = await Json(await guestOnOpay.PostAsJsonAsync($"/api/stays/{stayId}/verify", new { reference = stayRef }, Ct));
        Assert.Equal("Confirmed", stay.GetProperty("status").GetString());
        Assert.True(await CourseVerifyAsync(ApiFixture.On(Opay, learner), checkout.GetProperty("orderId").GetGuid(), courseRef));
        await guestOnOpay.PostAsync($"/api/stays/{stayId}/cancel", null, Ct);

        Assert.Contains(api.Gateway.Refunds, r => r.Reference == stayRef);
        Assert.False(api.Opay.StatusCalls.ContainsKey(stayRef));
        Assert.False(api.Opay.StatusCalls.ContainsKey(courseRef));
        Assert.DoesNotContain(api.Opay.Refunds.Values, r => r.OriginalReference == stayRef);
    }

    [Fact]
    public async Task Online_payments_off_refuses_opay_checkouts_for_courses_stays_and_boosts()
    {
        var (_, learner, course, _) = await CourseAsync("OffCourse", 200_000, Opay);
        var (agency, listing) = await ShortLetAsync("Off Opay Homes");
        var guest = ApiFixture.On(Opay, await kit.SeekerAsync("OffOpayGuest"));
        var before = api.Opay.Created.Count;

        await api.SetPaymentSettingsAsync(false, provider: "opay");
        try
        {
            var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
            var responses = new[]
            {
                await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct),
                await guest.PostAsJsonAsync("/api/stays", new
                {
                    listingId = listing, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(1).ToString("yyyy-MM-dd"), guests = 1, phone = "+2348066666666",
                }, Ct),
                await ApiFixture.On(Opay, agency.Client).PostAsJsonAsync($"/api/property/listings/{listing}/boosts", new { code = "top-7" }, Ct),
            };
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode));
        }
        finally { await api.ResetPaymentSettingsAsync(); }
        Assert.Equal(before, api.Opay.Created.Count);
    }

    [Fact]
    public async Task The_payment_settings_provider_picks_opay_for_sales_on_any_host()
    {
        var (_, listing) = await ShortLetAsync("Row Provider Homes");
        var guest = await kit.SeekerAsync("RowProviderGuest"); // the default host, whose config says Paystack
        await api.SetPaymentSettingsAsync(true, provider: "opay");
        try
        {
            var booking = await BookAsync(guest, listing);
            Assert.Equal("opay", (await StayAsync(ReferenceOf(booking))).Provider);
            Assert.StartsWith(FakeOpay.BaseUrl, booking.GetProperty("authorizationUrl").GetString());
        }
        finally { await api.ResetPaymentSettingsAsync(); }
    }
}
