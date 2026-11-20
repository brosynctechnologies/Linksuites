using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Orders;
using Saas.Domain.Orders;

namespace Saas.IntegrationTests;

/// <summary>Wave 4a items 1-3: stock tracking, staff seats and online order payments (docs/superpowers/specs/2026-11-17-phase2-modules-design.md).</summary>
[Collection(nameof(ApiCollection))]
public sealed class OrderWave4Tests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string StoreKey = "sk_test_orderlink_store_key_0001";

    // ---------------- Inventory ----------------

    [Fact]
    public async Task Free_plan_has_no_stock_tracking()
    {
        var s = await SellerAsync("FreeStock", pro: false);

        var create = await s.Client.PostAsJsonAsync("/api/products", new { name = "Rice", priceMinor = 1_000, stockQty = 5 }, Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, create.StatusCode);
        Assert.Equal("plan_limit", (await Json(create)).GetProperty("code").GetString());

        // A count saved while the store was on Pro is ignored on Free: the item stays on sale and nothing is taken.
        var id = await ProductAsync(s, "Beans", 1_000);
        await api.WithDbAsync(db => db.Products.IgnoreQueryFilters().Where(p => p.Id == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.StockQty, 0), Ct));
        var product = (await Json(await api.CreateClient().GetAsync($"/api/public/stores/{s.Slug}/products", Ct))).EnumerateArray().Single();
        Assert.True(product.GetProperty("inStock").GetBoolean());
        Assert.Equal(JsonValueKind.Null, product.GetProperty("stockLeft").ValueKind);
        await PlaceAsync(s.Slug, id, 2);
        Assert.Equal(0, await StockAsync(id));

        var inventory = await Json(await s.Client.GetAsync("/api/inventory", Ct));
        Assert.False(inventory.GetProperty("enabled").GetBoolean());
        var adjust = await s.Client.PostAsJsonAsync("/api/inventory/adjustments", new { productId = id, delta = 3, reason = "Restock" }, Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, adjust.StatusCode);
    }

    [Fact]
    public async Task Variant_stock_is_taken_by_an_order_shown_on_the_storefront_and_given_back_on_cancel()
    {
        var s = await SellerAsync("Variants");
        var dress = await Json(await s.Client.PostAsJsonAsync("/api/products", new
        {
            name = "Dress", priceMinor = 1_000,
            options = new[] { new { name = "Size", values = new[] { "S", "M" } } },
            variants = new[] { new { optionValues = new[] { "S" }, inStock = true, stockQty = (int?)3 }, new { optionValues = new[] { "M" }, inStock = true, stockQty = (int?)1 } },
        }, Ct));
        var productId = dress.GetProperty("id").GetGuid();
        var variants = dress.GetProperty("variants").EnumerateArray().ToList();
        Guid V(string size) => variants.Single(v => v.GetProperty("optionValues")[0].GetString() == size).GetProperty("id").GetGuid();
        Assert.Equal(3, variants.Single(v => v.GetProperty("id").GetGuid() == V("S")).GetProperty("stockQty").GetInt32());

        var code = await PlaceAsync(s.Slug, productId, 1, V("M"));
        Assert.Equal(0, await VariantStockAsync(V("M")));

        var shown = (await Json(await api.CreateClient().GetAsync($"/api/public/stores/{s.Slug}/products", Ct))).EnumerateArray().Single();
        Assert.True(shown.GetProperty("inStock").GetBoolean());
        var publicVariants = shown.GetProperty("variants").EnumerateArray().ToList();
        var small = publicVariants.Single(v => v.GetProperty("id").GetGuid() == V("S"));
        var medium = publicVariants.Single(v => v.GetProperty("id").GetGuid() == V("M"));
        Assert.Equal(3, small.GetProperty("stockLeft").GetInt32()); // at or under the default threshold of 5
        Assert.False(medium.GetProperty("inStock").GetBoolean());   // sold out

        var soldOut = await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{s.Slug}/orders", OrderBody(productId, 1, V("M")), Ct);
        Assert.Equal(HttpStatusCode.Conflict, soldOut.StatusCode);
        Assert.Equal("out_of_stock", (await Json(soldOut)).GetProperty("code").GetString());

        var orderId = await OrderIdAsync(s, code);
        (await s.Client.PatchAsJsonAsync($"/api/orders/{orderId}/status", new { status = "Cancelled" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(1, await VariantStockAsync(V("M")));

        var log = (await Json(await s.Client.GetAsync($"/api/inventory/adjustments?productId={productId}", Ct))).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(log, a => a.GetProperty("kind").GetString() == "Sale" && a.GetProperty("delta").GetInt32() == -1 && a.GetProperty("variantTitle").GetString() == "M");
        Assert.Contains(log, a => a.GetProperty("kind").GetString() == "Cancellation" && a.GetProperty("delta").GetInt32() == 1 && a.GetProperty("quantityAfter").GetInt32() == 1);
        Assert.Contains(log, a => a.GetProperty("kind").GetString() == "Count" && a.GetProperty("quantityAfter").GetInt32() == 3);
    }

    [Fact]
    public async Task Concurrent_orders_never_oversell()
    {
        var s = await SellerAsync("Rush");
        var id = await ProductAsync(s, "Last cakes", 1_000, stock: 5);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            api.CreateClient().PostAsJsonAsync($"/api/public/stores/{s.Slug}/orders", OrderBody(id, 1), Ct)));

        Assert.Equal(5, attempts.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(attempts.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(0, await StockAsync(id));
        var sales = await api.WithDbAsync(db => db.StockAdjustments.IgnoreQueryFilters().CountAsync(a => a.ProductId == id && a.Kind == StockChangeKind.Sale, Ct));
        Assert.Equal(5, sales);
    }

    [Fact]
    public async Task Low_stock_alert_goes_to_the_owner_once_per_crossing()
    {
        var s = await SellerAsync("Alerts");
        var id = await ProductAsync(s, "Chapman", 1_000, stock: 7);

        await PlaceAsync(s.Slug, id, 3); // 4 left: crosses the threshold of 5
        await PlaceAsync(s.Slug, id, 1); // 3 left: same dip, no second alert
        Assert.Equal(1, await AlertCountAsync(s));
        Assert.Equal(1, await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .CountAsync(n => n.TenantId == s.TenantId && n.Kind == StockLedger.LowStockKind && n.Link == "/app/inventory?filter=low", Ct)));

        var restock = await s.Client.PostAsJsonAsync("/api/inventory/adjustments", new { productId = id, delta = 10, reason = "New stock arrived" }, Ct);
        Assert.Equal(HttpStatusCode.OK, restock.StatusCode);
        Assert.Equal(13, (await Json(restock)).GetProperty("quantityAfter").GetInt32());
        await PlaceAsync(s.Slug, id, 9); // 4 left again: a new crossing
        Assert.Equal(2, await AlertCountAsync(s));

        var low = await Json(await s.Client.GetAsync("/api/inventory?filter=low", Ct));
        var item = Assert.Single(low.GetProperty("items").EnumerateArray());
        Assert.Equal("Low", item.GetProperty("status").GetString());
        Assert.Equal(4, item.GetProperty("stockQty").GetInt32());
    }

    [Fact]
    public async Task Manual_adjustments_are_logged_and_checked()
    {
        var s = await SellerAsync("Adjust");
        var id = await ProductAsync(s, "Soap", 500, stock: 2);
        var untracked = await ProductAsync(s, "Bag", 500);

        var below = await s.Client.PostAsJsonAsync("/api/inventory/adjustments", new { productId = id, delta = -3, reason = "Damaged" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, below.StatusCode);
        Assert.Equal("not_enough_stock", (await Json(below)).GetProperty("code").GetString());
        var notTracked = await s.Client.PostAsJsonAsync("/api/inventory/adjustments", new { productId = untracked, delta = 1, reason = "Found one" }, Ct);
        Assert.Equal("not_tracked", (await Json(notTracked)).GetProperty("code").GetString());
        var noReason = await s.Client.PostAsJsonAsync("/api/inventory/adjustments", new { productId = id, delta = 1, reason = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var ok = await Json(await s.Client.PostAsJsonAsync("/api/inventory/adjustments", new { productId = id, delta = -1, reason = "Damaged" }, Ct));
        Assert.Equal(1, ok.GetProperty("quantityAfter").GetInt32());
        Assert.Equal("Adjustment", ok.GetProperty("kind").GetString());
        Assert.Equal("Damaged", ok.GetProperty("reason").GetString());
        Assert.Equal("Adjust", ok.GetProperty("byName").GetString());

        var threshold = await s.Client.PutAsJsonAsync("/api/inventory/settings", new { lowStockThreshold = 1 }, Ct);
        Assert.Equal(1, (await Json(threshold)).GetProperty("lowStockThreshold").GetInt32());

        // Another store's product: 404, and nothing changes.
        var other = await SellerAsync("Other");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync("/api/inventory/adjustments", new { productId = id, delta = 5, reason = "x" }, Ct)).StatusCode);
        Assert.Empty((await Json(await other.Client.GetAsync($"/api/inventory/adjustments?productId={id}", Ct))).GetProperty("items").EnumerateArray());
        Assert.Equal(1, await StockAsync(id));
    }

    [Fact]
    public async Task Plan_data_step_adds_the_new_limits_to_existing_orderlink_plans()
    {
        await api.WithDbAsync(async db =>
        {
            var pro = await db.Plans.FirstAsync(p => p.Product == Products.WhatsAppOrders && p.Code == "pro", Ct);
            pro.Limits.Remove(LimitKeys.Inventory);
            pro.Limits.Remove(LimitKeys.TeamSeats);
            pro.Features.Remove(OrderPlanFeatures.Stock);
            db.Entry(pro).Property(p => p.Limits).IsModified = true;
            return await db.SaveChangesAsync(Ct);
        });
        await api.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(Saas.Infrastructure.Persistence.Migrations.OrderLinkWave4.PlansSql, Ct));
        await api.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(Saas.Infrastructure.Persistence.Migrations.OrderLinkWave4.PlansSql, Ct)); // twice

        var plans = await api.WithDbAsync(db => db.Plans.AsNoTracking().Where(p => p.Product == Products.WhatsAppOrders).ToListAsync(Ct));
        var proPlan = plans.Single(p => p.Code == "pro");
        var free = plans.Single(p => p.Code == "free");
        Assert.Equal(1, proPlan.Limit(LimitKeys.Inventory));
        Assert.Equal(3, proPlan.Limit(LimitKeys.TeamSeats));
        Assert.Equal(1, proPlan.Limit(LimitKeys.OnlinePayments));
        Assert.Single(proPlan.Features, f => f == OrderPlanFeatures.Stock);
        Assert.Equal(0, free.Limit(LimitKeys.Inventory));
        Assert.Equal(1, free.Limit(LimitKeys.TeamSeats));
    }

    // ---------------- Staff seats and roles ----------------

    [Fact]
    public async Task Pro_store_invites_admin_and_staff_up_to_three_seats()
    {
        var free = await SellerAsync("SoloSeat", pro: false);
        var refused = await free.Client.PostAsJsonAsync("/api/team/invites", new { email = $"a-{Guid.NewGuid():N}@example.com", role = "Staff" }, Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, refused.StatusCode);

        var s = await SellerAsync("Seats");
        var accountant = await s.Client.PostAsJsonAsync("/api/team/invites", new { email = $"acc-{Guid.NewGuid():N}@example.com", role = "Accountant" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, accountant.StatusCode);
        var (_, staffMembership) = await MemberAsync(s, "Staff");
        await MemberAsync(s, "Admin");
        var fourth = await s.Client.PostAsJsonAsync("/api/team/invites", new { email = $"x-{Guid.NewGuid():N}@example.com", role = "Staff" }, Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, fourth.StatusCode); // owner + 2 = 3 seats

        var toAccountant = await s.Client.PatchAsJsonAsync($"/api/team/members/{staffMembership}", new { role = "Accountant" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, toAccountant.StatusCode);
        var team = await Json(await s.Client.GetAsync("/api/team", Ct));
        Assert.Equal(3, team.GetProperty("seats").GetProperty("used").GetInt32());
        Assert.Equal(3, team.GetProperty("seats").GetProperty("limit").GetInt64());
    }

    /// <summary>"METHOD pattern" → the lowest role allowed (Staff &lt; Admin &lt; Owner). A new OrderLink route must be added here.</summary>
    private static readonly Dictionary<string, string> RoleTable = new()
    {
        ["GET /api/store/"] = "Staff",
        ["PUT /api/store/"] = "Admin",
        ["POST /api/store/logo"] = "Admin",
        ["POST /api/store/cover"] = "Admin",
        ["GET /api/store/domain/"] = "Admin",
        ["PUT /api/store/domain/"] = "Owner",
        ["DELETE /api/store/domain/"] = "Owner",
        ["POST /api/store/domain/check"] = "Admin",
        ["GET /api/store/payments/"] = "Admin",
        ["PUT /api/store/payments/"] = "Owner",
        ["DELETE /api/store/payments/"] = "Owner",
        ["GET /api/categories/"] = "Staff",
        ["POST /api/categories/"] = "Staff",
        ["PUT /api/categories/{id:guid}"] = "Staff",
        ["DELETE /api/categories/{id:guid}"] = "Staff",
        ["GET /api/products/"] = "Staff",
        ["GET /api/products/{id:guid}"] = "Staff",
        ["POST /api/products/"] = "Staff",
        ["PUT /api/products/{id:guid}"] = "Staff",
        ["DELETE /api/products/{id:guid}"] = "Staff",
        ["POST /api/products/{id:guid}/images"] = "Staff",
        ["DELETE /api/products/{id:guid}/images/{imageId:guid}"] = "Staff",
        ["GET /api/delivery-zones/"] = "Admin",
        ["POST /api/delivery-zones/"] = "Admin",
        ["PUT /api/delivery-zones/{id:guid}"] = "Admin",
        ["DELETE /api/delivery-zones/{id:guid}"] = "Admin",
        ["GET /api/discount-codes/"] = "Admin",
        ["POST /api/discount-codes/"] = "Admin",
        ["PUT /api/discount-codes/{id:guid}"] = "Admin",
        ["DELETE /api/discount-codes/{id:guid}"] = "Admin",
        ["GET /api/orders/"] = "Staff",
        ["GET /api/orders/{id:guid}"] = "Staff",
        ["PATCH /api/orders/{id:guid}/status"] = "Staff",
        ["PATCH /api/orders/{id:guid}/payment"] = "Staff",
        ["POST /api/orders/{id:guid}/payments/{reference}/verify"] = "Staff",
        ["GET /api/dashboard"] = "Staff",
        ["GET /api/inventory/"] = "Staff",
        ["PUT /api/inventory/settings"] = "Admin",
        ["POST /api/inventory/adjustments"] = "Staff",
        ["GET /api/inventory/adjustments"] = "Staff",
    };

    private static readonly string[] Prefixes =
        ["/api/store", "/api/categories", "/api/products", "/api/delivery-zones", "/api/discount-codes", "/api/orders", "/api/dashboard", "/api/inventory"];

    private List<(string Method, string Pattern)> OrderRoutes()
    {
        _ = api.CreateClient();
        return api.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } p && Prefixes.Any(x => p == x || p.StartsWith(x + "/", StringComparison.Ordinal)))
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (m, e.RoutePattern.RawText!)))
            .Distinct().ToList();
    }

    [Fact]
    public void Every_orderlink_route_is_in_the_role_table()
    {
        var routes = OrderRoutes().Select(r => $"{r.Method} {r.Pattern}").ToList();
        var missing = routes.Where(k => !RoleTable.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Decide who may use: " + string.Join(", ", missing));
        var stale = RoleTable.Keys.Where(k => !routes.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "No such route: " + string.Join(", ", stale));
    }

    [Fact]
    public async Task Each_role_reaches_exactly_its_orderlink_routes()
    {
        var s = await SellerAsync("Roles");
        var (staff, _) = await MemberAsync(s, "Staff");
        var (admin, _) = await MemberAsync(s, "Admin");
        var clients = new[] { ("Owner", s.Client), ("Admin", admin), ("Staff", staff) };
        var rank = new Dictionary<string, int> { ["Staff"] = 0, ["Admin"] = 1, ["Owner"] = 2 };
        var failures = new List<string>();
        foreach (var (method, pattern) in OrderRoutes())
        {
            // A random id finds nothing (404) or a body fails validation (400): either way the role check passed.
            var path = System.Text.RegularExpressions.Regex.Replace(pattern, @"\{[^}]+\}", m => m.Value.Contains("guid") ? Guid.NewGuid().ToString() : "ol_x");
            foreach (var (role, client) in clients)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                // Upload routes only match multipart requests (415 before any role check otherwise).
                if (pattern.EndsWith("/logo") || pattern.EndsWith("/cover") || pattern.EndsWith("/images"))
                    request.Content = new MultipartFormDataContent { { new ByteArrayContent([1]), "file", "x.png" } };
                else if (method is "POST" or "PUT" or "PATCH") request.Content = JsonContent.Create(new { });
                if (method == "DELETE" && pattern.StartsWith("/api/store/")) continue; // would remove the owner's real settings; covered by the 403 checks below
                var status = (await client.SendAsync(request, Ct)).StatusCode;
                var allowed = rank[role] >= rank[RoleTable[$"{method} {pattern}"]];
                if (allowed && (status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized || (int)status >= 500))
                    failures.Add($"{role} {method} {pattern} → {(int)status}, expected access");
                if (!allowed && status != HttpStatusCode.Forbidden)
                    failures.Add($"{role} {method} {pattern} → {(int)status}, expected 403");
            }
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync("/api/store/payments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync("/api/store/domain", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct)).StatusCode);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // ---------------- Online payments ----------------

    [Fact]
    public async Task Free_store_cannot_take_online_payments()
    {
        var s = await SellerAsync("NoPay", pro: false);
        var key = await s.Client.PutAsJsonAsync("/api/store/payments", new { secretKey = StoreKey }, Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, key.StatusCode);
        var id = await ProductAsync(s, "Rice", 1_000);
        var code = await PlaceAsync(s.Slug, id, 1);
        Assert.False((await Json(await api.CreateClient().GetAsync($"/api/public/orders/{code}", Ct))).GetProperty("canPayOnline").GetBoolean());
        var pay = await api.CreateClient().PostAsJsonAsync($"/api/public/orders/{code}/pay", new { }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, pay.StatusCode);
        Assert.Equal("online_payments_unavailable", (await Json(pay)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Customer_pays_online_and_the_webhook_marks_the_order_paid_once()
    {
        var s = await PayingSellerAsync("PayFlow");
        var settings = await Json(await s.Client.GetAsync("/api/store/payments", Ct));
        Assert.True(settings.GetProperty("connected").GetBoolean());
        Assert.EndsWith($"/api/webhooks/paystack/orders/{s.TenantId}", settings.GetProperty("webhookUrl").GetString());

        var id = await ProductAsync(s, "Rice", 250_000);
        var placed = await Json(await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{s.Slug}/orders", OrderBody(id, 2), Ct));
        var code = placed.GetProperty("code").GetString()!;
        Assert.EndsWith($"/o/{code}?pay=1", placed.GetProperty("payUrl").GetString());
        Assert.Contains($"Pay online: ", Uri.UnescapeDataString(placed.GetProperty("whatsappUrl").GetString()!));
        Assert.True((await Json(await api.CreateClient().GetAsync($"/api/public/orders/{code}", Ct))).GetProperty("canPayOnline").GetBoolean());

        var reference = await StartPayAsync(code);
        var init = api.Merchant.Initialized.Single(i => i.Reference == reference);
        Assert.Equal(500_000, init.AmountMinor);
        Assert.Contains($"/o/{code}?reference=", init.CallbackUrl);

        var body = ChargeBody(reference, 500_000);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(s.TenantId, body, Sign(body, "sk_test_someone_elses_key_000"))).StatusCode);
        Assert.Equal("Unpaid", (await PublicOrderAsync(code)).GetProperty("paymentStatus").GetString());

        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(s.TenantId, body, Sign(body, StoreKey))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(s.TenantId, body, Sign(body, StoreKey))).StatusCode); // Paystack retries
        api.Merchant.Charges[reference] = new VerifiedCharge(reference, 500_000, "NGN", DateTimeOffset.UtcNow);
        var afterReturn = await Json(await api.CreateClient().PostAsJsonAsync($"/api/public/orders/{code}/verify", new { reference }, Ct));

        Assert.Equal("Paid", afterReturn.GetProperty("paymentStatus").GetString());
        Assert.Equal("Paid", afterReturn.GetProperty("status").GetString());
        Assert.False(afterReturn.GetProperty("canPayOnline").GetBoolean());
        var orderId = await OrderIdAsync(s, code);
        var order = await Json(await s.Client.GetAsync($"/api/orders/{orderId}", Ct));
        Assert.Single(order.GetProperty("history").EnumerateArray(), h => h.GetProperty("note").GetString()?.StartsWith("Paid online") == true);
        Assert.Equal("Succeeded", order.GetProperty("payments")[0].GetProperty("status").GetString());
        Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TenantId == s.TenantId && n.TemplateKey == "order.payment", Ct)));

        var again = await api.CreateClient().PostAsJsonAsync($"/api/public/orders/{code}/pay", new { }, Ct);
        Assert.Equal("order_paid", (await Json(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_charge_for_a_different_amount_does_not_mark_the_order_paid()
    {
        var s = await PayingSellerAsync("Mismatch");
        var id = await ProductAsync(s, "Rice", 100_000);
        var code = await PlaceAsync(s.Slug, id, 1);
        var reference = await StartPayAsync(code);
        api.Merchant.Charges[reference] = new VerifiedCharge(reference, 1_000, "NGN", DateTimeOffset.UtcNow);

        var result = await Json(await api.CreateClient().PostAsJsonAsync($"/api/public/orders/{code}/verify", new { reference }, Ct));

        Assert.Equal("Unpaid", result.GetProperty("paymentStatus").GetString());
        var order = await Json(await s.Client.GetAsync($"/api/orders/{await OrderIdAsync(s, code)}", Ct));
        Assert.Equal("Mismatch", order.GetProperty("payments")[0].GetProperty("status").GetString());
        var notice = await api.WithDbAsync(db => db.Notifications.SingleAsync(n => n.TenantId == s.TenantId && n.TemplateKey == "order.payment", Ct));
        Assert.Equal("Online payment needs checking", notice.Payload["title"]);
    }

    [Fact]
    public async Task Background_recheck_confirms_a_stranded_checkout_and_abandons_a_dead_one()
    {
        var s = await PayingSellerAsync("Recheck");
        var id = await ProductAsync(s, "Rice", 100_000);
        var paidCode = await PlaceAsync(s.Slug, id, 1);
        var deadCode = await PlaceAsync(s.Slug, id, 1);
        var paidRef = await StartPayAsync(paidCode);
        var deadRef = await StartPayAsync(deadCode);
        api.Merchant.Charges[paidRef] = new VerifiedCharge(paidRef, 100_000, "NGN", DateTimeOffset.UtcNow);
        await api.WithDbAsync(db => db.OrderPayments.IgnoreQueryFilters().Where(p => p.Reference == paidRef)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.CreatedAt, DateTimeOffset.UtcNow.AddMinutes(-20)), Ct));
        await api.WithDbAsync(db => db.OrderPayments.IgnoreQueryFilters().Where(p => p.Reference == deadRef)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.CreatedAt, DateTimeOffset.UtcNow.AddHours(-30)), Ct));

        await api.Services.GetRequiredService<OrderJobs>().RunOnceAsync(DateTimeOffset.UtcNow, Ct);

        Assert.Equal("Paid", (await PublicOrderAsync(paidCode)).GetProperty("paymentStatus").GetString());
        Assert.Equal(OrderPaymentState.Abandoned, await api.WithDbAsync(db => db.OrderPayments.IgnoreQueryFilters().Where(p => p.Reference == deadRef).Select(p => p.Status).SingleAsync(Ct)));

        // A late webhook for the abandoned checkout still applies (Abandoned is soft).
        var body = ChargeBody(deadRef, 100_000);
        (await WebhookAsync(s.TenantId, body, Sign(body, StoreKey))).EnsureSuccessStatusCode();
        Assert.Equal("Paid", (await PublicOrderAsync(deadCode)).GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Payments_stay_inside_their_own_store()
    {
        var a = await PayingSellerAsync("StoreA");
        var b = await PayingSellerAsync("StoreB");
        var codeA = await PlaceAsync(a.Slug, await ProductAsync(a, "Rice", 100_000), 1);
        var codeB = await PlaceAsync(b.Slug, await ProductAsync(b, "Beans", 100_000), 1);
        var refA = await StartPayAsync(codeA);
        api.Merchant.Charges[refA] = new VerifiedCharge(refA, 100_000, "NGN", DateTimeOffset.UtcNow);

        // Store A's reference checked against store B's order, by the customer or by B's staff: not found, nothing applied.
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().PostAsJsonAsync($"/api/public/orders/{codeB}/verify", new { reference = refA }, Ct)).StatusCode);
        var orderA = await OrderIdAsync(a, codeA);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PostAsync($"/api/orders/{orderA}/payments/{refA}/verify", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.GetAsync($"/api/orders/{orderA}", Ct)).StatusCode);
        // A webhook for A's reference on B's route, signed with B's key (same key in this test): acknowledged, ignored.
        var body = ChargeBody(refA, 100_000);
        (await WebhookAsync(b.TenantId, body, Sign(body, StoreKey))).EnsureSuccessStatusCode();
        Assert.Equal("Unpaid", (await PublicOrderAsync(codeA)).GetProperty("paymentStatus").GetString());

        // A's own staff check applies it.
        var checkedOrder = await Json(await a.Client.PostAsync($"/api/orders/{orderA}/payments/{refA}/verify", null, Ct));
        Assert.Equal("Paid", checkedOrder.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Delivery_fee_to_be_agreed_cannot_be_paid_online()
    {
        var s = await PayingSellerAsync("NoZones");
        var id = await ProductAsync(s, "Rice", 100_000);
        var response = await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{s.Slug}/orders", new
        {
            items = new[] { new { productId = id, quantity = 1 } }, fulfilment = "Delivery", address = "1 Main St", customerName = "Bola", phone = "0803 000 0000",
        }, Ct);
        var placed = await Json(response);
        Assert.Equal(JsonValueKind.Null, placed.GetProperty("payUrl").ValueKind);
        var code = placed.GetProperty("code").GetString()!;
        Assert.False((await PublicOrderAsync(code)).GetProperty("canPayOnline").GetBoolean());
        var pay = await api.CreateClient().PostAsJsonAsync($"/api/public/orders/{code}/pay", new { }, Ct);
        Assert.Equal("delivery_fee_not_agreed", (await Json(pay)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Changing_the_key_while_customers_are_paying_needs_force()
    {
        var s = await PayingSellerAsync("KeySwap");
        var code = await PlaceAsync(s.Slug, await ProductAsync(s, "Rice", 100_000), 1);
        await StartPayAsync(code);

        var busy = await s.Client.PutAsJsonAsync("/api/store/payments", new { secretKey = "sk_test_orderlink_new_key_00002" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Equal("checkouts_open", (await Json(busy)).GetProperty("code").GetString());
        var forced = await s.Client.PutAsJsonAsync("/api/store/payments", new { secretKey = "sk_test_orderlink_new_key_00002", force = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        var bad = await s.Client.PutAsJsonAsync("/api/store/payments", new { secretKey = "pk_live_nope" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // ---------------- Helpers ----------------

    private sealed record Seller(HttpClient Client, Guid TenantId, string Slug);

    private async Task<Seller> SellerAsync(string name, bool pro = true)
    {
        var (client, auth) = await api.SignupAsync(name);
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        if (pro) await api.MakeProAsync(tenantId, Products.WhatsAppOrders);
        return new Seller(client, tenantId, auth.GetProperty("tenant").GetProperty("slug").GetString()!);
    }

    private async Task<Seller> PayingSellerAsync(string name)
    {
        var s = await SellerAsync(name);
        (await s.Client.PutAsJsonAsync("/api/store/payments", new { secretKey = StoreKey }, Ct)).EnsureSuccessStatusCode();
        return s;
    }

    /// <summary>Invites a team member with <paramref name="role"/>, accepts as them and returns their client and membership id.</summary>
    private async Task<(HttpClient Client, Guid MembershipId)> MemberAsync(Seller s, string role)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var invite = await s.Client.PostAsJsonAsync("/api/team/invites", new { email, role }, Ct);
        if (!invite.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)invite.StatusCode} {await invite.Content.ReadAsStringAsync(Ct)}");
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == email).FirstAsync(Ct))).Payload["link"];
        var accepted = await api.CreateClient().PostAsJsonAsync($"/api/auth/invites/{link[(link.LastIndexOf('/') + 1)..]}/accept",
            new { fullName = $"{role} Member", password = "password123" }, Ct);
        if (!accepted.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)accepted.StatusCode} {await accepted.Content.ReadAsStringAsync(Ct)}");
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(accepted)).GetProperty("accessToken").GetString());
        var membership = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == s.TenantId && m.User!.Email == email).Select(m => m.Id).FirstAsync(Ct));
        return (client, membership);
    }

    private async Task<Guid> ProductAsync(Seller s, string name, long price, int? stock = null)
    {
        var response = await s.Client.PostAsJsonAsync("/api/products", new { name, priceMinor = price, stockQty = stock }, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static object OrderBody(Guid productId, int quantity, Guid? variantId = null) => new
    {
        items = new[] { new { productId, variantId, quantity } }, fulfilment = "Pickup", customerName = "Bola", phone = "0803 000 0000",
    };

    private async Task<string> PlaceAsync(string slug, Guid productId, int quantity, Guid? variantId = null)
    {
        var response = await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/orders", OrderBody(productId, quantity, variantId), Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await Json(response)).GetProperty("code").GetString()!;
    }

    private Task<Guid> OrderIdAsync(Seller s, string code) =>
        api.WithDbAsync(db => db.Orders.IgnoreQueryFilters().Where(o => o.TenantId == s.TenantId && o.Code == code).Select(o => o.Id).SingleAsync(Ct));

    private async Task<string> StartPayAsync(string code)
    {
        var response = await api.CreateClient().PostAsJsonAsync($"/api/public/orders/{code}/pay", new { email = "bola@example.com" }, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        var body = await Json(response);
        Assert.StartsWith("https://checkout.example/", body.GetProperty("authorizationUrl").GetString());
        return body.GetProperty("reference").GetString()!;
    }

    private async Task<JsonElement> PublicOrderAsync(string code) => await Json(await api.CreateClient().GetAsync($"/api/public/orders/{code}", Ct));

    private Task<int> AlertCountAsync(Seller s) =>
        api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TenantId == s.TenantId && n.TemplateKey == StockLedger.LowStockKind, Ct));

    private Task<int?> StockAsync(Guid productId) =>
        api.WithDbAsync(db => db.Products.IgnoreQueryFilters().Where(p => p.Id == productId).Select(p => p.StockQty).SingleAsync(Ct));

    private Task<int?> VariantStockAsync(Guid variantId) =>
        api.WithDbAsync(db => db.ProductVariants.IgnoreQueryFilters().Where(v => v.Id == variantId).Select(v => v.StockQty).SingleAsync(Ct));

    private static string ChargeBody(string reference, long amount) =>
        $$$"""{"event":"charge.success","data":{"status":"success","reference":"{{{reference}}}","amount":{{{amount}}},"currency":"NGN","paid_at":"2026-11-20T10:00:00Z"}}""";

    private Task<HttpResponseMessage> WebhookAsync(Guid tenantId, string body, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/paystack/orders/{tenantId}") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("x-paystack-signature", signature);
        return api.CreateClient().SendAsync(request, Ct);
    }

    private static string Sign(string body, string key) =>
        Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body)));

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
