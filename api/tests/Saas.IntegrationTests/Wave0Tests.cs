using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Infrastructure.Persistence.Migrations;
using Saas.Infrastructure.Storage;

namespace Saas.IntegrationTests;

/// <summary>Wave 0 (roadmap 2026-11-17): nothing is sold that isn't built, and launch safety.</summary>
[Collection(nameof(ApiCollection))]
public sealed class Wave0Tests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string CustomDomain = "Custom domain";

    [Fact]
    public async Task OrderLink_pro_lists_custom_domain_again_now_it_works()
    {
        // Wave 3 built custom domains, so the Wave3CustomDomains migration put the feature back (see CustomDomainTests).
        var pro = await api.WithDbAsync(db =>
            db.Plans.AsNoTracking().SingleAsync(p => p.Product == Products.WhatsAppOrders && p.Code == "pro", Ct));
        Assert.Single(pro.Features, f => f == CustomDomain);
    }

    private static StorageOptions R2(string? privateBucket = "linksuite-private") => new()
    {
        Provider = "R2", R2AccountId = "acc", R2AccessKeyId = "id", R2SecretAccessKey = "secret",
        R2Bucket = "linksuite-public", R2PrivateBucket = privateBucket, R2PublicBaseUrl = "https://cdn.example.com/",
    };

    [Fact]
    public void R2_private_keys_go_to_the_private_bucket_with_no_public_url()
    {
        var o = R2();
        var priv = R2FileStorage.Target(o, "private/verification/t/r/0.jpg");
        Assert.Equal("linksuite-private", priv.Bucket);
        Assert.Null(priv.PublicUrl);
        Assert.Equal("private, no-store", priv.CacheControl);

        var pub = R2FileStorage.Target(o, "products/t/a.webp");
        Assert.Equal("linksuite-public", pub.Bucket);
        Assert.Equal("https://cdn.example.com/products/t/a.webp", pub.PublicUrl);
        Assert.Equal("public, max-age=31536000, immutable", pub.CacheControl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("linksuite-public")]
    public void R2_without_a_separate_private_bucket_is_refused(string? privateBucket)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => R2(privateBucket).ValidateR2());
        Assert.Contains("R2PrivateBucket", ex.Message);
    }

    [Fact]
    public void R2_with_a_separate_private_bucket_is_accepted() => R2().ValidateR2();

    [Fact]
    public async Task Migration_removes_custom_domain_from_plans_and_subscription_snapshots()
    {
        var (_, auth) = await api.SignupAsync("DomainSnapshot");
        var tenantId = Guid.Parse(auth.GetProperty("tenant").GetProperty("id").GetString()!);
        await api.MakeProAsync(tenantId, Products.WhatsAppOrders);

        await api.WithDbAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE plans SET features = array_append(features, 'Custom domain')
                WHERE product = 'whatsapp-orders' AND code = 'pro' AND NOT ('Custom domain' = ANY (features));
                """, Ct);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE subscriptions SET features = array_append(COALESCE(features, '{{}}'), 'Custom domain') WHERE tenant_id = {tenantId}", Ct);

            await db.Database.ExecuteSqlRawAsync(HideCustomDomain.Sql, Ct);
            await db.Database.ExecuteSqlRawAsync(HideCustomDomain.Sql, Ct); // safe to run twice

            var pro = await db.Plans.AsNoTracking().SingleAsync(p => p.Product == Products.WhatsAppOrders && p.Code == "pro", Ct);
            Assert.DoesNotContain(CustomDomain, pro.Features);
            Assert.Contains("Remove branding", pro.Features);
            var sub = await db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenantId).ToListAsync(Ct);
            Assert.All(sub, s => Assert.DoesNotContain(CustomDomain, s.Features ?? []));
            await tx.RollbackAsync(Ct);
            return true;
        });
    }
}
