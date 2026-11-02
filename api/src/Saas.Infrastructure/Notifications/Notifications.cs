using Saas.Infrastructure.Jobs;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Notifications;

public sealed record EmailMessage(string To, string Subject, string Html, string Text);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed class EmailOptions
{
    public const string Section = "Email";
    public string FromEmail { get; set; } = "no-reply@example.com";
    public string FromName { get; set; } = "OrderLink";
    /// <summary>Brevo API key. When empty (and no <see cref="SmtpHost"/>), emails are only written to the log (development).</summary>
    public string? BrevoApiKey { get; set; }
    /// <summary>SMTP relay host. When set, it is used instead of Brevo.</summary>
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 25;
    /// <summary>Optional; leave empty for a relay that trusts the sender's network (e.g. Postfix on the host).</summary>
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    /// <summary>None, StartTls, SslOnConnect or Auto (STARTTLS when the server offers it).</summary>
    public string SmtpSecurity { get; set; } = "Auto";
}

/// <summary>Development sender: logs the email instead of sending it, so OTP codes show in the console.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        logger.LogInformation("EMAIL to {To}: {Subject}\n{Text}", m.To, m.Subject, m.Text);
        return Task.CompletedTask;
    }
}

/// <summary>Brevo transactional email (free tier is about 300 emails a day).</summary>
public sealed class BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = o.FromName, email = o.FromEmail },
                to = new[] { new { email = m.To } },
                subject = m.Subject,
                htmlContent = m.Html,
                textContent = m.Text,
            }),
        };
        request.Headers.Add("api-key", o.BrevoApiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Brevo returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    }
}

/// <summary>Sends through any SMTP server: a local Postfix relay, a mailbox provider, or Brevo's SMTP endpoint.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        var o = options.Value;
        var message = new MimeKit.MimeMessage();
        message.From.Add(new MimeKit.MailboxAddress(o.FromName, o.FromEmail));
        message.To.Add(MimeKit.MailboxAddress.Parse(m.To));
        message.Subject = m.Subject;
        message.Body = new MimeKit.BodyBuilder { TextBody = m.Text, HtmlBody = string.IsNullOrEmpty(m.Html) ? null : m.Html }.ToMessageBody();

        var security = Enum.TryParse<MailKit.Security.SecureSocketOptions>(o.SmtpSecurity, ignoreCase: true, out var s)
            ? s : MailKit.Security.SecureSocketOptions.Auto;
        using var client = new MailKit.Net.Smtp.SmtpClient();
        await client.ConnectAsync(o.SmtpHost!, o.SmtpPort, security, ct); // only registered when SmtpHost is set
        if (!string.IsNullOrEmpty(o.SmtpUsername))
            await client.AuthenticateAsync(o.SmtpUsername, o.SmtpPassword ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}

public static class EmailTemplates
{
    public static EmailMessage Render(Notification n, string appName)
    {
        string P(string key) => n.Payload.GetValueOrDefault(key) ?? "";
        var (subject, text) = n.TemplateKey switch
        {
            "otp" => ($"Your {appName} login code: {P("code")}",
                $"Hi {P("name")},\n\nYour login code is {P("code")}. It expires in 10 minutes.\n\nIf you did not ask for it, ignore this email."),
            "password.reset" => ($"Reset your {appName} password",
                $"Hi {P("name")},\n\nSomeone (hopefully you) asked to reset your password. Choose a new one here within 10 minutes:\n\n{P("link")}\n\n" +
                "If you didn't ask, ignore this email: your password stays the same."),
            "password.changed" => ($"Your {appName} password was changed",
                $"Hi {P("name")},\n\nYour password was just changed and you were signed out on your other devices.\n\n" +
                "If this wasn't you, reset your password now from the sign-in page and contact support."),
            "admin.code" => ($"Admin panel sign-in code: {P("code")}",
                $"Hi {P("name")},\n\nYour admin panel sign-in code is {P("code")}. It expires in 10 minutes.\n\n" +
                "If you didn't just sign in, change your password now and tell another super admin."),
            "admin.added" => ($"You're now a {P("role")} admin on {appName}",
                $"Hi {P("name")},\n\nYou've been given the {P("role")} role in the admin panel. Sign in with your usual password and the code we email you: {P("link")}"),
            "business.suspended" => ($"{P("business")} has been suspended on {appName}",
                $"We've suspended {P("business")}. Its public pages are hidden and its team can't sign in for now.\n\n" +
                $"Reason: {P("reason")}\n\nReply to this email to talk to support. Payments already made are safe."),
            "business.reactivated" => ($"{P("business")} is active again on {appName}",
                $"{P("business")} is active again. Your public pages are back and your team can sign in.\n\nNote from support: {P("reason")}"),
            "welcome" => ($"Welcome to {appName}, {P("name")}",
                $"Hi {P("name")},\n\nYour account for {P("storeName")} is ready. Sign in any time to pick up where you left off.\n\n{appName}"),
            "order.new" => ($"New order #{P("number")} from {P("customer")} ({P("total")})",
                $"{P("message")}\n\nOpen the order: {P("link")}"),
            "invoice.sent" => ($"Invoice {P("number")} from {P("business")}",
                $"Hi {P("client")},\n\n{(P("message").Length > 0 ? P("message") + "\n\n" : "")}" +
                $"{P("business")} has sent you invoice {P("number")} for {P("total")}, due {P("due")}.\n\n" +
                $"View, download or pay it here: {P("link")}"),
            "invoice.reminder" => (P("overdue") == "yes"
                    ? $"Overdue: invoice {P("number")} from {P("business")}"
                    : $"Reminder: invoice {P("number")} from {P("business")} is due {P("due")}",
                $"Hi {P("client")},\n\nThis is a friendly reminder that invoice {P("number")} from {P("business")} " +
                $"has {P("balance")} outstanding{(P("overdue") == "yes" ? $", which was due on {P("due")}" : $", due on {P("due")}")}.\n\n" +
                $"View or pay it here: {P("link")}\n\nIf you have already paid, thank you and please ignore this message."),
            "quote.sent" => ($"Quote {P("number")} from {P("business")}",
                $"Hi {P("client")},\n\n{(P("message").Length > 0 ? P("message") + "\n\n" : "")}" +
                $"{P("business")} has sent you quote {P("number")} for {P("total")}, valid until {P("expires")}.\n\n" +
                $"View, accept or decline it here: {P("link")}"),
            "quote.response" => ($"Quote {P("number")} was {P("response")} by {P("client")}",
                $"{P("client")} {P("response")} quote {P("number")} ({P("total")})."),
            "receipt" => ($"Receipt {P("receipt")} from {P("business")}",
                $"Hi {P("client")},\n\nThank you. {P("business")} received {P("amount")} for invoice {P("number")}.\n" +
                $"Receipt number: {P("receipt")}. Balance remaining: {P("balance")}.\n\nView the invoice and download your receipt: {P("link")}"),
            "team.invite" => ($"{P("inviter")} invited you to {P("business")} on {appName}",
                $"Hi,\n\n{P("inviter")} has invited you to join {P("business")} as {P("role")}.\n\n" +
                $"Accept the invitation here (it expires in 7 days): {P("link")}"),
            "school.guardian_invite" => ($"{P("school")} has set up SchoolLink for {P("child")}",
                $"Hello {P("guardian")},\n\n{P("school")} has set up SchoolLink for {P("child")}. Use this link to create your parent account, " +
                $"or sign in if you already have one:\n\n{P("link")}\n\n" +
                "The link works once and expires in 14 days. If you weren't expecting this email, you can ignore it."),
            "school.absent" => ($"{P("child")} was marked absent at {P("school")}",
                $"Hello {P("guardian")},\n\n{P("child")} ({P("class")}) was marked absent at {P("school")} on {P("date")}.\n\n" +
                "If you were not expecting this, please contact the school."),
            "school.results_published" => ($"{P("child")}'s {P("term")} results are ready",
                $"Hello {P("guardian")},\n\n{P("school")} has published {P("child")}'s {P("term")} results. " +
                $"Sign in to the parent portal to read them: {P("link")}"),
            "school.receipt" => ($"Receipt {P("receipt")} from {P("school")}",
                $"Hello,\n\n{P("school")} received {P("amount")} for {P("child")}'s fees (invoice {P("invoice")}).\n" +
                $"Receipt number: {P("receipt")}. Balance remaining: {P("balance")}.\n\n" +
                $"Sign in to the parent portal to view or download the receipt: {P("link")}"),
            "school.fee_reminder" => (P("overdue") == "true" ? $"Fees overdue for {P("child")} at {P("school")}" : $"Fees due soon for {P("child")} at {P("school")}",
                $"Hello {P("guardian")},\n\n" +
                // Same words as the bell. Reminders queued before dueAmount existed fall back to the balance.
                $"This is a reminder about {P("child")}'s fees at {P("school")}. " +
                Saas.Application.School.FinanceService.FeeReminderText(P("dueAmount").Length > 0 ? P("dueAmount") : P("balance"), P("dueDate"),
                    P("overdue") == "true", P("invoice"), P("balance")) + "\n\n" +
                $"Sign in to the parent portal to see the invoice and pay: {P("link")}\n\n" +
                "If you have already paid, please ignore this reminder or contact the school office."),
            "school.payment_unverified" => ($"Check an online fee payment at {P("school")}",
                $"Hello,\n\nA parent's online payment of {P("amount")} for {P("child")}'s invoice {P("invoice")} couldn't be checked with the school's " +
                $"current Paystack key, so it was not applied. This can happen after the Paystack key was changed or removed.\n\nPaystack reference: {P("reference")}\n\n" +
                "Look for this reference in your Paystack dashboard. If the parent was charged, record the amount as a manual payment."),
            "school.payment_mismatch" => ($"Check an online fee payment at {P("school")}",
                $"Hello,\n\nPaystack reported {P("received")} for {P("child")}'s invoice {P("invoice")}, but the parent started a payment of {P("expected")}. " +
                $"The payment was not applied to the invoice.\n\nPaystack reference: {P("reference")}\n\n" +
                "Check the transaction in your Paystack dashboard, then record the correct amount as a manual payment or refund the parent."),
            "school.announcement" => ($"{P("school")}: {P("title")}",
                $"Hello {P("guardian")},\n\n{P("school")} posted an announcement.\n\n{P("title")}\n\n{P("body")}\n\n" +
                $"Read it in the parent portal: {P("link")}"),
            "school.message" => ($"New message from {P("sender")} about {P("child")}",
                $"Hello {P("name")},\n\n{P("sender")} sent a message about {P("child")}:\n\n{P("message")}\n\n" +
                $"Read and reply here: {P("link")}"),
            "school.demo_request" => ($"SchoolLink demo request: {P("school")}",
                $"{P("contact")} asked for a SchoolLink demo.\n\nSchool: {P("school")}\nStudents: about {P("students")}\n" +
                $"Email: {P("email")}\nPhone: {P("phone")}\n\nReply to them within one working day to book a time."),
            "booking.confirmed" => ($"Booked: {P("service")} at {P("business")}, {P("when")}",
                $"Hi {P("customer")},\n\nYou're booked for {P("service")} with {P("staff")} at {P("business")} on {P("when")}.\n" +
                (P("address").Length > 0 ? $"Address: {P("address")}\n" : "") +
                $"\nReschedule, cancel or add it to your calendar: {P("link")}"),
            "booking.new" => ($"New booking: {P("customer")}, {P("service")}, {P("when")}",
                $"{P("customer")} ({P("phone")}) booked {P("service")} with {P("staff")} on {P("when")}."),
            "booking.reminder" => ($"Reminder: {P("service")} at {P("business")}, {P("when")}",
                $"Hi {P("customer")},\n\nThis is a reminder of your {P("service")} appointment with {P("staff")} at {P("business")} on {P("when")}.\n" +
                (P("address").Length > 0 ? $"Address: {P("address")}\n" : "") +
                $"\nNeed to change it? {P("link")}"),
            "course.certificate" => ($"Your certificate for {P("course")}",
                $"Congratulations {P("name")}!\n\nYou finished {P("course")}. Your certificate is ready to view, download and share: {P("link")}"),
            "course.approved" => ($"{P("course")} is now in the marketplace",
                $"Good news: {P("course")} was approved and now appears in the {appName} marketplace."),
            "course.rejected" => ($"Changes needed before {P("course")} can join the marketplace",
                $"{P("course")} wasn't approved for the marketplace yet.\n\nWhat to change: {P("note")}\n\nMake the changes in your studio and send it for review again."),
            "payout.paid" => ($"Payout sent: {P("amount")}",
                $"We've sent {P("amount")} to your bank account. Transfer reference: {P("reference")}."),
            "payout.rejected" => ($"Payout request of {P("amount")} wasn't sent",
                $"Your payout request of {P("amount")} wasn't sent. {P("note")}\n\nThe amount is back in your available balance."),
            "listing.approved" => ($"{P("listing")} is live",
                $"Good news: {P("listing")} was approved and now appears in search.\n\nSee it in your studio: {P("link")}"),
            "listing.rejected" => ($"Changes needed before {P("listing")} can go live",
                $"{P("listing")} wasn't approved yet.\n\nWhat to change: {P("note")}\n\nEdit it and submit it again: {P("link")}"),
            "listing.hidden" => ($"{P("listing")} is hidden while we check reports",
                $"Several people reported {P("listing")}, so it's hidden from search while a moderator checks it. " +
                $"If it's already rented or sold, mark it so in your studio.\n\n{P("link")}"),
            "enquiry.new" => ($"New enquiry about {P("listing")} from {P("name")}",
                $"{P("name")} ({P("phone")}{(P("email").Length > 0 ? ", " + P("email") : "")}) asked about {P("listing")}:\n\n{P("message")}\n\n" +
                $"Reply from your inbox: {P("link")}"),
            "inspection.requested" => ($"Viewing request: {P("listing")}, {P("when")}",
                $"{P("name")}{(P("phone").Length > 0 ? $" ({P("phone")})" : "")} wants to view {P("listing")} on {P("when")}.\n\n" +
                $"Confirm or suggest another time: {P("link")}"),
            "inspection.confirmed" => ($"Viewing confirmed: {P("listing")}, {P("when")}",
                $"Your viewing of {P("listing")} is confirmed for {P("when")}.\n\nNever pay any money before you have inspected the property and met the agent.\n\n{P("link")}"),
            "inspection.cancelled" => ($"Viewing cancelled: {P("listing")}, {P("when")}",
                $"The viewing of {P("listing")} on {P("when")} was cancelled.\n\n{P("link")}"),
            "listing.expiring" => ($"Is {P("listing")} still available?",
                $"{P("listing")} comes down from search on {P("expires")}.\n\nStill available? Renew it with one click: {P("renewLink")}\n\n" +
                $"Rented or sold? Mark it in your studio so seekers stop calling: {P("link")}"),
            "listing.expired" => ($"{P("listing")} has come down from search",
                $"{P("listing")} expired and no longer appears in search.\n\nStill available? Put it back up: {P("renewLink")}\n\n{P("link")}"),
            "inspection.reminder" => ($"Reminder: viewing {P("listing")}, {P("when")}",
                $"Your viewing of {P("listing")} is on {P("when")} with {P("agent")}{(P("phone").Length > 0 ? $" ({P("phone")})" : "")}.\n\n" +
                $"Never pay any money before you have inspected the property and met the agent.\n\n{P("link")}"),
            "search.digest" => ($"{P("count")} new for \"{P("search")}\"",
                $"New listings matching your saved search \"{P("search")}\":\n\n{P("listings")}\n\nChange or stop these emails: {P("link")}"),
            "stay.confirmed" => ($"You're booked: {P("listing")}, {P("dates")}",
                $"Your stay at {P("listing")} is confirmed for {P("dates")}. You paid {P("total")}.\n\n" +
                $"Address: {P("address")}{(P("checkIn").Length > 0 ? $"\nCheck-in from {P("checkIn")}" : "")}\n\n" +
                $"Free cancellation until 48 hours before check-in. Your trip: {P("link")}"),
            "stay.new" => ($"New booking: {P("listing")}, {P("dates")}",
                $"{P("guest")}{(P("phone").Length > 0 ? $" ({P("phone")})" : "")} booked {P("listing")} for {P("dates")}, {P("guests")} guests.\n\n" +
                $"You'll receive {P("net")} after check-in. {P("link")}"),
            "stay.refunded" => ($"Refund on its way: {P("listing")}",
                $"{P("reason")}, so we've refunded {P("amount")} for {P("listing")}, {P("dates")}. Refunds usually reach your account within 5 to 10 working days.\n\n{P("link")}"),
            "stay.payment_mismatch" => ($"Your payment for {P("listing")} didn't match",
                $"We received {P("paid")} for {P("listing")}, but the stay costs {P("expected")}, so it isn't booked yet. Our team will contact you to refund or settle the difference. Reference: {P("reference")}.\n\n{P("link")}"),
            "payments.mismatch" => ($"Payment mismatch: {P("kind")} {P("reference")}",
                $"A {P("kind")} payment didn't match. Paid {P("paid")}, expected {P("expected")}, for {P("listing")}. Reference {P("reference")}. Check it in Paystack and refund or settle it."),
            "stay.cancelled" => ($"Stay cancelled: {P("listing")}, {P("dates")}",
                $"The stay at {P("listing")} for {P("dates")} was cancelled by {P("by")}. Refund to the guest: {P("refund")}.\n\n{P("link")}"),
            "search.alert" => ($"New match for \"{P("search")}\": {P("listing")}",
                $"A new listing matches your saved search \"{P("search")}\":\n\n{P("listing")}, {P("price")}\n\nSee it before it's gone: {P("link")}"),
            "verification.approved" => ($"You're verified on {appName}",
                $"Your documents were approved. Your listings now show the verified badge and go live without waiting for review.\n\n{P("link")}"),
            "verification.rejected" => ("We couldn't verify your agency yet",
                $"We couldn't approve your documents.\n\nWhat's missing: {P("note")}\n\nSend them again here: {P("link")}"),
            "invoice.paid" => ($"Payment received: {P("amount")} for invoice {P("number")}",
                $"{P("client")} paid {P("amount")} online for invoice {P("number")}. Balance remaining: {P("balance")}." +
                (P("overpaid").Length > 0
                    ? $"\n\nThis invoice is now overpaid by {P("overpaid")}: it was paid another way before this payment arrived. Refund the difference in Paystack or keep it as credit."
                    : "")),
            // CareLink (Task 14): the text is CareNotices' own sentence ("message"), which never carries clinical detail; patient emails
            // say only what is booked, ready or due and send the reader to the portal.
            "care.appointment_booked" or "care.appointment_changed" or "care.appointment_reminder" or "care.result_ready"
                or "care.summary_ready" or "care.bill_due" or "care.payment_received" => ($"{P("title")}: {P("clinic")}",
                $"Hello,\n\n{P("message")}\n\nSign in to your CareLink portal to see the details: {P("link")}\n\n" +
                $"This message comes from {P("clinic")} through CareLink."),
            // Task 15: the portal invite (the clinic, the patient's first name and the single-use link only).
            "care.patient_invite" => ($"{P("title")}: {P("clinic")}",
                $"Hello,\n\n{P("message")}\n\nAccept the invitation here. The link works once and lasts 14 days: {P("link")}\n\n" +
                $"This message comes from {P("clinic")} through CareLink."),
            "care.emergency_access" => ($"Emergency access at {P("clinic")}",
                $"Hello,\n\n{P("message")}\n\nSee who opened the record and why in the patient's access log: {P("link")}"),
            "care.payment_mismatch" or "care.payment_unverified" => ($"Check an online payment at {P("clinic")}",
                $"Hello,\n\n{P("message")}\n\nPaystack reference: {P("reference")}\n\nOpen the bill: {P("link")}"),
            _ => (appName, string.Join("\n", n.Payload.Select(kv => $"{kv.Key}: {kv.Value}"))),
        };
        var html = $"<div style=\"font-family:system-ui,sans-serif;font-size:15px;line-height:1.5\">{WebUtility.HtmlEncode(text).Replace("\n", "<br>")}</div>";
        return new EmailMessage(n.Recipient, subject, html, text);
    }
}

/// <summary>
/// Sends pending outbox notifications. Rows are claimed with FOR UPDATE SKIP LOCKED so several API
/// instances can run this safely. Failed sends are retried up to 5 times.
/// </summary>
public sealed class NotificationDispatcher(IServiceScopeFactory scopes, IOptions<EmailOptions> email, ILogger<NotificationDispatcher> logger)
    : BackgroundService
{
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            try
            {
                await Saas.Infrastructure.Jobs.JobRuns.TrackAsync(scopes, "email", async () => { while (await SendBatchAsync(stoppingToken) > 0) { } }, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Notification dispatch failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> SendBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.Notifications
            .FromSql($"SELECT * FROM notifications WHERE status = 'Pending' ORDER BY created_at LIMIT 20 FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        foreach (var n in batch)
        {
            try
            {
                if (n.Channel != NotificationChannel.Email) throw new NotSupportedException($"{n.Channel} is not set up yet.");
                await sender.SendAsync(EmailTemplates.Render(n, email.Value.FromName), ct);
                n.Status = NotificationStatus.Sent;
                n.SentAt = DateTimeOffset.UtcNow;
                n.Error = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                n.Attempts++;
                n.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                if (n.Attempts >= MaxAttempts || ex is NotSupportedException) n.Status = NotificationStatus.Failed;
                logger.LogWarning(ex, "Notification {Id} failed (attempt {Attempts})", n.Id, n.Attempts);
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // Only report work done when something was sent, so a run of failures does not spin.
        return batch.Count(n => n.Status == NotificationStatus.Sent);
    }
}
