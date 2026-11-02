using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>The shared in-app notification feed: scoping to the signed-in user, unread counts, mark read, cleanup.</summary>
[Collection(nameof(ApiCollection))]
public sealed class InAppNotificationTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task AddAsync(Guid userId, string title, Guid? tenantId = null, string kind = "test.kind")
    {
        using var scope = api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<InAppNotifications>().Add(userId, "school", kind, title, "Body text", "/app/students", tenantId);
        await scope.ServiceProvider.GetRequiredService<Saas.Infrastructure.Persistence.AppDbContext>().SaveChangesAsync(Ct);
    }

    private static async Task<List<string>> TitlesAsync(HttpClient client, string query = "")
    {
        var page = await Json(await client.GetAsync($"/api/me/notifications{query}", Ct));
        return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToList();
    }

    private static async Task<int> CountAsync(HttpClient client) =>
        (await Json(await client.GetAsync("/api/me/notifications/unread-count", Ct))).GetProperty("count").GetInt32();

    [Fact]
    public async Task A_user_sees_only_their_own_notifications()
    {
        var school = await kit.SchoolAsync("Own Notes School");
        var ownerId = await kit.OwnerIdAsync(school);
        var (other, otherId) = await kit.StaffAsync(school, MemberRole.Admin, "Other Admin");
        await AddAsync(ownerId, "For the owner", school.TenantId);
        await AddAsync(otherId, "For the admin", school.TenantId);

        Assert.Equal(["For the owner"], await TitlesAsync(school.Owner));
        Assert.Equal(["For the admin"], await TitlesAsync(other));
        Assert.Equal(1, await CountAsync(school.Owner));

        // Nobody can mark another user's notification read: it looks like it does not exist.
        var adminNoteId = await api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == otherId).Select(n => n.Id).SingleAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await school.Owner.PostAsync($"/api/me/notifications/{adminNoteId}/read", null, Ct)).StatusCode);
        Assert.Equal(1, await CountAsync(other));
    }

    [Fact]
    public async Task Business_users_see_only_this_school_while_customers_see_everything_of_theirs()
    {
        var school = await kit.SchoolAsync("Scope School");
        var elsewhere = await kit.SchoolAsync("Elsewhere School");
        var ownerId = await kit.OwnerIdAsync(school);
        await AddAsync(ownerId, "Here", school.TenantId);
        await AddAsync(ownerId, "There", elsewhere.TenantId);
        await AddAsync(ownerId, "Everywhere");
        Assert.Equal(["Here"], await TitlesAsync(school.Owner));
        Assert.Equal(1, await CountAsync(school.Owner));

        var (parent, parentId, _) = await kit.ParentAccountAsync();
        await AddAsync(parentId, "School A", school.TenantId);
        await AddAsync(parentId, "School B", elsewhere.TenantId);
        await AddAsync(parentId, "No school");
        Assert.Equal(3, (await TitlesAsync(parent)).Count);
        Assert.Equal(3, await CountAsync(parent));
    }

    [Fact]
    public async Task Unread_count_and_mark_read()
    {
        var (parent, parentId, _) = await kit.ParentAccountAsync();
        await AddAsync(parentId, "First");
        await AddAsync(parentId, "Second");
        await AddAsync(parentId, "Third");
        Assert.Equal(3, await CountAsync(parent));
        Assert.Equal("Third", (await TitlesAsync(parent))[0]); // newest first

        var first = await api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == parentId && n.Title == "First").Select(n => n.Id).SingleAsync(Ct));
        Assert.Equal(HttpStatusCode.NoContent, (await parent.PostAsync($"/api/me/notifications/{first}/read", null, Ct)).StatusCode);
        // Marking twice is harmless.
        Assert.Equal(HttpStatusCode.NoContent, (await parent.PostAsync($"/api/me/notifications/{first}/read", null, Ct)).StatusCode);
        Assert.Equal(2, await CountAsync(parent));
        Assert.Equal(["Third", "Second"], await TitlesAsync(parent, "?unread=true"));
        Assert.Equal(3, (await TitlesAsync(parent)).Count);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.PostAsync($"/api/me/notifications/{Guid.NewGuid()}/read", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_list_pages_by_twenty_and_the_count_stops_at_99()
    {
        var (parent, parentId, _) = await kit.ParentAccountAsync();
        using (var scope = api.Services.CreateScope())
        {
            var feed = scope.ServiceProvider.GetRequiredService<InAppNotifications>();
            for (var i = 1; i <= 105; i++) feed.Add(parentId, "school", "test.kind", $"Note {i}", "Body", "/parent");
            await scope.ServiceProvider.GetRequiredService<Saas.Infrastructure.Persistence.AppDbContext>().SaveChangesAsync(Ct);
        }

        Assert.Equal(99, await CountAsync(parent));
        var page1 = await Json(await parent.GetAsync("/api/me/notifications", Ct));
        Assert.Equal(20, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(105, page1.GetProperty("total").GetInt32());
        var page6 = await Json(await parent.GetAsync("/api/me/notifications?page=6", Ct));
        Assert.Equal(5, page6.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Read_all_marks_everything()
    {
        var school = await kit.SchoolAsync("Read All School");
        var ownerId = await kit.OwnerIdAsync(school);
        var (other, otherId) = await kit.StaffAsync(school, MemberRole.Admin, "Read All Admin");
        await AddAsync(ownerId, "One", school.TenantId);
        await AddAsync(ownerId, "Two", school.TenantId);
        await AddAsync(otherId, "Not yours", school.TenantId);

        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.PostAsync("/api/me/notifications/read-all", null, Ct)).StatusCode);

        Assert.Equal(0, await CountAsync(school.Owner));
        Assert.Empty(await TitlesAsync(school.Owner, "?unread=true"));
        Assert.Equal(2, (await TitlesAsync(school.Owner)).Count);
        Assert.Equal(1, await CountAsync(other)); // someone else's feed is untouched
    }

    [Fact]
    public async Task Support_cannot_mark_read_but_can_read()
    {
        var school = await kit.SchoolAsync("Support Notes School");
        var ownerId = await kit.OwnerIdAsync(school);
        await AddAsync(ownerId, "Seen by support", school.TenantId);
        var noteId = await api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == ownerId).Select(n => n.Id).SingleAsync(Ct));
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{school.TenantId}/view-as",
            new { reason = "Parent says the bell is empty." }, Ct));
        var viewer = api.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewAs.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/me/notifications", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/me/notifications/unread-count", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/me/notifications/{noteId}/read", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/me/notifications/read-all", null, Ct)).StatusCode);
        Assert.Equal(1, await CountAsync(school.Owner));
    }

    [Fact]
    public async Task Business_and_support_view_tokens_never_see_tenantless_notifications()
    {
        var school = await kit.SchoolAsync("Tenantless Notes School");
        var ownerId = await kit.OwnerIdAsync(school);
        await AddAsync(ownerId, "For this school", school.TenantId);
        await AddAsync(ownerId, "Private to the owner");
        var privateId = await api.WithDbAsync(db => db.UserNotifications
            .Where(n => n.UserId == ownerId && n.TenantId == null).Select(n => n.Id).SingleAsync(Ct));

        Assert.Equal(["For this school"], await TitlesAsync(school.Owner));
        Assert.Equal(1, await CountAsync(school.Owner));
        Assert.Equal(HttpStatusCode.NotFound, (await school.Owner.PostAsync($"/api/me/notifications/{privateId}/read", null, Ct)).StatusCode);

        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{school.TenantId}/view-as",
            new { reason = "Owner says the bell shows the wrong things." }, Ct));
        var viewer = api.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewAs.GetProperty("token").GetString());
        Assert.Equal(["For this school"], await TitlesAsync(viewer));
        Assert.Equal(1, await CountAsync(viewer));

        Assert.Null(await api.WithDbAsync(db => db.UserNotifications.Where(n => n.Id == privateId).Select(n => n.ReadAt).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Old_notifications_are_cleaned_up()
    {
        var (_, parentId, _) = await kit.ParentAccountAsync();
        await AddAsync(parentId, "Read and old");
        await AddAsync(parentId, "Read and recent");
        await AddAsync(parentId, "Unread and old");
        await AddAsync(parentId, "Unread and recent");
        var now = DateTimeOffset.UtcNow;
        await api.WithDbAsync(async db =>
        {
            var rows = await db.UserNotifications.Where(n => n.UserId == parentId).ToListAsync(Ct);
            foreach (var row in rows)
            {
                row.CreatedAt = row.Title.EndsWith("old") ? now.AddDays(-200) : now.AddDays(-10);
                if (row.Title.StartsWith("Read")) row.ReadAt = row.Title.EndsWith("old") ? now.AddDays(-100) : now.AddDays(-5);
            }
            return await db.SaveChangesAsync(Ct);
        });

        int deleted;
        using (var scope = api.Services.CreateScope())
            deleted = await scope.ServiceProvider.GetRequiredService<InAppNotifications>().CleanupAsync(now, Ct);

        var left = await api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == parentId).Select(n => n.Title).ToListAsync(Ct));
        Assert.True(deleted >= 2); // the two old rows of this user went (other users' old rows may also be cleaned)
        Assert.Equal(["Read and recent", "Unread and recent"], left.Order().ToList());

        // A clock 181 days on removes the rest: everything is then older than its limit.
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<InAppNotifications>().CleanupAsync(now.AddDays(181), Ct);
        Assert.Empty(await api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == parentId).ToListAsync(Ct)));
    }
}
