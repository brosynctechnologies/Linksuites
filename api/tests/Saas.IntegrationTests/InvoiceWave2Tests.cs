using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Invoices;
using Saas.Infrastructure.Persistence;

namespace Saas.IntegrationTests;

/// <summary>Wave 2 (roadmap 2026-11-17) InvoiceLink fixes.</summary>
[Collection(nameof(ApiCollection))]
public sealed class InvoiceWave2Tests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)); // Lagos

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // ---- I5: quote activity ----

    [Fact]
    public async Task QuoteFlow_AfterSendAcceptAndConvert_ReturnsEvents()
    {
        var (owner, _) = await api.SignupInvoicesAsync("QuoteEvents");
        var (id, token) = await CreateQuoteAsync(owner);
        Assert.Equal(["Created"], Types(await Json(await owner.GetAsync($"/api/quotes/{id}", Ct))));

        (await owner.PostAsync($"/api/quotes/{id}/mark-sent", null, Ct)).EnsureSuccessStatusCode();
        var anon = api.CreateClient();
        (await anon.GetAsync($"/api/public/quotes/{token}", Ct)).EnsureSuccessStatusCode();
        (await anon.PostAsync($"/api/public/quotes/{token}/accept", null, Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsync($"/api/quotes/{id}/convert", null, Ct)).EnsureSuccessStatusCode();

        var quote = await Json(await owner.GetAsync($"/api/quotes/{id}", Ct));
        Assert.Equal(["Created", "Sent", "Viewed", "Accepted", "Converted"], Types(quote));
    }

    // ---- I7: preview matches the PDF's branding ----

    [Fact]
    public async Task Invoice_and_quote_say_whether_the_pdf_prints_branding()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Branded");
        var (quoteId, _) = await CreateQuoteAsync(owner);
        var client = await CreateClientAsync(owner);
        var invoice = await Json(await owner.PostAsJsonAsync("/api/invoices", new
        {
            clientId = client, issueDate = Today, dueDate = Today.AddDays(14), currency = "NGN",
            items = new[] { new { description = "Work", quantity = 1, unitPriceMinor = 100_000, discountPercent = 0 } },
        }, Ct));
        var invoiceId = invoice.GetProperty("id").GetGuid();
        Assert.True(invoice.GetProperty("showBranding").GetBoolean());
        Assert.True((await Json(await owner.GetAsync($"/api/quotes/{quoteId}", Ct))).GetProperty("showBranding").GetBoolean());

        await api.MakeProAsync(auth.GetProperty("tenant").GetProperty("id").GetGuid(), Products.Invoices);
        Assert.False((await Json(await owner.GetAsync($"/api/invoices/{invoiceId}", Ct))).GetProperty("showBranding").GetBoolean());
        Assert.False((await Json(await owner.GetAsync($"/api/quotes/{quoteId}", Ct))).GetProperty("showBranding").GetBoolean());
    }

    // ---- I8: "Paid this month" opens the invoices paid this month ----

    [Fact]
    public async Task Invoice_list_filters_by_payment_date_like_paid_this_month()
    {
        var (owner, _) = await api.SignupInvoicesAsync("PaidMonth");
        var client = await CreateClientAsync(owner);
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        async Task<Guid> NewInvoice()
        {
            var inv = await Json(await owner.PostAsJsonAsync("/api/invoices", new
            {
                clientId = client, issueDate = monthStart.AddDays(-5), dueDate = Today.AddDays(14), currency = "NGN",
                items = new[] { new { description = "Work", quantity = 1, unitPriceMinor = 5_000_000, discountPercent = 0 } },
            }, Ct));
            return inv.GetProperty("id").GetGuid();
        }
        var partly = await NewInvoice();
        var unpaid = await NewInvoice();
        var lastMonth = await NewInvoice();
        (await owner.PostAsJsonAsync($"/api/invoices/{partly}/payments", new { amountMinor = 2_000_000, method = "Cash", paidAt = Today }, Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/invoices/{lastMonth}/payments", new { amountMinor = 5_000_000, method = "Cash", paidAt = monthStart.AddDays(-1) }, Ct))
            .EnsureSuccessStatusCode();

        var list = await Json(await owner.GetAsync($"/api/invoices?paidFrom={monthStart:yyyy-MM-dd}&paidTo={Today:yyyy-MM-dd}", Ct));
        var ids = list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([partly], ids);
        Assert.DoesNotContain(unpaid, ids);
    }

    // ---- I10: "The sender has been notified" only when true ----

    [Fact]
    public async Task Quote_response_says_whether_the_sender_was_notified()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Notified");
        var (first, firstToken) = await CreateQuoteAsync(owner);
        (await owner.PostAsync($"/api/quotes/{first}/mark-sent", null, Ct)).EnsureSuccessStatusCode();
        var accepted = await Json(await api.CreateClient().PostAsync($"/api/public/quotes/{firstToken}/accept", null, Ct));
        Assert.True(accepted.GetProperty("senderNotified").GetBoolean());

        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.WithDbAsync(db => db.BusinessSettings.IgnoreQueryFilters().Where(b => b.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Email, (string?)null), Ct));
        var (second, secondToken) = await CreateQuoteAsync(owner);
        (await owner.PostAsync($"/api/quotes/{second}/mark-sent", null, Ct)).EnsureSuccessStatusCode();
        var declined = await Json(await api.CreateClient().PostAsync($"/api/public/quotes/{secondToken}/decline", null, Ct));
        Assert.False(declined.GetProperty("senderNotified").GetBoolean());
    }

    // ---- Expense receipts are private ----

    [Fact]
    public async Task Expense_receipts_are_private_and_read_through_the_api_by_the_business()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Receipts");
        await api.MakeProAsync(auth.GetProperty("tenant").GetProperty("id").GetGuid(), Products.Invoices, "business");
        var expenseId = await CreateExpenseAsync(owner);
        var pdf = "%PDF-1.4 receipt"u8.ToArray();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(pdf), "file", "receipt.pdf" } };
        var withReceipt = await Json(await owner.PostAsync($"/api/expenses/{expenseId}/receipt", form, Ct));

        var key = await api.WithDbAsync(db => db.Expenses.IgnoreQueryFilters().Where(e => e.Id == expenseId).Select(e => e.ReceiptKey).SingleAsync(Ct));
        Assert.StartsWith("private/", key);
        var url = withReceipt.GetProperty("receiptUrl").GetString()!;
        Assert.DoesNotContain("/uploads/", url);
        Assert.True(withReceipt.GetProperty("receiptIsPdf").GetBoolean());

        var read = await owner.GetAsync($"/api/expenses/{expenseId}/receipt", Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("application/pdf", read.Content.Headers.ContentType?.MediaType);
        Assert.Equal(pdf, await read.Content.ReadAsByteArrayAsync(Ct));

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/api/expenses/{expenseId}/receipt", Ct)).StatusCode);
        var (other, otherAuth) = await api.SignupInvoicesAsync("Snooper");
        await api.MakeProAsync(otherAuth.GetProperty("tenant").GetProperty("id").GetGuid(), Products.Invoices, "business");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/expenses/{expenseId}/receipt", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/uploads/{key}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Existing_public_receipts_move_to_private_storage()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("LegacyReceipt");
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.MakeProAsync(tenantId, Products.Invoices, "business");
        var expenseId = await CreateExpenseAsync(owner);
        var legacyKey = $"{tenantId}/expenses/{expenseId:N}/{Guid.NewGuid():N}.png";
        using (var scope = api.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await using var content = new MemoryStream(Png);
            var legacyUrl = await storage.SaveAsync(legacyKey, content, "image/png", Ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Expenses.IgnoreQueryFilters().Where(e => e.Id == expenseId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReceiptKey, legacyKey).SetProperty(e => e.ReceiptUrl, legacyUrl), Ct);
            db.Files.Add(new Saas.Domain.Core.StoredFile { TenantId = tenantId, Key = legacyKey, Url = legacyUrl, MimeType = "image/png", SizeBytes = Png.Length, Purpose = "expense-receipt" });
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().GetAsync($"/uploads/{legacyKey}", Ct)).StatusCode); // public before

        using (var scope = api.Services.CreateScope())
        {
            var moved = await scope.ServiceProvider.GetRequiredService<ExpenseReceiptMigration>().RunAsync(Ct);
            Assert.True(moved >= 1);
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ExpenseReceiptMigration>().RunAsync(Ct)); // nothing left to move
        }

        var row = await api.WithDbAsync(db => db.Expenses.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == expenseId, Ct));
        Assert.StartsWith($"private/invoices/{tenantId}/expenses/", row.ReceiptKey);
        Assert.EndsWith(".png", row.ReceiptKey);
        Assert.Null(row.ReceiptUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/uploads/{legacyKey}", Ct)).StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Files.IgnoreQueryFilters().AnyAsync(f => f.Key == legacyKey, Ct)));
        var read = await owner.GetAsync($"/api/expenses/{expenseId}/receipt", Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(Png, await read.Content.ReadAsByteArrayAsync(Ct));
    }

    // ---- M8: a failing delete of the old image doesn't fail the save ----

    [Fact]
    public async Task Replacing_the_logo_succeeds_when_deleting_the_old_file_fails()
    {
        await using var failing = api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            var existing = s.Last(d => d.ServiceType == typeof(IFileStorage));
            s.Add(new ServiceDescriptor(typeof(IFileStorage), sp => new FailingDeleteStorage((IFileStorage)(existing.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, existing.ImplementationType!))), existing.Lifetime));
        }));
        var client = failing.CreateClient();
        var response = await api.SignUpAsync(client, "/api/auth/signup", new
        {
            fullName = "Logo Owner", email = $"logo-{Guid.NewGuid():N}@example.com", password = "password123",
            country = "NG", product = "invoices", businessName = "Logo Studio",
        }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        await api.MakeProAsync(auth.GetProperty("tenant").GetProperty("id").GetGuid(), Products.Invoices);

        (await client.PostAsync("/api/business/logo", ImageForm(Png), Ct)).EnsureSuccessStatusCode();
        var replaced = await client.PostAsync("/api/business/logo", ImageForm(Png), Ct);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/business/logo", Ct)).StatusCode);
    }

    // ---- M10: the stamp image prints like ink (white paper shows through, 90% strength) ----

    [Fact]
    public void Stamp_image_white_becomes_transparent_and_ink_is_slightly_see_through()
    {
        using var source = new SkiaSharp.SKBitmap(2, 1, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
        source.SetPixel(0, 0, new SkiaSharp.SKColor(255, 255, 255, 255)); // paper white
        source.SetPixel(1, 0, new SkiaSharp.SKColor(20, 40, 160, 255)); // blue ink
        using var encoded = SkiaSharp.SKImage.FromBitmap(source).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);

        var ink = Saas.Infrastructure.Invoices.StampInk.Apply(encoded.ToArray());

        using var result = SkiaSharp.SKBitmap.Decode(ink);
        Assert.Equal(0, result.GetPixel(0, 0).Alpha);
        var blue = result.GetPixel(1, 0);
        Assert.InRange(blue.Alpha, 180, 235); // about 90% of the ink's own strength
        Assert.True(blue.Blue > blue.Red);
    }

    // ---- M13: subscription snapshots get the new template copy ----

    [Fact]
    public async Task Subscription_snapshots_get_the_new_template_copy()
    {
        var (_, auth) = await api.SignupInvoicesAsync("OldCopy");
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.MakeProAsync(tenantId, Products.Invoices);
        await api.WithDbAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE subscriptions SET features = ARRAY['All 5 templates', '3 templates', 'Unlimited invoices'] WHERE tenant_id = {tenantId}", Ct);

            await db.Database.ExecuteSqlRawAsync(Wave2DataFixes.SubscriptionFeaturesSql, Ct);
            await db.Database.ExecuteSqlRawAsync(Wave2DataFixes.SubscriptionFeaturesSql, Ct); // safe to run twice

            var snapshot = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.TenantId == tenantId, Ct);
            Assert.Equal(["All 10 templates", "5 templates", "Unlimited invoices"], snapshot.Features!);
            await tx.RollbackAsync(Ct);
            return 0;
        });
    }

    // Helpers

    private sealed class FailingDeleteStorage(IFileStorage inner) : IFileStorage
    {
        public Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => inner.SaveAsync(key, content, contentType, ct);
        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new InvalidOperationException("Storage is unavailable.");
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => inner.OpenReadAsync(key, ct);
        public string? TemporaryUrl(string key, TimeSpan lifetime) => inner.TemporaryUrl(key, lifetime);
    }

    private static MultipartFormDataContent ImageForm(byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "file", "image.png" } };
    }

    private static List<string> Types(JsonElement doc) =>
        doc.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()!).ToList();

    private async Task<(Guid Id, string Token)> CreateQuoteAsync(HttpClient owner)
    {
        var client = await CreateClientAsync(owner);
        var quote = await Json(await owner.PostAsJsonAsync("/api/quotes", new
        {
            clientId = client, issueDate = Today, expiryDate = Today.AddDays(30), currency = "NGN",
            items = new[] { new { description = "Website", quantity = 1, unitPriceMinor = 5_000_000, discountPercent = 0 } },
        }, Ct));
        return (quote.GetProperty("id").GetGuid(), quote.GetProperty("publicUrl").GetString()!.Split("/q/")[1]);
    }

    private async Task<Guid> CreateClientAsync(HttpClient owner)
    {
        var response = await owner.PostAsJsonAsync("/api/clients", new { name = "Client W", email = "w@example.com" }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateExpenseAsync(HttpClient owner)
    {
        var response = await owner.PostAsJsonAsync("/api/expenses", new
        {
            date = Today, category = "Software", vendor = "Figma", amountMinor = 200_000, currency = "NGN", taxMinor = 0, paymentMethod = "Card",
        }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
