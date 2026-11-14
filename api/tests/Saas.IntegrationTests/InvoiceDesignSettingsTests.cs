using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;
using Saas.Infrastructure.Persistence.Migrations;

namespace Saas.IntegrationTests;

/// <summary>
/// InvoiceLink designs, Task 1: the letterhead settings, the ten-design catalogue, and logo, signature and stamp images, which
/// are a Pro feature (<c>brand_images</c>) both to upload and to appear on documents.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class InvoiceDesignSettingsTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)); // Lagos

    private const string ImagesMessage = "Your logo, signature and stamp are part of the Pro plan. Upgrade to add them.";
    private const string ImagesFeature = "Your logo, signature and stamp on documents";

    /// <summary>Upload path and the business field that holds its URL.</summary>
    private static readonly (string Kind, string Field)[] Images =
        [("logo", "logoUrl"), ("signature", "signatureImageUrl"), ("stamp", "stampImageUrl")];

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task New_business_has_the_default_letterhead()
    {
        var (owner, _) = await api.SignupInvoicesAsync("Defaults");
        var b = await Json(await owner.GetAsync("/api/business", Ct));

        Assert.Equal("Invoice", b.GetProperty("documentTitle").GetString());
        Assert.Equal("modern", b.GetProperty("fontStyle").GetString());
        Assert.Equal("medium", b.GetProperty("logoSize").GetString());
        Assert.Equal("address", b.GetProperty("headerLine").GetString());
        Assert.True(b.GetProperty("showTaxId").GetBoolean());
        Assert.True(b.GetProperty("showRegistrationNumber").GetBoolean());
        Assert.True(b.GetProperty("showBankDetails").GetBoolean());
        Assert.False(b.GetProperty("showSignature").GetBoolean());
        foreach (var field in new[] { "tagline", "registrationNumber", "secondaryColor", "signatureName", "signatureRole", "footerText",
                     "logoUrl", "signatureImageUrl", "stampImageUrl" })
            Assert.Equal(JsonValueKind.Null, b.GetProperty(field).ValueKind);
        Assert.False(b.GetProperty("brandImagesAvailable").GetBoolean());
    }

    [Fact]
    public async Task Letterhead_settings_round_trip()
    {
        var (owner, _) = await api.SignupInvoicesAsync("Roundtrip");
        var body = await UpdateBodyAsync(owner);
        body["tagline"] = "Bespoke tailoring for men and women";
        body["registrationNumber"] = "RC 1234567";
        body["headerLine"] = "registration";
        body["secondaryColor"] = "#0f766e";
        body["logoSize"] = "large";
        body["fontStyle"] = "serif";
        body["documentTitle"] = "Tax invoice";
        body["showTaxId"] = false;
        body["showRegistrationNumber"] = false;
        body["showBankDetails"] = false;
        body["showSignature"] = true;
        body["signatureName"] = "Ada Okafor";
        body["signatureRole"] = "Director";
        body["footerText"] = "Thank you for your business.";

        var saved = await Json(await owner.PutAsJsonAsync("/api/business", body, Ct));
        var reloaded = await Json(await owner.GetAsync("/api/business", Ct));
        foreach (var b in new[] { saved, reloaded })
        {
            Assert.Equal("Bespoke tailoring for men and women", b.GetProperty("tagline").GetString());
            Assert.Equal("RC 1234567", b.GetProperty("registrationNumber").GetString());
            Assert.Equal("registration", b.GetProperty("headerLine").GetString());
            Assert.Equal("#0f766e", b.GetProperty("secondaryColor").GetString());
            Assert.Equal("large", b.GetProperty("logoSize").GetString());
            Assert.Equal("serif", b.GetProperty("fontStyle").GetString());
            Assert.Equal("Tax invoice", b.GetProperty("documentTitle").GetString());
            Assert.False(b.GetProperty("showTaxId").GetBoolean());
            Assert.False(b.GetProperty("showRegistrationNumber").GetBoolean());
            Assert.False(b.GetProperty("showBankDetails").GetBoolean());
            Assert.True(b.GetProperty("showSignature").GetBoolean());
            Assert.Equal("Ada Okafor", b.GetProperty("signatureName").GetString());
            Assert.Equal("Director", b.GetProperty("signatureRole").GetString());
            Assert.Equal("Thank you for your business.", b.GetProperty("footerText").GetString());
        }

        // Every value of each choice is accepted; blank text clears.
        foreach (var (field, values) in new (string, string[])[]
                 {
                     ("headerLine", ["tagline", "address", "registration", "none"]),
                     ("logoSize", ["small", "medium", "large"]),
                     ("fontStyle", ["modern", "serif", "sans"]),
                     ("documentTitle", ["Invoice", "Tax invoice", "Bill", "Pro forma invoice", "Statement of account"]),
                 })
        foreach (var value in values)
        {
            var changed = await UpdateBodyAsync(owner);
            changed[field] = value;
            Assert.Equal(value, (await Json(await owner.PutAsJsonAsync("/api/business", changed, Ct))).GetProperty(field).GetString());
        }

        var cleared = await UpdateBodyAsync(owner);
        cleared["tagline"] = " ";
        cleared["secondaryColor"] = "";
        cleared["footerText"] = "";
        var after = await Json(await owner.PutAsJsonAsync("/api/business", cleared, Ct));
        Assert.Equal(JsonValueKind.Null, after.GetProperty("tagline").ValueKind);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("secondaryColor").ValueKind);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("footerText").ValueKind);
    }

    [Fact]
    public async Task Letterhead_settings_are_validated()
    {
        var (owner, _) = await api.SignupInvoicesAsync("Validated");
        var cases = new (string Field, object Value)[]
        {
            ("headerLine", "slogan"),
            ("headerLine", "Address"),
            ("logoSize", "huge"),
            ("fontStyle", "comic"),
            ("documentTitle", "Receipt"),
            ("documentTitle", "invoice"),
            ("secondaryColor", "blue"),
            ("secondaryColor", "#12345"),
            ("secondaryColor", "#1234567"),
            ("tagline", new string('t', 101)),
            ("registrationNumber", new string('r', 41)),
            ("signatureName", new string('n', 81)),
            ("signatureRole", new string('o', 81)),
            ("footerText", new string('f', 161)),
        };
        foreach (var (field, value) in cases)
        {
            var body = await UpdateBodyAsync(owner);
            body[field] = value;
            await AssertProblemAsync(await owner.PutAsJsonAsync("/api/business", body, Ct), HttpStatusCode.BadRequest, field: field);
        }

        // The longest allowed values are fine.
        var longest = await UpdateBodyAsync(owner);
        longest["tagline"] = new string('t', 100);
        longest["registrationNumber"] = new string('r', 40);
        longest["signatureName"] = new string('n', 80);
        longest["signatureRole"] = new string('o', 80);
        longest["footerText"] = new string('f', 160);
        (await owner.PutAsJsonAsync("/api/business", longest, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Template_catalogue_has_ten_designs_and_premium_needs_pro()
    {
        string[] keys = ["classic", "modern", "minimal", "bold", "elegant", "executive", "corporate", "ledger", "studio", "prestige"];
        string[] names = ["Classic", "Modern", "Minimal", "Bold", "Elegant", "Executive", "Corporate", "Ledger", "Studio", "Prestige"];
        var (owner, auth) = await api.SignupInvoicesAsync("Catalogue");

        var free = (await Json(await owner.GetAsync("/api/invoice-templates", Ct))).EnumerateArray().ToList();
        Assert.Equal(keys, free.Select(t => t.GetProperty("key").GetString()));
        Assert.Equal(names, free.Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(keys.Select((_, i) => i >= 5), free.Select(t => t.GetProperty("isPremium").GetBoolean()));
        Assert.Equal(keys.Select((_, i) => i < 5), free.Select(t => t.GetProperty("available").GetBoolean()));

        var publicList = (await Json(await api.CreateClient().GetAsync("/api/public/invoice-templates", Ct))).EnumerateArray().ToList();
        Assert.Equal(keys, publicList.Select(t => t.GetProperty("key").GetString()));

        // Bold and Elegant are free now; the new five are premium.
        var body = await UpdateBodyAsync(owner);
        body["defaultTemplate"] = "elegant";
        (await owner.PutAsJsonAsync("/api/business", body, Ct)).EnsureSuccessStatusCode();
        body["defaultTemplate"] = "prestige";
        await AssertProblemAsync(await owner.PutAsJsonAsync("/api/business", body, Ct), HttpStatusCode.PaymentRequired);

        await api.MakeProAsync(TenantId(auth), Products.Invoices);
        var pro = (await Json(await owner.GetAsync("/api/invoice-templates", Ct))).EnumerateArray().ToList();
        Assert.All(pro, t => Assert.True(t.GetProperty("available").GetBoolean()));
        (await owner.PutAsJsonAsync("/api/business", body, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Image_uploads_need_pro_and_removal_works_on_every_plan()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Uploads");
        var tenantId = TenantId(auth);
        foreach (var (kind, _) in Images)
        {
            await AssertProblemAsync(await owner.PostAsync($"/api/business/{kind}", ImageForm(Png), Ct), HttpStatusCode.PaymentRequired, ImagesMessage);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/business/{kind}", Ct)).StatusCode);
        }
        Assert.Equal(0, await api.WithDbAsync(db => db.Files.IgnoreQueryFilters()
            .CountAsync(f => f.TenantId == tenantId && f.Purpose.StartsWith("business-"), Ct)));

        await api.MakeProAsync(tenantId, Products.Invoices);
        Assert.True((await Json(await owner.GetAsync("/api/business", Ct))).GetProperty("brandImagesAvailable").GetBoolean());
        foreach (var (kind, field) in Images)
        {
            var uploaded = await Json(await owner.PostAsync($"/api/business/{kind}", ImageForm(Png), Ct));
            var url = uploaded.GetProperty(field).GetString();
            Assert.False(string.IsNullOrWhiteSpace(url));
            Assert.Contains($"/business/{kind}-", url);
            Assert.True(await api.WithDbAsync(db => db.Files.IgnoreQueryFilters()
                .AnyAsync(f => f.TenantId == tenantId && f.Url == url && f.Purpose == $"business-{kind}", Ct)));

            await AssertProblemAsync(await owner.PostAsync($"/api/business/{kind}", ImageForm("not an image"u8.ToArray()), Ct),
                HttpStatusCode.BadRequest, field: "file");
            Assert.Equal(url, (await Json(await owner.GetAsync("/api/business", Ct))).GetProperty(field).GetString());

            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/business/{kind}", Ct)).StatusCode);
            Assert.Equal(JsonValueKind.Null, (await Json(await owner.GetAsync("/api/business", Ct))).GetProperty(field).ValueKind);
        }
    }

    [Fact]
    public async Task Images_appear_on_documents_only_while_the_plan_allows_them()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Branded");
        var tenantId = TenantId(auth);
        await api.MakeProAsync(tenantId, Products.Invoices);
        var urls = new Dictionary<string, string>();
        foreach (var (kind, field) in Images)
            urls[field] = (await Json(await owner.PostAsync($"/api/business/{kind}", ImageForm(Png), Ct))).GetProperty(field).GetString()!;

        var client = (await Json(await owner.PostAsJsonAsync("/api/clients", new { name = "Client" }, Ct))).GetProperty("id").GetGuid();
        var items = new[] { new { description = "Work", quantity = 1, unitPriceMinor = 1_000_000, discountPercent = 0 } };
        var invoice = await Json(await owner.PostAsJsonAsync("/api/invoices", new
        {
            clientId = client, issueDate = Today, dueDate = Today.AddDays(14), currency = "NGN", items,
        }, Ct));
        var quote = await Json(await owner.PostAsJsonAsync("/api/quotes", new
        {
            clientId = client, issueDate = Today, expiryDate = Today.AddDays(14), currency = "NGN", items,
        }, Ct));
        var invoiceUrl = $"/api/public/invoices/{invoice.GetProperty("publicUrl").GetString()!.Split("/i/")[1]}";
        var quoteUrl = $"/api/public/quotes/{quote.GetProperty("publicUrl").GetString()!.Split("/q/")[1]}";
        var anon = api.CreateClient();

        async Task AssertPublicImagesAsync(bool shown)
        {
            foreach (var url in new[] { invoiceUrl, quoteUrl })
            {
                var business = (await Json(await anon.GetAsync(url, Ct))).GetProperty("business");
                foreach (var (_, field) in Images)
                {
                    if (shown) Assert.Equal(urls[field], business.GetProperty(field).GetString());
                    else Assert.Equal(JsonValueKind.Null, business.GetProperty(field).ValueKind);
                }
            }
            (await anon.GetAsync($"{invoiceUrl}/pdf", Ct)).EnsureSuccessStatusCode();
        }

        await AssertPublicImagesAsync(shown: true);

        // Downgrade: the images stay stored and visible to the owner, but leave every document.
        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionStatus.Cancelled), Ct));
        await AssertPublicImagesAsync(shown: false);
        var settings = await Json(await owner.GetAsync("/api/business", Ct));
        Assert.False(settings.GetProperty("brandImagesAvailable").GetBoolean());
        foreach (var (_, field) in Images) Assert.Equal(urls[field], settings.GetProperty(field).GetString());

        // Back on Pro, they return.
        await api.MakeProAsync(tenantId, Products.Invoices);
        await AssertPublicImagesAsync(shown: true);
    }

    [Fact]
    public async Task Removing_an_image_after_a_downgrade_is_allowed()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Downgraded");
        var tenantId = TenantId(auth);
        await api.MakeProAsync(tenantId, Products.Invoices);
        (await owner.PostAsync("/api/business/stamp", ImageForm(Png), Ct)).EnsureSuccessStatusCode();
        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionStatus.Cancelled), Ct));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/business/stamp", Ct)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(await owner.GetAsync("/api/business", Ct))).GetProperty("stampImageUrl").ValueKind);
        Assert.False(await api.WithDbAsync(db => db.Files.IgnoreQueryFilters()
            .AnyAsync(f => f.TenantId == tenantId && f.Purpose == "business-stamp", Ct)));
    }

    [Fact]
    public async Task Seeded_invoice_plans_carry_brand_images_and_the_new_feature_copy()
    {
        var plans = await api.WithDbAsync(db => db.Plans.AsNoTracking().Where(p => p.Product == Products.Invoices).ToListAsync(Ct));
        var free = plans.Single(p => p.Code == "free");
        var pro = plans.Single(p => p.Code == "pro");
        var business = plans.Single(p => p.Code == "business");

        Assert.Equal(0, free.Limits[LimitKeys.BrandImages]);
        Assert.Equal(1, pro.Limits[LimitKeys.BrandImages]);
        Assert.Equal(1, business.Limits[LimitKeys.BrandImages]);
        Assert.Contains("5 templates", free.Features);
        Assert.DoesNotContain("3 templates", free.Features);
        Assert.Contains("All 10 templates", pro.Features);
        Assert.DoesNotContain("All 5 templates", pro.Features);
        Assert.Single(pro.Features, f => f == ImagesFeature);
    }

    [Fact]
    public async Task Migration_data_steps_update_existing_rows()
    {
        var (_, noAddress) = await api.SignupInvoicesAsync("NoAddress");
        var (withAddressClient, withAddress) = await api.SignupInvoicesAsync("WithAddress");
        var body = await UpdateBodyAsync(withAddressClient);
        body["address"] = "12 Marina, Lagos";
        (await withAddressClient.PutAsJsonAsync("/api/business", body, Ct)).EnsureSuccessStatusCode();
        var (_, subscribed) = await api.SignupInvoicesAsync("Snapshot");
        var subscribedId = TenantId(subscribed);
        await api.MakeProAsync(subscribedId, Products.Invoices);

        await api.WithDbAsync(async db =>
        {
            // Run the steps against rows as they were before the release, then roll back so the shared database is untouched.
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE plans SET limits = limits - 'brand_images',
                    features = array_remove(array_replace(array_replace(features, '5 templates', '3 templates'), 'All 10 templates', 'All 5 templates'),
                        'Your logo, signature and stamp on documents')
                WHERE product = 'invoices';
                UPDATE business_settings SET header_line = 'address';
                """, Ct);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE subscriptions SET limits = CAST({"{\"premium_templates\": 1, \"invoices_per_month\": -1}"} AS jsonb) WHERE tenant_id = {subscribedId}", Ct);

            await db.Database.ExecuteSqlRawAsync(InvoiceDesigns.HeaderLineSql, Ct);
            await db.Database.ExecuteSqlRawAsync(InvoiceDesigns.PlansSql, Ct);
            await db.Database.ExecuteSqlRawAsync(InvoiceDesigns.PlansSql, Ct); // safe to run twice

            Assert.Equal("none", await HeaderLineAsync(db, TenantId(noAddress)));
            Assert.Equal("address", await HeaderLineAsync(db, TenantId(withAddress)));
            var plans = await db.Plans.AsNoTracking().Where(p => p.Product == Products.Invoices).ToListAsync(Ct);
            var free = plans.Single(p => p.Code == "free");
            var pro = plans.Single(p => p.Code == "pro");
            Assert.Equal(0, free.Limits[LimitKeys.BrandImages]);
            Assert.Equal(1, pro.Limits[LimitKeys.BrandImages]);
            Assert.Equal(1, plans.Single(p => p.Code == "business").Limits[LimitKeys.BrandImages]);
            Assert.Contains("5 templates", free.Features);
            Assert.DoesNotContain("3 templates", free.Features);
            Assert.Contains("All 10 templates", pro.Features);
            Assert.Single(pro.Features, f => f == ImagesFeature);
            Assert.DoesNotContain(ImagesFeature, free.Features);
            var snapshot = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.TenantId == subscribedId, Ct);
            Assert.Equal(1, snapshot.Limits![LimitKeys.BrandImages]);

            await tx.RollbackAsync(Ct);
            return true;
        });
    }

    [Fact]
    public async Task Generator_accepts_the_free_letterhead_fields()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/public/invoice-preview/pdf", new
        {
            business = new
            {
                name = "Freelancer", tagline = "Websites for small shops", registrationNumber = "BN 998877", headerLine = "tagline",
                secondaryColor = "#0f766e", fontStyle = "serif", documentTitle = "Bill", showSignature = true, signatureName = "Ada",
                signatureRole = "Owner", footerText = "Thank you.", logoUrl = "https://example.com/logo.png",
            },
            client = new { name = "Someone" },
            currency = "NGN",
            template = "studio", // premium: falls back to a free design
            items = new[] { new { description = "Work", quantity = 1, unitPriceMinor = 1_000_000, discountPercent = 0, taxRatePercent = 0 } },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString((await response.Content.ReadAsByteArrayAsync(Ct))[..4]));

        var invalid = await api.CreateClient().PostAsJsonAsync("/api/public/invoice-preview/pdf", new
        {
            business = new { name = "Freelancer", headerLine = "slogan", footerText = new string('f', 161) },
            items = Array.Empty<object>(),
        }, Ct);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, field: "business.headerLine");
    }

    // ------------------------------------------------------------------ final review fixes (M6, M7, M12, M14)

    private static readonly string[] LetterheadText = ["tagline", "registrationNumber", "secondaryColor", "signatureName", "signatureRole", "footerText"];

    [Fact]
    public async Task Saving_without_the_letterhead_text_keeps_it_and_an_empty_string_clears_it()
    {
        var (owner, _) = await api.SignupInvoicesAsync("KeepText");
        var body = await UpdateBodyAsync(owner);
        var values = new Dictionary<string, string>
        {
            ["tagline"] = "Bespoke tailoring", ["registrationNumber"] = "RC 1234567", ["secondaryColor"] = "#0f766e",
            ["signatureName"] = "Ada Okafor", ["signatureRole"] = "Director", ["footerText"] = "Thank you.",
        };
        foreach (var (field, value) in values) body[field] = value;
        await Json(await owner.PutAsJsonAsync("/api/business", body, Ct));

        // An older client that does not know the letterhead fields leaves them out: they are kept.
        var omitted = await UpdateBodyAsync(owner);
        foreach (var field in LetterheadText) omitted.Remove(field);
        omitted["phone"] = "0803 000 0000";
        var kept = await Json(await owner.PutAsJsonAsync("/api/business", omitted, Ct));
        foreach (var (field, value) in values) Assert.Equal(value, kept.GetProperty(field).GetString());
        Assert.Equal("0803 000 0000", kept.GetProperty("phone").GetString());

        // Null keeps too.
        var nulls = await UpdateBodyAsync(owner);
        foreach (var field in LetterheadText) nulls[field] = null;
        var stillKept = await Json(await owner.PutAsJsonAsync("/api/business", nulls, Ct));
        foreach (var (field, value) in values) Assert.Equal(value, stillKept.GetProperty(field).GetString());

        // An empty string clears.
        var empty = await UpdateBodyAsync(owner);
        foreach (var field in LetterheadText) empty[field] = "";
        var cleared = await Json(await owner.PutAsJsonAsync("/api/business", empty, Ct));
        foreach (var field in LetterheadText) Assert.Equal(JsonValueKind.Null, cleared.GetProperty(field).ValueKind);
    }

    [Fact]
    public async Task A_premium_default_design_after_a_downgrade_does_not_block_other_settings()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("PremiumDefault");
        var tenantId = TenantId(auth);
        await api.MakeProAsync(tenantId, Products.Invoices);
        var body = await UpdateBodyAsync(owner);
        body["defaultTemplate"] = "prestige";
        await Json(await owner.PutAsJsonAsync("/api/business", body, Ct));
        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionStatus.Cancelled), Ct));

        // The default design is unchanged, so other settings save.
        var other = await UpdateBodyAsync(owner);
        other["tagline"] = "Still in business";
        var saved = await Json(await owner.PutAsJsonAsync("/api/business", other, Ct));
        Assert.Equal("Still in business", saved.GetProperty("tagline").GetString());
        Assert.Equal("prestige", saved.GetProperty("defaultTemplate").GetString());

        // Changing to another premium design still needs the plan; a free one is fine.
        other["defaultTemplate"] = "ledger";
        await AssertProblemAsync(await owner.PutAsJsonAsync("/api/business", other, Ct), HttpStatusCode.PaymentRequired);
        other["defaultTemplate"] = "classic";
        Assert.Equal("classic", (await Json(await owner.PutAsJsonAsync("/api/business", other, Ct))).GetProperty("defaultTemplate").GetString());
    }

    [Fact]
    public async Task Signature_name_and_role_are_not_sent_publicly_while_the_switch_is_off()
    {
        var (owner, _) = await api.SignupInvoicesAsync("SignatureOff");
        var body = await UpdateBodyAsync(owner);
        body["showSignature"] = false;
        body["signatureName"] = "Ada Okafor";
        body["signatureRole"] = "Director";
        await Json(await owner.PutAsJsonAsync("/api/business", body, Ct));
        var client = (await Json(await owner.PostAsJsonAsync("/api/clients", new { name = "Client" }, Ct))).GetProperty("id").GetGuid();
        var invoice = await Json(await owner.PostAsJsonAsync("/api/invoices", new
        {
            clientId = client, issueDate = Today, dueDate = Today.AddDays(14), currency = "NGN",
            items = new[] { new { description = "Work", quantity = 1, unitPriceMinor = 1_000_000, discountPercent = 0 } },
        }, Ct));
        var url = $"/api/public/invoices/{invoice.GetProperty("publicUrl").GetString()!.Split("/i/")[1]}";
        var anon = api.CreateClient();

        var hidden = (await Json(await anon.GetAsync(url, Ct))).GetProperty("business");
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("signatureName").ValueKind);
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("signatureRole").ValueKind);

        body = await UpdateBodyAsync(owner);
        body["showSignature"] = true;
        await Json(await owner.PutAsJsonAsync("/api/business", body, Ct));
        var shown = (await Json(await anon.GetAsync(url, Ct))).GetProperty("business");
        Assert.Equal("Ada Okafor", shown.GetProperty("signatureName").GetString());
        Assert.Equal("Director", shown.GetProperty("signatureRole").GetString());
    }

    [Fact]
    public async Task Images_over_4000_pixels_a_side_are_refused()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("HugeImages");
        await api.MakeProAsync(TenantId(auth), Products.Invoices);
        foreach (var (bytes, type) in new[]
                 {
                     (PngHeader(20_000, 20_000), "image/png"), (PngHeader(4001, 10), "image/png"), (JpegHeader(300, 4500), "image/jpeg"),
                     (WebpHeader(6000, 100), "image/webp"),
                 })
        {
            foreach (var (kind, _) in Images)
            {
                var response = await owner.PostAsync($"/api/business/{kind}", ImageForm(bytes, type), Ct);
                await AssertProblemAsync(response, HttpStatusCode.BadRequest, field: "file");
                Assert.Contains("4000 × 4000 pixels", await response.Content.ReadAsStringAsync(Ct));
            }
        }
        // At the limit is fine.
        (await owner.PostAsync("/api/business/logo", ImageForm(PngHeader(4000, 4000)), Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsync("/api/business/stamp", ImageForm(JpegHeader(4000, 1200), "image/jpeg"), Ct)).EnsureSuccessStatusCode();
    }

    /// <summary>A PNG signature and IHDR chunk declaring the given size (enough for the header check).</summary>
    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        bytes.AddRange(BigEndian(width, 4));
        bytes.AddRange(BigEndian(height, 4));
        bytes.AddRange([8, 6, 0, 0, 0, 0, 0, 0, 0]);
        return [.. bytes];
    }

    /// <summary>A JPEG start, an APP0 segment and a baseline SOF0 frame header declaring the given size.</summary>
    private static byte[] JpegHeader(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 };
        bytes.AddRange([0xFF, 0xC0, 0, 17, 8]);
        bytes.AddRange(BigEndian(height, 2));
        bytes.AddRange(BigEndian(width, 2));
        bytes.AddRange([3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);
        return [.. bytes];
    }

    /// <summary>A RIFF WebP with an extended (VP8X) header declaring the given size.</summary>
    private static byte[] WebpHeader(int width, int height)
    {
        var bytes = new List<byte> { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 30, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        bytes.AddRange([(byte)'V', (byte)'P', (byte)'8', (byte)'X', 10, 0, 0, 0, 0, 0, 0, 0]);
        bytes.AddRange(LittleEndian(width - 1, 3));
        bytes.AddRange(LittleEndian(height - 1, 3));
        return [.. bytes];
    }

    private static IEnumerable<byte> BigEndian(int value, int count) => Enumerable.Range(0, count).Select(i => (byte)(value >> (8 * (count - 1 - i))));
    private static IEnumerable<byte> LittleEndian(int value, int count) => Enumerable.Range(0, count).Select(i => (byte)(value >> (8 * i)));

    // Helpers

    private static Guid TenantId(JsonElement auth) => auth.GetProperty("tenant").GetProperty("id").GetGuid();

    private static Task<string> HeaderLineAsync(AppDbContext db, Guid tenantId) =>
        db.Database.SqlQuery<string>($"SELECT header_line AS \"Value\" FROM business_settings WHERE tenant_id = {tenantId}").SingleAsync(Ct);

    /// <summary>The current settings as an update body (extra read-only fields are ignored by the API).</summary>
    private static async Task<Dictionary<string, object?>> UpdateBodyAsync(HttpClient owner)
    {
        var business = await Json(await owner.GetAsync("/api/business", Ct));
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(business.GetRawText())!;
    }

    private static MultipartFormDataContent ImageForm(byte[] bytes, string contentType = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", "image.png" } };
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        if (detail is null && field is null) return;
        var body = JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (detail is not null) Assert.Equal(detail, body.GetProperty("detail").GetString());
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
    }
}
