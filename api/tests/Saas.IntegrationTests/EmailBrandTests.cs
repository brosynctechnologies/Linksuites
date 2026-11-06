using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Infrastructure.Notifications;

namespace Saas.IntegrationTests;

/// <summary>
/// Platform emails (sign-in codes, welcome, password, team and business notices) speak the product's own name, CareLink, InvoiceLink and so
/// on, not one fixed name. An email that belongs to no single product (the admin console, an account on several products) says LinkSuite.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class EmailBrandTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail(string what) => $"{what}-{Guid.NewGuid():N}@example.com";

    private Task<Notification> LatestAsync(string email, string template) => api.WithDbAsync(db => db.Notifications
        .Where(n => n.TemplateKey == template && n.Recipient == email).OrderByDescending(n => n.CreatedAt).FirstAsync(Ct));

    /// <summary>The email as the dispatcher sends it: the brand it picks, rendered.</summary>
    private Task<EmailMessage> RenderAsync(Notification n) => api.WithDbAsync(async db =>
    {
        var brand = await NotificationDispatcher.BrandAsync(db, n, [], Ct);
        var message = EmailTemplates.Render(n, brand);
        return message with { FromName = message.FromName ?? brand };
    });

    private async Task SignUpBusinessAsync(string email, string product)
    {
        var anon = api.CreateClient();
        (await anon.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Brand Owner", email, password = "password123", country = "NG", product, businessName = $"Brand Biz {Guid.NewGuid():N}"[..20],
        }, Ct)).EnsureSuccessStatusCode();
        (await anon.PostAsJsonAsync("/api/auth/signup/confirm",
            new { email, code = await api.LatestCodeAsync(email), password = "password123", product }, Ct)).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("care", "CareLink")]
    [InlineData("invoices", "InvoiceLink")]
    [InlineData("school", "SchoolLink")]
    public async Task Business_signup_code_and_welcome_say_the_product(string product, string brand)
    {
        var email = NewEmail($"brand-{product}");
        await SignUpBusinessAsync(email, product);

        var code = await RenderAsync(await LatestAsync(email, "otp"));
        Assert.StartsWith($"Your {brand} login code: ", code.Subject);
        Assert.Equal(brand, code.FromName);
        Assert.DoesNotContain("OrderLink", code.Subject + code.Text);

        var welcome = await RenderAsync(await LatestAsync(email, "welcome"));
        Assert.StartsWith($"Welcome to {brand}, ", welcome.Subject);
        Assert.Equal(brand, welcome.FromName);
        Assert.DoesNotContain("OrderLink", welcome.Subject + welcome.Text);
    }

    /// <summary>A later code requested from the shared route (no product named) speaks the account's only product.</summary>
    [Fact]
    public async Task Code_request_without_a_product_uses_the_accounts_product()
    {
        var email = NewEmail("brand-later");
        await SignUpBusinessAsync(email, "bookings");
        (await api.CreateClient().PostAsJsonAsync("/api/auth/otp/request", new { email }, Ct)).EnsureSuccessStatusCode();
        Assert.StartsWith("Your BookLink login code: ", (await RenderAsync(await LatestAsync(email, "otp"))).Subject);

        // The page may name its product.
        (await api.CreateClient().PostAsJsonAsync("/api/auth/otp/request", new { email, product = "care" }, Ct)).EnsureSuccessStatusCode();
        Assert.StartsWith("Your CareLink login code: ", (await RenderAsync(await LatestAsync(email, "otp"))).Subject);
    }

    [Theory]
    [InlineData("patient", "CareLink")]
    [InlineData("parent", "SchoolLink")]
    [InlineData("learner", "LearnLink")]
    [InlineData("seeker", "PropertyLink")]
    public async Task Customer_signup_code_says_its_portal(string kind, string brand)
    {
        var email = NewEmail($"brand-{kind}");
        (await api.CreateClient().PostAsJsonAsync($"/api/auth/{kind}/signup", new { fullName = "Brand Customer", email, password = "password123" }, Ct))
            .EnsureSuccessStatusCode();
        var code = await RenderAsync(await LatestAsync(email, "otp"));
        Assert.StartsWith($"Your {brand} login code: ", code.Subject);
        Assert.Equal(brand, code.FromName);
    }

    [Fact]
    public async Task Password_reset_says_the_product()
    {
        var email = NewEmail("brand-reset");
        await SignUpBusinessAsync(email, "care");
        (await api.CreateClient().PostAsJsonAsync("/api/auth/password/forgot", new { email, product = "care" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal("Reset your CareLink password", (await RenderAsync(await LatestAsync(email, "password.reset"))).Subject);
    }

    /// <summary>No product (the admin console's code, or nothing to go on) reads LinkSuite, never another product's name.</summary>
    [Fact]
    public async Task No_product_reads_LinkSuite()
    {
        var n = new Notification { Recipient = "x@example.com", Channel = NotificationChannel.Email, TemplateKey = "otp",
            Payload = new() { ["name"] = "X", ["code"] = "123456" } };
        var email = await RenderAsync(n);
        Assert.Equal("Your LinkSuite login code: 123456", email.Subject);
        Assert.Equal("LinkSuite", email.FromName);
    }
}
