using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Saas.Application.Common;
using Saas.Application.Invoices;
using Saas.Infrastructure.Storage;

namespace Saas.Infrastructure.Invoices;

/// <summary>Renders invoices, quotes and receipts with QuestPDF (Community licence: free under $1M yearly revenue).</summary>
public sealed class QuestPdfRenderer(
    IHttpClientFactory httpFactory, AppUrls urls, IOptions<StorageOptions> storage, ILogger<QuestPdfRenderer> logger) : IDocumentPdfRenderer
{
    static QuestPdfRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private enum Header { Plain, Band, Centered }

    /// <summary>Each template is a handful of style choices over the same layout.</summary>
    private sealed record Style(Header Header, bool TableHeaderFilled, string? TableHeaderBg, bool Zebra, bool RuleUnderHeader, float TitleSize, string? BandBg = null);

    private static Style StyleFor(string template) => template switch
    {
        "modern" => new(Header.Band, true, null, Zebra: true, RuleUnderHeader: false, TitleSize: 26),
        "minimal" => new(Header.Plain, false, null, Zebra: false, RuleUnderHeader: false, TitleSize: 18),
        "bold" => new(Header.Band, true, null, Zebra: false, RuleUnderHeader: true, TitleSize: 34, BandBg: "#111827"),
        "elegant" => new(Header.Centered, false, null, Zebra: false, RuleUnderHeader: true, TitleSize: 24),
        _ => new(Header.Plain, true, "#F3F4F6", Zebra: false, RuleUnderHeader: true, TitleSize: 26), // classic
    };

    public async Task<byte[]> RenderAsync(DocumentPdfModel m, CancellationToken ct) =>
        Build(m, await LoadLogoAsync(m.LogoUrl, ct)).GeneratePdf();

    /// <summary>The QuestPDF document, before it becomes a PDF (also lets tooling render page images).</summary>
    public static IDocument Build(DocumentPdfModel m, byte[]? logo)
    {
        var style = StyleFor(m.Template);
        var brand = Color.FromHex(m.BrandColor);

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));

            page.Header().Element(c => ComposeHeader(c, m, style, brand, logo));
            page.Content().PaddingVertical(16).Column(col =>
            {
                col.Spacing(18);
                col.Item().Element(c => ComposeParties(c, m));
                col.Item().Element(c => ComposeLines(c, m, style, brand));
                col.Item().AlignRight().Width(250).Element(c => ComposeTotals(c, m, brand));
                if (!string.IsNullOrWhiteSpace(m.BankDetails)) col.Item().Element(c => Block(c, "Payment details", m.BankDetails!));
                if (!string.IsNullOrWhiteSpace(m.Notes)) col.Item().Element(c => Block(c, "Notes", m.Notes!));
                if (!string.IsNullOrWhiteSpace(m.Terms)) col.Item().Element(c => Block(c, "Terms", m.Terms!));
            });
            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(m.ShowBranding ? $"Made with {m.AppName}" : "").FontSize(8).FontColor(Colors.Grey.Medium);
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Medium));
                    t.Span($"{m.Number}  ·  Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }));
    }

    private static void ComposeHeader(IContainer c, DocumentPdfModel m, Style s, Color brand, byte[]? logo)
    {
        switch (s.Header)
        {
            case Header.Band:
                c.Background(s.BandBg is { } band ? Color.FromHex(band) : brand)
                    .BorderBottom(s.BandBg is null ? 0 : 6).BorderColor(brand).Padding(18).Row(row =>
                {
                    if (logo is not null) row.ConstantItem(70).Height(50).Image(logo).FitArea();
                    row.RelativeItem().PaddingLeft(logo is null ? 0 : 12).Column(col =>
                    {
                        col.Item().Text(m.Business.Name).FontSize(14).Bold().FontColor(Colors.White);
                        foreach (var line in m.Business.Lines) col.Item().Text(line).FontSize(9).FontColor(Colors.White);
                    });
                    row.RelativeItem().AlignRight().Column(col =>
                    {
                        col.Item().AlignRight().Text(m.Title).FontSize(s.TitleSize).Bold().FontColor(Colors.White);
                        col.Item().AlignRight().Text(m.Number).FontSize(11).FontColor(Colors.White);
                        if (m.Stamp is not null) col.Item().AlignRight().PaddingTop(4).Text(m.Stamp).FontSize(11).Bold().FontColor(Colors.White);
                    });
                });
                break;

            case Header.Centered:
                c.Column(col =>
                {
                    if (logo is not null) col.Item().AlignCenter().Height(50).Image(logo).FitHeight();
                    col.Item().AlignCenter().Text(m.Business.Name).FontSize(15).Bold();
                    col.Item().AlignCenter().Text(string.Join("  ·  ", m.Business.Lines)).FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(10).LineHorizontal(0.75f).LineColor(brand);
                    col.Item().PaddingTop(10).AlignCenter().Text(t =>
                    {
                        t.Span(m.Title).FontSize(s.TitleSize).LetterSpacing(0.25f).FontColor(brand);
                        t.Span($"   {m.Number}").FontSize(11).FontColor(Colors.Grey.Darken2);
                    });
                    if (m.Stamp is not null) col.Item().AlignCenter().Text(m.Stamp).FontSize(11).Bold().FontColor(StampColor(m.Stamp));
                    col.Item().PaddingTop(10).LineHorizontal(0.75f).LineColor(brand);
                });
                break;

            default:
                c.Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            if (logo is not null) left.Item().Height(50).Image(logo).FitArea();
                            left.Item().Text(m.Business.Name).FontSize(14).Bold();
                            foreach (var line in m.Business.Lines) left.Item().Text(line).FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                        row.RelativeItem().AlignRight().Column(right =>
                        {
                            right.Item().AlignRight().Text(m.Title).FontSize(s.TitleSize).Bold()
                                .FontColor(s.TableHeaderFilled ? brand : Colors.Grey.Darken4);
                            right.Item().AlignRight().Text(m.Number).FontSize(11).FontColor(Colors.Grey.Darken1);
                            if (m.Stamp is not null)
                                right.Item().AlignRight().PaddingTop(4).Text(m.Stamp).FontSize(11).Bold().FontColor(StampColor(m.Stamp));
                        });
                    });
                    if (s.RuleUnderHeader) col.Item().PaddingTop(12).LineHorizontal(2).LineColor(brand);
                });
                break;
        }
    }

    private static void ComposeParties(IContainer c, DocumentPdfModel m) => c.Row(row =>
    {
        row.RelativeItem().Column(col =>
        {
            col.Item().Text(m.Title == "QUOTE" ? "QUOTE FOR" : m.Title == "RECEIPT" ? "RECEIVED FROM" : "BILL TO")
                .FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(2).Text(m.Client.Name).FontSize(11).Bold();
            foreach (var line in m.Client.Lines) col.Item().Text(line).FontSize(9);
        });
        row.ConstantItem(200).Column(col =>
        {
            foreach (var meta in m.Meta)
            {
                col.Item().Row(r =>
                {
                    r.RelativeItem().AlignRight().Text(meta.Label).FontSize(9).FontColor(Colors.Grey.Darken1);
                    var value = r.ConstantItem(100).AlignRight().Text(meta.Value).FontSize(9);
                    if (meta.Strong) value.Bold();
                });
            }
        });
    });

    private static void ComposeLines(IContainer c, DocumentPdfModel m, Style s, Color brand)
    {
        var showDiscount = m.Lines.Any(l => l.Discount is not null);
        var showTax = m.Lines.Any(l => l.Tax is not null);
        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(5);
                cols.RelativeColumn(1.2f);
                cols.RelativeColumn(2);
                if (showDiscount) cols.RelativeColumn(1.3f);
                if (showTax) cols.RelativeColumn(1.3f);
                cols.RelativeColumn(2.2f);
            });

            table.Header(h =>
            {
                var bg = s.TableHeaderFilled ? (s.TableHeaderBg is { } hex ? Color.FromHex(hex) : brand) : Colors.White;
                var fg = s.TableHeaderFilled && s.TableHeaderBg != "#F3F4F6" ? Colors.White : Colors.Grey.Darken3;
                IContainer Cell(IContainer x) => s.TableHeaderFilled
                    ? x.Background(bg).PaddingVertical(6).PaddingHorizontal(6)
                    : x.BorderBottom(1).BorderColor(brand).PaddingVertical(6).PaddingHorizontal(6);
                h.Cell().Element(Cell).Text("Description").FontSize(9).Bold().FontColor(fg);
                h.Cell().Element(Cell).AlignRight().Text("Qty").FontSize(9).Bold().FontColor(fg);
                h.Cell().Element(Cell).AlignRight().Text("Price").FontSize(9).Bold().FontColor(fg);
                if (showDiscount) h.Cell().Element(Cell).AlignRight().Text("Disc.").FontSize(9).Bold().FontColor(fg);
                if (showTax) h.Cell().Element(Cell).AlignRight().Text("Tax").FontSize(9).Bold().FontColor(fg);
                h.Cell().Element(Cell).AlignRight().Text("Amount").FontSize(9).Bold().FontColor(fg);
            });

            for (var i = 0; i < m.Lines.Count; i++)
            {
                var line = m.Lines[i];
                var zebra = s.Zebra && i % 2 == 1;
                IContainer Cell(IContainer x) =>
                    (zebra ? x.Background(Colors.Grey.Lighten4) : x).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .PaddingVertical(6).PaddingHorizontal(6);
                table.Cell().Element(Cell).Text(line.Description);
                table.Cell().Element(Cell).AlignRight().Text(line.Quantity);
                table.Cell().Element(Cell).AlignRight().Text(line.UnitPrice);
                if (showDiscount) table.Cell().Element(Cell).AlignRight().Text(line.Discount ?? "");
                if (showTax) table.Cell().Element(Cell).AlignRight().Text(line.Tax ?? "");
                table.Cell().Element(Cell).AlignRight().Text(line.Amount);
            }
        });
    }

    private static void ComposeTotals(IContainer c, DocumentPdfModel m, Color brand) => c.Column(col =>
    {
        foreach (var row in m.Totals)
        {
            col.Item().Element(x => row.Strong ? x.BorderTop(1).BorderColor(brand).PaddingTop(4) : x).PaddingVertical(2).Row(r =>
            {
                var label = r.RelativeItem().Text(row.Label).FontSize(row.Strong ? 11 : 9.5f);
                var value = r.ConstantItem(110).AlignRight().Text(row.Value).FontSize(row.Strong ? 11 : 9.5f);
                if (row.Strong)
                {
                    label.Bold();
                    value.Bold();
                }
            });
        }
    });

    private static void Block(IContainer c, string title, string body) => c.Column(col =>
    {
        col.Item().Text(title.ToUpperInvariant()).FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
        col.Item().PaddingTop(2).Text(body).FontSize(9);
    });

    private static Color StampColor(string stamp) => stamp switch
    {
        "PAID" or "ACCEPTED" => Colors.Green.Darken2,
        "OVERDUE" or "DECLINED" => Colors.Red.Darken1,
        _ => Colors.Grey.Darken1,
    };

    /// <summary>Only loads logos from our own storage, so a stored URL can't make the server fetch arbitrary addresses.</summary>
    private async Task<byte[]?> LoadLogoAsync(string? url, CancellationToken ct)
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
            return bytes.Length <= ImageUploads.MaxBytes ? bytes : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not load logo {Url} for a PDF", url);
            return null;
        }
    }
}
