using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Saas.Application.School;
using Saas.Domain.School;
using SkiaSharp;

namespace Saas.Infrastructure.Schools;

/// <summary>SchoolLink's PDFs with QuestPDF (Community licence). ID cards now; report cards and receipts are added by later tasks.</summary>
public sealed class SchoolPdf : ISchoolPdf
{
    public const int CardsPerPage = 8;
    private const int Columns = 2;
    private const float CardHeightMm = 60;

    static SchoolPdf() => QuestPDF.Settings.License = LicenseType.Community;

    public Task<byte[]> IdCardsAsync(IReadOnlyList<IdCardData> cards, CancellationToken ct) => Task.FromResult(IdCardDocument(cards).GeneratePdf());

    public Task<byte[]> ReportCardAsync(ReportCardData card, CancellationToken ct) => Task.FromResult(ReportCardDocument([card]).GeneratePdf());

    public Task<byte[]> ReportCardsAsync(IReadOnlyList<ReportCardData> cards, CancellationToken ct) => Task.FromResult(ReportCardDocument(cards).GeneratePdf());

    public Task<byte[]> BroadsheetAsync(BroadsheetData data, CancellationToken ct) => Task.FromResult(BroadsheetDocument(data).GeneratePdf());

    public Task<byte[]> ReceiptAsync(ReceiptData receipt, CancellationToken ct) => Task.FromResult(ReceiptDocument(receipt).GeneratePdf());

    /// <summary>A fee receipt on A5. Text grows instead of being clipped; a reversed payment carries the reversal note in red.</summary>
    public static IDocument ReceiptDocument(ReceiptData r)
    {
        var logo = Usable(r.Logo, keepTransparency: true);
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));
            page.Header().PaddingBottom(4, Unit.Millimetre).Row(row =>
            {
                if (logo is not null) row.ConstantItem(18, Unit.Millimetre).Height(18, Unit.Millimetre).Image(logo).FitArea();
                row.RelativeItem().PaddingLeft(logo is null ? 0 : 4, Unit.Millimetre).Column(col =>
                {
                    col.Item().Text(r.SchoolName).FontSize(14).Bold();
                    if (!string.IsNullOrWhiteSpace(r.Address)) col.Item().Text(r.Address).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
            page.Content().Column(col =>
            {
                col.Spacing(3, Unit.Millimetre);
                col.Item().Background("#1F2937").Padding(2, Unit.Millimetre).Row(row =>
                {
                    row.RelativeItem().Text("Receipt").Bold().FontSize(11).FontColor(Colors.White);
                    row.AutoItem().Text(r.ReceiptNumber).FontColor(Colors.White);
                });
                if (r.ReversedNote is not null) col.Item().Text(r.ReversedNote).Bold().FontColor(Colors.Red.Darken2);
                col.Item().Element(c => ReceiptRow(c, "Date paid", r.PaidOn));
                col.Item().Element(c => ReceiptRow(c, "Student", $"{r.StudentName} ({r.AdmissionNo})"));
                col.Item().Element(c => ReceiptRow(c, "Class", r.ClassName));
                col.Item().Element(c => ReceiptRow(c, "Invoice", $"{r.InvoiceNumber}, {r.TermTitle}"));
                col.Item().Element(c => ReceiptRow(c, "Paid by", r.Method));
                if (!string.IsNullOrWhiteSpace(r.Reference)) col.Item().Element(c => ReceiptRow(c, "Reference", r.Reference));
                col.Item().PaddingTop(2, Unit.Millimetre).BorderTop(0.5f).BorderColor(Colors.Grey.Medium).PaddingTop(2, Unit.Millimetre).Row(row =>
                {
                    row.RelativeItem().Text("Amount received").SemiBold();
                    row.AutoItem().Text(r.Amount).FontSize(14).Bold();
                });
                col.Item().Element(c => ReceiptRow(c, "Invoice balance now", r.InvoiceBalance));
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Grey.Medium));
                text.Span("Thank you. Keep this receipt for your records.");
                if (r.ShowBranding) text.Span(" Powered by SchoolLink");
            });
        }));
    }

    private static void ReceiptRow(IContainer container, string label, string value) =>
        container.Row(row =>
        {
            row.ConstantItem(38, Unit.Millimetre).Text(label).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().Text(value).SemiBold();
        });

    /// <summary>
    /// Report cards, each starting on a new A4 page. Nothing that holds text has a fixed height, so long names, long remarks and many
    /// subjects grow the card onto more pages instead of failing; the subject table repeats its header on every page.
    /// </summary>
    public static IDocument ReportCardDocument(IReadOnlyList<ReportCardData> cards)
    {
        var shared = new Dictionary<byte[], byte[]?>(ReferenceEqualityComparer.Instance);
        return Document.Create(doc =>
        {
            foreach (var card in cards)
            {
                var logo = Usable(card.Logo, shared, keepTransparency: true);
                var signature = Usable(card.Signature, shared, keepTransparency: true);
                var photo = Usable(card.Photo);
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(14, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Grey.Darken4));
                    page.Header().PaddingBottom(4, Unit.Millimetre).Row(row =>
                    {
                        if (logo is not null) row.ConstantItem(22, Unit.Millimetre).Height(22, Unit.Millimetre).Image(logo).FitArea();
                        row.RelativeItem().PaddingLeft(logo is null ? 0 : 4, Unit.Millimetre).Column(col =>
                        {
                            col.Item().Text(card.SchoolName).FontSize(16).Bold();
                            if (!string.IsNullOrWhiteSpace(card.Motto)) col.Item().Text(card.Motto).Italic().FontColor(Colors.Grey.Darken1);
                            if (!string.IsNullOrWhiteSpace(card.Address)) col.Item().Text(card.Address).FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    page.Content().Column(col =>
                    {
                        col.Spacing(4, Unit.Millimetre);
                        col.Item().Background("#1F2937").Padding(2, Unit.Millimetre).Row(row =>
                        {
                            row.RelativeItem().Text("Report card").Bold().FontSize(11).FontColor(Colors.White);
                            row.AutoItem().Text(card.TermTitle).FontColor(Colors.White);
                        });
                        foreach (var notice in card.Notices) col.Item().Text(notice).Bold().FontColor(Colors.Red.Darken2);
                        col.Item().Element(c => StudentBlock(c, card, photo));
                        var developmental = card.Mode == AssessmentMode.Developmental;
                        if (!developmental)
                        {
                            if (card.Subjects.Count == 0) col.Item().Text("No scores have been entered for this term yet.").FontColor(Colors.Grey.Darken1);
                            else col.Item().Element(c => SubjectTable(c, card));
                        }
                        if (card.Developmental is { Count: > 0 }) col.Item().Element(c => DevelopmentalBlock(c, card.Developmental));
                        if (card.Traits is { Count: > 0 })
                            foreach (var block in card.Traits) col.Item().Element(c => TraitBlock(c, block));
                        col.Item().ShowEntire().Column(bottom =>
                        {
                            bottom.Spacing(4, Unit.Millimetre);
                            bottom.Item().Row(row =>
                            {
                                row.Spacing(3, Unit.Millimetre);
                                if (!developmental)
                                {
                                    row.RelativeItem().Element(c => Figure(c, "Total", card.Total));
                                    row.RelativeItem().Element(c => Figure(c, "Average", card.Average));
                                    row.RelativeItem().Element(c => Figure(c, "Position in class", card.Position));
                                }
                                row.RelativeItem().Element(c => Figure(c, "Attendance", card.Attendance));
                            });
                            bottom.Item().Element(c => Remark(c, "Class teacher's remark", card.TeacherRemark));
                            bottom.Item().Element(c => Remark(c, "Principal's remark", card.PrincipalRemark));
                            bottom.Item().Row(row =>
                            {
                                row.RelativeItem().AlignBottom().Text(text =>
                                {
                                    if (string.IsNullOrWhiteSpace(card.NextTermBegins)) return;
                                    text.Span("Next term begins ");
                                    text.Span(card.NextTermBegins).SemiBold();
                                });
                                row.ConstantItem(55, Unit.Millimetre).Column(sign =>
                                {
                                    sign.Item().Height(16, Unit.Millimetre).AlignBottom().AlignCenter().Element(c =>
                                    {
                                        if (signature is not null) c.Image(signature).FitArea();
                                    });
                                    sign.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Medium).PaddingTop(1, Unit.Millimetre).AlignCenter().Text("Principal").FontSize(8);
                                });
                            });
                        });
                    });
                    if (card.ShowBranding) page.Footer().AlignCenter().Text("Powered by SchoolLink").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            }
        });
    }

    private static void StudentBlock(IContainer container, ReportCardData card, byte[]? photo)
    {
        container.Row(row =>
        {
            row.ConstantItem(26, Unit.Millimetre).Height(32, Unit.Millimetre).Element(c =>
            {
                if (photo is not null) c.Image(photo).FitArea();
                else c.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).AlignCenter().AlignMiddle()
                    .Text(Initials(card.StudentName)).FontSize(18).Bold().FontColor(Colors.Grey.Darken1);
            });
            row.RelativeItem().PaddingLeft(4, Unit.Millimetre).Column(info =>
            {
                info.Spacing(1.5f, Unit.Millimetre);
                info.Item().Text(card.StudentName).FontSize(12).Bold();
                Detail(info, "Admission no. ", card.AdmissionNo);
                Detail(info, "Class ", card.ClassName);
                if (!string.IsNullOrWhiteSpace(card.Gender)) Detail(info, "Gender ", card.Gender);
                if (!string.IsNullOrWhiteSpace(card.DateOfBirth)) Detail(info, "Date of birth ", card.DateOfBirth);
            });
        });
    }

    private static void Detail(ColumnDescriptor column, string label, string value) =>
        column.Item().Text(text =>
        {
            text.Span(label).FontColor(Colors.Grey.Darken1);
            text.Span(value).SemiBold();
        });

    private static void DevelopmentalBlock(IContainer container, IReadOnlyList<ReportCardDevelopmentalRow> rows)
    {
        container.Column(col =>
        {
            col.Spacing(1.5f, Unit.Millimetre);
            col.Item().Text("Developmental areas").Bold();
            col.Item().Text("These are not exam marks.").FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(3);
                });
                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Area");
                    header.Cell().Element(HeaderCell).Text("Scale");
                    header.Cell().Element(HeaderCell).Text("Comment");
                });
                foreach (var row in rows)
                {
                    table.Cell().Element(BodyCell).Text(row.Area);
                    table.Cell().Element(BodyCell).Text(row.Scale);
                    table.Cell().Element(BodyCell).Text(row.Comment ?? "");
                }
            });
        });
    }

    private static void TraitBlock(IContainer container, ReportCardTraitBlock block)
    {
        container.Column(col =>
        {
            col.Spacing(1.5f, Unit.Millimetre);
            col.Item().Text(block.Heading).Bold();
            col.Item().Text("Rated from 1 to 5. These are not exam marks.").FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.ConstantColumn(28, Unit.Millimetre);
                });
                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Item");
                    header.Cell().Element(HeaderCell).Text("Rating");
                });
                foreach (var row in block.Rows)
                {
                    table.Cell().Element(BodyCell).Text(row.Name);
                    table.Cell().Element(BodyCell).Text(row.Rating);
                }
            });
        });
    }

    private static void SubjectTable(IContainer container, ReportCardData card)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                foreach (var _ in card.ComponentHeaders) c.ConstantColumn(17, Unit.Millimetre);
                c.ConstantColumn(13, Unit.Millimetre);
                c.ConstantColumn(12, Unit.Millimetre);
                c.ConstantColumn(15, Unit.Millimetre);
                c.ConstantColumn(14, Unit.Millimetre);
                c.RelativeColumn(2);
            });
            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Subject");
                foreach (var h in card.ComponentHeaders) header.Cell().Element(HeaderCell).AlignCenter().Text(h);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Total");
                header.Cell().Element(HeaderCell).AlignCenter().Text("Grade");
                header.Cell().Element(HeaderCell).AlignCenter().Text("Class avg.");
                header.Cell().Element(HeaderCell).AlignCenter().Text("Position");
                header.Cell().Element(HeaderCell).Text("Remark");
            });
            foreach (var subject in card.Subjects)
            {
                table.Cell().Element(BodyCell).Text(subject.Name);
                foreach (var score in subject.Scores) table.Cell().Element(BodyCell).AlignCenter().Text(score);
                table.Cell().Element(BodyCell).AlignCenter().Text(subject.Total).SemiBold();
                table.Cell().Element(BodyCell).AlignCenter().Text(subject.Grade).SemiBold();
                table.Cell().Element(BodyCell).AlignCenter().Text(subject.ClassAverage);
                table.Cell().Element(BodyCell).AlignCenter().Text(subject.Position);
                table.Cell().Element(BodyCell).Text(subject.Remark);
            }
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(1.5f, Unit.Millimetre).DefaultTextStyle(t => t.Bold().FontSize(8));

    private static IContainer BodyCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(1.5f, Unit.Millimetre);

    private static void Figure(IContainer container, string label, string value) =>
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2, Unit.Millimetre).Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().Text(value).Bold();
        });

    private static void Remark(IContainer container, string label, string? remark) =>
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2, Unit.Millimetre).Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().Text(string.IsNullOrWhiteSpace(remark) ? " " : remark);
        });

    /// <summary>
    /// One class broadsheet on landscape A4. Text wraps inside the cells, and the column header repeats on later pages.
    /// A nursery sheet says the columns are developmental levels, so they are not read as exam marks.
    /// </summary>
    public static IDocument BroadsheetDocument(BroadsheetData data) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Grey.Darken4));
            page.Header().PaddingBottom(3, Unit.Millimetre).Column(col =>
            {
                col.Spacing(1, Unit.Millimetre);
                col.Item().Text(data.SchoolName).FontSize(14).Bold();
                col.Item().Text("Broadsheet").FontSize(11).SemiBold();
                col.Item().Text($"{data.ClassName}, {data.TermTitle}");
                if (data.Developmental)
                    col.Item().Text("Developmental levels, not exam marks.").Italic().FontColor(Colors.Grey.Darken1);
            });
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var _ in data.Columns) columns.RelativeColumn();
                });
                table.Header(header =>
                {
                    foreach (var column in data.Columns)
                        header.Cell().Element(HeaderCell).Text(column.Header);
                });
                foreach (var row in data.Rows)
                    foreach (var cell in row)
                        table.Cell().Element(BodyCell).Text(string.IsNullOrEmpty(cell) ? " " : cell);
            });
        }));

    /// <summary>The ID card document: A4 pages of two columns by four rows.</summary>
    public static IDocument IdCardDocument(IReadOnlyList<IdCardData> cards)
    {
        // Photos and logos are re-encoded once: a corrupt image falls back to the placeholder instead of failing the whole sheet.
        var photos = cards.Select(c => Usable(c.Photo)).ToList();
        var logos = new Dictionary<byte[], byte[]?>(ReferenceEqualityComparer.Instance);
        return Document.Create(doc =>
        {
            for (var start = 0; start < cards.Count; start += CardsPerPage)
            {
                var pageStart = start;
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(12, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Grey.Darken4));
                    page.Content().Column(col =>
                    {
                        for (var rowIndex = 0; rowIndex < CardsPerPage / Columns; rowIndex++)
                            col.Item().Height(CardHeightMm + 4, Unit.Millimetre).Row(row =>
                            {
                                for (var colIndex = 0; colIndex < Columns; colIndex++)
                                {
                                    var i = pageStart + rowIndex * Columns + colIndex;
                                    var cell = row.RelativeItem().Padding(2, Unit.Millimetre);
                                    if (i < cards.Count) cell.Element(c => Card(c, cards[i], photos[i], Usable(cards[i].Logo, logos)));
                                }
                            });
                    });
                });
            }
        });
    }

    private static void Card(IContainer container, IdCardData card, byte[]? photo, byte[]? logo)
    {
        container.Height(CardHeightMm, Unit.Millimetre).Border(0.75f).BorderColor(Colors.Grey.Medium).Column(col =>
        {
            col.Item().Background("#1F2937").PaddingVertical(2.5f, Unit.Millimetre).PaddingHorizontal(3, Unit.Millimetre).Row(row =>
            {
                if (logo is not null) row.ConstantItem(9, Unit.Millimetre).Height(9, Unit.Millimetre).Image(logo).FitArea();
                row.RelativeItem().AlignMiddle().PaddingLeft(logo is null ? 0 : 2, Unit.Millimetre)
                    .Text(card.SchoolName).FontSize(10).Bold().FontColor(Colors.White);
            });
            col.Item().Padding(3, Unit.Millimetre).Row(row =>
            {
                row.ConstantItem(24, Unit.Millimetre).Height(30, Unit.Millimetre).Element(c =>
                {
                    if (photo is not null) c.Image(photo).FitArea();
                    else c.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).AlignCenter().AlignMiddle()
                        .Text(Initials(card.StudentName)).FontSize(18).Bold().FontColor(Colors.Grey.Darken1);
                });
                row.RelativeItem().PaddingLeft(3, Unit.Millimetre).Column(info =>
                {
                    info.Spacing(1.5f, Unit.Millimetre);
                    info.Item().Text(card.StudentName).FontSize(11).Bold();
                    info.Item().Text(text =>
                    {
                        text.Span("Admission no. ").FontColor(Colors.Grey.Darken1);
                        text.Span(card.AdmissionNo).SemiBold();
                    });
                    info.Item().Text(text =>
                    {
                        text.Span("Class ").FontColor(Colors.Grey.Darken1);
                        text.Span(card.ClassName).SemiBold();
                    });
                    info.Item().Text(text =>
                    {
                        text.Span("Session ").FontColor(Colors.Grey.Darken1);
                        text.Span(card.SessionName).SemiBold();
                    });
                });
            });
        });
    }

    /// <summary>The first letters of the first and last names: "Ada Obi" gives "AO".</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : parts.Length == 1 ? char.ToUpperInvariant(parts[0][0]).ToString() : $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }

    /// <summary>Re-encodes the image so QuestPDF can place it; null when it cannot be read. Logos and signatures use PNG so transparency stays transparent.</summary>
    private static byte[]? Usable(byte[]? image, Dictionary<byte[], byte[]?>? cache = null, bool keepTransparency = false)
    {
        if (image is null or { Length: 0 }) return null;
        if (cache is not null && cache.TryGetValue(image, out var known)) return known;
        byte[]? result;
        try
        {
            using var bitmap = SKBitmap.Decode(image);
            if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0) result = null;
            else
            {
                using var encoded = keepTransparency
                    ? SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100)
                    : SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 85);
                result = encoded?.ToArray();
            }
        }
        catch (Exception)
        {
            result = null;
        }
        cache?.Add(image, result);
        return result;
    }
}
