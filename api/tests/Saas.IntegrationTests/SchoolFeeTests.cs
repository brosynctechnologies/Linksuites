using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink fees: fee items, the fee structure, generating invoices, optional items, discounts and instalments.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolFeeTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == r.StatusCode, $"expected {(int)expected}, got {(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid LevelId, Guid TermId, Guid Tuition, Guid Uniform, Guid Bus);

    private async Task<Guid> ItemAsync(SchoolKit.School school, string name, bool optional) =>
        (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name, isOptional = optional }, Ct))).GetProperty("id").GetGuid();

    private async Task<Setup> SetupAsync(string name, int students = 2)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        for (var i = 0; i < students; i++) await kit.StudentAsync(school, classId, $"Pupil{i}");
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var tuition = await ItemAsync(school, "Tuition", false);
        var uniform = await ItemAsync(school, "Uniform", false);
        var bus = await ItemAsync(school, "School bus", true);
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}", new
        {
            lines = new[]
            {
                new { feeItemId = tuition, amountMinor = 10_000_000L },
                new { feeItemId = uniform, amountMinor = 2_000_000L },
                new { feeItemId = bus, amountMinor = 1_500_000L },
            },
        }, Ct));
        return new Setup(school, classId, levelId, termId, tuition, uniform, bus);
    }

    private static async Task<JsonElement> GenerateAsync(Setup s) =>
        await Json(await s.School.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId = s.TermId }, Ct));

    private static async Task<JsonElement> ListAsync(Setup s, string query = "") =>
        await Json(await s.School.Owner.GetAsync($"/api/school/invoices?termId={s.TermId}{query}", Ct));

    private static async Task<JsonElement> DetailAsync(Setup s, Guid id) =>
        await Json(await s.School.Owner.GetAsync($"/api/school/invoices/{id}", Ct));

    [Fact]
    public async Task Generating_invoices_uses_the_class_structure()
    {
        var s = await SetupAsync("Fee Gen Academy");
        var result = await GenerateAsync(s);
        Assert.Equal(2, result.GetProperty("created").GetInt32());
        Assert.Equal(0, result.GetProperty("refreshed").GetInt32());
        Assert.Empty(result.GetProperty("needsReview").EnumerateArray());

        var invoices = (await ListAsync(s)).EnumerateArray().ToList();
        Assert.Equal(2, invoices.Count);
        var year = await api.WithDbAsync(db => db.AcademicSessions.IgnoreQueryFilters().Where(x => x.TenantId == s.School.TenantId && x.IsCurrent).Select(x => x.StartDate.Year).FirstAsync(Ct));
        foreach (var invoice in invoices)
        {
            // Required items only; the optional bus is not included.
            Assert.Equal(12_000_000L, invoice.GetProperty("total").GetInt64());
            Assert.Equal(0L, invoice.GetProperty("paid").GetInt64());
            Assert.Equal(12_000_000L, invoice.GetProperty("balance").GetInt64());
            Assert.Equal("Unpaid", invoice.GetProperty("status").GetString());
            Assert.StartsWith($"INV-{year}-", invoice.GetProperty("number").GetString());
        }
        Assert.Equal(2, invoices.Select(i => i.GetProperty("number").GetString()).Distinct().Count());
        Assert.Contains(invoices, i => i.GetProperty("number").GetString() == $"INV-{year}-00001");

        var term = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.Id == s.TermId).Select(t => t.StartDate).FirstAsync(Ct));
        Assert.Equal(term.AddDays(14).ToString("yyyy-MM-dd"), invoices[0].GetProperty("dueDate").GetString());

        var detail = await DetailAsync(s, invoices[0].GetProperty("id").GetGuid());
        Assert.Equal(2, detail.GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task Generating_twice_refreshes_unpaid_and_leaves_paid_invoices_for_review()
    {
        var s = await SetupAsync("Fee Refresh Academy");
        await GenerateAsync(s);
        var invoices = (await ListAsync(s)).EnumerateArray().ToList();
        var paidId = invoices[0].GetProperty("id").GetGuid();
        var unpaidId = invoices[1].GetProperty("id").GetGuid();
        await api.WithDbAsync(async db =>
        {
            var inv = await db.StudentInvoices.IgnoreQueryFilters().FirstAsync(i => i.Id == paidId, Ct);
            inv.Paid = 500_000; inv.Balance = inv.Total - 500_000; inv.Status = InvoiceStatus.PartlyPaid;
            await db.SaveChangesAsync(Ct);
            return 0;
        });

        // The structure changes: tuition goes up.
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/fee-structure/{s.TermId}/{s.LevelId}", new
        {
            lines = new[]
            {
                new { feeItemId = s.Tuition, amountMinor = 11_000_000L },
                new { feeItemId = s.Uniform, amountMinor = 2_000_000L },
                new { feeItemId = s.Bus, amountMinor = 1_500_000L },
            },
        }, Ct));

        var again = await GenerateAsync(s);
        Assert.Equal(0, again.GetProperty("created").GetInt32());
        Assert.Equal(1, again.GetProperty("refreshed").GetInt32());
        // Left alone because it has payments: listed apart, not flagged for review (recheck finding 3).
        Assert.Empty(again.GetProperty("needsReview").EnumerateArray());
        Assert.Equal([paidId], again.GetProperty("withPayments").EnumerateArray().Select(e => e.GetGuid()).ToArray());

        Assert.Equal(13_000_000L, (await DetailAsync(s, unpaidId)).GetProperty("total").GetInt64());
        var paid = await DetailAsync(s, paidId);
        Assert.Equal(12_000_000L, paid.GetProperty("total").GetInt64());
        Assert.Equal(500_000L, paid.GetProperty("paid").GetInt64());
        Assert.Equal(2, (await ListAsync(s)).GetArrayLength());
    }

    [Fact]
    public async Task Optional_items_and_percentage_discounts_apply()
    {
        var s = await SetupAsync("Fee Discount Academy", 1);
        await GenerateAsync(s);
        var id = (await ListAsync(s))[0].GetProperty("id").GetGuid();

        var withBus = await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/invoices/{id}/optional-items", new { feeItemIds = new[] { s.Bus } }, Ct));
        Assert.Equal(13_500_000L, withBus.GetProperty("total").GetInt64());

        var discounted = await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Percent", value = 10, reason = "Staff child" }, Ct));
        // 10% of tuition only (1,000,000), not of the bus or uniform.
        Assert.Equal(12_500_000L, discounted.GetProperty("total").GetInt64());
        Assert.Equal(12_500_000L, discounted.GetProperty("balance").GetInt64());
        var discount = discounted.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("amountMinor").GetInt64() < 0);
        Assert.Equal(-1_000_000L, discount.GetProperty("amountMinor").GetInt64());

        var fixedOff = await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Amount", value = 500_000, reason = "Scholarship" }, Ct));
        Assert.Equal(12_000_000L, fixedOff.GetProperty("total").GetInt64());

        var tooBig = await ProblemAsync(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Amount", value = 99_000_000, reason = "Mistake" }, Ct), HttpStatusCode.BadRequest);
        Assert.True(tooBig.GetProperty("errors").TryGetProperty("value", out _));

        var withoutBus = await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/invoices/{id}/optional-items", new { feeItemIds = Array.Empty<Guid>() }, Ct));
        Assert.Equal(10_500_000L, withoutBus.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Percentage_discount_needs_a_tuition_line()
    {
        var s = await SetupAsync("Fee No Tuition Academy", 1);
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/fee-structure/{s.TermId}/{s.LevelId}", new
        {
            lines = new[] { new { feeItemId = s.Uniform, amountMinor = 2_000_000L } },
        }, Ct));
        await GenerateAsync(s);
        var id = (await ListAsync(s))[0].GetProperty("id").GetGuid();
        var problem = await ProblemAsync(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Percent", value = 10, reason = "Staff child" }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains("Add a tuition fee before a percentage discount.", problem.GetRawText());
    }

    [Fact]
    public async Task Instalments_must_total_the_invoice()
    {
        var s = await SetupAsync("Fee Instalment Academy", 1);
        await GenerateAsync(s);
        var id = (await ListAsync(s))[0].GetProperty("id").GetGuid();
        var (start, end) = await api.WithDbAsync(async db =>
        {
            var session = await db.AcademicSessions.IgnoreQueryFilters().FirstAsync(x => x.TenantId == s.School.TenantId && x.IsCurrent, Ct);
            return (session.StartDate, session.EndDate);
        });

        var bad = await ProblemAsync(await s.School.Owner.PutAsJsonAsync($"/api/school/invoices/{id}/instalments", new[]
        {
            new { dueDate = start.AddDays(10), amountMinor = 5_000_000L },
            new { dueDate = start.AddDays(40), amountMinor = 5_000_000L },
        }, Ct), HttpStatusCode.BadRequest);
        Assert.True(bad.GetProperty("errors").TryGetProperty("instalments", out _));

        var outside = await ProblemAsync(await s.School.Owner.PutAsJsonAsync($"/api/school/invoices/{id}/instalments", new[]
        {
            new { dueDate = end.AddDays(30), amountMinor = 12_000_000L },
        }, Ct), HttpStatusCode.BadRequest);
        Assert.True(outside.GetProperty("errors").TryGetProperty("instalments", out _));

        var ok = await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/invoices/{id}/instalments", new[]
        {
            new { dueDate = start.AddDays(10), amountMinor = 7_000_000L },
            new { dueDate = start.AddDays(50), amountMinor = 5_000_000L },
        }, Ct));
        Assert.Equal(2, ok.GetProperty("instalments").GetArrayLength());

        var cleared = await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/invoices/{id}/instalments", Array.Empty<object>(), Ct));
        Assert.Equal(0, cleared.GetProperty("instalments").GetArrayLength());
    }

    [Fact]
    public async Task Teachers_cannot_see_fees()
    {
        var s = await SetupAsync("Fee Roles Academy", 1);
        var (teacher, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Teacher");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");

        var forbidden = await ProblemAsync(await teacher.GetAsync("/api/school/fee-items", Ct), HttpStatusCode.Forbidden);
        Assert.Equal("You need the Owner or Admin or Accountant role for this.", forbidden.GetProperty("detail").GetString());
        await ProblemAsync(await teacher.GetAsync($"/api/school/invoices?termId={s.TermId}", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await teacher.PostAsJsonAsync("/api/school/invoices/generate", new { termId = s.TermId }, Ct), HttpStatusCode.Forbidden);

        Assert.Equal(HttpStatusCode.OK, (await bursar.GetAsync("/api/school/fee-items", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bursar.PostAsJsonAsync("/api/school/invoices/generate", new { termId = s.TermId }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Fee_structure_copies_and_fee_items_in_use_cannot_be_deleted()
    {
        var s = await SetupAsync("Fee Copy Academy", 1);
        var other = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == s.School.TenantId && !t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var copied = await Json(await s.School.Owner.PostAsync($"/api/school/fee-structure/{other}/copy-from/{s.TermId}", null, Ct));
        Assert.Equal(3, copied.GetProperty("lines").GetArrayLength());
        await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/fee-items/{s.Tuition}", Ct), HttpStatusCode.Conflict);
        var spare = await ItemAsync(s.School, "Sports", false);
        Assert.Equal(HttpStatusCode.NoContent, (await s.School.Owner.DeleteAsync($"/api/school/fee-items/{spare}", Ct)).StatusCode);
    }

    // ---------- Refresh rules (fix round 1) ----------

    private static async Task SetStructureAsync(Setup s, long tuition, long uniform, long? bus = null)
    {
        var lines = new List<object>();
        if (tuition > 0) lines.Add(new { feeItemId = s.Tuition, amountMinor = tuition });
        if (uniform > 0) lines.Add(new { feeItemId = s.Uniform, amountMinor = uniform });
        if (bus is { } b) lines.Add(new { feeItemId = s.Bus, amountMinor = b });
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/fee-structure/{s.TermId}/{s.LevelId}", new { lines }, Ct));
    }

    private static async Task<Guid> FirstInvoiceAsync(Setup s) => (await ListAsync(s))[0].GetProperty("id").GetGuid();

    private static Guid[] Review(JsonElement result) => result.GetProperty("needsReview").EnumerateArray().Select(e => e.GetGuid()).ToArray();

    [Fact]
    public async Task Refresh_keeps_optional_items()
    {
        var s = await SetupAsync("Fee Keep Optional Academy", 1);
        await GenerateAsync(s);
        var id = await FirstInvoiceAsync(s);
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/invoices/{id}/optional-items", new { feeItemIds = new[] { s.Bus } }, Ct));
        await SetStructureAsync(s, 11_000_000, 2_000_000, 1_600_000);
        var again = await GenerateAsync(s);
        Assert.Equal(1, again.GetProperty("refreshed").GetInt32());
        var detail = await DetailAsync(s, id);
        Assert.Equal(3, detail.GetProperty("lines").GetArrayLength());
        Assert.Equal(14_600_000L, detail.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Refresh_recomputes_a_percentage_discount_from_the_current_tuition()
    {
        var s = await SetupAsync("Fee Percent Refresh Academy", 1);
        await GenerateAsync(s);
        var id = await FirstInvoiceAsync(s);
        var first = await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Percent", value = 10, reason = "Staff child" }, Ct));
        Assert.Equal(11_000_000L, first.GetProperty("total").GetInt64());

        await SetStructureAsync(s, 11_000_000, 2_000_000);
        var again = await GenerateAsync(s);
        Assert.Empty(Review(again));
        var detail = await DetailAsync(s, id);
        // 110,000 naira tuition: 10% is 11,000 naira off.
        Assert.Equal(-1_100_000L, detail.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("amountMinor").GetInt64() < 0).GetProperty("amountMinor").GetInt64());
        Assert.Equal(11_900_000L, detail.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task A_discount_that_no_longer_fits_is_dropped_and_listed_for_review()
    {
        var s = await SetupAsync("Fee Drop Discount Academy", 1);
        await GenerateAsync(s);
        var id = await FirstInvoiceAsync(s);
        await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Amount", value = 11_000_000, reason = "Scholarship" }, Ct));

        await SetStructureAsync(s, 0, 500_000);
        var again = await GenerateAsync(s);
        Assert.Equal([id], Review(again));
        var detail = await DetailAsync(s, id);
        Assert.Equal(500_000L, detail.GetProperty("total").GetInt64());
        Assert.DoesNotContain(detail.GetProperty("lines").EnumerateArray(), l => l.GetProperty("amountMinor").GetInt64() < 0);
    }

    [Fact]
    public async Task Refresh_clears_instalments_that_no_longer_total_and_lists_the_invoice()
    {
        var s = await SetupAsync("Fee Instalment Refresh Academy", 1);
        await GenerateAsync(s);
        var id = await FirstInvoiceAsync(s);
        var start = await api.WithDbAsync(db => db.AcademicSessions.IgnoreQueryFilters().Where(x => x.TenantId == s.School.TenantId && x.IsCurrent).Select(x => x.StartDate).FirstAsync(Ct));
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/invoices/{id}/instalments", new[]
        {
            new { dueDate = start.AddDays(10), amountMinor = 6_000_000L },
            new { dueDate = start.AddDays(50), amountMinor = 6_000_000L },
        }, Ct));

        await SetStructureAsync(s, 11_000_000, 2_000_000);
        var again = await GenerateAsync(s);
        Assert.Equal([id], Review(again));
        Assert.Equal(0, (await DetailAsync(s, id)).GetProperty("instalments").GetArrayLength());
    }

    [Fact]
    public async Task Refresh_lists_the_invoice_when_the_level_no_longer_has_a_structure()
    {
        var s = await SetupAsync("Fee Empty Structure Academy", 1);
        await GenerateAsync(s);
        var id = await FirstInvoiceAsync(s);
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/fee-structure/{s.TermId}/{s.LevelId}", new { lines = Array.Empty<object>() }, Ct));
        var again = await GenerateAsync(s);
        Assert.Equal([id], Review(again));
        Assert.Equal(0L, (await DetailAsync(s, id)).GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task A_cancelled_invoice_is_skipped_by_generate()
    {
        var s = await SetupAsync("Fee Cancelled Academy", 1);
        await GenerateAsync(s);
        var id = await FirstInvoiceAsync(s);
        await api.WithDbAsync(async db =>
        {
            var inv = await db.StudentInvoices.IgnoreQueryFilters().FirstAsync(i => i.Id == id, Ct);
            inv.Status = InvoiceStatus.Cancelled;
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        await SetStructureAsync(s, 11_000_000, 2_000_000);
        var again = await GenerateAsync(s);
        Assert.Equal(0, again.GetProperty("created").GetInt32());
        Assert.Equal(0, again.GetProperty("refreshed").GetInt32());
        Assert.Empty(Review(again));
        var detail = await DetailAsync(s, id);
        Assert.Equal(12_000_000L, detail.GetProperty("total").GetInt64());
        Assert.Equal("Cancelled", detail.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Percentage_discounts_round_half_away_from_zero_to_the_kobo()
    {
        var s = await SetupAsync("Fee Rounding Academy", 1);
        // Tuition 33,333.33 naira; 7.5% is 2,499.99975 naira = 249,999.975 kobo, which rounds to 250,000 kobo.
        await SetStructureAsync(s, 3_333_333, 2_000_000);
        await GenerateAsync(s);
        var id = await FirstInvoiceAsync(s);
        var result = await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Percent", value = 7.5, reason = "Sibling" }, Ct));
        Assert.Equal(-250_000L, result.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("amountMinor").GetInt64() < 0).GetProperty("amountMinor").GetInt64());
        Assert.Equal(3_333_333L + 2_000_000L - 250_000L, result.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Percentage_discounts_together_cannot_exceed_the_tuition()
    {
        var s = await SetupAsync("Fee Over Hundred Academy", 1);
        await GenerateAsync(s);
        var id = await FirstInvoiceAsync(s);
        await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Percent", value = 60, reason = "First" }, Ct));
        var problem = await ProblemAsync(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Percent", value = 50, reason = "Second" }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains("Discounts can't be more than the tuition.", problem.GetProperty("errors").GetProperty("value").GetRawText());
        var ok = await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Percent", value = 40, reason = "Third" }, Ct));
        Assert.Equal(2_000_000L, ok.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Two_generates_at_once_create_one_invoice_per_student_with_distinct_numbers()
    {
        var s = await SetupAsync("Fee Concurrent Academy", 4);
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => s.School.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId = s.TermId }, Ct)));
        foreach (var r in responses) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var invoices = (await ListAsync(s)).EnumerateArray().ToList();
        Assert.Equal(4, invoices.Count);
        Assert.Equal(4, invoices.Select(i => i.GetProperty("number").GetString()).Distinct().Count());
        Assert.Equal(4, invoices.Select(i => i.GetProperty("studentId").GetGuid()).Distinct().Count());
    }
}
