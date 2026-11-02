using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class OrderFlowTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Customer_order_is_saved_priced_on_the_server_and_sent_to_whatsapp()
    {
        var (seller, auth) = await api.SignupAsync("Ada");
        var slug = auth.GetProperty("tenant").GetProperty("slug").GetString();
        var zone = await Json(await seller.PostAsJsonAsync("/api/delivery-zones", new { name = "Lekki", feeMinor = 150_000, isActive = true }, Ct));
        var rice = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Jollof rice", priceMinor = 300_000, inStock = true, isActive = true }, Ct));

        var customer = api.CreateClient();
        var response = await customer.PostAsJsonAsync($"/api/public/stores/{slug}/orders", new
        {
            items = new[] { new { productId = rice.GetProperty("id").GetGuid(), quantity = 2 } },
            fulfilment = "Delivery",
            deliveryZoneId = zone.GetProperty("id").GetGuid(),
            customerName = "Bola",
            phone = "0803 000 0000",
            address = "12 Admiralty Way",
        }, Ct);
        var order = await Json(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(750_000, order.GetProperty("totalMinor").GetInt64());
        Assert.Equal(1001, order.GetProperty("number").GetInt64());
        var whatsapp = order.GetProperty("whatsappUrl").GetString()!;
        Assert.StartsWith("https://wa.me/2348031234567?text=", whatsapp);
        Assert.Contains("2 x Jollof rice", Uri.UnescapeDataString(whatsapp));

        var list = await Json(await seller.GetAsync("/api/orders", Ct));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("2348030000000", list.GetProperty("items")[0].GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Editing_a_product_can_add_variants_and_images()
    {
        var (seller, _) = await api.SignupAsync("Editor");
        var product = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Dress", priceMinor = 1_000 }, Ct));
        var id = product.GetProperty("id").GetGuid();

        var updated = await seller.PutAsJsonAsync($"/api/products/{id}", new
        {
            name = "Dress",
            priceMinor = 1_000,
            options = new[] { new { name = "Size", values = new[] { "S", "M" } } },
            variants = new[] { new { optionValues = new[] { "S" }, inStock = true }, new { optionValues = new[] { "M" }, inStock = true } },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, (await Json(updated)).GetProperty("variants").GetArrayLength());

        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var form = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "dot.png" } };
        var upload = await seller.PostAsync($"/api/products/{id}/images", form, Ct);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(1, (await Json(upload)).GetProperty("images").GetArrayLength());

        using var fake = new MultipartFormDataContent { { new ByteArrayContent("<script>"u8.ToArray()), "file", "x.png" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.PostAsync($"/api/products/{id}/images", fake, Ct)).StatusCode);
    }

    [Fact]
    public async Task Tenants_cannot_see_or_change_each_others_data()
    {
        var (first, _) = await api.SignupAsync("First");
        var (second, _) = await api.SignupAsync("Second");
        var product = await Json(await first.PostAsJsonAsync("/api/products", new { name = "Secret", priceMinor = 100 }, Ct));
        var id = product.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/products/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.PutAsJsonAsync($"/api/products/{id}", new { name = "Hacked", priceMinor = 1 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.DeleteAsync($"/api/products/{id}", Ct)).StatusCode);
        Assert.Empty((await Json(await second.GetAsync("/api/products", Ct))).EnumerateArray());
        Assert.Equal("Secret", (await Json(await first.GetAsync($"/api/products/{id}", Ct))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Free_plan_product_limit_returns_402()
    {
        var (seller, _) = await api.SignupAsync("Limit");
        for (var i = 0; i < 20; i++)
            (await seller.PostAsJsonAsync("/api/products", new { name = $"P{i}", priceMinor = 100 }, Ct)).EnsureSuccessStatusCode();

        var response = await seller.PostAsJsonAsync("/api/products", new { name = "One too many", priceMinor = 100 }, Ct);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal("plan_limit", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Signed_paystack_webhook_upgrades_the_plan_once()
    {
        var (seller, auth) = await api.SignupAsync("Payer");
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        var reference = $"sub_test_{Guid.NewGuid():N}";
        await api.WithDbAsync(async db =>
        {
            var pro = await db.Plans.FirstAsync(p => p.Product == Products.WhatsAppOrders && p.Code == "pro", Ct);
            db.Payments.Add(new Payment
            {
                TenantId = tenantId, AmountMinor = pro.PriceMinor, Currency = "NGN", Provider = "paystack",
                ProviderRef = reference, Purpose = "subscription", Metadata = new() { ["planId"] = pro.Id.ToString() },
            });
            return await db.SaveChangesAsync(Ct);
        });

        var body = $$$"""{"event":"charge.success","data":{"status":"success","reference":"{{{reference}}}","amount":300000,"currency":"NGN"}}""";
        var forged = await PostWebhook(body, "00");
        var first = await PostWebhook(body, Sign(body));
        var retry = await PostWebhook(body, Sign(body));

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var billing = await Json(await seller.GetAsync("/api/billing", Ct));
        Assert.Equal("pro", billing.GetProperty("currentPlan").GetProperty("code").GetString());
        var subscriptions = await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, Ct));
        Assert.Equal(1, subscriptions);
    }

    [Fact]
    public async Task Cart_quote_keeps_lines_over_stock_and_its_subtotal_matches_the_lines()
    {
        var (seller, auth) = await api.SignupAsync("Mama");
        var slug = auth.GetProperty("tenant").GetProperty("slug").GetString();
        var rice = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Jollof rice", priceMinor = 300_000, inStock = true, isActive = true }, Ct));
        var chapman = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Chapman", priceMinor = 150_000, inStock = true, isActive = true, stockQty = 5 }, Ct));

        var quote = await Json(await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/quote", new
        {
            items = new[]
            {
                new { productId = rice.GetProperty("id").GetGuid(), quantity = 4 },
                new { productId = chapman.GetProperty("id").GetGuid(), quantity = 6 },
            },
            fulfilment = "Pickup",
        }, Ct));

        var lines = quote.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        var chapmanLine = lines.Single(l => l.GetProperty("name").GetString() == "Chapman");
        Assert.Equal(5, chapmanLine.GetProperty("quantity").GetInt32());
        Assert.Equal(5, chapmanLine.GetProperty("stockQty").GetInt32());
        Assert.Equal(750_000, chapmanLine.GetProperty("lineTotalMinor").GetInt64());
        Assert.Equal(JsonValueKind.Null, lines.Single(l => l.GetProperty("name").GetString() == "Jollof rice").GetProperty("stockQty").ValueKind);
        Assert.Equal(lines.Sum(l => l.GetProperty("lineTotalMinor").GetInt64()), quote.GetProperty("subtotalMinor").GetInt64());
        Assert.Equal(1_950_000, quote.GetProperty("subtotalMinor").GetInt64());
    }

    // ---- Recheck fixes (orders-invoices.md) ----

    [Fact]
    public async Task DiscountCode_CreatedWithoutMinimumOrder_Succeeds()
    {
        var (seller, auth) = await api.SignupAsync("Discounter");
        await api.MakeProAsync(TenantId(auth), Products.WhatsAppOrders);

        var response = await seller.PostAsJsonAsync("/api/discount-codes", new
        {
            code = "WELCOME10", type = "Percent", value = 10, minOrderMinor = (long?)null, expiresAt = (DateTimeOffset?)null,
            maxUses = (int?)null, isActive = true,
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await Json(response)).GetProperty("minOrderMinor").GetInt64());
    }

    [Fact]
    public async Task Quote_TwoVariantsOfTrackedProduct_ClampsToSharedStock()
    {
        var (seller, auth) = await api.SignupAsync("Sizes");
        var slug = Slug(auth);
        var (productId, small, medium) = await DressWithSizesAsync(seller, stock: 4);

        var quote = await Json(await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/quote", new
        {
            items = new[] { new { productId, variantId = small, quantity = 3 }, new { productId, variantId = medium, quantity = 3 } },
            fulfilment = "Pickup",
        }, Ct));

        var lines = quote.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(4, lines.Sum(l => l.GetProperty("quantity").GetInt32()));
        // Each line reports what is left for it after the lines before it, so the cart can clamp per line.
        Assert.Equal(4, lines[0].GetProperty("stockQty").GetInt32());
        Assert.Equal(1, lines[1].GetProperty("stockQty").GetInt32());
        Assert.Equal(4 * 1_000, quote.GetProperty("subtotalMinor").GetInt64());
    }

    [Fact]
    public async Task PlaceOrder_TwoVariantsExceedingSharedStock_Returns409FromPricing()
    {
        var (seller, auth) = await api.SignupAsync("Sizes2");
        var slug = Slug(auth);
        var (productId, small, medium) = await DressWithSizesAsync(seller, stock: 4);

        var response = await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/orders", new
        {
            items = new[] { new { productId, variantId = small, quantity = 3 }, new { productId, variantId = medium, quantity = 3 } },
            fulfilment = "Pickup", customerName = "Bola", phone = "0803 000 0000",
        }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("out_of_stock", body.GetProperty("code").GetString());
        // Raised by pricing (which sees both lines), not by the per-line stock decrement.
        Assert.Contains("is out of stock", body.GetProperty("detail").GetString());
        Assert.Equal(4, await StockAsync(productId));
    }

    [Fact]
    public async Task PlaceOrder_WithInactiveProduct_Returns409Unavailable()
    {
        var (seller, auth) = await api.SignupAsync("Inactive");
        var slug = Slug(auth);
        var a = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "A", priceMinor = 500_000, isActive = true }, Ct));
        var b = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "B", priceMinor = 700_000, isActive = false }, Ct));

        var response = await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/orders", new
        {
            items = new[] { new { productId = a.GetProperty("id").GetGuid(), quantity = 1 }, new { productId = b.GetProperty("id").GetGuid(), quantity = 1 } },
            fulfilment = "Pickup", customerName = "Bola", phone = "0803 000 0000",
        }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("unavailable", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task MarkPaid_OrderAppearsInPaidFilter()
    {
        var (seller, auth) = await api.SignupAsync("PaidTab");
        var slug = Slug(auth);
        var product = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Rice", priceMinor = 1_000 }, Ct));
        var productId = product.GetProperty("id").GetGuid();
        await PlaceAsync(slug, productId, 1);
        await PlaceAsync(slug, productId, 1);
        var orders = (await Json(await seller.GetAsync("/api/orders", Ct))).GetProperty("items").EnumerateArray().ToList();
        var paidId = orders[0].GetProperty("id").GetGuid();

        (await seller.PatchAsJsonAsync($"/api/orders/{paidId}/payment", new { paymentStatus = "Paid" }, Ct)).EnsureSuccessStatusCode();

        var paid = await Json(await seller.GetAsync("/api/orders?paymentStatus=Paid", Ct));
        var only = Assert.Single(paid.GetProperty("items").EnumerateArray());
        Assert.Equal(paidId, only.GetProperty("id").GetGuid());
        var unpaid = await Json(await seller.GetAsync("/api/orders?paymentStatus=Unpaid", Ct));
        Assert.Equal(1, unpaid.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task MarkUnpaid_AfterStatusPaid_DoesNotLeaveContradictoryState()
    {
        var (seller, auth) = await api.SignupAsync("Unpaid");
        var slug = Slug(auth);
        var product = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Rice", priceMinor = 1_000 }, Ct));
        await PlaceAsync(slug, product.GetProperty("id").GetGuid(), 1);
        var id = (await Json(await seller.GetAsync("/api/orders", Ct))).GetProperty("items")[0].GetProperty("id").GetGuid();

        var paid = await Json(await seller.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Paid" }, Ct));
        Assert.Equal("Paid", paid.GetProperty("paymentStatus").GetString());
        var unpaid = await Json(await seller.PatchAsJsonAsync($"/api/orders/{id}/payment", new { paymentStatus = "Unpaid" }, Ct));

        Assert.Equal("Unpaid", unpaid.GetProperty("paymentStatus").GetString());
        Assert.NotEqual("Paid", unpaid.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CancelOrder_RestoresTrackedStock()
    {
        var (seller, auth) = await api.SignupAsync("Canceller");
        var slug = Slug(auth);
        var tracked = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Chapman", priceMinor = 1_000, stockQty = 5 }, Ct));
        var untracked = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Water", priceMinor = 100 }, Ct));
        var trackedId = tracked.GetProperty("id").GetGuid();
        var response = await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/orders", new
        {
            items = new[] { new { productId = trackedId, quantity = 3 }, new { productId = untracked.GetProperty("id").GetGuid(), quantity = 2 } },
            fulfilment = "Pickup", customerName = "Bola", phone = "0803 000 0000",
        }, Ct);
        response.EnsureSuccessStatusCode();
        Assert.Equal(2, await StockAsync(trackedId));
        var id = (await Json(await seller.GetAsync("/api/orders", Ct))).GetProperty("items")[0].GetProperty("id").GetGuid();

        var first = await seller.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Cancelled" }, Ct);
        var again = await seller.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Cancelled" }, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(5, await StockAsync(trackedId)); // given back once, not twice
        Assert.Null(await StockAsync(untracked.GetProperty("id").GetGuid()));

        // The stock is already back on sale, so the order can't be reopened.
        var reopen = await seller.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Confirmed" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);
        Assert.Equal(5, await StockAsync(trackedId));
    }

    [Fact]
    public async Task CancelOrder_KeepsDiscountUseConsumed()
    {
        var (seller, auth) = await api.SignupAsync("CancelCode");
        await api.MakeProAsync(TenantId(auth), Products.WhatsAppOrders);
        var slug = Slug(auth);
        var product = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Rice", priceMinor = 10_000 }, Ct));
        (await seller.PostAsJsonAsync("/api/discount-codes", new { code = "ONCE", type = "Percent", value = 10, minOrderMinor = 0, maxUses = 1, isActive = true }, Ct))
            .EnsureSuccessStatusCode();
        (await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/orders", new
        {
            items = new[] { new { productId = product.GetProperty("id").GetGuid(), quantity = 1 } },
            fulfilment = "Pickup", customerName = "Bola", phone = "0803 000 0000", discountCode = "ONCE",
        }, Ct)).EnsureSuccessStatusCode();
        var id = (await Json(await seller.GetAsync("/api/orders", Ct))).GetProperty("items")[0].GetProperty("id").GetGuid();

        (await seller.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Cancelled" }, Ct)).EnsureSuccessStatusCode();

        var code = (await Json(await seller.GetAsync("/api/discount-codes", Ct))).EnumerateArray().Single();
        Assert.Equal(1, code.GetProperty("uses").GetInt32()); // ruling: a cancelled order does not give the use back
    }

    [Fact]
    public async Task SaveProduct_WithStaleStock_DoesNotOverwriteSales()
    {
        var (seller, auth) = await api.SignupAsync("Stale");
        var slug = Slug(auth);
        var product = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Bag", priceMinor = 1_000, stockQty = 10 }, Ct));
        var id = product.GetProperty("id").GetGuid();
        // The seller opens the edit form (stock 10), then a customer buys 3.
        await PlaceAsync(slug, id, 3);
        Assert.Equal(7, await StockAsync(id));

        // Only the price changes; the form sends back the stock it loaded.
        var saved = await seller.PutAsJsonAsync($"/api/products/{id}", new { name = "Bag", priceMinor = 1_200, stockQty = 10, expectedStockQty = 10 }, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(7, await StockAsync(id));
        Assert.Equal(1_200, (await Json(saved)).GetProperty("priceMinor").GetInt64());

        // Changing the stock from a stale value is refused rather than wiping out the sale.
        var stale = await seller.PutAsJsonAsync($"/api/products/{id}", new { name = "Bag", priceMinor = 1_200, stockQty = 12, expectedStockQty = 10 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("stock_changed", (await Json(stale)).GetProperty("code").GetString());
        Assert.Equal(7, await StockAsync(id));

        // From the current value it applies.
        var fresh = await seller.PutAsJsonAsync($"/api/products/{id}", new { name = "Bag", priceMinor = 1_200, stockQty = 12, expectedStockQty = 7 }, Ct);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Equal(12, await StockAsync(id));
    }

    [Fact]
    public async Task PublicOrder_OnPlanWithRemoveBranding_ShowBrandingFalse()
    {
        var (seller, auth) = await api.SignupAsync("Unbranded");
        var slug = Slug(auth);
        var product = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Rice", priceMinor = 1_000 }, Ct));
        var code = await PlaceAsync(slug, product.GetProperty("id").GetGuid(), 1);

        var free = await Json(await api.CreateClient().GetAsync($"/api/public/orders/{code}", Ct));
        Assert.True(free.GetProperty("showBranding").GetBoolean());

        await api.MakeProAsync(TenantId(auth), Products.WhatsAppOrders);
        var pro = await Json(await api.CreateClient().GetAsync($"/api/public/orders/{code}", Ct));
        Assert.False(pro.GetProperty("showBranding").GetBoolean());
    }

    private static Guid TenantId(JsonElement auth) => auth.GetProperty("tenant").GetProperty("id").GetGuid();
    private static string Slug(JsonElement auth) => auth.GetProperty("tenant").GetProperty("slug").GetString()!;

    private Task<int?> StockAsync(Guid productId) =>
        api.WithDbAsync(db => db.Products.IgnoreQueryFilters().Where(p => p.Id == productId).Select(p => p.StockQty).SingleAsync(Ct));

    private async Task<string> PlaceAsync(string slug, Guid productId, int quantity)
    {
        var response = await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/orders", new
        {
            items = new[] { new { productId, quantity } }, fulfilment = "Pickup", customerName = "Bola", phone = "0803 000 0000",
        }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("code").GetString()!;
    }

    private async Task<(Guid Product, Guid Small, Guid Medium)> DressWithSizesAsync(HttpClient seller, int stock)
    {
        var product = await Json(await seller.PostAsJsonAsync("/api/products", new
        {
            name = "Dress", priceMinor = 1_000, stockQty = stock,
            options = new[] { new { name = "Size", values = new[] { "S", "M" } } },
            variants = new[] { new { optionValues = new[] { "S" }, inStock = true }, new { optionValues = new[] { "M" }, inStock = true } },
        }, Ct));
        var variants = product.GetProperty("variants").EnumerateArray().ToList();
        Guid Find(string size) => variants.Single(v => v.GetProperty("optionValues")[0].GetString() == size).GetProperty("id").GetGuid();
        return (product.GetProperty("id").GetGuid(), Find("S"), Find("M"));
    }

    private async Task<HttpResponseMessage> PostWebhook(string body, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paystack") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("x-paystack-signature", signature);
        return await api.CreateClient().SendAsync(request, Ct);
    }

    private static string Sign(string body) =>
        Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(ApiFixture.PaystackSecret), Encoding.UTF8.GetBytes(body)));

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
