using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Payouts;
using Saas.Domain.Courses;
using Saas.Domain.Payouts;
using Saas.Domain.Property;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 3: automated payouts through Paystack Transfers (verified bank accounts, Finance approval sends one transfer, webhooks and the
/// re-check settle it) and Paystack subaccounts (split payments settle the seller's share directly).
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class PaystackPayoutTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private WebApplicationFactory<Program> Host => api.PayoutsHost;
    private FakePaystackTransfers Paystack => api.PaystackTransfers;

    // Bank accounts

    [Fact]
    public async Task Verifying_a_bank_account_resolves_the_name_and_creates_a_transfer_recipient()
    {
        var (tutor, _) = await TutorAsync("Verify", Host);

        var resolved = await Json(await tutor.PostAsJsonAsync("/api/payouts/resolve-account", new { bankCode = "058", accountNumber = "0123456789" }, Ct));
        Assert.Equal("ADA LOVELACE", resolved.GetProperty("accountName").GetString());
        var banks = await Json(await tutor.GetAsync("/api/payouts/banks", Ct));
        Assert.Contains(banks.EnumerateArray(), b => b.GetProperty("code").GetString() == "058");
        Assert.DoesNotContain(banks.EnumerateArray(), b => b.GetProperty("code").GetString() == "999"); // inactive banks are left out

        var before = Paystack.Recipients.Count;
        var saved = await Json(await tutor.PutAsJsonAsync("/api/academy/payout-account",
            new { bankName = "Guaranty Trust Bank", bankCode = "058", accountNumber = "0123456789", accountName = "Ada typed this" }, Ct));
        var account = saved.GetProperty("payoutAccount");
        Assert.Equal("ADA LOVELACE", account.GetProperty("accountName").GetString()); // the bank's name wins over what was typed
        Assert.True(account.GetProperty("verified").GetBoolean());
        var recipient = Paystack.Recipients.Skip(before).Single();
        Assert.Equal("nuban", recipient.GetProperty("type").GetString());
        Assert.Equal("0123456789", recipient.GetProperty("account_number").GetString());
        Assert.Equal("058", recipient.GetProperty("bank_code").GetString());
        Assert.Equal("ADA LOVELACE", recipient.GetProperty("name").GetString());
        Assert.Equal("NGN", recipient.GetProperty("currency").GetString());

        var unknown = await tutor.PutAsJsonAsync("/api/academy/payout-account",
            new { bankName = "Guaranty Trust Bank", bankCode = "058", accountNumber = "5555555555", accountName = "Nobody" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        // With automatic transfers on, an account has to be verified (picked from the bank list).
        var unverified = await tutor.PutAsJsonAsync("/api/academy/payout-account", new { bankName = "GTBank", accountNumber = "0123456789", accountName = "Ada" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unverified.StatusCode);

        var settings = await Json(await tutor.GetAsync("/api/payouts/settings", Ct));
        Assert.True(settings.GetProperty("automaticTransfers").GetBoolean());
    }

    // Transfers

    [Fact]
    public async Task Approval_sends_exactly_one_transfer_and_the_success_webhook_marks_it_paid()
    {
        var (tutor, tenantId, payoutId) = await RequestedPayoutAsync("Approve", 600_000);
        var admin = ApiFixture.On(Host, await AdminAsync());

        var before = Paystack.TransferCalls.Count;
        var sent = await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct);
        sent.EnsureSuccessStatusCode();
        var dto = await Json(sent);
        Assert.Equal("Processing", dto.GetProperty("status").GetString());
        var reference = dto.GetProperty("transferReference").GetString()!;
        Assert.Matches("^[a-z0-9_-]{16,}$", reference);
        var transfer = Paystack.Transfers[reference];
        Assert.Equal(600_000, transfer.Amount);
        Assert.StartsWith("RCP_", transfer.Recipient);

        // Approving twice (a double click, two Finance staff) never sends a second transfer.
        var again = await Task.WhenAll(admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct), admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct));
        Assert.All(again, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));
        Assert.Single(Paystack.TransferCalls.Skip(before));
        // A sent payout can't be marked paid or rejected by hand while Paystack has it.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/payouts/{payoutId}/paid", new { reference = "MANUAL" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/payouts/{payoutId}/reject", new { note = "no" }, Ct)).StatusCode);

        // While it is in flight the amount is not available again.
        Assert.Equal(200_000, (await LedgerAsync(tenantId)).AvailableMinor);

        (await TransferWebhookAsync("transfer.success", reference, 600_000)).EnsureSuccessStatusCode();
        var payout = await PayoutAsync(payoutId);
        Assert.Equal(PayoutStatus.Paid, payout.Status);
        Assert.NotNull(payout.PaidAt);
        Assert.Equal("success", payout.TransferStatus);
        Assert.Equal(reference, payout.Reference);
        Assert.Equal(600_000, (await LedgerAsync(tenantId)).PaidOutMinor);
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "payout.paid" && n.TenantId == tenantId, Ct)));

        // A repeated webhook changes nothing.
        (await TransferWebhookAsync("transfer.success", reference, 600_000)).EnsureSuccessStatusCode();
        Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "payout.paid" && n.TenantId == tenantId, Ct)));

        var list = await Json(await admin.GetAsync("/api/admin/payouts?status=Paid", Ct));
        var row = list.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == payoutId);
        Assert.Equal("success", row.GetProperty("transferStatus").GetString());
        _ = tutor;
    }

    [Fact]
    public async Task A_failed_transfer_returns_the_amount_alerts_Finance_and_can_be_retried_with_a_new_reference()
    {
        var (_, tenantId, payoutId) = await RequestedPayoutAsync("Failing", 500_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        var first = (await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct))).GetProperty("transferReference").GetString()!;

        // A webhook with the wrong amount is not trusted.
        (await TransferWebhookAsync("transfer.failed", first, 1)).EnsureSuccessStatusCode();
        Assert.Equal(PayoutStatus.Processing, (await PayoutAsync(payoutId)).Status);

        (await TransferWebhookAsync("transfer.failed", first, 500_000, "Account is dormant")).EnsureSuccessStatusCode();
        var failed = await PayoutAsync(payoutId);
        Assert.Equal(PayoutStatus.Failed, failed.Status);
        Assert.Equal("failed", failed.TransferStatus);
        Assert.Equal(300_000 + 500_000, (await LedgerAsync(tenantId)).AvailableMinor); // back to available
        Assert.Contains(await FinanceAlertsAsync(), n => n.Payload["reference"] == first);

        // Finance retries: a new attempt with a new reference (Paystack would answer the old reference with the failed transfer).
        var retried = await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct));
        Assert.Equal("Processing", retried.GetProperty("status").GetString());
        var second = retried.GetProperty("transferReference").GetString()!;
        Assert.NotEqual(first, second);
        Assert.True(Paystack.Transfers.ContainsKey(second));
        Assert.Equal(300_000, (await LedgerAsync(tenantId)).AvailableMinor);

        // A late webhook for the first attempt doesn't touch the second.
        (await TransferWebhookAsync("transfer.success", first, 500_000)).EnsureSuccessStatusCode();
        Assert.Equal(PayoutStatus.Processing, (await PayoutAsync(payoutId)).Status);
    }

    [Fact]
    public async Task A_refused_transfer_fails_at_once_and_a_retry_needs_the_money_to_still_be_available()
    {
        var (tutor, tenantId, payoutId) = await RequestedPayoutAsync("Refused", 500_000, earned: 500_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        Paystack.RefuseNextTransfer = "Your balance is not enough to fulfil this request";
        var refused = await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct));
        Assert.Equal("Failed", refused.GetProperty("status").GetString());
        Assert.Contains("balance", refused.GetProperty("failureReason").GetString());
        Assert.Contains(await FinanceAlertsAsync(), n => n.Payload["business"] == "Refused Academy");

        // The business asks for the money again in the meantime; retrying the failed payout would pay it twice.
        (await ApiFixture.On(Host, tutor).PostAsJsonAsync("/api/studio/payouts", new { amountMinor = 500_000 }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct)).StatusCode);
        Assert.Equal(0, (await LedgerAsync(tenantId)).AvailableMinor);
    }

    [Fact]
    public async Task A_reversed_transfer_after_success_returns_the_amount_to_available()
    {
        var (_, tenantId, payoutId) = await RequestedPayoutAsync("Reversed", 500_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        var reference = (await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct))).GetProperty("transferReference").GetString()!;
        (await TransferWebhookAsync("transfer.success", reference, 500_000)).EnsureSuccessStatusCode();
        Assert.Equal(500_000, (await LedgerAsync(tenantId)).PaidOutMinor);

        (await TransferWebhookAsync("transfer.reversed", reference, 500_000)).EnsureSuccessStatusCode();
        var payout = await PayoutAsync(payoutId);
        Assert.Equal(PayoutStatus.Failed, payout.Status);
        Assert.Equal("reversed", payout.TransferStatus);
        Assert.Null(payout.PaidAt);
        var ledger = await LedgerAsync(tenantId);
        Assert.Equal(0, ledger.PaidOutMinor);
        Assert.Equal(800_000, ledger.AvailableMinor);
        Assert.Contains(await FinanceAlertsAsync(), n => n.Payload["reference"] == reference);
    }

    [Fact]
    public async Task The_recheck_resends_with_the_same_reference_when_Paystack_never_got_it_and_settles_from_verify()
    {
        var (_, _, payoutId) = await RequestedPayoutAsync("Recheck", 500_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        Paystack.UnavailableNextTransfer = true;
        var sent = await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct));
        Assert.Equal("Processing", sent.GetProperty("status").GetString()); // no answer: not failed, not paid
        var reference = sent.GetProperty("transferReference").GetString()!;
        Assert.False(Paystack.Transfers.ContainsKey(reference));

        // Too soon: the re-check leaves fresh transfers alone.
        await RecheckAsync(DateTimeOffset.UtcNow);
        Assert.False(Paystack.Transfers.ContainsKey(reference));

        // Later: Paystack doesn't know the reference, so it is sent again with the same reference.
        await RecheckAsync(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(2, Paystack.TransferCalls.Count(r => r == reference));
        Assert.True(Paystack.Transfers.ContainsKey(reference));
        Assert.Equal(PayoutStatus.Processing, (await PayoutAsync(payoutId)).Status);

        // Still pending at Paystack: nothing changes and nothing is sent again.
        await RecheckAsync(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(2, Paystack.TransferCalls.Count(r => r == reference));

        Paystack.Transfers[reference].Status = "success";
        await RecheckAsync(DateTimeOffset.UtcNow.AddHours(1));
        var payout = await PayoutAsync(payoutId);
        Assert.Equal(PayoutStatus.Paid, payout.Status);
        Assert.Equal(2, Paystack.TransferCalls.Count(r => r == reference));
    }

    [Fact]
    public async Task The_recheck_fails_a_transfer_Paystack_reports_failed()
    {
        var (_, tenantId, payoutId) = await RequestedPayoutAsync("RecheckFail", 500_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        var reference = (await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct))).GetProperty("transferReference").GetString()!;
        Paystack.Transfers[reference].Status = "failed";
        await RecheckAsync(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(PayoutStatus.Failed, (await PayoutAsync(payoutId)).Status);
        Assert.Equal(800_000, (await LedgerAsync(tenantId)).AvailableMinor);
    }

    [Fact]
    public async Task With_automatic_transfers_off_Finance_pays_by_hand_and_nothing_is_sent()
    {
        var (_, tenantId, payoutId) = await RequestedPayoutAsync("Manual", 500_000, host: api);
        var admin = await AdminAsync();
        var settings = await Json(await admin.GetAsync("/api/admin/payouts/settings", Ct));
        Assert.False(settings.GetProperty("automaticTransfers").GetBoolean());

        var before = Paystack.TransferCalls.Count;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct)).StatusCode);
        Assert.Equal(before, Paystack.TransferCalls.Count);
        (await admin.PostAsJsonAsync($"/api/admin/payouts/{payoutId}/paid", new { reference = "GTB-123" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(PayoutStatus.Paid, (await PayoutAsync(payoutId)).Status);
        Assert.Equal(500_000, (await LedgerAsync(tenantId)).PaidOutMinor);
    }

    // Subaccounts (split payments)

    [Fact]
    public async Task A_course_sale_with_a_subaccount_is_split_by_Paystack_and_settled_outside_the_payout_ledger()
    {
        var (tutor, tenantId) = await TutorAsync("Split", Host);
        await VerifiedAccountAsync(tutor);
        var before = Paystack.Subaccounts.Count;
        var enabled = await Json(await tutor.PostAsync("/api/academy/payout-account/direct-settlement", null, Ct));
        Assert.True(enabled.GetProperty("payoutAccount").GetProperty("directSettlement").GetBoolean());
        var sub = Paystack.Subaccounts.Skip(before).Single();
        Assert.Equal("058", sub.GetProperty("settlement_bank").GetString());
        Assert.Equal("0123456789", sub.GetProperty("account_number").GetString());
        Assert.Equal("Split Academy", sub.GetProperty("business_name").GetString());
        Assert.True(sub.GetProperty("percentage_charge").GetDecimal() > 0);

        var course = await PublishedCourseAsync(tutor, "Split course", 1_000_000);
        var learner = ApiFixture.On(Host, await LearnerAsync("SplitBuyer"));
        var checkout = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct));
        var init = api.Gateway.Initialized.Single(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        Assert.NotNull(init.Split);
        Assert.StartsWith("ACCT_", init.Split!.Subaccount);

        api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, 1_000_000, "NGN", DateTimeOffset.UtcNow);
        (await learner.PostAsJsonAsync($"/api/learn/orders/{checkout.GetProperty("orderId").GetGuid()}/verify", new { reference = init.Reference }, Ct)).EnsureSuccessStatusCode();
        var earning = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().SingleAsync(e => e.TenantId == tenantId, Ct));
        Assert.True(earning.SettledByProvider);
        Assert.Equal(init.Split.PlatformFeeMinor, earning.FeeMinor); // the fee Paystack kept is the fee recorded
        Assert.Equal(1_000_000 - init.Split.PlatformFeeMinor, earning.NetMinor);

        var ledger = await LedgerAsync(tenantId, DateTimeOffset.UtcNow.AddDays(60));
        Assert.Equal(LedgerRules.EarningState.Settled, ledger.States[earning.Id]);
        Assert.Equal(0, ledger.AvailableMinor);
        Assert.Equal(0, ledger.PendingMinor);
        Assert.Equal(earning.NetMinor, ledger.SettledMinor);

        // Switched off direct settlement: the next sale goes through the platform account as before.
        (await tutor.DeleteAsync("/api/academy/payout-account/direct-settlement", Ct)).EnsureSuccessStatusCode();
        var other = await PublishedCourseAsync(tutor, "Unsplit course", 1_000_000);
        var next = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = other }, Ct));
        Assert.Null(api.Gateway.Initialized.Single(i => next.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Split);
    }

    [Fact]
    public async Task A_stay_with_a_subaccount_carries_the_split_and_the_commission()
    {
        var kit = new PropertyKit(api);
        var agency = await kit.AgencyAsync("Split Homes", verified: true);
        var client = ApiFixture.On(Host, agency.Client);
        (await client.PutAsJsonAsync("/api/property/payout-account",
            new { bankName = "Guaranty Trust Bank", bankCode = "058", accountNumber = "0987654321", accountName = "Grace" }, Ct)).EnsureSuccessStatusCode();
        var enabled = await Json(await client.PostAsync("/api/property/payout-account/direct-settlement", null, Ct));
        Assert.True(enabled.GetProperty("directSettlement").GetBoolean());

        var listing = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Split apartment", purpose: ListingPurpose.ShortLet, price: 5_000_000));
        var guest = ApiFixture.On(Host, await kit.SeekerAsync("Split Guest"));
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var booking = await Json(await guest.PostAsJsonAsync("/api/stays",
            new { listingId = listing, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(2).ToString("yyyy-MM-dd"), guests = 2, phone = "+2348066666666" }, Ct));
        var init = api.Gateway.Initialized.Last(i => booking.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        Assert.NotNull(init.Split);
        var stay = await api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().SingleAsync(s => s.Reference == init.Reference, Ct));
        Assert.Equal(stay.CommissionMinor, init.Split!.PlatformFeeMinor);

        api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, stay.TotalMinor, "NGN", DateTimeOffset.UtcNow);
        (await guest.PostAsJsonAsync($"/api/stays/{stay.Id}/verify", new { reference = init.Reference }, Ct)).EnsureSuccessStatusCode();
        var earning = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().SingleAsync(e => e.SourceId == stay.Id, Ct));
        Assert.True(earning.SettledByProvider);
        var states = await Json(await client.GetAsync("/api/property/earnings", Ct));
        Assert.Equal("Settled", states.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == earning.Id).GetProperty("status").GetString());
    }

    [Fact]
    public async Task With_subaccounts_off_a_stored_subaccount_is_not_used()
    {
        var (tutor, tenantId) = await TutorAsync("NoSplit", Host);
        await VerifiedAccountAsync(tutor);
        (await tutor.PostAsync("/api/academy/payout-account/direct-settlement", null, Ct)).EnsureSuccessStatusCode();
        var course = await PublishedCourseAsync(tutor, "Plain course", 1_000_000);

        var learner = await LearnerAsync("PlainBuyer"); // the default host: subaccounts off
        var checkout = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct));
        var init = api.Gateway.Initialized.Single(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        Assert.Null(init.Split);
        api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, 1_000_000, "NGN", DateTimeOffset.UtcNow);
        (await learner.PostAsJsonAsync($"/api/learn/orders/{checkout.GetProperty("orderId").GetGuid()}/verify", new { reference = init.Reference }, Ct)).EnsureSuccessStatusCode();
        Assert.False((await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().SingleAsync(e => e.TenantId == tenantId, Ct))).SettledByProvider);
        // And the switch being off refuses new subaccounts.
        Assert.Equal(HttpStatusCode.Conflict, (await ApiFixture.On(api, tutor).PostAsync("/api/academy/payout-account/direct-settlement", null, Ct)).StatusCode);
    }

    // Fix round: refunds of sales Paystack settled directly (C2)

    [Fact]
    public async Task A_tutor_cannot_refund_a_directly_settled_sale_and_Finance_refunds_it_with_a_clawback()
    {
        var (tutor, tenantId, orderId, reference) = await SplitCourseSaleAsync("Clawback");
        var earning = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.SourceId == orderId, Ct));
        Assert.True(earning.SettledByProvider);

        var refused = await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Ask LinkSuite support to refund it", await refused.Content.ReadAsStringAsync(Ct));
        Assert.DoesNotContain(api.Gateway.Refunds, r => r.Reference == reference);
        Assert.Equal(OrderState.Paid, (await OrderAsync(orderId)).Status);

        // Finance refunds it from the platform's balance; the tutor's settled share is taken back from their next payouts.
        await SeedEarningAsync(tenantId, 2_000_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        (await admin.PostAsync($"/api/admin/course-orders/{orderId}/refund", null, Ct)).EnsureSuccessStatusCode();
        Assert.Single(api.Gateway.Refunds, r => r.Reference == reference && r.AmountMinor == 1_000_000);
        Assert.Equal(OrderState.Refunded, (await OrderAsync(orderId)).Status);
        var clawback = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.SourceId == orderId && e.Source == EarningSource.CourseClawback, Ct));
        Assert.Equal(-earning.NetMinor, clawback.NetMinor);
        Assert.Equal(tenantId, clawback.TenantId);
        var ledger = await LedgerAsync(tenantId);
        Assert.Equal(2_000_000 - earning.NetMinor, ledger.AvailableMinor);
        Assert.Equal(LedgerRules.EarningState.Clawback, ledger.States[clawback.Id]);
        var alert = Assert.Single(await ClawbackAlertsAsync(), n => n.Payload["reference"] == reference);
        Assert.Contains("Clawback Academy", alert.Payload["business"]);

        // Refunding again changes nothing.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/course-orders/{orderId}/refund", null, Ct)).StatusCode);
        Assert.Single(api.Gateway.Refunds, r => r.Reference == reference);
    }

    [Fact]
    public async Task A_host_cannot_cancel_a_directly_settled_stay_and_a_guest_cancellation_claws_the_share_back()
    {
        var (host, guest, stay) = await SplitStayAsync("Clawback Homes");
        var earning = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.SourceId == stay.Id, Ct));

        var refused = await host.PostAsync($"/api/property/stays/{stay.Id}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Ask LinkSuite support to refund it", await refused.Content.ReadAsStringAsync(Ct));
        Assert.DoesNotContain(api.Gateway.Refunds, r => r.Reference == stay.Reference);

        // The guest cancels a month ahead: all of it back, from the platform's balance; the host's settled share is clawed back.
        (await guest.PostAsync($"/api/stays/{stay.Id}/cancel", null, Ct)).EnsureSuccessStatusCode();
        Assert.Single(api.Gateway.Refunds, r => r.Reference == stay.Reference && r.AmountMinor == stay.TotalMinor);
        var clawback = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.SourceId == stay.Id && e.Source == EarningSource.StayClawback, Ct));
        Assert.Equal(-earning.NetMinor, clawback.NetMinor);
        Assert.Contains(await ClawbackAlertsAsync(), n => n.Payload["reference"] == stay.Reference);
        var states = await Json(await host.GetAsync("/api/property/earnings", Ct));
        Assert.Equal("Clawback", states.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == clawback.Id).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_split_payment_that_lands_after_the_guest_cancelled_is_refunded_and_clawed_back()
    {
        var (_, guest, stay) = await SplitStayAsync("Late Split Homes", pay: false);
        (await guest.PostAsync($"/api/stays/{stay.Id}/cancel", null, Ct)).EnsureSuccessStatusCode(); // cancelled while unpaid
        api.Gateway.Charges[stay.Reference] = new VerifiedCharge(stay.Reference, stay.TotalMinor, "NGN", DateTimeOffset.UtcNow);

        using (var scope = Host.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<SalesPaymentChecks>().CheckAsync(stay.Reference, TimeSpan.Zero, Ct));

        Assert.Single(api.Gateway.Refunds, r => r.Reference == stay.Reference && r.AmountMinor == stay.TotalMinor);
        var clawback = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.SourceId == stay.Id && e.Source == EarningSource.StayClawback, Ct));
        Assert.Equal(-(stay.TotalMinor - stay.CommissionMinor), clawback.NetMinor); // what Paystack settled to the host
        Assert.Contains(await ClawbackAlertsAsync(), n => n.Payload["reference"] == stay.Reference);
    }

    // Fix round: transfers that may have been sent (I1) and the re-check across businesses (I2)

    [Fact]
    public async Task A_verify_answer_that_is_not_not_found_never_resends_the_transfer()
    {
        var (_, _, payoutId) = await RequestedPayoutAsync("VerifyBad", 500_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        var reference = (await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct))).GetProperty("transferReference").GetString()!;

        // Paystack answers verify with a 400 (or a 200 with status false) that isn't "transfer not found": nothing can be concluded.
        Paystack.AnswerNextVerify(reference, HttpStatusCode.BadRequest, "Invalid key");
        await RecheckAsync(DateTimeOffset.UtcNow.AddHours(1));
        Paystack.AnswerNextVerify(reference, HttpStatusCode.OK, "Something went wrong");
        await RecheckAsync(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Single(Paystack.TransferCalls, r => r == reference);
        Assert.Equal(PayoutStatus.Processing, (await PayoutAsync(payoutId)).Status);
    }

    [Fact]
    public async Task A_refused_resend_is_checked_with_Paystack_before_the_payout_is_failed()
    {
        var (_, _, payoutId) = await RequestedPayoutAsync("ResendRefused", 500_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        var reference = (await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct))).GetProperty("transferReference").GetString()!;

        // Verify says not found once (Paystack was still writing it), so the re-check sends it again; Paystack refuses the duplicate.
        Paystack.AnswerNextVerify(reference, HttpStatusCode.NotFound, "Transfer not found");
        Paystack.RefuseNextTransfer = "Duplicate Transaction Reference";
        await RecheckAsync(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(PayoutStatus.Processing, (await PayoutAsync(payoutId)).Status); // the transfer exists and is pending: not failed

        Paystack.Transfers[reference].Status = "success";
        await RecheckAsync(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(PayoutStatus.Paid, (await PayoutAsync(payoutId)).Status);
    }

    [Fact]
    public async Task A_success_after_a_failure_marks_the_payout_paid_and_alerts_Finance()
    {
        var (tutor, tenantId, payoutId) = await RequestedPayoutAsync("LateSuccess", 500_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        var reference = (await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct))).GetProperty("transferReference").GetString()!;
        (await TransferWebhookAsync("transfer.failed", reference, 500_000, "Timeout at the bank")).EnsureSuccessStatusCode();
        Assert.Equal(PayoutStatus.Failed, (await PayoutAsync(payoutId)).Status);
        Assert.Equal(800_000, (await LedgerAsync(tenantId)).AvailableMinor);

        (await TransferWebhookAsync("transfer.success", reference, 500_000)).EnsureSuccessStatusCode();
        var payout = await PayoutAsync(payoutId);
        Assert.Equal(PayoutStatus.Paid, payout.Status);
        Assert.Equal("success", payout.TransferStatus);
        var ledger = await LedgerAsync(tenantId);
        Assert.Equal(300_000, ledger.AvailableMinor); // the money left after all: held again
        Assert.Equal(500_000, ledger.PaidOutMinor);
        Assert.Contains(await api.WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.TemplateKey == "payout.transfer_late_success").ToListAsync(Ct)),
            n => n.Payload["reference"] == reference);
        _ = tutor;
    }

    [Fact]
    public async Task The_recheck_settles_payouts_of_several_businesses_in_one_run()
    {
        var admin = ApiFixture.On(Host, await AdminAsync());
        var references = new List<(Guid PayoutId, string Reference)>();
        foreach (var name in new[] { "RecheckOne", "RecheckTwo", "RecheckThree" })
        {
            var (_, _, payoutId) = await RequestedPayoutAsync(name, 500_000);
            references.Add((payoutId, (await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct))).GetProperty("transferReference").GetString()!));
        }
        foreach (var (_, reference) in references) Paystack.Transfers[reference].Status = "success";

        await RecheckAsync(DateTimeOffset.UtcNow.AddHours(1));
        foreach (var (payoutId, _) in references) Assert.Equal(PayoutStatus.Paid, (await PayoutAsync(payoutId)).Status);
    }

    [Fact]
    public async Task With_automatic_transfers_off_the_recheck_still_settles_but_never_resends()
    {
        var (_, _, payoutId) = await RequestedPayoutAsync("OffRecheck", 500_000);
        var admin = ApiFixture.On(Host, await AdminAsync());
        Paystack.UnavailableNextTransfer = true; // Paystack never got it
        var reference = (await Json(await admin.PostAsync($"/api/admin/payouts/{payoutId}/send", null, Ct))).GetProperty("transferReference").GetString()!;

        // Automatic transfers are switched off while it is in flight: the default host's re-check keeps asking, but doesn't send.
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PayoutAdminService>().RecheckTransfersAsync(DateTimeOffset.UtcNow.AddHours(1), Ct);
        Assert.Single(Paystack.TransferCalls, r => r == reference);
        Assert.Equal(PayoutStatus.Processing, (await PayoutAsync(payoutId)).Status);
    }

    // Helpers

    private Task<List<Saas.Domain.Core.Notification>> ClawbackAlertsAsync() =>
        api.WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.TemplateKey == "payout.clawback").ToListAsync(Ct));

    private Task<CourseOrder> OrderAsync(Guid id) => api.WithDbAsync(db => db.CourseOrders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == id, Ct));

    private Task SeedEarningAsync(Guid tenantId, long amount) => api.WithDbAsync(async db =>
    {
        db.Earnings.Add(new Earning
        {
            TenantId = tenantId, Source = EarningSource.CourseOrder, SourceId = Guid.NewGuid(), Description = "Seeded sale",
            UserId = Guid.NewGuid(), Channel = SaleChannel.Academy, GrossMinor = amount, NetMinor = amount, AvailableAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        return await db.SaveChangesAsync(Ct);
    });

    /// <summary>A tutor with direct settlement and a ₦10,000 course sale Paystack split (paid and confirmed).</summary>
    private async Task<(HttpClient Tutor, Guid TenantId, Guid OrderId, string Reference)> SplitCourseSaleAsync(string name)
    {
        var (tutor, tenantId) = await TutorAsync(name, Host);
        await VerifiedAccountAsync(tutor);
        (await tutor.PostAsync("/api/academy/payout-account/direct-settlement", null, Ct)).EnsureSuccessStatusCode();
        var course = await PublishedCourseAsync(tutor, $"{name} course", 1_000_000);
        var learner = ApiFixture.On(Host, await LearnerAsync($"{name}Buyer"));
        var checkout = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct));
        var init = api.Gateway.Initialized.Single(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        Assert.NotNull(init.Split);
        api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, 1_000_000, "NGN", DateTimeOffset.UtcNow);
        var orderId = checkout.GetProperty("orderId").GetGuid();
        (await learner.PostAsJsonAsync($"/api/learn/orders/{orderId}/verify", new { reference = init.Reference }, Ct)).EnsureSuccessStatusCode();
        return (tutor, tenantId, orderId, init.Reference);
    }

    /// <summary>A host with direct settlement and a stay a month ahead, booked with a Paystack split (and paid, unless told not to).</summary>
    private async Task<(HttpClient Host, HttpClient Guest, Stay Stay)> SplitStayAsync(string name, bool pay = true)
    {
        var kit = new PropertyKit(api);
        var agency = await kit.AgencyAsync(name, verified: true);
        var client = ApiFixture.On(Host, agency.Client);
        (await client.PutAsJsonAsync("/api/property/payout-account",
            new { bankName = "Guaranty Trust Bank", bankCode = "058", accountNumber = "0987654321", accountName = "Grace" }, Ct)).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/property/payout-account/direct-settlement", null, Ct)).EnsureSuccessStatusCode();
        var listing = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"{name} apartment", purpose: ListingPurpose.ShortLet, price: 5_000_000));
        var guest = ApiFixture.On(Host, await kit.SeekerAsync($"{name} Guest"));
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var booking = await Json(await guest.PostAsJsonAsync("/api/stays",
            new { listingId = listing, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(2).ToString("yyyy-MM-dd"), guests = 2, phone = "+2348066666666" }, Ct));
        var init = api.Gateway.Initialized.Last(i => booking.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        Assert.NotNull(init.Split);
        var stay = await api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Reference == init.Reference, Ct));
        if (pay)
        {
            api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, stay.TotalMinor, "NGN", DateTimeOffset.UtcNow);
            (await guest.PostAsJsonAsync($"/api/stays/{stay.Id}/verify", new { reference = init.Reference }, Ct)).EnsureSuccessStatusCode();
        }
        return (client, guest, stay);
    }

    private async Task<(HttpClient Client, Guid TenantId)> TutorAsync(string name, WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var response = await api.SignUpAsync(client, "/api/auth/signup", new
        {
            fullName = $"{name} Tutor", email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
            country = "NG", product = "courses", businessName = $"{name} Academy",
        }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return (client, auth.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    private async Task<HttpClient> LearnerAsync(string name)
    {
        var client = api.CreateClient();
        var response = await api.SignUpAsync(client, "/api/auth/learner/signup",
            new { fullName = $"{name} Learner", email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123" }, Ct);
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(response)).GetProperty("accessToken").GetString());
        return client;
    }

    private async Task VerifiedAccountAsync(HttpClient tutor) =>
        (await tutor.PutAsJsonAsync("/api/academy/payout-account",
            new { bankName = "Guaranty Trust Bank", bankCode = "058", accountNumber = "0123456789", accountName = "Ada" }, Ct)).EnsureSuccessStatusCode();

    /// <summary>A course with one text lesson, published.</summary>
    private async Task<Guid> PublishedCourseAsync(HttpClient tutor, string title, long price)
    {
        var created = await Json(await tutor.PostAsJsonAsync("/api/studio/courses", new { title, priceMinor = price, isFree = false, level = "AllLevels", listOnMarketplace = false }, Ct));
        var id = created.GetProperty("id").GetGuid();
        var section = created.GetProperty("sections")[0].GetProperty("id").GetGuid();
        var built = await Json(await tutor.PostAsJsonAsync($"/api/studio/sections/{section}/lessons", new { title = "Reading", type = "Text" }, Ct));
        var lesson = built.GetProperty("sections")[0].GetProperty("lessons")[0].GetProperty("id").GetGuid();
        (await tutor.PutAsJsonAsync($"/api/studio/lessons/{lesson}", new { title = "Reading", type = "Text", isPreview = false, content = "# Hello" }, Ct)).EnsureSuccessStatusCode();
        (await tutor.PostAsync($"/api/studio/courses/{id}/publish", null, Ct)).EnsureSuccessStatusCode();
        return id;
    }

    /// <summary>A tutor with <paramref name="earned"/> (default ₦8,000) available, a verified bank account, and a payout requested.</summary>
    private async Task<(HttpClient Tutor, Guid TenantId, Guid PayoutId)> RequestedPayoutAsync(string name, long amount, long earned = 800_000,
        WebApplicationFactory<Program>? host = null)
    {
        host ??= Host;
        var (tutor, tenantId) = await TutorAsync(name, host);
        await api.WithDbAsync(async db =>
        {
            db.Earnings.Add(new Earning
            {
                TenantId = tenantId, Source = EarningSource.CourseOrder, SourceId = Guid.NewGuid(), Description = "Seeded sale",
                UserId = Guid.NewGuid(), Channel = SaleChannel.Academy, GrossMinor = earned, NetMinor = earned, AvailableAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            return await db.SaveChangesAsync(Ct);
        });
        var account = new Dictionary<string, object?> { ["bankName"] = "Guaranty Trust Bank", ["accountNumber"] = "0123456789", ["accountName"] = "Ada" };
        if (!ReferenceEquals(host, api)) account["bankCode"] = "058";
        (await tutor.PutAsJsonAsync("/api/academy/payout-account", account, Ct)).EnsureSuccessStatusCode();
        var payout = await Json(await tutor.PostAsJsonAsync("/api/studio/payouts", new { amountMinor = amount }, Ct));
        return (tutor, tenantId, payout.GetProperty("id").GetGuid());
    }

    private async Task<HttpClient> AdminAsync()
    {
        var admin = api.CreateClient();
        var login = await api.SignUpAsync(admin, "/api/auth/learner/signup", new { fullName = "Admin", email = ApiFixture.AdminEmail, password = "password123" }, Ct);
        if (!login.IsSuccessStatusCode)
            (await admin.PostAsJsonAsync("/api/auth/learner/login", new { email = ApiFixture.AdminEmail, password = "password123" }, Ct)).EnsureSuccessStatusCode();
        return (await PlatformAuthTests.PanelLoginAsync(api, ApiFixture.AdminEmail, "password123")).Client;
    }

    /// <summary>Alerts emailed to Finance about payout transfers that failed or were reversed.</summary>
    private Task<List<Saas.Domain.Core.Notification>> FinanceAlertsAsync() =>
        api.WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.TemplateKey == "payout.transfer_failed").ToListAsync(Ct));

    private Task<Payout> PayoutAsync(Guid id) => api.WithDbAsync(db => db.Payouts.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == id, Ct));

    private Task<LedgerRules.Ledger> LedgerAsync(Guid tenantId, DateTimeOffset? at = null) =>
        api.AsTenantAsync<PayoutService, LedgerRules.Ledger>(tenantId, s => s.LedgerAsync(Ct, at));

    private async Task RecheckAsync(DateTimeOffset now)
    {
        using var scope = Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PayoutAdminService>().RecheckTransfersAsync(now, Ct);
    }

    private Task<HttpResponseMessage> TransferWebhookAsync(string ev, string reference, long amount, string? reason = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            @event = ev,
            data = new { reference, amount, currency = "NGN", status = ev["transfer.".Length..], transfer_code = "TRF_hook", reason = reason ?? "LinkSuite payout" },
        });
        var signature = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(ApiFixture.PaystackSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paystack") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("x-paystack-signature", signature);
        return api.CreateClient().SendAsync(request, Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) Assert.Fail($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }
}
