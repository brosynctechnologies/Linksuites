using Saas.Domain.Core;
using Saas.Infrastructure.Notifications;

namespace Saas.IntegrationTests;

public class EmailSenderNameTests
{
    private static Notification N(string template, Dictionary<string, string> payload) =>
        new() { Recipient = "someone@example.com", TemplateKey = template, Payload = payload };

    [Theory]
    [InlineData("invoice.sent", "business", "Ada's Bakery")]
    [InlineData("booking.reminder", "business", "Glow Salon")]
    [InlineData("school.fee_reminder", "school", "Green Valley School")]
    [InlineData("care.appointment_booked", "clinic", "St. Mary Clinic")]
    public void Customer_facing_emails_go_out_under_the_business_name(string template, string key, string name)
    {
        var email = EmailTemplates.Render(N(template, new() { [key] = name }), "LinkSuite");

        Assert.Equal(name, email.FromName);
    }

    [Theory]
    [InlineData("otp")]
    [InlineData("password.reset")]
    [InlineData("admin.code")]
    [InlineData("order.new")]
    [InlineData("invoice.paid")]
    [InlineData("business.suspended")]
    public void Platform_emails_use_the_default_sender(string template)
    {
        var email = EmailTemplates.Render(N(template, new() { ["business"] = "Ada's Bakery", ["name"] = "Ada" }), "LinkSuite");

        Assert.Null(email.FromName);
    }

    [Fact]
    public void Missing_or_blank_business_name_falls_back_to_the_default_sender()
    {
        Assert.Null(EmailTemplates.Render(N("invoice.sent", new()), "LinkSuite").FromName);
        Assert.Null(EmailTemplates.Render(N("invoice.sent", new() { ["business"] = "  " }), "LinkSuite").FromName);
    }

    [Fact]
    public void Business_name_is_made_safe_for_the_from_header()
    {
        var email = EmailTemplates.Render(N("receipt", new() { ["business"] = "Ada's\r\nBakery\tLtd " + new string('x', 100) }), "LinkSuite");

        Assert.DoesNotContain('\n', email.FromName);
        Assert.StartsWith("Ada's Bakery Ltd x", email.FromName);
        Assert.True(email.FromName!.Length <= 70);
    }
}
