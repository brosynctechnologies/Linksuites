using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Saas.Application.Common;
using Saas.Application.Invoices;
using Saas.Infrastructure.Storage;
using static Saas.Infrastructure.Invoices.DocumentColors;

namespace Saas.Infrastructure.Invoices;

/// <summary>
/// The images a document prints, already decoded (once per render: each is drawn from the same decoded image). The model only
/// carries their URLs when the plan allows them.
/// </summary>
public sealed class PdfImages
{
    public static readonly PdfImages None = new((Image?)null, null, null);

    public PdfImages(Image? logo, Image? signature, Image? stamp)
    {
        Logo = logo;
        Signature = signature;
        Stamp = stamp;
    }

    /// <summary>From encoded files (PNG, JPEG or WebP), decoded here once.</summary>
    public PdfImages(byte[]? logo, byte[]? signature, byte[]? stamp) : this(Decode(logo), Decode(signature), Decode(stamp)) { }

    public Image? Logo { get; }
    public Image? Signature { get; }
    public Image? Stamp { get; }

    private static Image? Decode(byte[]? bytes) => bytes is null ? null : Image.FromBinaryData(bytes);
}

/// <summary>
/// Renders invoices, quotes and receipts with QuestPDF (Community licence: free under $1M yearly revenue).
/// One composer per design over shared blocks; each design matches its web preview in invoice-web/src/components/invoice-designs
/// (sizes there are in rem; here 1rem = 12pt, 1px = 0.75pt).
/// </summary>
/// <remarks>
/// Layout rules every design keeps: a short invoice fits on one page; user text (names, references, emails, account numbers)
/// is never laid out in an auto-sized item, so long unbroken values wrap instead of failing the layout; the totals, the
/// payment and contact blocks, notes and terms and the signature move to the next page together when they don't fit.
/// </remarks>
public sealed class QuestPdfRenderer(
    IHttpClientFactory httpFactory, AppUrls urls, IOptions<StorageOptions> storage, ILogger<QuestPdfRenderer> logger) : IDocumentPdfRenderer
{
    static QuestPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        InvoiceFonts.EnsureRegistered();
    }

    private const float Margin = 40;
    private const float PageWidth = 595.28f;
    private const float ContentWidth = PageWidth - 2 * Margin;
    private const float TotalsWidth = 240;
    private const float Hair = 0.75f;

    /// <summary>The width of the name column in two-column headers (55% of the content width).</summary>
    private const float HeaderColumn = ContentWidth * 0.55f;

    /// <summary>Executive's accent sidebar: 40% of the sheet, and the room its name has inside the padding.</summary>
    private const float SidebarWidth = PageWidth * 0.4f;
    private const float SidebarText = SidebarWidth - Margin - 18;

    private const string MadeWith = "free invoices in 60 seconds";

    public async Task<byte[]> RenderAsync(DocumentPdfModel m, CancellationToken ct)
    {
        var images = new PdfImages(
            await LoadImageAsync(m.LogoUrl, ct), await LoadImageAsync(m.Signature?.ImageUrl, ct), await LoadImageAsync(m.Signature?.StampImageUrl, ct));
        return Build(m, images).GeneratePdf();
    }

    /// <summary>The QuestPDF document, before it becomes a PDF (also lets tooling render page images).</summary>
    public static IDocument Build(DocumentPdfModel m, PdfImages images)
    {
        InvoiceFonts.EnsureRegistered();
        var x = new Ctx(m, images);

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Colors.White);
            // Inter swaps a hyphen between capitals ("INV-0042") for a glyph with no Unicode value, so copied text would be wrong.
            page.DefaultTextStyle(t => t.FontFamily(Families(x.Body)).FontSize(9.75f).LineHeight(1.3f).FontColor(C(Graphite))
                .DisableFontFeature("calt"));

            // Designs place their own 40pt margins so bands can reach the edge on page 1; later pages start 40pt down.
            page.Header().SkipOnce().Height(Margin);
            page.Background().Element(c => Decoration(c, x));
            page.Content().Column(col => Compose(col, x));
            page.Footer().Element(c => Footer(c, x));
        }));
    }

    // ------------------------------------------------------------------ the ten designs

    private static void Compose(ColumnDescriptor page, Ctx x)
    {
        switch (x.M.Template)
        {
            case "modern": Modern(page, x); break;
            case "minimal": Minimal(page, x); break;
            case "bold": Bold(page, x); break;
            case "elegant": Elegant(page, x); break;
            case "executive": Executive(page, x); break;
            case "corporate": Corporate(page, x); break;
            case "ledger": Ledger(page, x); break;
            case "studio": Studio(page, x); break;
            case "prestige": Prestige(page, x); break;
            default: Classic(page, x); break;
        }
    }

    /// <summary>Page-level shapes: Bold's accent edge on every page, Studio's corner shape on page 1.</summary>
    private static void Decoration(IContainer c, Ctx x)
    {
        if (x.M.Template == "bold")
            c.AlignLeft().Width(10).ExtendVertical().Background(C(x.Accent));
        else if (x.M.Template == "studio")
        {
            // Behind: a second-accent rectangle 32% × 18% of the sheet width; in front: an accent quarter circle, radius 24%.
            float w = PageWidth * 0.32f, h = PageWidth * 0.18f, r = PageWidth * 0.24f;
            c.ShowOnce().AlignRight().AlignTop().Width(w).Height(r).Layers(l =>
            {
                l.PrimaryLayer().AlignTop().Height(h).Background(C(x.Second));
                l.Layer().AlignRight().AlignTop().Width(r).Height(r).Background(C(x.Accent)).CornerRadiusBottomLeft(r);
            });
        }
    }

    /// <summary>Classic: logo, name and line left; title in accent text and the meta grid right; 2px accent rule under the header.</summary>
    private static void Classic(ColumnDescriptor page, Ctx x) => Padded(page, col =>
    {
        col.Item().BorderBottom(1.5f).BorderColor(C(x.Accent)).PaddingBottom(15).Row(r =>
        {
            r.RelativeItem(55).Column(left => Letterhead(left, x, HeaderColumn));
            r.RelativeItem(45).AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text(x.M.Title).FontFamily(Families(x.Heading)).FontSize(24).SemiBold().LineHeight(1).FontColor(C(x.AccentText));
                right.Item().PaddingTop(6).AlignRight().Element(c => MetaStack(c, x));
            });
        });
        col.Item().PaddingTop(15).Element(c => BillTo(c, x));
        col.Item().Element(c => LineTable(c, x, new TableStyle(HeadFill: Tint(x.Accent, 0.1))));
        Closing(col, x, c => c.Column(t =>
        {
            var style = new TotalsStyle(TotalRule: x.Accent, TotalRuleWidth: 1.5f);
            TotalsRows(t, x, style);
            t.Item().Element(c => x.IsQuote ? c.PaddingTop(4.5f).BorderTop(1.5f).BorderColor(C(x.Accent)).PaddingTop(7.5f) : c.PaddingTop(4.5f))
                .Element(c => GrandRow(c, x, labelColor: x.AccentText, valueColor: x.AccentText, valueSize: 16.5f));
            AfterRows(t, x, style);
        }), c => InfoBlocks(c, x, rule: Hairline));
    });

    /// <summary>Modern: full-width accent band (logo on a white chip) with a second-accent strip; ink table head, zebra rows.</summary>
    private static void Modern(ColumnDescriptor page, Ctx x)
    {
        page.Item().Background(C(x.Accent)).PaddingHorizontal(Margin).PaddingTop(24).PaddingBottom(16).Row(r =>
        {
            r.RelativeItem(55).Column(left => Letterhead(left, x, HeaderColumn, color: x.OnAccent, lineColor: x.OnAccent, logoChip: true));
            r.RelativeItem(45).AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text(x.M.Title).FontFamily(Families(x.Heading)).FontSize(24).SemiBold().LineHeight(1).FontColor(C(x.OnAccent));
                // Labels on the accent use the full on-accent colour (no fading), so they keep the band's contrast.
                right.Item().PaddingTop(6).AlignRight().Element(c => MetaStack(c, x, x.OnAccent, x.OnAccent));
            });
        });
        page.Item().Height(3).Background(C(x.Second));
        Padded(page, col =>
        {
            col.Item().PaddingTop(15).Element(c => BillTo(c, x));
            col.Item().Element(c => LineTable(c, x, new TableStyle(HeadFill: Graphite, HeadText: White, Zebra: true)));
            Closing(col, x, c => c.Column(t =>
            {
                var style = new TotalsStyle(TotalRule: Hairline);
                TotalsRows(t, x, style);
                t.Item().PaddingTop(6).BorderTop(1.5f).BorderColor(C(x.Accent)).Background(C(Tint(x.Accent, 0.12)))
                    .PaddingHorizontal(6).PaddingVertical(7.5f).Element(c => GrandRow(c, x, valueColor: x.AccentText, valueSize: 16.5f));
                AfterRows(t, x, style);
            }), c => InfoBlocks(c, x, rule: Hairline));
        }, top: 0);
    }

    /// <summary>Minimal: no colour but the emphasised figure; light large title; hairline rules only.</summary>
    private static void Minimal(ColumnDescriptor page, Ctx x) => Padded(page, col =>
    {
        col.Item().PaddingBottom(22).Row(r =>
        {
            r.RelativeItem(55).Column(left => Letterhead(left, x, HeaderColumn, nameWeight: 600));
            r.RelativeItem(45).AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text(x.M.Title).FontFamily(Families(x.Heading)).FontSize(27).Light().LineHeight(1).FontColor(C(Graphite));
                right.Item().PaddingTop(6).AlignRight().Element(c => MetaStack(c, x, Slate));
            });
        });
        col.Item().Element(c => BillTo(c, x, Slate));
        col.Item().Element(c => LineTable(c, x, new TableStyle(HeadRule: Graphite)));
        Closing(col, x, c => c.Column(t =>
        {
            var style = new TotalsStyle(TotalRule: Graphite, LabelColor: Slate);
            TotalsRows(t, x, style);
            t.Item().Element(c => x.IsQuote ? c.PaddingTop(4.5f).BorderTop(Hair).BorderColor(C(Graphite)).PaddingTop(7.5f) : c.PaddingTop(4.5f))
                .Element(c => GrandRow(c, x, valueColor: x.AccentText, valueSize: 16.5f, valueWeight: 600));
            AfterRows(t, x, style);
        }), c => InfoBlocks(c, x, labelColor: Slate, top: 22));
    });

    /// <summary>Bold: accent edge down the left; logo beside a larger extra-bold name; a very large accent title; filled total bar.</summary>
    private static void Bold(ColumnDescriptor page, Ctx x) => Padded(page, col =>
    {
        col.Item().Row(r =>
        {
            if (x.Images.Logo is not null) r.AutoItem().PaddingRight(9).Element(c => Logo(c, x));
            r.RelativeItem().Column(name =>
            {
                name.Item().Element(c => Name(c, x, ContentWidth - x.LogoRoom, size: 27, weight: 800, lineHeight: 1.05f, letterSpacing: -0.02f));
                LetterLine(name, x);
            });
        });
        col.Item().PaddingTop(12).Text(x.M.Title).FontFamily(Families(x.Heading)).FontSize(36).ExtraBold().LineHeight(0.9f)
            .LetterSpacing(-0.02f).FontColor(C(x.AccentText));
        col.Item().PaddingTop(10).PaddingBottom(12).Element(c => MetaRow(c, x));
        col.Item().Element(c => BillTo(c, x));
        col.Item().Element(c => LineTable(c, x, new TableStyle(HeadFill: x.Accent, HeadText: x.OnAccent)));
        Closing(col, x, c => c.Column(t =>
        {
            var style = new TotalsStyle(TotalRule: Hairline);
            TotalsRows(t, x, style);
            t.Item().PaddingTop(6).Background(C(x.Accent)).CornerRadius(2).PaddingHorizontal(9).PaddingVertical(7.5f)
                .Element(c => GrandRow(c, x, labelColor: x.OnAccent, valueColor: x.OnAccent, valueSize: 18, valueWeight: 800, labelWeight: 700));
            AfterRows(t, x, style);
        }), c => InfoBlocks(c, x, rule: x.Accent, ruleWidth: 1.5f));
    });

    /// <summary>Elegant: centred, serif headings; the title framed by fine double accent rules; centred totals.</summary>
    private static void Elegant(ColumnDescriptor page, Ctx x) => Padded(page, col =>
    {
        col.Item().Column(h => Letterhead(h, x, ContentWidth, nameWeight: 600, center: true));
        col.Item().PaddingTop(10).Element(c => DoubleRule(c, x.Accent));
        col.Item().PaddingVertical(4.5f).AlignCenter().Text(x.M.Title).FontFamily(Families(InvoiceFonts.Serif)).FontSize(21).Italic()
            .LineHeight(1.25f).FontColor(C(x.AccentText));
        col.Item().Element(c => DoubleRule(c, x.Accent));
        col.Item().PaddingTop(10).PaddingBottom(12).Element(c => MetaRow(c, x, center: true));
        col.Item().Element(c => BillTo(c, x));
        col.Item().Element(c => LineTable(c, x, new TableStyle(HeadText: x.AccentText, HeadRule: x.Accent)));
        Closing(col, x, center: true, top: 15, totals: c => c.Column(t =>
        {
            var style = new TotalsStyle(TotalRule: Hairline);
            TotalsRows(t, x, style);
            t.Item().PaddingTop(6).Element(c => DoubleRule(c, x.Accent));
            t.Item().PaddingTop(7.5f).Element(c => GrandRow(c, x, labelSize: 12, labelSerifItalic: true, valueSize: 16.5f, valueWeight: 600, valueSerif: true));
            AfterRows(t, x, style);
        }), info: c => InfoBlocks(c, x, rule: Hairline));
    });

    /// <summary>Executive: an accent sidebar (left 40%) with logo, name and line; title and meta on the right; boxed totals.</summary>
    private static void Executive(ColumnDescriptor page, Ctx x)
    {
        page.Item().Row(r =>
        {
            r.ConstantItem(SidebarWidth).Background(C(x.Accent)).PaddingLeft(Margin).PaddingRight(18).PaddingTop(24).PaddingBottom(16)
                .Column(left => Letterhead(left, x, SidebarText, color: x.OnAccent, lineColor: x.OnAccent, logoChip: true, nameSize: 18));
            r.RelativeItem().PaddingHorizontal(Margin).PaddingTop(24).PaddingBottom(16).AlignBottom().Column(right =>
            {
                right.Item().Text(x.M.Title).FontFamily(Families(x.Heading)).FontSize(24).SemiBold().LineHeight(1).FontColor(C(x.AccentText));
                right.Item().PaddingTop(6).AlignLeft().Element(c => MetaStack(c, x));
            });
        });
        page.Item().Height(3).Background(C(x.Second));
        Padded(page, col =>
        {
            col.Item().PaddingTop(15).Element(c => BillTo(c, x));
            col.Item().Element(c => LineTable(c, x, new TableStyle(HeadFill: x.Second, HeadText: x.OnSecond)));
            Closing(col, x, c => c.Border(Hair).BorderColor(C(Rule)).CornerRadius(4).Background(C(Fill))
                .Padding(12).Column(t =>
                {
                    var style = new TotalsStyle(TotalRule: Rule);
                    TotalsRows(t, x, style);
                    t.Item().PaddingTop(6).BorderTop(1.5f).BorderColor(C(x.Second)).PaddingTop(7.5f)
                        .Element(c => GrandRow(c, x, valueColor: x.SecondText, valueSize: 16.5f));
                    AfterRows(t, x, style);
                }), c => InfoBlocks(c, x, rule: Hairline));
        }, top: 0);
    }

    /// <summary>Corporate: title top right; a meta strip (number, dates, balance due) between two accent rules; a grid table.</summary>
    private static void Corporate(ColumnDescriptor page, Ctx x) => Padded(page, col =>
    {
        col.Item().PaddingBottom(12).Row(r =>
        {
            r.RelativeItem(55).Column(left => Letterhead(left, x, HeaderColumn));
            r.RelativeItem(45).AlignRight().Text(x.M.Title).FontFamily(Families(x.Heading)).FontSize(24).SemiBold().LineHeight(1)
                .FontColor(C(x.AccentText));
        });
        // Four equal columns; a receipt's extra entries (invoice, method, reference) wrap onto a second row.
        col.Item().PaddingBottom(12).BorderTop(Hair).BorderBottom(Hair).BorderColor(C(x.Accent)).PaddingVertical(7.5f).Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                for (var i = 0; i < 4; i++) cd.RelativeColumn();
            });
            var cells = x.M.Meta.Select(m => (m.Label, m.Value, Color: Graphite)).Append((x.M.Grand.Label, x.M.Grand.Value, Color: x.AccentText));
            foreach (var (label, value, color) in cells)
                t.Cell().PaddingRight(12).PaddingVertical(1.5f).Element(c => StripCell(c, x, label, value, color));
        });
        col.Item().Element(c => BillTo(c, x));
        col.Item().Element(c => LineTable(c, x, new TableStyle(HeadFill: Fill, HeadRule: Rule, ColumnRules: Rule, Outer: Rule)));
        Closing(col, x, c => c.Column(t =>
        {
            var style = new TotalsStyle(RowRule: Hairline);
            TotalsRows(t, x, style);
            t.Item().PaddingTop(3).BorderTop(1.5f).BorderColor(C(x.Accent)).Background(C(Tint(x.Accent, 0.1))).Padding(6)
                .Element(c => GrandRow(c, x, valueColor: x.AccentText, valueSize: 15));
            AfterRows(t, x, style);
        }), c => InfoBlocks(c, x, rule: Hairline));
    });

    /// <summary>Ledger: compact letterhead with the registration number boxed; a ruled title bar; every row ruled; double-ruled totals box.</summary>
    private static void Ledger(ColumnDescriptor page, Ctx x)
    {
        var regBox = x.M.RegistrationText;
        var contact = regBox is null ? x.M.Contact : x.M.Contact.Where(c => c != regBox).ToList();
        Padded(page, col =>
        {
            col.Item().PaddingBottom(12).Row(r =>
            {
                if (x.Images.Logo is not null) r.AutoItem().PaddingRight(9).Element(c => Logo(c, x));
                r.RelativeItem().Column(name =>
                {
                    name.Item().Element(c => Name(c, x, ContentWidth - x.LogoRoom - (regBox is null ? 0 : LedgerRegBox)));
                    LetterLine(name, x);
                });
                if (regBox is not null)
                    r.AutoItem().PaddingLeft(12).MaxWidth(LedgerRegBox - 12).AlignTop().Border(Hair).BorderColor(C(Graphite)).CornerRadius(2)
                        .PaddingVertical(3).PaddingHorizontal(6).Text(regBox).FontSize(10.5f).SemiBold().Tabular(x.Tabular).FontColor(C(Graphite));
            });
            col.Item().PaddingBottom(12).BorderTop(Hair).BorderColor(C(Graphite)).Element(c => c.BorderBottom(1.5f).BorderColor(C(x.Accent)))
                .PaddingVertical(6).Row(r =>
                {
                    r.AutoItem().AlignBottom().Text(x.M.Title).FontFamily(Families(x.Heading)).FontSize(18).SemiBold().LineHeight(1.25f)
                        .FontColor(C(Graphite));
                    r.RelativeItem().PaddingLeft(18).AlignBottom().Element(c => MetaRow(c, x, gap: 18, right: true));
                });
            col.Item().Element(c => BillTo(c, x));
            col.Item().Element(c => LineTable(c, x, new TableStyle(HeadFill: Tint(x.Accent, 0.06), HeadRule: Graphite, HeadRuleWidth: 1.5f,
                RowRule: LedgerRule, ColumnRules: Rule)));
            Closing(col, x, c => c.Border(Hair).BorderColor(C(Graphite)).Padding(1.5f).Border(Hair)
                .BorderColor(C(Graphite)).Padding(9).Column(t =>
                {
                    var style = new TotalsStyle(RowRule: Hairline, RowPadding: 3);
                    TotalsRows(t, x, style);
                    t.Item().PaddingTop(6).Row(r =>
                    {
                        r.RelativeItem().AlignBottom().Text(x.M.Grand.Label).FontSize(10.5f).SemiBold().FontColor(C(Graphite));
                        r.AutoItem().Column(fig =>
                        {
                            fig.Item().AlignRight().Text(x.M.Grand.Value).FontSize(15).Bold().Tabular(x.Tabular).FontColor(C(Graphite));
                            fig.Item().PaddingTop(1.5f).Element(c => DoubleRule(c, Graphite));
                        });
                    });
                    AfterRows(t, x, style);
                }), c => InfoBlocks(c, x, rule: LedgerRule, contact: contact));
        });
    }

    /// <summary>Room Ledger keeps for its boxed registration number beside the name.</summary>
    private const float LedgerRegBox = 132;

    /// <summary>Studio: a geometric corner shape (page decoration), an oversized name, open table, balance due in an accent card.</summary>
    private static void Studio(ColumnDescriptor page, Ctx x) => Padded(page, col =>
    {
        const float column = PageWidth * 0.58f;
        col.Item().MaxWidth(column).Column(h =>
        {
            if (x.Images.Logo is not null) h.Item().PaddingBottom(9).Element(c => Logo(c, x, StudioLogoHeight));
            h.Item().Element(c => Name(c, x, column, size: 30, weight: 800, lineHeight: 1.05f, letterSpacing: -0.02f, stepTo: 22));
            LetterLine(h, x);
        });
        // The title row runs the full width below the corner shape: the title, then number and dates beside it (wrapping under it
        // when they run out of room).
        col.Item().PaddingTop(12).PaddingBottom(12).Row(r =>
        {
            r.AutoItem().AlignBottom().Text(x.M.Title).FontFamily(Families(x.Heading)).FontSize(18).SemiBold().LineHeight(1.1f).FontColor(C(Graphite));
            r.RelativeItem().PaddingLeft(24).AlignBottom().Element(c => MetaRow(c, x));
        });
        col.Item().Element(c => BillTo(c, x));
        col.Item().Element(c => LineTable(c, x, new TableStyle(HeadText: Muted, HeadRule: Graphite, HeadRuleWidth: 1.5f, RowPadding: 6)));
        Closing(col, x, c => c.Column(t =>
        {
            var style = new TotalsStyle(TotalRule: Hairline);
            TotalsRows(t, x, style);
            t.Item().PaddingTop(6).Background(C(x.Accent)).CornerRadius(8).PaddingHorizontal(15).PaddingVertical(9).Column(card =>
            {
                card.Item().Text(x.M.Grand.Label).FontSize(10.5f).SemiBold().FontColor(C(x.OnAccent));
                card.Item().PaddingTop(3).Text(x.M.Grand.Value).FontSize(21).ExtraBold().LineHeight(1).Tabular(x.Tabular).FontColor(C(x.OnAccent));
            });
            AfterRows(t, x, style);
        }), c => InfoBlocks(c, x, rule: Hairline));
    });

    /// <summary>Prestige: centred and serif; logo or monogram in a fine double accent frame; second-accent rules; serif figures.</summary>
    private static void Prestige(ColumnDescriptor page, Ctx x) => Padded(page, col =>
    {
        col.Item().PaddingBottom(6).AlignCenter().Border(Hair).BorderColor(C(x.Accent)).Padding(4.5f).Border(Hair).BorderColor(C(x.Accent))
            .MinWidth(42).MinHeight(42).Padding(4.5f).AlignCenter().AlignMiddle().Element(c =>
            {
                if (x.Images.Logo is not null) Logo(c, x);
                else c.Text(Initials(x.M.BusinessName)).FontFamily(Families(InvoiceFonts.Serif)).FontSize(18).SemiBold().LineHeight(1)
                    .FontColor(C(x.AccentText));
            });
        col.Item().AlignCenter().Element(c => Name(c, x, ContentWidth, weight: 600, letterSpacing: 0.02f, center: true));
        LetterLine(col, x, center: true, serifItalic: true);
        col.Item().PaddingVertical(6).AlignCenter().Width(ContentWidth * 0.4f).LineHorizontal(Hair).LineColor(C(x.Second));
        col.Item().AlignCenter().Text(x.M.Title).FontFamily(Families(InvoiceFonts.Serif)).FontSize(18).LineHeight(1.25f).LetterSpacing(0.08f)
            .FontColor(C(x.AccentText));
        col.Item().PaddingTop(6).PaddingBottom(10).Element(c => MetaRow(c, x, center: true));
        col.Item().Element(c => BillTo(c, x));
        col.Item().Element(c => LineTable(c, x, new TableStyle(HeadText: Muted, HeadRule: x.Second, RowPadding: 6.5f, SerifFigures: true)));
        Closing(col, x, top: 12, totals: c => c.Column(t =>
        {
            var style = new TotalsStyle(TotalRule: Hairline, SerifFigures: true);
            TotalsRows(t, x, style);
            t.Item().PaddingTop(6).BorderTop(Hair).BorderBottom(Hair).BorderColor(C(x.Second)).PaddingVertical(7.5f)
                .Element(c => GrandRow(c, x, labelSize: 12, labelSerifItalic: true, valueColor: x.AccentText, valueSize: 18, valueWeight: 600, valueSerif: true));
            AfterRows(t, x, style);
        }), info: c => InfoBlocks(c, x, rule: Hairline), signatureRule: x.Second);
    });

    // ------------------------------------------------------------------ shared blocks

    private static void Padded(ColumnDescriptor page, Action<ColumnDescriptor> body, float top = Margin) =>
        page.Item().PaddingHorizontal(Margin).PaddingTop(top).Column(body);

    /// <summary>
    /// The end of every document: the totals on the right (<see cref="TotalsWidth"/> wide) with the signature block beside them on
    /// the left, bottom-aligned, then payment details, contact, notes and terms. The signature sits with the amounts it
    /// authorises, and that row never splits. The whole group moves to the next page when it doesn't fit; only a group taller
    /// than a page splits, after the totals row. <paramref name="center"/> centres the totals when there is no signature (Elegant).
    /// </summary>
    private static void Closing(
        ColumnDescriptor col, Ctx x, Action<IContainer> totals, Action<IContainer> info, string signatureRule = Graphite, bool center = false,
        float top = 12) =>
        col.Item().PreventPageBreak().Column(group =>
        {
            group.Item().PaddingTop(top).PreventPageBreak().Row(r =>
            {
                if (x.M.Signature is not null)
                {
                    r.RelativeItem().AlignLeft().AlignBottom().Element(c => Signature(c, x, signatureRule));
                    r.ConstantItem(15);
                }
                else r.RelativeItem();
                r.ConstantItem(TotalsWidth).Element(totals);
                if (x.M.Signature is null && center) r.RelativeItem();
            });
            group.Item().Element(info);
        });

    /// <summary>Logo (when printed), name and the line under the name. <paramref name="width"/> is the room the name has.</summary>
    private static void Letterhead(
        ColumnDescriptor col, Ctx x, float width, string color = Graphite, string lineColor = Slate, int nameWeight = 700, bool center = false,
        bool logoChip = false, float nameSize = 21)
    {
        if (x.Images.Logo is not null)
        {
            var item = col.Item().PaddingBottom(9);
            if (center) item = item.AlignCenter();
            else item = item.AlignLeft();
            if (logoChip) item = item.Background(Colors.White).CornerRadius(4).Padding(4.5f);
            item.Element(c => Logo(c, x));
        }
        col.Item().Element(c => Name(c, x, width, size: nameSize, weight: nameWeight, color: color, center: center));
        LetterLine(col, x, lineColor, center);
    }

    private static void Logo(IContainer c, Ctx x, float? maxHeight = null)
    {
        if (x.Images.Logo is { } logo) c.MaxWidth(x.LogoWidth).MaxHeight(Math.Min(x.LogoHeight, maxHeight ?? x.LogoHeight)).Image(logo).FitArea();
    }

    /// <summary>Studio stacks the logo above its oversized name, so there the logo is at most the medium height (50pt).</summary>
    private const float StudioLogoHeight = 50;

    /// <summary>
    /// The business name: 21pt bold by default. Never cut: it wraps, and a name that would take more than two lines at the
    /// design's size steps down one size (see <see cref="NameSize"/>; Studio's oversized name steps to <paramref name="stepTo"/>).
    /// </summary>
    private static void Name(
        IContainer c, Ctx x, float width, float size = 21, int weight = 700, float lineHeight = 1.15f, string color = Graphite,
        float letterSpacing = 0, bool center = false, float? stepTo = null)
    {
        var text = c.Text(x.M.BusinessName).FontFamily(Families(x.Heading)).FontSize(NameSize(x.M.BusinessName, size, width, stepTo))
            .LineHeight(lineHeight).FontColor(C(color));
        Weight(text, weight);
        if (letterSpacing != 0) text.LetterSpacing(letterSpacing);
        if (center) text.AlignCenter();
    }

    /// <summary>
    /// How many characters of a name fit on two lines <paramref name="width"/> points wide at <paramref name="size"/>: about 0.6em
    /// a character with 90% of each line used, so 3 × width ÷ size. The web documents use the same rule (nameCapacity in model.ts).
    /// </summary>
    public static int NameCapacity(float width, float size) => (int)(3 * width / size);

    /// <summary>The name's size: one size down (80%, rounded, unless the design sets it) when it would take more than two lines.</summary>
    public static float NameSize(string name, float size, float width, float? stepTo = null) =>
        name.Length > NameCapacity(width, size) ? stepTo ?? MathF.Round(size * 0.8f) : size;

    private static void LetterLine(ColumnDescriptor col, Ctx x, string color = Slate, bool center = false, bool serifItalic = false)
    {
        if (x.M.HeaderLineText is not { } line) return;
        var text = col.Item().PaddingTop(3).Text(line).FontSize(10.5f).FontColor(C(color));
        if (center) text.AlignCenter();
        if (serifItalic) text.FontFamily(Families(InvoiceFonts.Serif)).Italic();
    }

    /// <summary>Number and dates as label/value rows (label left, value right). Long values wrap within the 200pt box.</summary>
    private static void MetaStack(IContainer c, Ctx x, string labelColor = Muted, string? valueColor = null) => c.Width(200).Column(col =>
    {
        foreach (var meta in x.M.Meta)
        {
            col.Item().PaddingVertical(0.75f).Row(r =>
            {
                r.ConstantItem(66).Text(meta.Label).FontColor(C(labelColor));
                var value = r.RelativeItem().PaddingLeft(6).Text(meta.Value).AlignRight().Tabular(x.Tabular).FontColor(C(valueColor ?? Graphite));
                if (meta.Strong) value.SemiBold();
            });
        }
    });

    /// <summary>Number and dates as a run of cells, label above value, that wraps onto a second line when it runs out of room.</summary>
    private static void MetaRow(IContainer c, Ctx x, bool center = false, float gap = 24, string labelColor = Muted, bool right = false) =>
        c.Inlined(items =>
        {
            items.HorizontalSpacing(gap);
            items.VerticalSpacing(6);
            items.BaselineTop();
            if (center) items.AlignCenter();
            else if (right) items.AlignRight();
            else items.AlignLeft();
            foreach (var meta in x.M.Meta)
            {
                items.Item().Column(cell =>
                {
                    cell.Item().Text(meta.Label).FontSize(9).FontColor(C(labelColor));
                    var value = cell.Item().Text(meta.Value).Tabular(x.Tabular);
                    if (meta.Strong) value.SemiBold();
                });
            }
        });

    private static void StripCell(IContainer c, Ctx x, string label, string value, string valueColor) => c.Column(cell =>
    {
        cell.Item().Text(label).FontSize(9).FontColor(C(Muted));
        cell.Item().Text(value).FontSize(10.5f).SemiBold().Tabular(x.Tabular).FontColor(C(valueColor));
    });

    /// <summary>Bill to (Prepared for, Received from) with the status stamp on the right.</summary>
    private static void BillTo(IContainer c, Ctx x, string labelColor = Muted) => c.PaddingBottom(12).Row(r =>
    {
        r.RelativeItem().Column(col =>
        {
            col.Item().PaddingBottom(3).Text(x.M.ClientLabel).FontSize(9).SemiBold().FontColor(C(labelColor));
            col.Item().Text(x.M.Client.Name).SemiBold().FontColor(C(Graphite));
            foreach (var line in x.M.Client.Lines) col.Item().Text(line).FontColor(C(Slate));
        });
        if (x.M.Stamp is { } stamp) r.AutoItem().PaddingTop(14).PaddingRight(12).Element(s => Stamp(s, stamp));
    });

    /// <summary>The rubber stamp: rotated, double-ruled, in the status colour. The only uppercase label on the document.</summary>
    private static void Stamp(IContainer c, string stamp)
    {
        var color = C(stamp switch
        {
            "PAID" or "ACCEPTED" => "#13795b",
            "OVERDUE" or "DECLINED" => "#b83227",
            "EXPIRED" => "#a86a12",
            _ => Slate,
        });
        c.Rotate(stamp is "VOID" or "DECLINED" ? -11 : -7).Border(1.5f).BorderColor(color).Padding(1.5f).Border(Hair).BorderColor(color)
            .PaddingVertical(3).PaddingHorizontal(9).Text(stamp).FontSize(16).Bold().LetterSpacing(0.08f).FontColor(color);
    }

    private sealed record TableStyle(
        string? HeadFill = null, string HeadText = Graphite, string? HeadRule = null, float HeadRuleWidth = Hair, bool Zebra = false,
        string RowRule = Hairline, string? ColumnRules = null, string? Outer = null, float RowPadding = 6, bool SerifFigures = false);

    /// <summary>Description, Qty, Price, Amount; discount and tax print under the description.</summary>
    private static void LineTable(IContainer c, Ctx x, TableStyle s)
    {
        var lines = x.M.Lines;
        var box = s.Outer is { } outer ? c.Border(Hair).BorderColor(C(outer)) : c;
        string[] heads = ["Description", "Qty", "Price", "Amount"];
        var figures = Families(s.SerifFigures ? InvoiceFonts.Serif : x.Body);

        box.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn();
                cd.ConstantColumn(42);
                cd.ConstantColumn(84);
                cd.ConstantColumn(96);
            });

            t.Header(h =>
            {
                for (var i = 0; i < heads.Length; i++)
                {
                    IContainer cell = h.Cell();
                    if (s.HeadFill is { } fill) cell = cell.Background(C(fill));
                    if (s.HeadRule is { } rule) cell = cell.BorderBottom(s.HeadRuleWidth).BorderColor(C(rule));
                    if (s.ColumnRules is { } cr && i > 0) cell = cell.BorderLeft(Hair).BorderColor(C(cr));
                    var text = cell.PaddingVertical(6).PaddingHorizontal(6).Text(heads[i]).FontSize(9).SemiBold().FontColor(C(s.HeadText));
                    if (i > 0) text.AlignRight();
                }
            });

            for (var row = 0; row < lines.Count; row++)
            {
                var line = lines[row];
                var last = row == lines.Count - 1;
                IContainer Cell(IContainer cell, int column)
                {
                    if (s.Zebra && row % 2 == 1) cell = cell.Background(C(Fill));
                    if (!(last && s.Outer is not null)) cell = cell.BorderBottom(Hair).BorderColor(C(s.RowRule));
                    if (s.ColumnRules is { } cr && column > 0) cell = cell.BorderLeft(Hair).BorderColor(C(cr));
                    return cell.PaddingVertical(s.RowPadding).PaddingHorizontal(6);
                }

                t.Cell().Element(cell => Cell(cell, 0)).Column(d =>
                {
                    d.Item().Text(line.Description).Medium().FontColor(C(Graphite));
                    var extras = new[] { line.Discount is { } disc ? $"Discount {disc}" : null, line.Tax }.Where(e => e is not null);
                    if (extras.Any()) d.Item().Text(string.Join(", ", extras)).FontSize(9).FontColor(C(Muted));
                });
                t.Cell().Element(cell => Cell(cell, 1)).AlignRight().Text(line.Quantity).Tabular(x.Tabular);
                t.Cell().Element(cell => Cell(cell, 2)).AlignRight().Text(line.UnitPrice).FontFamily(figures).Tabular(x.Tabular);
                t.Cell().Element(cell => Cell(cell, 3)).AlignRight().Text(line.Amount).FontFamily(figures).Tabular(x.Tabular).Medium();
            }
        });
    }

    private sealed record TotalsStyle(
        string? TotalRule = null, float TotalRuleWidth = Hair, string LabelColor = Muted, string? RowRule = null, float RowPadding = 4.5f,
        bool SerifFigures = false);

    /// <summary>Subtotal, discount, taxes, shipping, then the invoice "Total" row and amount paid. The design draws the emphasised figure.</summary>
    private static void TotalsRows(ColumnDescriptor col, Ctx x, TotalsStyle s, IReadOnlyList<PdfRow>? rows = null)
    {
        var figures = Families(s.SerifFigures ? InvoiceFonts.Serif : x.Body);
        foreach (var row in rows ?? x.M.Totals)
        {
            var item = col.Item();
            if (row.Strong && s.TotalRule is { } rule) item = item.PaddingTop(4.5f).BorderTop(s.TotalRuleWidth).BorderColor(C(rule)).PaddingTop(4.5f);
            item = s.RowRule is { } rr ? item.BorderBottom(Hair).BorderColor(C(rr)).PaddingVertical(s.RowPadding) : item.PaddingVertical(1.5f);
            item.Row(r =>
            {
                var label = r.RelativeItem().Text(row.Label).FontColor(C(row.Strong ? Graphite : s.LabelColor));
                var value = r.AutoItem().PaddingLeft(12).AlignRight().Text(row.Value).FontFamily(figures).Tabular(x.Tabular).FontColor(C(Graphite));
                if (row.Strong)
                {
                    label.SemiBold();
                    value.SemiBold();
                }
            });
        }
    }

    /// <summary>Ordinary rows under the emphasised figure (a receipt's balance remaining), in the design's totals style.</summary>
    private static void AfterRows(ColumnDescriptor col, Ctx x, TotalsStyle s)
    {
        if (x.M.AfterGrand is not { Count: > 0 } rows) return;
        col.Item().Height(4.5f);
        TotalsRows(col, x, s with { TotalRule = null }, rows);
    }

    /// <summary>The emphasised figure (balance due on an invoice, total on a quote, amount paid on a receipt) as a label/value row.</summary>
    private static void GrandRow(
        IContainer c, Ctx x, string labelColor = Graphite, string valueColor = Graphite, float labelSize = 10.5f, int labelWeight = 600,
        bool labelSerifItalic = false, float valueSize = 16.5f, int valueWeight = 700, bool valueSerif = false) => c.Row(r =>
    {
        var label = r.RelativeItem().AlignBottom().Text(x.M.Grand.Label).FontSize(labelSize).FontColor(C(labelColor));
        if (labelSerifItalic) label.FontFamily(Families(InvoiceFonts.Serif)).Italic();
        else Weight(label, labelWeight);
        var value = r.AutoItem().PaddingLeft(12).AlignBottom().Text(x.M.Grand.Value).FontSize(valueSize).LineHeight(1).Tabular(x.Tabular)
            .FontColor(C(valueColor));
        Weight(value, valueWeight);
        if (valueSerif) value.FontFamily(Families(InvoiceFonts.Serif));
    });

    /// <summary>Payment details and the contact row side by side, then notes and terms.</summary>
    private static void InfoBlocks(
        IContainer c, Ctx x, string labelColor = Muted, string? rule = null, float ruleWidth = Hair, float top = 15, IReadOnlyList<string>? contact = null)
    {
        contact ??= x.M.Contact;
        var bank = x.M.BankDetails;
        if (bank is null && contact.Count == 0 && x.M.Notes is null && x.M.Terms is null) return;

        var box = c.PaddingTop(top);
        if (rule is not null) box = box.BorderTop(ruleWidth).BorderColor(C(rule)).PaddingTop(10);
        box.Column(col =>
        {
            col.Spacing(10);
            if (bank is not null || contact.Count > 0)
            {
                col.Item().EnsureSpace(42).Row(r =>
                {
                    r.Spacing(15);
                    if (bank is not null) r.RelativeItem().Element(b => Block(b, "Payment details", bank, labelColor));
                    if (contact.Count > 0)
                    {
                        r.RelativeItem().Column(cc =>
                        {
                            cc.Item().PaddingBottom(3).Text("Contact").FontSize(9).SemiBold().FontColor(C(labelColor));
                            cc.Item().Inlined(items =>
                            {
                                items.HorizontalSpacing(12);
                                items.VerticalSpacing(1.5f);
                                foreach (var entry in contact) items.Item().Text(entry).FontColor(C(Slate));
                            });
                        });
                    }
                });
            }
            if (x.M.Notes is not null || x.M.Terms is not null)
            {
                col.Item().EnsureSpace(42).Row(r =>
                {
                    r.Spacing(15);
                    r.RelativeItem().Element(b => { if (x.M.Notes is { } notes) Block(b, "Notes", notes, labelColor); });
                    r.RelativeItem().Element(b => { if (x.M.Terms is { } terms) Block(b, "Terms", terms, labelColor); });
                });
            }
        });
    }

    private static void Block(IContainer c, string title, string body, string labelColor) => c.Column(col =>
    {
        col.Item().PaddingBottom(3).Text(title).FontSize(9).SemiBold().FontColor(C(labelColor));
        col.Item().Text(body).FontColor(C(Slate));
    });

    /// <summary>
    /// Signature block, beside the totals (see <see cref="Closing"/>): the signature image (max 48pt tall) on a 200pt rule labelled
    /// "Authorised signature", name and role below; the stamp (max 80pt square) at the left end of the rule, three quarters
    /// outside, two thirds above the rule. Space for an image is only reserved when there is one (signature or stamp); otherwise
    /// there is 18pt to sign in. The 6pt above leaves room for the stamp's top edge.
    /// </summary>
    private static void Signature(IContainer c, Ctx x, string ruleColor = Graphite)
    {
        if (x.M.Signature is not { } s) return;
        const float width = 200, stampSize = 80;
        var stamp = x.Images.Stamp;
        var imageHeight = x.Images.Signature is not null || stamp is not null ? 48f : 18f;
        var stampOut = stamp is null ? 0 : stampSize * 0.75f;

        c.PaddingTop(6).AlignLeft().Width(width + stampOut).Layers(l =>
        {
            l.PrimaryLayer().PaddingLeft(stampOut).Column(col =>
            {
                col.Item().Height(imageHeight).AlignCenter().AlignBottom().Element(img =>
                {
                    if (x.Images.Signature is { } signature) img.MaxWidth(width).MaxHeight(imageHeight).Image(signature).FitArea();
                });
                col.Item().BorderTop(Hair).BorderColor(C(ruleColor)).PaddingTop(4.5f).Column(text =>
                {
                    text.Item().AlignCenter().Text("Authorised signature").FontSize(9).FontColor(C(Muted));
                    if (s.Name is { } name) text.Item().AlignCenter().Text(name).SemiBold().FontColor(C(Graphite));
                    if (s.Role is { } role) text.Item().AlignCenter().Text(role).FontColor(C(Slate));
                });
            });
            if (stamp is not null)
                l.Layer().AlignLeft().AlignTop().Unconstrained().OffsetY(imageHeight - stampSize * 2 / 3)
                    .Width(stampSize).Height(stampSize).AlignCenter().AlignMiddle().Image(stamp).FitArea();
        });
    }

    /// <summary>The footer line above the page number on every page, with the free-plan "Made with" line (same words as the web).</summary>
    private static void Footer(IContainer c, Ctx x) => c.PaddingHorizontal(Margin).PaddingTop(9).PaddingBottom(20).Column(col =>
    {
        if (x.M.FooterText is { } footer)
        {
            var item = col.Item().PaddingBottom(6);
            if (x.FooterRule is { } rule) item = item.BorderTop(Hair).BorderColor(C(rule));
            var text = item.PaddingTop(7.5f).Text(footer).AlignCenter().FontSize(9).FontColor(C(Muted));
            if (x.FooterItalic) text.Italic();
            if (x.FooterSerif) text.FontFamily(Families(InvoiceFonts.Serif));
        }
        col.Item().Row(row =>
        {
            row.RelativeItem().Text(x.M.ShowBranding ? $"Made with {x.M.AppName} — {MadeWith}" : "").FontSize(8).FontColor(C(Muted));
            row.RelativeItem().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(C(Muted)));
                t.Span($"{x.M.Number}  ·  Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        });
    });

    /// <summary>A 3px double rule on the web: two 0.75pt lines with a 0.75pt gap.</summary>
    private static void DoubleRule(IContainer c, string color) => c.Column(col =>
    {
        col.Item().LineHorizontal(Hair).LineColor(C(color));
        col.Item().PaddingTop(Hair).LineHorizontal(Hair).LineColor(C(color));
    });

    private static void Weight(TextSpanDescriptor text, int weight)
    {
        switch (weight)
        {
            case <= 300: text.Light(); break;
            case <= 400: text.NormalWeight(); break;
            case <= 500: text.Medium(); break;
            case <= 600: text.SemiBold(); break;
            case <= 700: text.Bold(); break;
            default: text.ExtraBold(); break;
        }
    }

    private static Color C(string hex) => Color.FromHex(hex);

    private static string[] Families(string primary) => [primary, .. InvoiceFonts.Fallbacks.Where(f => f != primary)];

    /// <summary>Business initials for the Prestige monogram: "Ada Designs Ltd" gives "AD".</summary>
    internal static string Initials(string name)
    {
        string[] skip = ["ltd", "limited", "plc", "llc", "inc", "and", "of", "the"];
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => char.IsLetterOrDigit(w[0]) && !skip.Contains(w.ToLowerInvariant().TrimEnd('.')))
            .ToList();
        if (words.Count == 0) words = [string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()];
        return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    /// <summary>Resolved colours, fonts and sizes for one document.</summary>
    private sealed class Ctx
    {
        public Ctx(DocumentPdfModel m, PdfImages images)
        {
            M = m;
            Images = images;
            Accent = Normalize(m.BrandColor);
            Second = DocumentColors.Second(Accent, m.SecondaryColor);
            OnAccent = On(Accent);
            AccentText = TextOnWhite(Accent);
            OnSecond = On(Second);
            SecondText = TextOnWhite(Second);
            Body = InvoiceFonts.For(m.FontStyle);
            Heading = m.Template is "elegant" or "prestige" ? InvoiceFonts.Serif : Body;
            Tabular = Body != InvoiceFonts.Modern;
            (LogoWidth, LogoHeight) = m.LogoSize switch
            {
                "small" => (100f, 36f),
                "large" => (190f, 68f),
                _ => (140f, 50f),
            };
            LogoRoom = images.Logo is null ? 0 : LogoWidth + 9;
            FooterRule = m.Template switch
            {
                "minimal" => null,
                "elegant" => Accent,
                "prestige" => Second,
                "ledger" => LedgerRule,
                _ => Hairline,
            };
            FooterItalic = m.Template is "elegant" or "prestige";
            FooterSerif = m.Template == "prestige";
        }

        public DocumentPdfModel M { get; }
        public PdfImages Images { get; }
        public string Accent { get; }
        public string Second { get; }
        public string OnAccent { get; }
        public string AccentText { get; }
        public string OnSecond { get; }
        public string SecondText { get; }
        public string Body { get; }
        public string Heading { get; }

        /// <summary>
        /// Whether figures ask for tabular digits. Off for Inter: its tabular digits are alternate glyphs with no Unicode value,
        /// so amounts would copy, search and read aloud as private-use characters. Noto Sans and Noto Serif are unaffected.
        /// </summary>
        public bool Tabular { get; }
        public float LogoWidth { get; }
        public float LogoHeight { get; }

        /// <summary>The width a logo beside the name takes (its box and the gap), or 0 without a logo.</summary>
        public float LogoRoom { get; }
        public string? FooterRule { get; }
        public bool FooterItalic { get; }
        public bool FooterSerif { get; }
        public bool IsQuote => M.Kind == DocumentPdfModel.Quote;
    }

    // ------------------------------------------------------------------ images

    /// <summary>
    /// Only loads images from our own storage, so a stored URL can't make the server fetch arbitrary addresses. Each image is
    /// decoded once, here; one whose header declares more than 4000 pixels a side (an upload from before that limit) is left out.
    /// </summary>
    private async Task<Image?> LoadImageAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var allowed = new[] { $"{urls.ApiBaseUrl.TrimEnd('/')}/uploads/", storage.Value.R2PublicBaseUrl is { Length: > 0 } r2 ? $"{r2.TrimEnd('/')}/" : null }
            .Where(p => p is not null).Cast<string>();
        if (!allowed.Any(p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            using var http = httpFactory.CreateClient("pdf-logo");
            http.Timeout = TimeSpan.FromSeconds(5);
            var bytes = await http.GetByteArrayAsync(url, ct);
            if (bytes.Length > ImageUploads.MaxBytes) return null;
            if (ImageUploads.Dimensions(bytes) is not { } size || size.Width is <= 0 or > ImageUploads.MaxDocumentImageSide
                || size.Height is <= 0 or > ImageUploads.MaxDocumentImageSide)
            {
                logger.LogWarning("Image {Url} is too large or unreadable for a PDF", url);
                return null;
            }
            return Image.FromBinaryData(bytes); // a file that can't be decoded would fail the whole PDF; leave it out instead
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not load image {Url} for a PDF", url);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Image {Url} could not be decoded for a PDF", url);
            return null;
        }
    }
}

internal static class PdfTextExtensions
{
    /// <summary>Tabular digits (the "tnum" font feature) when <paramref name="on"/>; see <c>Ctx.Tabular</c>.</summary>
    public static T Tabular<T>(this T text, bool on) where T : TextSpanDescriptor => on ? text.EnableFontFeature("tnum") : text;
}
