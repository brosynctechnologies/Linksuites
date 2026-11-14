using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Saas.Application.Billing;
using Saas.Application.Invoices;
using Saas.Domain.Core;
using BusinessSettings = Saas.Domain.Invoices.BusinessSettings;
using ClientSnapshot = Saas.Domain.Invoices.ClientSnapshot;
using DocumentLine = Saas.Domain.Invoices.DocumentLine;
using Invoice = Saas.Domain.Invoices.Invoice;
using InvoiceItem = Saas.Domain.Invoices.InvoiceItem;
using InvoicePayment = Saas.Domain.Invoices.InvoicePayment;
using PaymentMethod = Saas.Domain.Invoices.PaymentMethod;
using Saas.Infrastructure.Invoices;
using UglyToad.PdfPig;

namespace Saas.IntegrationTests;

/// <summary>
/// InvoiceLink designs, Tasks 2 and 3: the ten PDF designs. The renderer tests build the PDF model directly (every design, every
/// kind, long documents, images); the API tests check that a business's letterhead settings reach the PDF.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class InvoiceDesignPdfTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)); // Lagos

    private static readonly string[] Designs =
        ["classic", "modern", "minimal", "bold", "elegant", "executive", "corporate", "ledger", "studio", "prestige"];
    private static readonly string[] Kinds = [DocumentPdfModel.Invoice, DocumentPdfModel.Quote, DocumentPdfModel.Receipt];

    private const string Name = "Ada Designs Ltd";
    private const string Tagline = "Bespoke tailoring for men and women";
    private const string Email = "accounts@adadesigns.example";
    private const string Footer = "Goods remain ours until paid for in full.";
    private const string Bank = "GTBank 0123456789";

    public static TheoryData<string, string> DesignsAndKinds()
    {
        var data = new TheoryData<string, string>();
        foreach (var design in Designs)
            foreach (var kind in Kinds)
                data.Add(design, kind);
        return data;
    }

    // ------------------------------------------------------------------ the renderer

    [Theory]
    [MemberData(nameof(DesignsAndKinds))]
    public void Every_design_renders_every_kind_with_the_letterhead(string design, string kind)
    {
        var text = Text(Render(Sample(design, kind)));

        AssertContains(text, Name, Tagline, Footer, "Contact", Email, "Authorised signature", "Ada Obi", "Managing director", "₦");
        AssertContains(text, kind switch
        {
            DocumentPdfModel.Quote => "Quote",
            DocumentPdfModel.Receipt => "Receipt",
            _ => "Tax invoice",
        });
        if (kind == DocumentPdfModel.Invoice) AssertContains(text, Bank);

        // The line under the name is the chosen one, and the email has moved down to the contact row.
        var name = text.IndexOf(Squash(Name), StringComparison.Ordinal);
        Assert.True(text.IndexOf(Squash(Tagline), StringComparison.Ordinal) > name, "the line under the name comes after the name");
        Assert.True(text.IndexOf(Squash(Email), StringComparison.Ordinal) > text.IndexOf("Contact", StringComparison.Ordinal),
            "the email prints in the contact row, not in the letterhead");
    }

    [Theory]
    [InlineData("modern")]
    [InlineData("serif")]
    [InlineData("sans")]
    public void Every_font_style_prints_the_naira_sign(string fontStyle)
    {
        Assert.True(InvoiceFonts.EnsureRegistered() >= 18, "the embedded fonts are registered");
        foreach (var design in Designs)
        {
            // Amounts and numbers must come back out as real characters, so they can be copied, searched and read aloud.
            var text = Text(Render(Sample(design, DocumentPdfModel.Invoice) with { FontStyle = fontStyle }));
            AssertContains(text, "₦1,250,000.00", "INV-0042", "₦850,000.00");
            Assert.DoesNotContain(text, c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.PrivateUse);
        }
    }

    [Fact]
    public void Hidden_blocks_stay_off_the_page()
    {
        foreach (var design in Designs)
        {
            var text = Text(Render(Sample(design, DocumentPdfModel.Invoice) with
            {
                BankDetails = null, Signature = null, FooterText = null, HeaderLineText = null, Notes = null, Terms = null,
            }));
            AssertContains(text, Name, "Contact", Email);
            foreach (var gone in new[] { Bank, "Payment details", "Authorised signature", Footer, Tagline, "Notes", "Terms" })
                Assert.DoesNotContain(Squash(gone), text);
        }
    }

    [Fact]
    public void Long_documents_break_across_pages_and_repeat_the_footer()
    {
        var lines = Enumerable.Range(1, 60)
            .Select(i => new PdfLine($"Line {i}: tailoring, fitting and delivery for the wedding party", "2", "₦25,000.00", "5%", "VAT 7.5%", "₦51,062.50"))
            .ToList();
        foreach (var design in Designs)
        {
            var model = Sample(design, DocumentPdfModel.Invoice) with
            {
                Lines = lines,
                BusinessName = "Ada Designs and Bespoke Tailoring Services Nigeria Limited, Lagos and Abuja",
                LogoSize = "large",
                Notes = string.Join(" ", Enumerable.Repeat("Thank you for your business.", 30)),
            };
            using var pdf = PdfDocument.Open(Render(model, Pictures));
            Assert.True(pdf.NumberOfPages >= 2, $"{design}: expected more than one page");
            foreach (var page in pdf.GetPages())
                Assert.Contains(Squash(Footer), Squash(page.Text));
        }
    }

    [Fact]
    public void Images_print_only_when_they_are_given()
    {
        foreach (var design in Designs)
        {
            var model = Sample(design, DocumentPdfModel.Invoice);
            var plain = Render(model);
            var branded = Render(model, Pictures);
            Assert.True(branded.Length > plain.Length, $"{design}: the PDF with a logo, signature and stamp should be larger");
        }
    }

    /// <summary>
    /// Page 1 of every design as a PNG, for comparing with the web preview. Written to the plan workspace when it exists
    /// (.superpowers/sdd/2026-11-10-invoice-designs/pdf-previews); otherwise the images are only rendered.
    /// </summary>
    [Fact]
    public void Page_images_of_all_ten_designs()
    {
        var folder = PreviewFolder();
        foreach (var design in Designs)
        {
            var model = RealInvoiceModel(design) with { Stamp = design == "bold" ? "PAID" : null };
            var png = QuestPdfRenderer.Build(model, Pictures)
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 110 }).First();
            Assert.True(png.Length > 10_000, $"{design}: page image looks empty");
            if (folder is not null) File.WriteAllBytes(Path.Combine(folder, $"{design}.png"), png);
        }
    }

    // ------------------------------------------------------------------ realistic documents (final review C1, C2, I1, I2, I3, M1)

    /// <summary>75 characters: wraps in every design, and to more than two lines in the narrow ones.</summary>
    private const string LongName = "Adewale Babatunde Engineering, Fabrication and Maintenance Services Limited";

    public static TheoryData<string, bool> DesignsWithAndWithoutImages()
    {
        var data = new TheoryData<string, bool>();
        foreach (var design in Designs)
        {
            data.Add(design, true);
            data.Add(design, false);
        }
        return data;
    }

    public static TheoryData<string> AllDesigns() => [.. Designs];

    [Theory]
    [MemberData(nameof(DesignsWithAndWithoutImages))]
    public void A_realistic_three_line_invoice_is_one_page(string design, bool images)
    {
        var model = RealInvoiceModel(design);
        var pdf = Render(model, images ? Pictures : null);

        using var document = PdfDocument.Open(pdf);
        Assert.True(document.NumberOfPages == 1, $"{design}: a three-line invoice printed on {document.NumberOfPages} pages");
        var text = Text(pdf);
        AssertContains(text, LongName, "Authorised signature", "Adewale Babatunde", "Managing director", "Balance due",
            "Guaranty Trust Bank", "Account no. 0123456789", "Lekki Phase 1", "Payment within 14 days");
        Assert.DoesNotContain("…", text);
    }

    [Theory]
    [MemberData(nameof(AllDesigns))]
    public void A_receipt_for_an_online_payment_renders_in_every_design(string design)
    {
        var reference = "inv_" + Guid.NewGuid().ToString("N") + "_abcd1234"; // a Paystack reference: 45 characters, no spaces
        Assert.Equal(45, reference.Length);
        foreach (var (method, value) in new[] { (PaymentMethod.Online, reference), (PaymentMethod.BankTransfer, new string('R', 100)) })
        {
            var model = RealReceiptModel(design, method, value);
            var pdf = Render(model, Pictures);
            using var document = PdfDocument.Open(pdf);
            Assert.True(document.NumberOfPages == 1, $"{design}: a receipt printed on {document.NumberOfPages} pages");
            var text = Text(pdf);
            AssertContains(text, LongName, "Receipt", value, "Amount paid", "Balance remaining", "₦500,000");
            Assert.DoesNotContain("Balancedue", text);
        }
    }

    [Fact]
    public void A_receipt_headlines_the_amount_paid_then_the_balance_remaining()
    {
        var model = RealReceiptModel("classic", PaymentMethod.BankTransfer, "TRF-1");
        Assert.Equal(new PdfRow("Amount paid", "₦500,000", true), model.Grand);
        Assert.DoesNotContain(model.Totals, r => r.Strong);
        Assert.DoesNotContain(model.Totals, r => r.Label == "Amount paid");
        Assert.Equal(new[] { new PdfRow("Balance remaining", "₦775,000") }, model.AfterGrand ?? []);

        foreach (var design in Designs)
        {
            var text = Text(Render(RealReceiptModel(design, PaymentMethod.BankTransfer, "TRF-1")));
            var paid = text.IndexOf("Amountpaid", StringComparison.Ordinal);
            var remaining = text.IndexOf("Balanceremaining", StringComparison.Ordinal);
            Assert.True(paid >= 0 && remaining > paid, $"{design}: Amount paid comes before Balance remaining");
        }
    }

    [Fact]
    public void Corporate_respects_the_signature_switch()
    {
        var business = RealBusiness();
        business.ShowSignature = false;
        var model = InvoiceWorkspace.PdfModel(DocumentPdfModel.Invoice, RealInvoice("corporate"), RealInvoice("corporate").Items, business,
            Dates, null, showBranding: false, brandImages: true);
        Assert.Null(model.Signature);
        Assert.DoesNotContain("Authorisedsignature", Text(Render(model)));
    }

    [Theory]
    [MemberData(nameof(AllDesigns))]
    public void Long_business_names_wrap_and_are_never_cut(string design)
    {
        foreach (var name in new[] { LongName, "Adewale Babatunde Engineering Services Limited", new string('W', 60) })
        {
            var text = Text(Render(RealInvoiceModel(design) with { BusinessName = name }, Pictures));
            Assert.Contains(Squash(name), text);
            Assert.DoesNotContain("…", text);
        }
    }

    [Theory]
    [MemberData(nameof(AllDesigns))]
    public void Long_unbroken_values_wrap_instead_of_breaking_the_layout(string design)
    {
        var email = "accounts.receivable.department." + new string('x', 40) + "@adewalebabatunde-engineering.example";
        var model = RealInvoiceModel(design) with
        {
            Number = "INV-" + new string('9', 26),
            Meta = [new("Invoice no.", "INV-" + new string('9', 26), true), new("Reference", new string('R', 100)), .. Dates],
            Contact = [email, "+234 803 000 0000", "Account" + new string('0', 60)],
            Client = new PdfParty("Bola Adeyemi", [email, new string('A', 90)]),
            BankDetails = "Account no. " + new string('1', 80),
        };
        var text = Text(Render(model, Pictures));
        AssertContains(text, email, new string('R', 100), new string('A', 90));
    }

    [Fact]
    public void The_signature_never_prints_on_a_page_without_the_totals()
    {
        foreach (var design in Designs)
        {
            for (var count = 8; count <= 30; count += 2)
            {
                var invoice = RealInvoice(design, count);
                var model = InvoiceWorkspace.PdfModel(DocumentPdfModel.Invoice, invoice, invoice.Items, RealBusiness(), Dates, null,
                    showBranding: false, brandImages: true);
                using var document = PdfDocument.Open(Render(model, Pictures));
                foreach (var page in document.GetPages())
                {
                    // "Subtotal" opens the totals block (Corporate also prints "Balance due" in its header strip, so that can't be the marker).
                    var text = Squash(page.Text);
                    var signature = text.Contains("Authorisedsignature", StringComparison.Ordinal);
                    var totals = text.Contains("Subtotal", StringComparison.Ordinal);
                    Assert.True(signature == totals, $"{design}, {count} lines: page {page.Number} splits the totals from the signature");
                    if (totals) Assert.Contains("Balancedue", text);
                }
            }
        }
    }

    // ------------------------------------------------------------------ through the API

    [Fact]
    public async Task Documents_print_the_letterhead_the_business_chose()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Letterhead");
        await api.MakeProAsync(TenantId(auth), Products.Invoices);
        var body = await UpdateBodyAsync(owner);
        body["legalName"] = Name;
        body["address"] = "12 Allen Avenue\nIkeja, Lagos";
        body["email"] = Email;
        body["phone"] = "0803 000 0000";
        body["taxId"] = "12345678-0001";
        body["bankDetails"] = Bank;
        body["tagline"] = Tagline;
        body["registrationNumber"] = "RC 1234567";
        body["headerLine"] = "tagline";
        body["documentTitle"] = "Tax invoice";
        body["fontStyle"] = "serif";
        body["secondaryColor"] = "#0f766e";
        body["showBankDetails"] = false;
        body["showSignature"] = true;
        body["signatureName"] = "Ada Obi";
        body["signatureRole"] = "Managing director";
        body["footerText"] = Footer;
        await Json(await owner.PutAsJsonAsync("/api/business", body, Ct));
        var client = await ClientAsync(owner);

        Guid lastInvoice = default;
        foreach (var design in Designs)
        {
            var invoice = await Json(await owner.PostAsJsonAsync("/api/invoices", InvoiceBody(client, design), Ct));
            lastInvoice = invoice.GetProperty("id").GetGuid();
            var text = Text(await PdfAsync(await owner.GetAsync($"/api/invoices/{lastInvoice}/pdf", Ct)));
            AssertContains(text, Name, Tagline, "Tax invoice", Footer, "Contact", Email, "0803 000 0000", "12 Allen Avenue, Ikeja, Lagos",
                "Tax ID: 12345678-0001", "Reg. no. RC 1234567", "Authorised signature", "Ada Obi", "Managing director", "Balance due", "₦");
            Assert.DoesNotContain(Squash(Bank), text);
            Assert.True(text.IndexOf(Squash(Email), StringComparison.Ordinal) > text.IndexOf("Contact", StringComparison.Ordinal),
                $"{design}: the email prints in the contact row");
        }

        // Quotes and receipts keep their own titles.
        var quote = await Json(await owner.PostAsJsonAsync("/api/quotes", new
        {
            clientId = client, issueDate = Today, expiryDate = Today.AddDays(14), currency = "NGN", template = "executive", items = Items,
        }, Ct));
        var quoteText = Text(await PdfAsync(await owner.GetAsync($"/api/quotes/{quote.GetProperty("id").GetGuid()}/pdf", Ct)));
        AssertContains(quoteText, Name, Tagline, "Quote", Footer, Email);
        Assert.DoesNotContain("Taxinvoice", quoteText);

        await Json(await owner.PostAsJsonAsync($"/api/invoices/{lastInvoice}/send", new { message = "Thank you." }, Ct));
        var paid = await Json(await owner.PostAsJsonAsync($"/api/invoices/{lastInvoice}/payments",
            new { amountMinor = 500_000, method = "BankTransfer", paidAt = Today, reference = "TRF-1", sendReceipt = false }, Ct));
        var token = paid.GetProperty("publicUrl").GetString()!.Split("/i/")[1];
        var payment = paid.GetProperty("payments")[0].GetProperty("id").GetGuid();
        var receiptText = Text(await PdfAsync(await api.CreateClient().GetAsync($"/api/public/invoices/{token}/receipts/{payment}/pdf", Ct)));
        AssertContains(receiptText, Name, Tagline, "Receipt", Footer, Email, "Received from");
        Assert.DoesNotContain("Taxinvoice", receiptText);

        // Switch the bank details back on and the tax ID off: both take effect on the next PDF.
        body = await UpdateBodyAsync(owner); // the next invoice number has moved on
        body["showBankDetails"] = true;
        body["showTaxId"] = false;
        body["headerLine"] = "registration";
        await Json(await owner.PutAsJsonAsync("/api/business", body, Ct));
        var after = Text(await PdfAsync(await api.CreateClient().GetAsync($"/api/public/invoices/{token}/pdf", Ct)));
        AssertContains(after, Bank, "Reg. no. RC 1234567");
        Assert.DoesNotContain("TaxID", after);
        Assert.DoesNotContain(Squash(Tagline), after);
    }

    [Fact]
    public async Task Premium_designs_need_pro_to_save_and_still_print_after_a_downgrade()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Premium");
        var tenantId = TenantId(auth);
        var client = await ClientAsync(owner);

        Assert.Equal(HttpStatusCode.PaymentRequired,
            (await owner.PostAsJsonAsync("/api/invoices", InvoiceBody(client, "ledger"), Ct)).StatusCode);
        var classic = await Json(await owner.PostAsJsonAsync("/api/invoices", InvoiceBody(client, "classic"), Ct));
        Assert.DoesNotContain("Authorisedsignature",
            Text(await PdfAsync(await owner.GetAsync($"/api/invoices/{classic.GetProperty("id").GetGuid()}/pdf", Ct))));

        await api.MakeProAsync(tenantId, Products.Invoices);
        var ids = new Dictionary<string, Guid>();
        foreach (var design in Designs[5..])
            ids[design] = (await Json(await owner.PostAsJsonAsync("/api/invoices", InvoiceBody(client, design), Ct))).GetProperty("id").GetGuid();

        // Corporate respects the signature switch like every other design (final review I2).
        Assert.DoesNotContain("Authorisedsignature", Text(await PdfAsync(await owner.GetAsync($"/api/invoices/{ids["corporate"]}/pdf", Ct))));

        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionStatus.Cancelled), Ct));
        foreach (var (design, id) in ids)
        {
            var text = Text(await PdfAsync(await owner.GetAsync($"/api/invoices/{id}/pdf", Ct)));
            AssertContains(text, "Balance due", "₦");
            Assert.True(text.Contains("Madewith", StringComparison.Ordinal), $"{design}: the free plan prints the Made with line");
        }
    }

    [Fact]
    public async Task Generator_pdf_prints_the_free_letterhead_fields()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/public/invoice-preview/pdf", new
        {
            business = new
            {
                name = Name, email = Email, address = "12 Allen Avenue, Ikeja", tagline = Tagline, registrationNumber = "BN 998877",
                headerLine = "tagline", secondaryColor = "#0f766e", fontStyle = "serif", documentTitle = "Bill", showSignature = true,
                signatureName = "Ada Obi", signatureRole = "Owner", footerText = Footer, logoUrl = "https://example.com/logo.png",
                signatureImageUrl = "https://example.com/signature.png",
            },
            client = new { name = "Someone" },
            currency = "NGN",
            template = "elegant",
            items = new[] { new { description = "Work", quantity = 1, unitPriceMinor = 1_000_000, discountPercent = 0, taxRatePercent = 7.5 } },
        }, Ct);
        var text = Text(await PdfAsync(response));
        AssertContains(text, Name, Tagline, "Bill", Footer, "Contact", Email, "12 Allen Avenue, Ikeja", "Reg. no. BN 998877",
            "Authorised signature", "Ada Obi", "Owner", "₦");
    }

    // ------------------------------------------------------------------ helpers

    private static readonly object[] Items = [new { description = "Work", quantity = 1, unitPriceMinor = 1_000_000, discountPercent = 0 }];

    private static object InvoiceBody(Guid client, string template) =>
        new { clientId = client, issueDate = Today, dueDate = Today.AddDays(14), currency = "NGN", template, items = Items };

    private static async Task<Guid> ClientAsync(HttpClient owner) =>
        (await Json(await owner.PostAsJsonAsync("/api/clients", new { name = "Bola Ltd", email = "bola@example.com" }, Ct)))
            .GetProperty("id").GetGuid();

    private static Guid TenantId(JsonElement auth) => auth.GetProperty("tenant").GetProperty("id").GetGuid();

    private static async Task<Dictionary<string, object?>> UpdateBodyAsync(HttpClient owner)
    {
        var business = await Json(await owner.GetAsync("/api/business", Ct));
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(business.GetRawText())!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task<byte[]> PdfAsync(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        return await r.Content.ReadAsByteArrayAsync(Ct);
    }

    private static byte[] Render(DocumentPdfModel model, PdfImages? images = null)
    {
        var bytes = QuestPdfRenderer.Build(model, images ?? PdfImages.None).GeneratePdf();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        return bytes;
    }

    /// <summary>All the text of a PDF with the white space removed, so wrapping and letter spacing can't break a match.</summary>
    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return Squash(string.Concat(document.GetPages().Select(p => p.Text)));
    }

    private static string Squash(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    private static void AssertContains(string squashedText, params string[] expected)
    {
        foreach (var value in expected) Assert.Contains(Squash(value), squashedText);
    }

    private static DocumentPdfModel Sample(string design, string kind)
    {
        var invoice = kind == DocumentPdfModel.Invoice;
        var title = kind switch
        {
            DocumentPdfModel.Quote => "Quote",
            DocumentPdfModel.Receipt => "Receipt",
            _ => "Tax invoice",
        };
        List<PdfRow> meta = kind switch
        {
            DocumentPdfModel.Quote => [new("Quote no.", "QUO-0007", true), new("Issued", "10 Nov 2026"), new("Valid until", "24 Nov 2026")],
            DocumentPdfModel.Receipt => [new("Receipt no.", "RCT-0003", true), new("Paid on", "10 Nov 2026"), new("Method", "Bank transfer")],
            _ => [new("Invoice no.", "INV-0042", true), new("Issued", "10 Nov 2026"), new("Due", "24 Nov 2026")],
        };
        List<PdfLine> lines =
        [
            new("Bridal gown, made to measure", "1", "₦850,000.00", null, "VAT 7.5%", "₦850,000.00"),
            new("Groom's agbada, three piece", "2", "₦180,000.00", "10%", "VAT 7.5%", "₦324,000.00"),
            new("Fitting and delivery", "1", "₦25,000.00", null, null, "₦25,000.00"),
        ];
        List<PdfRow> totals =
        [
            new("Subtotal", "₦1,199,000.00"), new("VAT 7.5%", "₦88,050.00"), new("Shipping", "₦12,950.00"),
            new("Total", "₦1,300,000.00", true), new("Amount paid", "-₦50,000.00"),
        ];

        return new DocumentPdfModel(
            kind, title, meta[0].Value, design, "#1d4ed8", "#0f766e", "modern", null, "medium", Name, Tagline, "Reg. no. RC 1234567",
            [Email, "0803 000 0000", "12 Allen Avenue, Ikeja, Lagos", "Tax ID: 12345678-0001", "Reg. no. RC 1234567"],
            new PdfParty("Bola Adeyemi", ["Bola Ltd", "bola@example.com"]),
            kind switch { DocumentPdfModel.Quote => "Prepared for", DocumentPdfModel.Receipt => "Received from", _ => "Bill to" },
            meta, lines, totals, new PdfRow(kind == DocumentPdfModel.Quote ? "Total" : "Balance due", "₦1,250,000.00", true),
            invoice ? Bank : null, "Thank you for your business.", "Payment within 14 days.",
            new PdfSignature("Ada Obi", "Managing director", null, null), Footer, null, ShowBranding: false, "InvoiceLink");
    }

    private static readonly IReadOnlyList<PdfRow> Dates = [new("Issued", "10 Nov 2026"), new("Due", "24 Nov 2026")];

    /// <summary>A business with every letterhead field filled in, a long name and multi-line bank details.</summary>
    private static BusinessSettings RealBusiness() => new()
    {
        LegalName = LongName,
        Address = "Plot 7, Industrial Layout\nOregun, Ikeja\nLagos",
        Email = "accounts@adewalebabatunde.example",
        Phone = "+234 803 000 0000",
        TaxId = "12345678-0001",
        BankDetails = "Guaranty Trust Bank\nAccount name: Adewale Babatunde Engineering Ltd\nAccount no. 0123456789",
        BrandColor = "#1d4ed8",
        Tagline = "Fabrication, installation and maintenance since 1998",
        RegistrationNumber = "RC 1234567",
        HeaderLine = "tagline",
        LogoSize = "large",
        DocumentTitle = "Tax invoice",
        ShowSignature = true,
        SignatureName = "Adewale Babatunde",
        SignatureRole = "Managing director",
        LogoUrl = "https://files.example/logo.png",
        SignatureImageUrl = "https://files.example/signature.png",
        StampImageUrl = "https://files.example/stamp.png",
        FooterText = "Goods remain ours until paid for in full. Thank you for your business.",
    };

    /// <summary>An invoice with a client address of several lines, notes, terms and <paramref name="lineCount"/> lines.</summary>
    private static Invoice RealInvoice(string design, int lineCount = 3)
    {
        string[] descriptions = ["Steel gate, fabricated and installed", "Generator servicing, quarterly", "Site visit and inspection"];
        var invoice = new Invoice
        {
            Number = "INV-0042",
            PublicToken = "token",
            Currency = "NGN",
            Template = design,
            IssueDate = new DateOnly(2026, 11, 10),
            DueDate = new DateOnly(2026, 11, 24),
            Client = new ClientSnapshot
            {
                Name = "Bola Adeyemi",
                Company = "Bola Fashion House Ltd",
                Address = "Plot 14, Admiralty Way\nLekki Phase 1\nLagos 106104",
                Email = "bola.adeyemi@bolafashion.example",
                Phone = "0803 555 0101",
            },
            Notes = "Thank you for your business. Please quote the invoice number with your payment.",
            Terms = "Payment within 14 days. Goods remain ours until paid for in full.",
            Items = Enumerable.Range(0, lineCount).Select(i => new InvoiceItem
            {
                Description = descriptions[i % descriptions.Length],
                Quantity = i % 3 + 1,
                UnitPriceMinor = 25_000_000 * (i % 3 + 1),
                TaxName = i % 3 == 2 ? null : "VAT",
                TaxRatePercent = i % 3 == 2 ? 0 : 7.5m,
                Position = i,
            }).ToList(),
        };
        InvoiceMath.Apply(invoice, invoice.Items.Cast<DocumentLine>().ToList());
        invoice.AmountPaidMinor = 5_000_000;
        invoice.BalanceMinor = invoice.TotalMinor - invoice.AmountPaidMinor;
        return invoice;
    }

    private static DocumentPdfModel RealInvoiceModel(string design)
    {
        var invoice = RealInvoice(design);
        return InvoiceWorkspace.PdfModel(DocumentPdfModel.Invoice, invoice, invoice.Items, RealBusiness(), Dates, null,
            showBranding: false, brandImages: true);
    }

    /// <summary>A ₦500,000 payment on a ₦1,325,000 invoice with ₦50,000 paid before.</summary>
    private static DocumentPdfModel RealReceiptModel(string design, PaymentMethod method, string reference)
    {
        var invoice = RealInvoice(design);
        invoice.Items = [new InvoiceItem { Description = "Steel gate", Quantity = 1, UnitPriceMinor = 132_500_000 }];
        InvoiceMath.Apply(invoice, invoice.Items.Cast<DocumentLine>().ToList());
        var payment = new InvoicePayment
        {
            AmountMinor = 50_000_000, Method = method, Reference = reference, PaidAt = new DateOnly(2026, 11, 12), ReceiptNumber = "RCT-0003",
            Note = "Part payment for the steel gate.",
        };
        return InvoiceWorkspace.ReceiptPdfModel(invoice, payment, 5_000_000, RealBusiness(), showBranding: false, brandImages: true);
    }

    /// <summary>A logo, a signature and a stamp drawn with QuestPDF, so the tests need no image files.</summary>
    private static readonly PdfImages Pictures = new(
        Picture(220, 80, c => c.Background("#1d4ed8").AlignCenter().AlignMiddle().Text("ADA").FontSize(40).ExtraBold().FontColor(Colors.White)),
        Picture(200, 60, c => c.AlignCenter().AlignMiddle().Text("Ada Obi").FontFamily(InvoiceFonts.Serif).FontSize(30).Italic().FontColor("#1e3a8a")),
        Picture(120, 120, c => c.Padding(6).Border(4).BorderColor("#b83227").AlignCenter().AlignMiddle().Text("APPROVED").FontSize(16).Bold()
            .FontColor("#b83227")));

    private static byte[] Picture(float width, float height, Action<IContainer> draw)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        InvoiceFonts.EnsureRegistered();
        return Document.Create(d => d.Page(page =>
            {
                page.Size(width, height);
                page.Margin(0);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontFamily(InvoiceFonts.Modern));
                page.Content().Element(draw);
            }))
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 }).First();
    }

    private static string? PreviewFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var workspace = Path.Combine(dir.FullName, ".superpowers", "sdd", "2026-11-10-invoice-designs");
            if (!Directory.Exists(workspace)) continue;
            return Directory.CreateDirectory(Path.Combine(workspace, "pdf-previews")).FullName;
        }
        return null;
    }
}
