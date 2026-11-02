using Microsoft.EntityFrameworkCore;
using Saas.Infrastructure.Notifications;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class EmailReplyToTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Replies_go_to_the_business_contact_email_then_the_owner()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@example.com";
        await api.SignupInvoicesAsync("Ada", ownerEmail);
        var tenantId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.User!.Email == ownerEmail).Select(m => m.TenantId).SingleAsync(Ct));

        await SetContactAsync(tenantId, " billing@ada.test ");
        Assert.Equal("billing@ada.test", await ReplyToAsync(tenantId));

        await SetContactAsync(tenantId, null);
        Assert.Equal(ownerEmail, await ReplyToAsync(tenantId));

        await SetContactAsync(tenantId, "not an email");
        Assert.Equal(ownerEmail, await ReplyToAsync(tenantId));
    }

    [Fact]
    public async Task Unknown_business_has_no_reply_to()
    {
        Assert.Null(await ReplyToAsync(Guid.NewGuid()));
    }

    private Task<string?> ReplyToAsync(Guid tenantId) =>
        api.WithDbAsync(db => NotificationDispatcher.BusinessReplyToAsync(db, tenantId, Ct));

    private Task<int> SetContactAsync(Guid tenantId, string? email) => api.WithDbAsync(async db =>
    {
        var settings = await db.BusinessSettings.IgnoreQueryFilters().SingleAsync(b => b.TenantId == tenantId, Ct);
        settings.Email = email;
        return await db.SaveChangesAsync(Ct);
    });
}
