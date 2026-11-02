using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School.Imports.Importers;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>
/// Fee imports: fee items (<c>fee-items</c>), a level's fee structure for a term (<c>fee-structure</c>, a roster of items), payments
/// (<c>payments</c>, through the record-payment path) and expenses (<c>expenses</c>). The fee policy: proprietor, principal and bursar;
/// a teacher gets 403. Money cells are naira with up to two decimals, read into kobo exactly.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed partial class SchoolImportFeesTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    private const string PaymentHeader = "invoice_no,admission_no,amount,method,reference,paid_on";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static string Csv(string header, params string[] lines) => string.Join("\r\n", new[] { header }.Concat(lines)) + "\r\n";

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string kind, string csv, string query = "") =>
        client.PostAsync($"/api/school/imports/{kind}/preview{query}", Form(Encoding.UTF8.GetBytes(csv), $"{kind}.csv"), Ct);

    private static Task<HttpResponseMessage> PreviewBytesAsync(HttpClient client, string kind, byte[] bytes, string fileName, string query = "") =>
        client.PostAsync($"/api/school/imports/{kind}/preview{query}", Form(bytes, fileName), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, JsonElement preview) =>
        client.PostAsync($"/api/school/imports/{preview.GetProperty("token").GetString()}/confirm", null, Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();
    private static string Status(JsonElement row) => row.GetProperty("status").GetString()!;
    private static string? Message(JsonElement row) => row.GetProperty("message").GetString();

    private static void Counts(JsonElement preview, int @new, int update, int unchanged, int error)
    {
        var c = preview.GetProperty("counts");
        Assert.Equal((@new, update, unchanged, error),
            (c.GetProperty("new").GetInt32(), c.GetProperty("update").GetInt32(), c.GetProperty("unchanged").GetInt32(), c.GetProperty("error").GetInt32()));
    }

    private static async Task<Guid> ItemAsync(HttpClient owner, string name, bool optional = false) =>
        (await Json(await owner.PostAsJsonAsync("/api/school/fee-items", new { name, isOptional = optional }, Ct))).GetProperty("id").GetGuid();

    private Task<List<Guid>> TermsAsync(SchoolKit.School school) =>
        api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId).OrderBy(t => t.StartDate).Select(t => t.Id).ToListAsync(Ct));

    private Task<Guid> LevelOfAsync(Guid classId) =>
        api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));

    private sealed record Invoice(Guid Id, string Number, string AdmissionNo, Guid StudentId, Guid TermId, long Balance);

    private sealed record Fees(SchoolKit.School School, Guid ClassId, Guid LevelId, Guid TermId, Guid Term2Id, Invoice Ada, Invoice Bola, string GuardianEmail, Guid GuardianId);

    /// <summary>JSS 1A with Ada (a guardian with an email) and Bola, a tuition of ₦100,000 for the first term, and their first-term invoices.</summary>
    private async Task<Fees> FeesAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var ada = await kit.StudentAsync(school, classId, "Ada");
        await kit.StudentAsync(school, classId, "Bola");
        var email = $"guardian-{Guid.NewGuid():N}@example.com";
        var guardianId = await kit.GuardianAsync(school, ada, "Ngozi Obi", null, email);
        var levelId = await LevelOfAsync(classId);
        var terms = await TermsAsync(school);
        var tuition = await ItemAsync(school.Owner, "Tuition");
        foreach (var termId in terms.Take(2))
            await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}",
                new { lines = new[] { new { feeItemId = tuition, amountMinor = 10_000_000L } } }, Ct));
        await Json(await school.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId = terms[0] }, Ct));
        var invoices = await InvoicesAsync(school, terms[0]);
        return new Fees(school, classId, levelId, terms[0], terms[1], invoices.Single(i => i.StudentId == ada), invoices.Single(i => i.StudentId != ada), email, guardianId);
    }

    private static async Task<List<Invoice>> InvoicesAsync(SchoolKit.School school, Guid termId) =>
        (await Json(await school.Owner.GetAsync($"/api/school/invoices?termId={termId}", Ct))).EnumerateArray()
            .Select(i => new Invoice(i.GetProperty("id").GetGuid(), i.GetProperty("number").GetString()!, i.GetProperty("admissionNo").GetString()!,
                i.GetProperty("studentId").GetGuid(), i.GetProperty("termId").GetGuid(), i.GetProperty("balance").GetInt64())).ToList();

    private Task<List<FeePayment>> PaymentsAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).OrderBy(p => p.ReceiptNumber).ToListAsync(Ct));

    // ---------- Fee items ----------

    [Fact]
    public async Task Fee_items_import()
    {
        var school = await kit.SchoolAsync("Fee Item Import School");
        await ItemAsync(school.Owner, "Tuition");
        await ItemAsync(school.Owner, "School bus", optional: true);
        await ItemAsync(school.Owner, "Books");

        var preview = await Json(await PreviewAsync(school.Owner, "fee-items", Csv("name,optional",
            "tuition,no",            // same flag: unchanged
            "school  BUS,no",         // existing (case and spaces ignored): becomes required
            "Books,",                // blank keeps the flag
            "Uniform,yes",
            "PTA levy,")));
        Assert.Equal("fee-items", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.Equal(["Unchanged", "Update", "Unchanged", "New", "New"], rows.Select(Status));
        Assert.Equal("School bus", rows[1].GetProperty("label").GetString());
        Counts(preview, 2, 1, 2, 0);

        var done = await Json(await ConfirmAsync(school.Owner, preview));
        Assert.Equal((2, 1), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()));
        var items = (await Json(await school.Owner.GetAsync("/api/school/fee-items", Ct))).EnumerateArray()
            .ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("isOptional").GetBoolean());
        Assert.Equal(new Dictionary<string, bool> { ["Books"] = false, ["PTA levy"] = false, ["School bus"] = false, ["Tuition"] = false, ["Uniform"] = true }, items);

        // Errors: a bad yes/no, a blank name, a name over 80 characters, the same new name twice. Nothing is saved.
        var bad = await Json(await PreviewAsync(school.Owner, "fee-items", Csv("name,optional",
            "Sports,maybe", ",yes", new string('x', 81) + ",no", "Lab,yes", "lab,no")));
        Assert.Equal([FeeItemImporter.YesOrNo, "Enter a name for the fee item.", "Use 80 characters or fewer for the name.", FeeItemImporter.Twice, FeeItemImporter.Twice],
            Rows(bad).Select(Message));
        var refused = await ProblemAsync(await ConfirmAsync(school.Owner, bad), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        Assert.Equal(5, (await Json(await school.Owner.GetAsync("/api/school/fee-items", Ct))).GetArrayLength());
    }

    // ---------- Fee structure ----------

    [Fact]
    public async Task Fee_structure_import_matches_screen()
    {
        var school = await kit.SchoolAsync("Fee Structure Import School");
        var classId = await kit.ClassAsync(school);
        var levelId = await LevelOfAsync(classId);
        var otherLevel = (await Json(await school.Owner.PostAsJsonAsync("/api/school/levels", new { name = "JSS 2", stage = "Jss", order = 20, hasStudentLogins = true }, Ct)))
            .GetProperty("id").GetGuid();
        var termId = (await TermsAsync(school))[0];
        var tuition = await ItemAsync(school.Owner, "Tuition");
        var books = await ItemAsync(school.Owner, "Books");
        var bus = await ItemAsync(school.Owner, "Bus", optional: true);
        var lab = await ItemAsync(school.Owner, "Lab");
        // The screen's save: tuition ₦50,000 and lab ₦2,000 for JSS 1.
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}",
            new { lines = new[] { new { feeItemId = tuition, amountMinor = 5_000_000L }, new { feeItemId = lab, amountMinor = 200_000L } } }, Ct));
        var query = $"?termId={termId}&levelId={levelId}";

        // The template: every item A to Z with the stored amounts, and sheet_for naming the session, level and term.
        var template = await school.Owner.GetAsync($"/api/school/imports/fee-structure/template.csv{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        var bytes = await template.Content.ReadAsByteArrayAsync(Ct);
        var lines = Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("fee_item,amount,sheet_for,row_version", lines[0]);
        var session = await api.WithDbAsync(db => (from t in db.Terms.IgnoreQueryFilters() join s in db.AcademicSessions.IgnoreQueryFilters() on t.SessionId equals s.Id
                                                   where t.Id == termId select s.Name + " / JSS 1 / " + t.Name).FirstAsync(Ct));
        // Data cells are written formula-safe (quoted); the values are what counts. The row_version (last) is blank for an item the level
        // has no line for, and a short hash of the stored amount otherwise.
        var cells = lines.Skip(1).Select(l => l.Replace("\"", "").Split(',')).ToList();
        Assert.Equal([$"Books,,{session}", $"Bus,,{session}", $"Lab,2000,{session}", $"Tuition,50000,{session}"], cells.Select(c => string.Join(',', c.Take(3))));
        Assert.Equal(["", ""], cells.Take(2).Select(c => c[3]));
        Assert.All(cells.Skip(2), c => Assert.Matches("^v[0-9a-f]{12}$", c[3]));
        var versions = cells.ToDictionary(c => c[0], c => c[3]);
        Assert.StartsWith("20", session);

        // Round trip: the template unchanged is all Unchanged and writes nothing.
        var xlsx = await (await school.Owner.GetAsync($"/api/school/imports/fee-structure/template.xlsx{query}", Ct)).Content.ReadAsByteArrayAsync(Ct);
        var sameXlsx = await Json(await PreviewBytesAsync(school.Owner, "fee-structure", xlsx, "structure.xlsx", query));
        Counts(sameXlsx, 0, 0, 4, 0);
        Assert.Empty(sameXlsx.GetProperty("warnings").EnumerateArray());
        var same = await Json(await PreviewBytesAsync(school.Owner, "fee-structure", bytes, "structure.csv", query));
        Counts(same, 0, 0, 4, 0);
        var before = await api.WithDbAsync(db => db.FeeStructureLines.IgnoreQueryFilters().AsNoTracking().Where(l => l.TermId == termId).OrderBy(l => l.Id).ToListAsync(Ct));
        var nothing = await Json(await ConfirmAsync(school.Owner, same));
        Assert.Equal((0, 0), (nothing.GetProperty("created").GetInt32(), nothing.GetProperty("updated").GetInt32()));
        var after = await api.WithDbAsync(db => db.FeeStructureLines.IgnoreQueryFilters().AsNoTracking().Where(l => l.TermId == termId).OrderBy(l => l.Id).ToListAsync(Ct));
        Assert.Equal(before.Select(l => (l.Id, l.AmountMinor, l.UpdatedAt)), after.Select(l => (l.Id, l.AmountMinor, l.UpdatedAt)));

        // Books new at ₦12,500.50, Bus left blank (not charged), Lab blank (kept), Tuition raised to ₦52,000.
        var preview = await Json(await PreviewAsync(school.Owner, "fee-structure", Csv("fee_item,amount,sheet_for,row_version",
            $"Books,\"12,500.50\",{session},", $"Bus,,{session},", $"Lab,,{session},{versions["Lab"]}", $"tuition,52000,{session},{versions["Tuition"]}"), query));
        Assert.Equal(["New", "Unchanged", "Unchanged", "Update"], Rows(preview).Select(Status));
        var done = await Json(await ConfirmAsync(school.Owner, preview));
        Assert.Equal((1, 1), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()));

        // The same as the screen's save of those amounts on another level.
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{otherLevel}",
            new { lines = new[] { new { feeItemId = books, amountMinor = 1_250_050L }, new { feeItemId = lab, amountMinor = 200_000L }, new { feeItemId = tuition, amountMinor = 5_200_000L } } }, Ct));
        var structure = (await Json(await school.Owner.GetAsync($"/api/school/fee-structure/{termId}", Ct))).EnumerateArray()
            .ToDictionary(s => s.GetProperty("levelId").GetGuid(), s => s.GetProperty("lines").EnumerateArray()
                .Select(l => (l.GetProperty("feeItemName").GetString(), l.GetProperty("amountMinor").GetInt64())).ToList());
        Assert.Equal([("Books", 1_250_050L), ("Lab", 200_000L), ("Tuition", 5_200_000L)], structure[levelId]);
        Assert.Equal(structure[otherLevel], structure[levelId]);
        Assert.DoesNotContain(structure[levelId], l => l.Item1 == "Bus");
        _ = bus;

        // Errors: another level's sheet, an unknown item, zero, three decimals, the same item twice.
        var wrong = await Json(await PreviewAsync(school.Owner, "fee-structure", Csv("fee_item,amount,sheet_for,row_version",
            $"Books,100,{session.Replace("JSS 1", "JSS 2")},", $"Sports,100,{session},", $"Lab,0,{session},", $"Bus,1.005,{session},",
            $"Tuition,1,{session},", $"tuition,2,{session},"), query));
        Assert.Equal([$"This sheet was made for {session.Replace("JSS 1", "JSS 2")}. Download the template for this level and term.",
            FeeStructureImporter.ItemMissing, FeeStructureImporter.NotZero, FeeImports.TooManyDecimals, FeeStructureImporter.Twice, FeeStructureImporter.Twice],
            Rows(wrong).Select(Message));
    }

    // ---------- Payments ----------

    [GeneratedRegex(@"^RCT-\d{4}-\d{5}$")]
    private static partial Regex ReceiptPattern();

    [Fact]
    public async Task Payment_import_records_receipts()
    {
        var f = await FeesAsync("Payment Import School");
        // One payment on the screen first: the import's receipts follow its number.
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{f.Ada.Id}/payments",
            new { amountMinor = 1_000_000L, method = "Cash", reference = (string?)null, paidOn = Today.AddDays(-5) }, Ct));
        var serial = (Today.AddDays(-1).ToDateTime(TimeOnly.MinValue) - new DateTime(1899, 12, 30)).TotalDays.ToString(CultureInfo.InvariantCulture);

        var preview = await Json(await PreviewAsync(f.School.Owner, "payments", Csv(PaymentHeader,
            $"{f.Ada.Number},,\"30,000\",Transfer,TRF-001,{Iso(Today.AddDays(-2))}",
            $",{f.Bola.AdmissionNo},12500.50,cash,,{Today.AddDays(-2).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
            $"{f.Ada.Number.ToLowerInvariant()},{f.Ada.AdmissionNo},5000,POS,POS-77,{serial}"), $"?termId={f.TermId}"));
        Assert.Equal(["New", "New", "New"], Rows(preview).Select(Status));
        Assert.StartsWith(f.Ada.Number, Rows(preview)[0].GetProperty("label").GetString());
        Assert.DoesNotContain(await PaymentsAsync(f.School.TenantId), p => p.Reference != null);

        var done = await Json(await ConfirmAsync(f.School.Owner, preview));
        Assert.Equal(3, done.GetProperty("created").GetInt32());

        var payments = await PaymentsAsync(f.School.TenantId);
        Assert.Equal(4, payments.Count);
        Assert.All(payments, p => Assert.Matches(ReceiptPattern(), p.ReceiptNumber));
        var numbers = payments.Select(p => int.Parse(p.ReceiptNumber![^5..], CultureInfo.InvariantCulture)).Order().ToList();
        Assert.Equal([1, 2, 3, 4], numbers);
        var transfer = payments.Single(p => p.Reference == "TRF-001");
        Assert.Equal((FeePaymentMethod.Transfer, 3_000_000L, Today.AddDays(-2), FeePaymentStatus.Succeeded), (transfer.Method, transfer.AmountMinor, transfer.PaidOn, transfer.Status));
        var cash = payments.Single(p => p.InvoiceId == f.Bola.Id);
        Assert.Equal((FeePaymentMethod.Cash, 1_250_050L, (string?)null), (cash.Method, cash.AmountMinor, cash.Reference));
        var pos = payments.Single(p => p.Reference == "POS-77");
        Assert.Equal((FeePaymentMethod.Pos, 500_000L, Today.AddDays(-1)), (pos.Method, pos.AmountMinor, pos.PaidOn));
        Assert.All(payments, p => Assert.NotNull(p.RecordedBy));

        // The invoices are recalculated, and Ada's guardian gets one receipt email per payment, as on the screen.
        var invoices = await InvoicesAsync(f.School, f.TermId);
        Assert.Equal(10_000_000L - 1_000_000 - 3_000_000 - 500_000, invoices.Single(i => i.Id == f.Ada.Id).Balance);
        Assert.Equal(10_000_000L - 1_250_050, invoices.Single(i => i.Id == f.Bola.Id).Balance);
        var emails = await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.receipt" && n.Recipient == f.GuardianEmail).ToListAsync(Ct));
        Assert.Equal(3, emails.Count);
        Assert.Contains(emails, e => e.Payload["receipt"] == transfer.ReceiptNumber);
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == f.School.TenantId && a.Action == "imports.payments", Ct)));
    }

    [Fact]
    public async Task Payment_over_balance_error()
    {
        var f = await FeesAsync("Payment Balance School");
        var day = Iso(Today.AddDays(-1));
        var preview = await Json(await PreviewAsync(f.School.Owner, "payments", Csv(PaymentHeader,
            $"{f.Ada.Number},,60000,Cash,,{day}",
            $"{f.Ada.Number},,50000,Cash,,{day}",      // with the row above, more than ₦100,000
            $"{f.Bola.Number},,100000.01,Cash,,{day}", // over on its own
            $"{f.Bola.Number},,100000,Cash,,{day}")));
        var rows = Rows(preview);
        Assert.Equal(["New", "Error", "Error", "New"], rows.Select(Status));
        Assert.Equal("That is more than the balance of ₦40,000. Enter that amount or less.", Message(rows[1]));
        Assert.Equal("That is more than the balance of ₦100,000. Enter that amount or less.", Message(rows[2]));

        var refused = await ProblemAsync(await ConfirmAsync(f.School.Owner, preview), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        Assert.Empty(await PaymentsAsync(f.School.TenantId));

        // Unknown and cancelled invoices, a bad method, a bad date and a future date.
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{f.Bola.Id}/cancel", new { reason = "Left the school" }, Ct));
        var bad = await Json(await PreviewAsync(f.School.Owner, "payments", Csv(PaymentHeader,
            $"INV-NOPE,,1000,Cash,,{day}", $"{f.Bola.Number},,1000,Cash,,{day}", $"{f.Ada.Number},,1000,Online,,{day}",
            $"{f.Ada.Number},,1000,Cash,,31/31/2026", $"{f.Ada.Number},,1000,Cash,,{Iso(Today.AddDays(2))}", $",,1000,Cash,,{day}",
            $"{f.Ada.Number},{f.Bola.AdmissionNo},1000,Cash,,{day}")));
        Assert.Equal([PaymentImporter.InvoiceMissing, PaymentImporter.InvoiceCancelled, PaymentImporter.BadMethod, FeeImports.BadDate,
            PaymentImporter.FutureDate, PaymentImporter.NoInvoice, PaymentImporter.OtherStudent], Rows(bad).Select(Message));
    }

    [Fact]
    public async Task Admission_number_with_two_open_invoices_error()
    {
        var f = await FeesAsync("Payment Admission School");
        // Second-term invoices too: each student now has two open invoices.
        await Json(await f.School.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId = f.Term2Id }, Ct));
        var day = Iso(Today.AddDays(-1));
        var csv = Csv(PaymentHeader, $",{f.Ada.AdmissionNo},1000,Cash,,{day}");

        var any = await Json(await PreviewAsync(f.School.Owner, "payments", csv));
        Assert.Equal("This student has more than one open invoice. Enter the invoice number.", Message(Rows(any)[0]));

        // On the payments page's term, the one open invoice of that term is found.
        var term = await Json(await PreviewAsync(f.School.Owner, "payments", csv, $"?termId={f.TermId}"));
        Assert.Equal("New", Status(Rows(term)[0]));
        Assert.StartsWith(f.Ada.Number, Rows(term)[0].GetProperty("label").GetString());

        // No open invoice that term (Bola's is paid in full), and an admission number nobody has.
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{f.Bola.Id}/payments",
            new { amountMinor = 10_000_000L, method = "Cash", reference = (string?)null, paidOn = Today.AddDays(-1) }, Ct));
        var none = await Json(await PreviewAsync(f.School.Owner, "payments",
            Csv(PaymentHeader, $",{f.Bola.AdmissionNo},1000,Cash,,{day}", $",ADM-NOBODY,1000,Cash,,{day}"), $"?termId={f.TermId}"));
        Assert.Equal(["This student has no open invoice for First term. Enter the invoice number.", PaymentImporter.StudentMissing], Rows(none).Select(Message));
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(f.School.Owner, any)).StatusCode);
    }

    [Fact]
    public async Task Duplicate_reference_error()
    {
        var f = await FeesAsync("Payment Reference School");
        var day = Iso(Today.AddDays(-1));
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{f.Ada.Id}/payments",
            new { amountMinor = 100_000L, method = "Transfer", reference = "TRF-9", paidOn = Today.AddDays(-1) }, Ct));

        var preview = await Json(await PreviewAsync(f.School.Owner, "payments", Csv(PaymentHeader,
            $"{f.Ada.Number},,1000,Transfer,trf-9,{day}",  // already used on the screen (case ignored)
            $"{f.Ada.Number},,1000,Transfer,A-1,{day}",
            $"{f.Bola.Number},,1000,Transfer,a-1,{day}",   // earlier in the file
            $"{f.Bola.Number},,1000,Cash,,{day}",
            $"{f.Bola.Number},,1000,Cash,,{day}")));     // blank references are never duplicates
        Assert.Equal(["Error", "New", "Error", "New", "New"], Rows(preview).Select(Status));
        Assert.Equal(PaymentImporter.ReferenceUsed, Message(Rows(preview)[0]));
        Assert.Equal(PaymentImporter.ReferenceEarlier, Message(Rows(preview)[2]));
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(f.School.Owner, preview)).StatusCode);
        Assert.Single(await PaymentsAsync(f.School.TenantId));

        // A reversed payment gave its reference back.
        var paymentId = (await PaymentsAsync(f.School.TenantId)).Single().Id;
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{paymentId}/reverse", new { reason = "Entered twice" }, Ct));
        var again = await Json(await PreviewAsync(f.School.Owner, "payments", Csv(PaymentHeader, $"{f.Ada.Number},,1000,Transfer,TRF-9,{day}")));
        Assert.Equal("New", Status(Rows(again)[0]));
    }

    // ---------- Money ----------

    [Fact]
    public async Task Naira_amount_parses_to_kobo()
    {
        Assert.Equal(1_250_050L, FeeImports.ParseNaira("12,500.50", out _));
        Assert.Equal(1_250_000L, FeeImports.ParseNaira("12500", out _));
        Assert.Equal(1_250_050L, FeeImports.ParseNaira(" ₦12,500.5 ", out _));
        Assert.Equal(705L, FeeImports.ParseNaira("7.05", out _));
        Assert.Equal(50L, FeeImports.ParseNaira("0.5", out _));
        Assert.Equal(100_000_000L, FeeImports.ParseNaira("1,000,000", out _));
        Assert.Equal(1_999_999_999_99L, FeeImports.ParseNaira("1999999999.99", out _));
        foreach (var (raw, why) in new[]
        {
            ("12.505", FeeImports.TooManyDecimals), ("-5", FeeImports.AboveZero), ("abc", FeeImports.NotAnAmount), ("1,25", FeeImports.NotAnAmount),
            ("12,50.00", FeeImports.NotAnAmount), ("", FeeImports.NotAnAmount), ("1e5", FeeImports.NotAnAmount), ("12 500", FeeImports.NotAnAmount), ("0,500", FeeImports.NotAnAmount), ("0.30000000000000004", FeeImports.TooManyDecimals),
        })
        {
            Assert.Null(FeeImports.ParseNaira(raw, out var error));
            Assert.Equal(why, error);
        }
        Assert.Equal("12500.50", FeeImports.FormatNaira(1_250_050));
        Assert.Equal("50000", FeeImports.FormatNaira(5_000_000));

        // Through an import: the stored kobo is exact.
        var school = await kit.SchoolAsync("Naira Parse School");
        var preview = await Json(await PreviewAsync(school.Owner, "expenses", Csv("date,category,amount,note",
            $"{Iso(Today.AddDays(-1))},Diesel,\"12,500.50\",", $"{Iso(Today.AddDays(-1))},Diesel,0.1,", $"{Iso(Today.AddDays(-1))},Diesel,-3,")));
        Assert.Equal(["New", "New", "Error"], Rows(preview).Select(Status));
        Assert.Equal(FeeImports.AboveZero, Message(Rows(preview)[2]));
        var ok = await Json(await PreviewAsync(school.Owner, "expenses", Csv("date,category,amount,note",
            $"{Iso(Today.AddDays(-1))},Diesel,\"12,500.50\",", $"{Iso(Today.AddDays(-1))},Diesel,0.1,")));
        await Json(await ConfirmAsync(school.Owner, ok));
        var amounts = await api.WithDbAsync(db => db.SchoolExpenses.IgnoreQueryFilters().Where(x => x.TenantId == school.TenantId).Select(x => x.AmountMinor).OrderBy(a => a).ToListAsync(Ct));
        Assert.Equal([10L, 1_250_050L], amounts);
    }

    // ---------- Expenses ----------

    [Fact]
    public async Task Expenses_import()
    {
        var school = await kit.SchoolAsync("Expense Import School");
        var (bursar, bursarId) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        await Json(await school.Owner.PostAsJsonAsync("/api/school/expenses", new { category = "Diesel", amountMinor = 100_000L, date = Today.AddDays(-9), note = (string?)null }, Ct));
        var serial = (Today.AddDays(-3).ToDateTime(TimeOnly.MinValue) - new DateTime(1899, 12, 30)).TotalDays.ToString(CultureInfo.InvariantCulture);

        var preview = await Json(await PreviewAsync(bursar, "expenses", Csv("date,category,amount,note",
            $"{Iso(Today.AddDays(-1))},diesel,45000,Generator",
            $"{Today.AddDays(-2).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)},Repairs,\"1,200.75\",Roof leak",
            $"{serial},Stationery,800,")));
        Assert.Equal(["New", "New", "New"], Rows(preview).Select(Status));
        Assert.Equal($"{Iso(Today.AddDays(-1))}, diesel", Rows(preview)[0].GetProperty("label").GetString());
        var done = await Json(await ConfirmAsync(bursar, preview));
        Assert.Equal(3, done.GetProperty("created").GetInt32());

        var saved = await api.WithDbAsync(db => db.SchoolExpenses.IgnoreQueryFilters().Where(x => x.TenantId == school.TenantId).OrderByDescending(x => x.Date).ToListAsync(Ct));
        Assert.Equal(4, saved.Count);
        Assert.Equal(("Diesel", 4_500_000L, Today.AddDays(-1), "Generator", (Guid?)bursarId), (saved[0].Category, saved[0].AmountMinor, saved[0].Date, saved[0].Note, saved[0].RecordedBy));
        Assert.Equal(("Repairs", 120_075L, Today.AddDays(-2), "Roof leak"), (saved[1].Category, saved[1].AmountMinor, saved[1].Date, saved[1].Note));
        Assert.Equal(("Stationery", 80_000L, Today.AddDays(-3), (string?)null), (saved[2].Category, saved[2].AmountMinor, saved[2].Date, saved[2].Note));

        var bad = await Json(await PreviewAsync(bursar, "expenses", Csv("date,category,amount,note",
            $"{Iso(Today.AddDays(2))},Diesel,100,", $",Diesel,100,", $"{Iso(Today)},,100,", $"{Iso(Today)},{new string('c', 61)},100,",
            $"{Iso(Today)},Diesel,0,", $"{Iso(Today)},Diesel,ten,", $"{Iso(Today)},Diesel,100,{new string('n', 301)}")));
        Assert.Equal([ExpenseImporter.FutureDate, ExpenseImporter.NoDate, ExpenseImporter.NoCategory, "Use 60 characters or fewer for the category.",
            FeeImports.AboveZero, FeeImports.NotAnAmount, "Use 300 characters or fewer for the note."], Rows(bad).Select(Message));
    }

    // ---------- Access ----------

    [Fact]
    public async Task Teacher_refused_fees_imports()
    {
        var f = await FeesAsync("Fee Import Access School");
        var (teacher, _) = await kit.StaffAsync(f.School, MemberRole.Staff, "Teacher");
        var (principal, _) = await kit.StaffAsync(f.School, MemberRole.Admin, "Principal");
        var (bursar, _) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");
        var structureQuery = $"?termId={f.TermId}&levelId={f.LevelId}";
        var kinds = new[] { ("fee-items", "", "name,optional"), ("fee-structure", structureQuery, "fee_item,amount,sheet_for,row_version"),
            ("payments", "", PaymentHeader), ("expenses", "", "date,category,amount,note") };

        foreach (var (kind, query, header) in kinds)
        {
            foreach (var ext in new[] { "xlsx", "csv" })
            {
                var refused = await ProblemAsync(await teacher.GetAsync($"/api/school/imports/{kind}/template.{ext}{query}", Ct), HttpStatusCode.Forbidden);
                Assert.Equal(FeeImports.NotForTeachers, refused.GetProperty("detail").GetString());
                foreach (var allowed in new[] { f.School.Owner, principal, bursar })
                    Assert.Equal(HttpStatusCode.OK, (await allowed.GetAsync($"/api/school/imports/{kind}/template.{ext}{query}", Ct)).StatusCode);
            }
            // Refused before the file or the scope is looked at.
            await ProblemAsync(await PreviewAsync(teacher, kind, Csv(header, "x,y"), query), HttpStatusCode.Forbidden);
            await ProblemAsync(await teacher.PostAsync($"/api/school/imports/{kind}/preview", new MultipartFormDataContent { { new StringContent("none"), "note" } }, Ct), HttpStatusCode.Forbidden);
        }

        // A teacher can't confirm a batch either, even one held under their own id.
        var token = "teacher-fee-batch";
        var teacherUserId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == f.School.TenantId && m.Role == MemberRole.Staff).Select(m => m.UserId).FirstAsync(Ct));
        await api.WithDbAsync(async db =>
        {
            db.ImportBatches.Add(new ImportBatch
            {
                TenantId = f.School.TenantId, Token = token, Kind = "expenses", Payload = "{\"fileName\":\"x.csv\",\"rows\":[]}",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), CreatedBy = teacherUserId,
            });
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        await ProblemAsync(await teacher.PostAsync($"/api/school/imports/{token}/confirm", null, Ct), HttpStatusCode.Forbidden);

        // The bursar imports payments end to end.
        var preview = await Json(await PreviewAsync(bursar, "payments", Csv(PaymentHeader, $"{f.Ada.Number},,1000,Cash,,{Iso(Today.AddDays(-1))}")));
        Assert.Equal(1, (await Json(await ConfirmAsync(bursar, preview))).GetProperty("created").GetInt32());
        Assert.Empty(await api.WithDbAsync(db => db.SchoolExpenses.IgnoreQueryFilters().Where(x => x.TenantId == f.School.TenantId).ToListAsync(Ct)));

        // The role is checked before the scope is looked up: a made-up term is still 403 for a teacher, 404 for the owner.
        var bogus = $"?termId={Guid.NewGuid()}&levelId={Guid.NewGuid()}";
        await ProblemAsync(await PreviewAsync(teacher, "fee-structure", Csv("fee_item,amount,sheet_for,row_version", "Tuition,1,x,"), bogus), HttpStatusCode.Forbidden);
        await ProblemAsync(await PreviewAsync(f.School.Owner, "fee-structure", Csv("fee_item,amount,sheet_for,row_version", "Tuition,1,x,"), bogus), HttpStatusCode.NotFound);
    }

    // ---------- Fix round 1: data changing after preview, concurrent confirms, rollback, numeric cells ----------

    [Fact]
    public async Task Payment_changed_between_preview_and_confirm_refuses()
    {
        var f = await FeesAsync("Payment Changed School");
        var day = Iso(Today.AddDays(-1));

        // The balance: ₦60,000 previewed, then ₦50,000 recorded on the screen.
        var balance = await Json(await PreviewAsync(f.School.Owner, "payments", Csv(PaymentHeader, $"{f.Ada.Number},,60000,Cash,R-1,{day}")));
        Assert.Equal("New", Status(Rows(balance)[0]));
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{f.Ada.Id}/payments",
            new { amountMinor = 5_000_000L, method = "Cash", reference = (string?)null, paidOn = Today.AddDays(-1) }, Ct));
        var refused = await ProblemAsync(await ConfirmAsync(f.School.Owner, balance), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", refused.GetProperty("code").GetString());
        var row = Assert.Single(refused.GetProperty("rows").EnumerateArray());
        Assert.Equal(2, row.GetProperty("row").GetInt32());
        Assert.Equal("That is more than the balance of ₦50,000. Enter that amount or less.", Message(row));

        // The reference: previewed, then used on the screen.
        var reference = await Json(await PreviewAsync(f.School.Owner, "payments", Csv(PaymentHeader, $"{f.Bola.Number},,1000,Transfer,SAME-1,{day}")));
        Assert.Equal("New", Status(Rows(reference)[0]));
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{f.Bola.Id}/payments",
            new { amountMinor = 100_000L, method = "Transfer", reference = "same-1", paidOn = Today.AddDays(-1) }, Ct));
        var again = await ProblemAsync(await ConfirmAsync(f.School.Owner, reference), HttpStatusCode.Conflict);
        Assert.Equal(PaymentImporter.ReferenceUsed, Message(Assert.Single(again.GetProperty("rows").EnumerateArray())));

        // Only the two screen payments exist.
        Assert.Equal(2, (await PaymentsAsync(f.School.TenantId)).Count);
    }

    [Fact]
    public async Task Concurrent_confirms_never_record_a_reference_twice()
    {
        var f = await FeesAsync("Payment Race School");
        var day = Iso(Today.AddDays(-1));
        var csv = Csv(PaymentHeader, Enumerable.Range(1, 150).Select(i => $"{f.Ada.Number},,100,Transfer,RACE-{i},{day}").ToArray());
        // Two people preview the same bank statement; both previews are clean.
        var first = await Json(await PreviewAsync(f.School.Owner, "payments", csv));
        var second = await Json(await PreviewAsync(f.School.Owner, "payments", csv));
        Counts(first, 150, 0, 0, 0);
        Counts(second, 150, 0, 0, 0);

        var results = await Task.WhenAll(ConfirmAsync(f.School.Owner, first), ConfirmAsync(f.School.Owner, second));
        var statuses = results.Select(r => r.StatusCode).Order().ToList();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
        var loser = await ProblemAsync(results.Single(r => r.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", loser.GetProperty("code").GetString());
        Assert.All(loser.GetProperty("rows").EnumerateArray(), r => Assert.Equal(PaymentImporter.ReferenceUsed, Message(r)));

        var payments = await PaymentsAsync(f.School.TenantId);
        Assert.Equal(150, payments.Count);
        Assert.Equal(150, payments.Select(p => p.Reference).Distinct().Count());
        Assert.Equal(10_000_000L - 150 * 10_000, (await InvoicesAsync(f.School, f.TermId)).Single(i => i.Id == f.Ada.Id).Balance);
    }

    [Fact]
    public async Task Failed_import_rolls_back_receipts_and_notices()
    {
        var f = await FeesAsync("Payment Rollback School");
        var (_, parentUserId) = await kit.ParentAsync(f.School, f.GuardianId);
        var day = Iso(Today.AddDays(-1));
        var boom = $"BOOM-{Guid.NewGuid():N}";
        // A database failure on the third row's insert, after two rows are recorded in the same transaction.
        await api.WithDbAsync(db => db.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION test_fee_boom() RETURNS trigger AS $$
            BEGIN IF NEW.reference = '{boom}' THEN RAISE EXCEPTION 'boom'; END IF; RETURN NEW; END $$ LANGUAGE plpgsql;
            DROP TRIGGER IF EXISTS test_fee_boom ON fee_payments;
            CREATE TRIGGER test_fee_boom BEFORE INSERT ON fee_payments FOR EACH ROW EXECUTE FUNCTION test_fee_boom();
            """, Ct));
        try
        {
            var preview = await Json(await PreviewAsync(f.School.Owner, "payments", Csv(PaymentHeader,
                $"{f.Ada.Number},,1000,Transfer,OK-1,{day}", $"{f.Bola.Number},,2000,Cash,,{day}", $"{f.Ada.Number},,3000,Transfer,{boom},{day}")));
            Counts(preview, 3, 0, 0, 0);
            var failed = await ConfirmAsync(f.School.Owner, preview);
            Assert.False(failed.IsSuccessStatusCode);
        }
        finally
        {
            await api.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS test_fee_boom ON fee_payments; DROP FUNCTION IF EXISTS test_fee_boom();", Ct));
        }

        // Nothing landed: no payments, balances untouched, no receipt emails, no parent notices, no audit entry.
        Assert.Empty(await PaymentsAsync(f.School.TenantId));
        Assert.All(await InvoicesAsync(f.School, f.TermId), i => Assert.Equal(10_000_000L, i.Balance));
        Assert.Empty(await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.receipt" && n.Recipient == f.GuardianEmail).ToListAsync(Ct)));
        Assert.Empty(await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().Where(n => n.UserId == parentUserId && n.Kind == "school.payment_received").ToListAsync(Ct)));
        Assert.False(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == f.School.TenantId && a.Action == "imports.payments", Ct)));

        // The receipt counter was not used up: the next payment is receipt 1, and its notice reaches the parent.
        var paid = await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{f.Ada.Id}/payments",
            new { amountMinor = 100_000L, method = "Cash", reference = (string?)null, paidOn = Today.AddDays(-1) }, Ct));
        Assert.EndsWith("-00001", paid.GetProperty("receiptNumber").GetString());
        Assert.Single(await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().Where(n => n.UserId == parentUserId && n.Kind == "school.payment_received").ToListAsync(Ct)));
    }

    [Fact]
    public async Task Numeric_xlsx_amounts_parse_exactly()
    {
        var school = await kit.SchoolAsync("Numeric Amount School");
        var day = Iso(Today.AddDays(-1));
        static byte[] Workbook(string day, params double[] amounts)
        {
            using var book = new ClosedXML.Excel.XLWorkbook();
            var sheet = book.AddWorksheet("Expenses");
            string[] heads = ["date", "category", "amount", "note"];
            for (var c = 0; c < heads.Length; c++) sheet.Cell(1, c + 1).Value = heads[c];
            for (var r = 0; r < amounts.Length; r++)
            {
                sheet.Cell(r + 2, 1).Value = day;
                sheet.Cell(r + 2, 2).Value = "Diesel";
                sheet.Cell(r + 2, 3).Value = amounts[r]; // a real number cell, not text
            }
            using var stream = new MemoryStream();
            book.SaveAs(stream);
            return stream.ToArray();
        }

        // ClosedXML writes 0.1 + 0.2 as 0.3, but Excel stores a formula's float artefact with all 17 digits. Write it as Excel would.
        static byte[] WithArtefact(byte[] xlsx)
        {
            using var stream = new MemoryStream();
            stream.Write(xlsx);
            using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
            {
                var entry = zip.Entries.Single(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal));
                string xml;
                using (var reader = new StreamReader(entry.Open())) xml = reader.ReadToEnd();
                var patched = Regex.Replace(xml, @"(<(?:\w+:)?v>)0\.3(</)", "${1}" + (0.1 + 0.2).ToString("R", CultureInfo.InvariantCulture) + "${2}");
                Assert.NotEqual(xml, patched);
                entry.Delete();
                var copy = zip.CreateEntry(entry.FullName);
                using var writer = new StreamWriter(copy.Open());
                writer.Write(patched);
            }
            return stream.ToArray();
        }

        var preview = await Json(await PreviewBytesAsync(school.Owner, "expenses", WithArtefact(Workbook(day, 12500.5, 45000, 0.3, 1234567.89, 12500.505)), "expenses.xlsx"));
        var rows = Rows(preview);
        Assert.Equal(["New", "New", "Error", "New", "Error"], rows.Select(Status));
        Assert.Equal("Enter the amount with at most two decimal places, for example 12500.50.", Message(rows[2]));
        Assert.Equal(FeeImports.TooManyDecimals, Message(rows[4]));

        var ok = await Json(await PreviewBytesAsync(school.Owner, "expenses", Workbook(day, 12500.5, 45000, 1234567.89), "expenses.xlsx"));
        await Json(await ConfirmAsync(school.Owner, ok));
        var amounts = await api.WithDbAsync(db => db.SchoolExpenses.IgnoreQueryFilters().Where(x => x.TenantId == school.TenantId).Select(x => x.AmountMinor).OrderBy(a => a).ToListAsync(Ct));
        Assert.Equal([1_250_050L, 4_500_000L, 123_456_789L], amounts);
    }
}
