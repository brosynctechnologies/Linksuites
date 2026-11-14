using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Domain.Core;
using Saas.Domain.Courses;
using Saas.Domain.Property;

namespace Saas.IntegrationTests;

/// <summary>
/// The admin payments switch and plan payments by bank transfer: while online payment is off, no new payment reaches the platform
/// gateway, and a business pays for a plan by transfer, which a platform admin confirms or rejects.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class PlanBankTransferTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    /// <summary>A WhatsApp Orders business on the free plan (its Pro plan costs 300,000 kobo).</summary>
    private async Task<(HttpClient Owner, Guid TenantId)> BusinessAsync(string name)
    {
        var (owner, auth) = await api.SignupAsync(name.Replace(" ", ""));
        return (owner, auth.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> ClaimAsync(HttpClient owner, string plan = "pro") =>
        owner.PostAsJsonAsync("/api/billing/transfer", new { planCode = plan }, Ct);

    private static async Task<string> PlanAsync(HttpClient owner) =>
        (await Json(await owner.GetAsync("/api/billing", Ct))).GetProperty("currentPlan").GetProperty("code").GetString()!;

    private Task<Payment> PaymentAsync(string reference) =>
        api.WithDbAsync(db => db.Payments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.ProviderRef == reference, Ct));

    private Task<Guid> PaymentIdAsync(string reference) => api.WithDbAsync(db =>
        db.Payments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference).Select(p => p.Id).SingleAsync(Ct));

    private Task<HttpResponseMessage> PaystackWebhookAsync(string reference, long amount)
    {
        var body = $$$"""{"event":"charge.success","data":{"status":"success","reference":"{{{reference}}}","amount":{{{amount}}},"currency":"NGN"}}""";
        var signature = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(ApiFixture.PaystackSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paystack") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("x-paystack-signature", signature);
        return api.CreateClient().SendAsync(request, Ct);
    }

    /// <summary>Runs <paramref name="body"/> with online payment off (bank details as given), then puts the suite's default back.</summary>
    private async Task OfflineAsync(Func<Task> body, bool bankDetails = true)
    {
        if (bankDetails) await api.SetPaymentSettingsAsync(false);
        else await api.SetPaymentSettingsAsync(false, bankName: null, accountNumber: null, accountName: null);
        try { await body(); }
        finally { await api.ResetPaymentSettingsAsync(); }
    }

    private Task<(HttpClient Client, Guid UserId, string Token)> AdminAsync(PlatformRole role) => PlatformAuthTests.PanelAdminAsync(api, role);

    // The switch

    [Fact]
    public async Task Plan_checkout_is_refused_and_bank_details_are_offered_while_online_payment_is_off()
    {
        var (owner, _) = await BusinessAsync("Switch Off Plans");
        await OfflineAsync(async () =>
        {
            var before = api.Gateway.Initialized.Count;
            var billing = await Json(await owner.GetAsync("/api/billing", Ct));
            var checkout = await owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct);

            Assert.False(billing.GetProperty("paymentsEnabled").GetBoolean());
            var bank = billing.GetProperty("bankTransfer");
            Assert.Equal((ApiFixture.InitialBankName, ApiFixture.InitialAccountNumber, ApiFixture.InitialAccountName),
                (bank.GetProperty("bankName").GetString(), bank.GetProperty("accountNumber").GetString(), bank.GetProperty("accountName").GetString()));
            Assert.Equal(JsonValueKind.Null, billing.GetProperty("pendingTransfer").ValueKind);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, checkout.StatusCode);
            Assert.Equal(before, api.Gateway.Initialized.Count);
        });

        var on = await Json(await owner.GetAsync("/api/billing", Ct));
        Assert.True(on.GetProperty("paymentsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, on.GetProperty("bankTransfer").ValueKind); // only offered while online payment is off
    }

    [Fact]
    public async Task Course_sales_stays_and_boosts_never_reach_the_gateway_while_online_payment_is_off()
    {
        // A published paid course.
        var tutor = api.CreateClient();
        var signup = await Json(await api.SignUpAsync(tutor, "/api/auth/signup", new
        {
            fullName = "Offline Tutor", email = $"offline-tutor-{Guid.NewGuid():N}@example.com", password = "password123", country = "NG",
            product = "courses", businessName = "Offline Academy",
        }, Ct));
        tutor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signup.GetProperty("accessToken").GetString());
        var course = (await Json(await tutor.PostAsJsonAsync("/api/studio/courses",
            new { title = "Offline course", priceMinor = 500_000, isFree = false, level = "AllLevels", listOnMarketplace = false }, Ct))).GetProperty("id").GetGuid();
        await api.WithDbAsync(db => db.Courses.IgnoreQueryFilters().Where(c => c.Id == course)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CourseStatus.Published), Ct));
        var learner = api.CreateClient();
        var learnerAuth = await Json(await api.SignUpAsync(learner, "/api/auth/learner/signup",
            new { fullName = "Offline Learner", email = $"offline-learner-{Guid.NewGuid():N}@example.com", password = "password123" }, Ct));
        learner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", learnerAuth.GetProperty("accessToken").GetString());

        // A live short-let and a guest.
        var kit = new PropertyKit(api);
        var agency = await kit.AgencyAsync("Offline Homes", verified: true);
        var listing = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Offline apartment", purpose: ListingPurpose.ShortLet, price: 5_000_000));
        var guest = await kit.SeekerAsync("Offline Guest");
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        await OfflineAsync(async () =>
        {
            var before = api.Gateway.Initialized.Count;
            var buy = await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct);
            var stay = await guest.PostAsJsonAsync("/api/stays", new
            {
                listingId = listing, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(2).ToString("yyyy-MM-dd"), guests = 2, phone = "+2348066666666",
            }, Ct);
            var boost = await agency.Client.PostAsJsonAsync($"/api/property/listings/{listing}/boosts", new { code = "featured-7" }, Ct);

            Assert.Equal((HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable),
                (buy.StatusCode, stay.StatusCode, boost.StatusCode));
            Assert.Equal(before, api.Gateway.Initialized.Count);
        });

        // Back on: the same purchase goes through.
        var again = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct));
        Assert.False(again.GetProperty("enrolled").GetBoolean());
    }

    [Fact]
    public async Task Payments_taken_before_the_switch_still_confirm_by_webhook_and_return_check()
    {
        var (byWebhook, _) = await BusinessAsync("Switch Webhook");
        var (byReturn, _) = await BusinessAsync("Switch Return");
        var webhookRef = (await Json(await byWebhook.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct)))
            .GetProperty("authorizationUrl").GetString()!.Split('/').Last();
        var returnRef = (await Json(await byReturn.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct)))
            .GetProperty("authorizationUrl").GetString()!.Split('/').Last();

        await OfflineAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await PaystackWebhookAsync(webhookRef, 300_000)).StatusCode);
            api.Gateway.Charges[returnRef] = new VerifiedCharge(returnRef, 300_000, "NGN", DateTimeOffset.UtcNow);
            var verified = await Json(await byReturn.PostAsJsonAsync("/api/billing/verify", new { reference = returnRef }, Ct));

            Assert.Equal(PaymentStatus.Succeeded, (await PaymentAsync(webhookRef)).Status);
            Assert.Equal("Paid", verified.GetProperty("status").GetString());
            Assert.Equal(("pro", "pro"), (await PlanAsync(byWebhook), await PlanAsync(byReturn)));
        });
    }

    // Claims

    [Fact]
    public async Task A_transfer_claim_records_one_pending_transfer_with_the_server_amount_and_tells_the_admins()
    {
        var (owner, tenantId) = await BusinessAsync("Transfer Claim");
        await OfflineAsync(async () =>
        {
            // The page shows the reference for the transfer note before the claim; it stays the same across reloads.
            var shown = (await Json(await owner.GetAsync("/api/billing", Ct))).GetProperty("bankTransfer").GetProperty("reference").GetString();
            var reloaded = (await Json(await owner.GetAsync("/api/billing", Ct))).GetProperty("bankTransfer").GetProperty("reference").GetString();
            var claim = await Json(await ClaimAsync(owner));
            var reference = claim.GetProperty("reference").GetString()!;

            Assert.Matches("^LS-[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}$", reference);
            Assert.Equal((reference, reference), (shown, reloaded));
            var next = (await Json(await owner.GetAsync("/api/billing", Ct))).GetProperty("bankTransfer").GetProperty("reference").GetString();
            Assert.NotEqual(reference, next); // a later claim gets a new one
            Assert.Equal(("pro", 300_000L, "NGN"), (claim.GetProperty("planCode").GetString(), claim.GetProperty("amountMinor").GetInt64(), claim.GetProperty("currency").GetString()));
            var payment = await PaymentAsync(reference);
            Assert.Equal(("transfer", PaymentStatus.Pending, "subscription", 300_000L, "NGN", tenantId),
                (payment.Provider, payment.Status, payment.Purpose, payment.AmountMinor, payment.Currency, payment.TenantId));

            var billing = await Json(await owner.GetAsync("/api/billing", Ct));
            var pending = billing.GetProperty("pendingTransfer");
            Assert.Equal((reference, "Pro", 300_000L), (pending.GetProperty("reference").GetString(), pending.GetProperty("planName").GetString(), pending.GetProperty("amountMinor").GetInt64()));
            Assert.Equal("free", billing.GetProperty("currentPlan").GetProperty("code").GetString());

            var emails = await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "plan.transfer_claimed").ToListAsync(Ct));
            Assert.Contains(emails, n => n.Payload.GetValueOrDefault("reference") == reference && n.Payload.GetValueOrDefault("amount")!.Contains("3,000"));
        });
    }

    [Fact]
    public async Task A_new_claim_replaces_the_pending_one_and_cancel_withdraws_it()
    {
        var (owner, tenantId) = await BusinessAsync("Transfer Replace");
        await OfflineAsync(async () =>
        {
            var first = (await Json(await ClaimAsync(owner))).GetProperty("reference").GetString()!;
            var second = (await Json(await ClaimAsync(owner))).GetProperty("reference").GetString()!;

            Assert.NotEqual(first, second);
            Assert.Equal(PaymentStatus.Abandoned, (await PaymentAsync(first)).Status);
            Assert.Equal(PaymentStatus.Pending, (await PaymentAsync(second)).Status);
            Assert.Equal(1, await api.WithDbAsync(db => db.Payments.IgnoreQueryFilters()
                .CountAsync(p => p.TenantId == tenantId && p.Provider == "transfer" && p.Status == PaymentStatus.Pending, Ct)));

            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/billing/transfer", Ct)).StatusCode);
            Assert.Equal(PaymentStatus.Abandoned, (await PaymentAsync(second)).Status);
            Assert.Equal(JsonValueKind.Null, (await Json(await owner.GetAsync("/api/billing", Ct))).GetProperty("pendingTransfer").ValueKind);
        });
    }

    [Fact]
    public async Task A_claim_is_refused_while_online_payment_is_on()
    {
        var (owner, tenantId) = await BusinessAsync("Transfer Online On");
        var claim = await ClaimAsync(owner);
        Assert.Equal(HttpStatusCode.Conflict, claim.StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Payments.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId, Ct)));
    }

    [Fact]
    public async Task A_claim_is_refused_without_bank_details()
    {
        var (owner, _) = await BusinessAsync("Transfer No Bank");
        await OfflineAsync(async () =>
        {
            var billing = await Json(await owner.GetAsync("/api/billing", Ct));
            Assert.Equal(JsonValueKind.Null, billing.GetProperty("bankTransfer").ValueKind);
            Assert.Equal(HttpStatusCode.Conflict, (await ClaimAsync(owner)).StatusCode);
        }, bankDetails: false);
    }

    [Fact]
    public async Task A_claim_is_refused_for_a_free_or_unknown_plan_and_for_members_who_cannot_buy_plans()
    {
        // A courses business on Pro (3 seats) with a staff member.
        var owner = api.CreateClient();
        var signup = await Json(await api.SignUpAsync(owner, "/api/auth/signup", new
        {
            fullName = "Transfer Owner", email = $"transfer-owner-{Guid.NewGuid():N}@example.com", password = "password123", country = "NG",
            product = "courses", businessName = "Transfer Academy",
        }, Ct));
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signup.GetProperty("accessToken").GetString());
        await api.MakeProAsync(signup.GetProperty("tenant").GetProperty("id").GetGuid(), "courses");
        var staffEmail = $"transfer-staff-{Guid.NewGuid():N}@example.com";
        (await owner.PostAsJsonAsync("/api/team/invites", new { email = staffEmail, role = "Admin" }, Ct)).EnsureSuccessStatusCode();
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == staffEmail).FirstAsync(Ct))).Payload["link"];
        var accepted = await Json(await api.CreateClient().PostAsJsonAsync($"/api/auth/invites/{link[(link.LastIndexOf('/') + 1)..]}/accept",
            new { fullName = "Transfer Staff", password = "password123" }, Ct));
        var staff = api.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accepted.GetProperty("accessToken").GetString());

        await OfflineAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await ClaimAsync(owner, "free")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ClaimAsync(owner, "no-such-plan")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ClaimAsync(staff, "pro")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync("/api/billing/transfer", Ct)).StatusCode);
        });
    }

    [Fact]
    public async Task The_return_check_and_the_recheck_job_never_ask_a_provider_about_a_transfer()
    {
        var (owner, _) = await BusinessAsync("Transfer No Provider");
        await OfflineAsync(async () =>
        {
            var reference = (await Json(await ClaimAsync(owner))).GetProperty("reference").GetString()!;

            var verified = await Json(await owner.PostAsJsonAsync("/api/billing/verify", new { reference }, Ct));
            Assert.Equal("Pending", verified.GetProperty("status").GetString());

            // Old enough for the re-check and past its 24-hour window: still left alone.
            await api.WithDbAsync(db => db.Payments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, DateTimeOffset.UtcNow.AddHours(-30)), Ct));
            using (var scope = api.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<PlanPaymentJobs>().RunOnceAsync(DateTimeOffset.UtcNow, Ct);

            var payment = await PaymentAsync(reference);
            Assert.Equal((PaymentStatus.Pending, (DateTimeOffset?)null), (payment.Status, payment.RecheckedAt));
            Assert.False(api.Gateway.StatusCalls.ContainsKey(reference));
            Assert.False(api.Opay.StatusCalls.ContainsKey(reference));

            // A provider webhook naming a transfer reference changes nothing either.
            Assert.Equal(HttpStatusCode.OK, (await PaystackWebhookAsync(reference, 300_000)).StatusCode);
            Assert.Equal(PaymentStatus.Pending, (await PaymentAsync(reference)).Status);
        });
    }

    // Admin confirm and reject

    [Fact]
    public async Task Admin_confirm_starts_a_paid_period_and_records_the_payment()
    {
        var (owner, tenantId) = await BusinessAsync("Transfer Confirm");
        var (finance, financeId, _) = await AdminAsync(PlatformRole.Finance);
        await OfflineAsync(async () =>
        {
            var reference = (await Json(await ClaimAsync(owner))).GetProperty("reference").GetString()!;
            var id = await PaymentIdAsync(reference);

            var list = await Json(await finance.GetAsync("/api/platform/transfers", Ct));
            var row = list.EnumerateArray().First(r => r.GetProperty("id").GetGuid() == id);
            Assert.Equal(("Pending", reference, 300_000L, "Pro", "whatsapp-orders"),
                (row.GetProperty("status").GetString(), row.GetProperty("reference").GetString(), row.GetProperty("amountMinor").GetInt64(),
                 row.GetProperty("planName").GetString(), row.GetProperty("product").GetString()));
            Assert.Equal("Pending", list[0].GetProperty("status").GetString()); // pending first

            var confirmed = await Json(await finance.PostAsync($"/api/platform/transfers/{id}/confirm", null, Ct));
            Assert.Equal("Succeeded", confirmed.GetProperty("status").GetString());

            var payment = await PaymentAsync(reference);
            Assert.Equal(PaymentStatus.Succeeded, payment.Status);
            Assert.NotNull(payment.PaidAt);
            var sub = await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenantId, Ct));
            Assert.Equal((SubscriptionStatus.Active, "transfer", reference, 300_000L), (sub.Status, sub.Provider, sub.ProviderRef, sub.PriceMinor));
            Assert.InRange(sub.CurrentPeriodEnd!.Value, DateTimeOffset.UtcNow.AddDays(27), DateTimeOffset.UtcNow.AddDays(32));
            var billing = await Json(await owner.GetAsync("/api/billing", Ct));
            Assert.Equal("pro", billing.GetProperty("currentPlan").GetProperty("code").GetString());
            Assert.False(billing.GetProperty("subscription").GetProperty("complimentary").GetBoolean());
            Assert.Equal(JsonValueKind.Null, billing.GetProperty("pendingTransfer").ValueKind);

            Assert.True(await api.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "transfer.confirm" && a.EntityId == id.ToString() && a.ActorId == financeId, Ct)));
            Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "plan.transfer_confirmed" && n.TenantId == tenantId, Ct)));

            // Confirming again is a no-op that reports the state.
            var again = await Json(await finance.PostAsync($"/api/platform/transfers/{id}/confirm", null, Ct));
            Assert.Equal("Succeeded", again.GetProperty("status").GetString());
            Assert.Equal(1, await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, Ct)));
        });
    }

    [Fact]
    public async Task Two_confirms_at_once_give_one_subscription()
    {
        var (owner, tenantId) = await BusinessAsync("Transfer Double");
        var (finance, _, token) = await AdminAsync(PlatformRole.Finance);
        await OfflineAsync(async () =>
        {
            var id = await PaymentIdAsync((await Json(await ClaimAsync(owner))).GetProperty("reference").GetString()!);
            var clients = Enumerable.Range(0, 4).Select(_ =>
            {
                var c = api.CreateClient();
                c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return c;
            }).ToList();

            var answers = await Task.WhenAll(clients.Select(c => c.PostAsync($"/api/platform/transfers/{id}/confirm", null, Ct)));

            Assert.All(answers, a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
            Assert.Equal(1, await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, Ct)));
            Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "plan.transfer_confirmed" && n.TenantId == tenantId, Ct)));
        });
    }

    [Fact]
    public async Task Reject_marks_the_claim_failed_and_leaves_the_plan()
    {
        var (owner, tenantId) = await BusinessAsync("Transfer Reject");
        var (finance, _, _) = await AdminAsync(PlatformRole.Finance);
        await OfflineAsync(async () =>
        {
            var reference = (await Json(await ClaimAsync(owner))).GetProperty("reference").GetString()!;
            var id = await PaymentIdAsync(reference);

            Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync($"/api/platform/transfers/{id}/reject", new { reason = "no" }, Ct)).StatusCode);
            var rejected = await Json(await finance.PostAsJsonAsync($"/api/platform/transfers/{id}/reject",
                new { reason = "No transfer with this reference reached the account." }, Ct));

            Assert.Equal("Failed", rejected.GetProperty("status").GetString());
            Assert.Equal(PaymentStatus.Failed, (await PaymentAsync(reference)).Status);
            Assert.Equal("free", await PlanAsync(owner));
            Assert.False(await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId, Ct)));
            Assert.True(await api.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "transfer.reject" && a.EntityId == id.ToString(), Ct)));
            var email = await api.WithDbAsync(db => db.Notifications.SingleAsync(n => n.TemplateKey == "plan.transfer_rejected" && n.TenantId == tenantId, Ct));
            Assert.Equal("No transfer with this reference reached the account.", email.Payload["reason"]);

            // A rejected claim can't be confirmed afterwards.
            var confirm = await Json(await finance.PostAsync($"/api/platform/transfers/{id}/confirm", null, Ct));
            Assert.Equal("Failed", confirm.GetProperty("status").GetString());
            Assert.Equal("free", await PlanAsync(owner));
        });
    }

    [Fact]
    public async Task Only_finance_and_super_admins_handle_transfers()
    {
        var (owner, _) = await BusinessAsync("Transfer Forbidden");
        var (support, _, _) = await AdminAsync(PlatformRole.Support);
        await OfflineAsync(async () =>
        {
            var id = await PaymentIdAsync((await Json(await ClaimAsync(owner))).GetProperty("reference").GetString()!);

            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/platform/transfers", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/platform/transfers/{id}/confirm", null, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsync($"/api/platform/transfers/{id}/confirm", null, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync($"/api/platform/transfers/{id}/reject",
                new { reason = "Support should not be able to do this." }, Ct)).StatusCode);
            Assert.Equal(PaymentStatus.Pending, (await api.WithDbAsync(db => db.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == id, Ct))).Status);
        });
    }

    // Settings

    private static object Settings(bool online, string provider = "paystack", string bankName = "OPay", string accountNumber = "9067382888",
        string accountName = "Brosync Technologies") => new
    {
        onlinePaymentsEnabled = online, planProvider = provider, bankName, bankAccountNumber = accountNumber, bankAccountName = accountName,
    };

    [Fact]
    public async Task Settings_show_the_switch_the_bank_details_and_which_providers_are_configured()
    {
        var (finance, _, _) = await AdminAsync(PlatformRole.Finance);

        var settings = await Json(await finance.GetAsync("/api/platform/payment-settings", Ct));

        Assert.True(settings.GetProperty("onlinePaymentsEnabled").GetBoolean());
        Assert.Equal("paystack", settings.GetProperty("planProvider").GetString()); // from config while the row has none
        Assert.Equal(ApiFixture.InitialAccountNumber, settings.GetProperty("bankAccountNumber").GetString());
        Assert.True(settings.GetProperty("providers").GetProperty("paystack").GetBoolean());
        Assert.True(settings.GetProperty("providers").GetProperty("opay").GetBoolean());
        Assert.DoesNotContain(FakeOpay.SecretKey, settings.GetRawText());
        Assert.DoesNotContain(ApiFixture.PaystackSecret, settings.GetRawText());
    }

    [Fact]
    public async Task Settings_are_validated_and_only_super_admins_change_them()
    {
        var (super, _, _) = await AdminAsync(PlatformRole.SuperAdmin);
        var (finance, _, _) = await AdminAsync(PlatformRole.Finance);
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await finance.PutAsJsonAsync("/api/platform/payment-settings", Settings(false), Ct)).StatusCode);
            foreach (var bad in new[]
            {
                Settings(false, accountNumber: "12345"), Settings(false, accountNumber: "90673828ab"), Settings(false, accountNumber: "90673828881"),
                Settings(false, bankName: "  "), Settings(false, accountName: ""), Settings(false, accountName: new string('a', 101)),
                Settings(false, provider: "stripe"),
            })
                Assert.Equal(HttpStatusCode.BadRequest, (await super.PutAsJsonAsync("/api/platform/payment-settings", bad, Ct)).StatusCode);

            var saved = await Json(await super.PutAsJsonAsync("/api/platform/payment-settings",
                Settings(false, bankName: "  Moniepoint  ", accountNumber: " 0123456789 ", accountName: " Brosync Technologies Ltd "), Ct));
            Assert.Equal((false, "Moniepoint", "0123456789", "Brosync Technologies Ltd"),
                (saved.GetProperty("onlinePaymentsEnabled").GetBoolean(), saved.GetProperty("bankName").GetString(),
                 saved.GetProperty("bankAccountNumber").GetString(), saved.GetProperty("bankAccountName").GetString()));
            var logs = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.Action == "payment_settings.update").Select(a => a.AfterJson).ToListAsync(Ct));
            Assert.Contains(logs, after => after!.Contains("Moniepoint"));
        }
        finally
        {
            await api.ResetPaymentSettingsAsync();
        }
    }

    [Fact]
    public async Task Online_payment_cannot_be_switched_on_with_a_provider_that_is_not_configured()
    {
        var (super, _, _) = await AdminAsync(PlatformRole.SuperAdmin);
        var unconfigured = ApiFixture.On(api.UnconfiguredOpayHost, super); // OPay's secret key is missing on this host
        try
        {
            var settings = await Json(await unconfigured.GetAsync("/api/platform/payment-settings", Ct));
            Assert.False(settings.GetProperty("providers").GetProperty("opay").GetBoolean());

            Assert.Equal(HttpStatusCode.BadRequest, (await unconfigured.PutAsJsonAsync("/api/platform/payment-settings", Settings(true, provider: "opay"), Ct)).StatusCode);
            // While online payment is off it can be chosen, ready for when its keys are added; switching on is still refused.
            (await unconfigured.PutAsJsonAsync("/api/platform/payment-settings", Settings(false, provider: "opay"), Ct)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.BadRequest, (await unconfigured.PutAsJsonAsync("/api/platform/payment-settings", Settings(true, provider: "opay"), Ct)).StatusCode);
            (await unconfigured.PutAsJsonAsync("/api/platform/payment-settings", Settings(true, provider: "paystack"), Ct)).EnsureSuccessStatusCode();
        }
        finally
        {
            await api.ResetPaymentSettingsAsync();
        }
    }

    [Fact]
    public async Task Setting_changes_take_effect_at_once()
    {
        var (super, _, _) = await AdminAsync(PlatformRole.SuperAdmin);
        var (owner, _) = await BusinessAsync("Settings Live");
        try
        {
            (await super.PutAsJsonAsync("/api/platform/payment-settings", Settings(false, bankName: "Kuda", accountNumber: "1122334455"), Ct)).EnsureSuccessStatusCode();
            var off = await Json(await owner.GetAsync("/api/billing", Ct));
            Assert.False(off.GetProperty("paymentsEnabled").GetBoolean());
            Assert.Equal(("Kuda", "1122334455"), (off.GetProperty("bankTransfer").GetProperty("bankName").GetString(),
                off.GetProperty("bankTransfer").GetProperty("accountNumber").GetString()));

            // Online with OPay: new plan checkouts go to OPay without a restart.
            (await super.PutAsJsonAsync("/api/platform/payment-settings", Settings(true, provider: "opay"), Ct)).EnsureSuccessStatusCode();
            Assert.True((await Json(await owner.GetAsync("/api/billing", Ct))).GetProperty("paymentsEnabled").GetBoolean());
            var url = (await Json(await owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct))).GetProperty("authorizationUrl").GetString()!;
            Assert.StartsWith($"{FakeOpay.BaseUrl}/cashier/sub_", url);
            Assert.Equal("opay", (await PaymentAsync(url.Split('/').Last())).Provider);
        }
        finally
        {
            await api.ResetPaymentSettingsAsync();
        }
    }
}
