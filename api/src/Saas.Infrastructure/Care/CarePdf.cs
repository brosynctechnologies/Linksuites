using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Saas.Application.Care;
using Saas.Domain.Care;
using SkiaSharp;

namespace Saas.Infrastructure.Care;

/// <summary>CareLink's PDFs with QuestPDF (Community licence): the prescription and the discharge summary on the clinic's letterhead. Later tasks add receipts.</summary>
public sealed class CarePdf : ICarePdf
{
    static CarePdf() => QuestPDF.Settings.License = LicenseType.Community;

    public Task<byte[]> PrescriptionAsync(PrescriptionPdfData data, CancellationToken ct) => Task.FromResult(PrescriptionDocument(data).GeneratePdf());

    /// <summary>Plain words for the frequency codes, printed beside them.</summary>
    public static string Describe(DoseFrequency frequency) => frequency switch
    {
        DoseFrequency.OD => "once daily",
        DoseFrequency.BD => "twice daily",
        DoseFrequency.TDS => "three times daily",
        DoseFrequency.QDS => "four times daily",
        DoseFrequency.Q4H => "every 4 hours",
        DoseFrequency.Q6H => "every 6 hours",
        DoseFrequency.Q8H => "every 8 hours",
        DoseFrequency.Nocte => "at night",
        DoseFrequency.Stat => "once, now",
        DoseFrequency.PRN => "as needed",
        _ => frequency.ToString(),
    };

    /// <summary>A prescription on A5: letterhead (logo when there is one), patient, the drugs in order, and the prescriber.</summary>
    public static IDocument PrescriptionDocument(PrescriptionPdfData d)
    {
        var logo = Usable(d.Logo);
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Grey.Darken4));
            page.Header().PaddingBottom(4, Unit.Millimetre).BorderBottom(0.75f).BorderColor(Colors.Grey.Medium).PaddingBottom(3, Unit.Millimetre).Row(row =>
            {
                if (logo is not null) row.ConstantItem(18, Unit.Millimetre).Height(18, Unit.Millimetre).Image(logo).FitArea();
                row.RelativeItem().PaddingLeft(logo is null ? 0 : 4, Unit.Millimetre).Column(col =>
                {
                    col.Item().Text(d.ClinicName).FontSize(14).Bold();
                    if (!string.IsNullOrWhiteSpace(d.Address)) col.Item().Text(d.Address).FontSize(8).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(d.Phone)) col.Item().Text(d.Phone).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
            page.Content().Column(col =>
            {
                col.Spacing(3, Unit.Millimetre);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text("Prescription").FontSize(13).Bold();
                    row.AutoItem().AlignBottom().Text(d.Date);
                });
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span("Patient: ").SemiBold();
                        t.Span($"{d.PatientName} ({d.PatientNumber})");
                    });
                    row.AutoItem().Text($"{d.Sex}, {d.AgeYears} years");
                });
                if (d.AllergyAcknowledged)
                    col.Item().Text("Prescribed with a recorded allergy acknowledged by the prescriber.").Italic().FontColor(Colors.Red.Darken2);

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(6, Unit.Millimetre);
                        c.RelativeColumn(4);
                        c.RelativeColumn(4);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        foreach (var label in new[] { "", "Drug", "Dose and frequency", "Duration" })
                            h.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Medium).PaddingVertical(1, Unit.Millimetre).Text(label).SemiBold();
                    });
                    for (var i = 0; i < d.Items.Count; i++)
                    {
                        var item = d.Items[i];
                        table.Cell().Element(Cell).Text($"{i + 1}.");
                        table.Cell().Element(Cell).Column(c =>
                        {
                            c.Item().Text(string.IsNullOrWhiteSpace(item.Strength) ? item.Drug : $"{item.Drug} {item.Strength}").SemiBold();
                            if (!string.IsNullOrWhiteSpace(item.Route)) c.Item().Text(item.Route).FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                        table.Cell().Element(Cell).Column(c =>
                        {
                            c.Item().Text($"{item.Dose}, {item.Frequency} ({Describe(item.Frequency)})");
                            if (!string.IsNullOrWhiteSpace(item.Instructions)) c.Item().Text(item.Instructions).FontSize(8).Italic();
                        });
                        table.Cell().Element(Cell).Column(c =>
                        {
                            if (item.DurationDays is { } days) c.Item().Text(days == 1 ? "1 day" : $"{days} days");
                            if (item.Quantity is { } qty) c.Item().Text($"Qty {qty}").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    }
                });

                col.Item().PaddingTop(8, Unit.Millimetre).Row(row =>
                {
                    row.RelativeItem();
                    row.ConstantItem(60, Unit.Millimetre).Column(c =>
                    {
                        c.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Darken1).PaddingTop(1, Unit.Millimetre).Text(d.DoctorName).SemiBold();
                        if (!string.IsNullOrWhiteSpace(d.DoctorTitle)) c.Item().Text(d.DoctorTitle).FontSize(8);
                        if (!string.IsNullOrWhiteSpace(d.LicenceNumber)) c.Item().Text($"Licence {d.LicenceNumber}").FontSize(8);
                    });
                });
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Colors.Grey.Medium));
                text.Span("Confidential medical document.");
                if (d.ShowBranding) text.Span(" Powered by CareLink");
            });
        }));

        static IContainer Cell(IContainer c) => c.BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(1.5f, Unit.Millimetre).PaddingRight(2, Unit.Millimetre);
    }

    public Task<byte[]> DischargeAsync(DischargePdfData data, CancellationToken ct) => Task.FromResult(DischargeDocument(data).GeneratePdf());

    /// <summary>
    /// A discharge summary on A4: letterhead (logo when there is one), the patient and the stay (admitted, discharged, ward, how they
    /// left; time and cause of death when they died), the summary's sections, the follow-up date, and the signing doctor.
    /// </summary>
    public static IDocument DischargeDocument(DischargePdfData d)
    {
        var logo = Usable(d.Logo);
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));
            page.Header().PaddingBottom(4, Unit.Millimetre).BorderBottom(0.75f).BorderColor(Colors.Grey.Medium).PaddingBottom(3, Unit.Millimetre).Row(row =>
            {
                if (logo is not null) row.ConstantItem(20, Unit.Millimetre).Height(20, Unit.Millimetre).Image(logo).FitArea();
                row.RelativeItem().PaddingLeft(logo is null ? 0 : 4, Unit.Millimetre).Column(col =>
                {
                    col.Item().Text(d.ClinicName).FontSize(15).Bold();
                    if (!string.IsNullOrWhiteSpace(d.Address)) col.Item().Text(d.Address).FontSize(8).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(d.Phone)) col.Item().Text(d.Phone).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
            page.Content().Column(col =>
            {
                col.Spacing(3, Unit.Millimetre);
                col.Item().Text("Discharge summary").FontSize(14).Bold();
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span("Patient: ").SemiBold();
                        t.Span($"{d.PatientName} ({d.PatientNumber})");
                    });
                    row.AutoItem().Text($"{d.Sex}, {d.AgeYears} years");
                });
                col.Item().Text(t =>
                {
                    t.Span("Admitted: ").SemiBold();
                    t.Span(d.AdmittedOn);
                    t.Span("    Discharged: ").SemiBold();
                    t.Span(d.DischargedOn ?? "Not yet discharged");
                    if (!string.IsNullOrWhiteSpace(d.WardName))
                    {
                        t.Span("    Ward: ").SemiBold();
                        t.Span(d.WardName);
                    }
                });
                if (d.DischargeType is not null) col.Item().Text(d.DischargeType).Italic();
                if (d.DeathTime is not null)
                    col.Item().Text(t =>
                    {
                        t.Span("Time of death: ").SemiBold();
                        t.Span(d.DeathTime);
                        if (!string.IsNullOrWhiteSpace(d.DeathCause))
                        {
                            t.Span("    Cause: ").SemiBold();
                            t.Span(d.DeathCause);
                        }
                    });
                Section(col, "Diagnoses", d.Diagnoses);
                Section(col, "Treatment given", d.Treatment);
                Section(col, "Condition at discharge", d.Condition);
                Section(col, "Take-home instructions", d.TakeHome);
                if (d.FollowUp is not null)
                    col.Item().Text(t =>
                    {
                        t.Span("Follow-up: ").SemiBold();
                        t.Span(d.FollowUp);
                    });

                col.Item().PaddingTop(8, Unit.Millimetre).Row(row =>
                {
                    row.RelativeItem();
                    row.ConstantItem(70, Unit.Millimetre).Column(c =>
                    {
                        c.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Darken1).PaddingTop(1, Unit.Millimetre).Text(d.DoctorName).SemiBold();
                        if (!string.IsNullOrWhiteSpace(d.DoctorTitle)) c.Item().Text(d.DoctorTitle).FontSize(8);
                        if (!string.IsNullOrWhiteSpace(d.LicenceNumber)) c.Item().Text($"Licence {d.LicenceNumber}").FontSize(8);
                        c.Item().Text($"Signed {d.SignedOn}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Colors.Grey.Medium));
                text.Span("Confidential medical document.");
                if (d.ShowBranding) text.Span(" Powered by CareLink");
            });
        }));

        static void Section(ColumnDescriptor col, string title, string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            col.Item().PaddingTop(1, Unit.Millimetre).Text(title).FontSize(11).SemiBold();
            col.Item().Text(body);
        }
    }

    public Task<byte[]> ReceiptAsync(CareReceiptData data, CancellationToken ct) => Task.FromResult(ReceiptDocument(data).GeneratePdf());

    /// <summary>A payment receipt on A5: letterhead, receipt number and date, patient, bill, amount, method and reference, the bill's balance after it.</summary>
    public static IDocument ReceiptDocument(CareReceiptData d)
    {
        var logo = Usable(d.Logo);
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Grey.Darken4));
            page.Header().PaddingBottom(4, Unit.Millimetre).BorderBottom(0.75f).BorderColor(Colors.Grey.Medium).PaddingBottom(3, Unit.Millimetre).Row(row =>
            {
                if (logo is not null) row.ConstantItem(18, Unit.Millimetre).Height(18, Unit.Millimetre).Image(logo).FitArea();
                row.RelativeItem().PaddingLeft(logo is null ? 0 : 4, Unit.Millimetre).Column(col =>
                {
                    col.Item().Text(d.ClinicName).FontSize(14).Bold();
                    if (!string.IsNullOrWhiteSpace(d.Address)) col.Item().Text(d.Address).FontSize(8).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(d.Phone)) col.Item().Text(d.Phone).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
            page.Content().Column(col =>
            {
                col.Spacing(3, Unit.Millimetre);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Receipt {d.ReceiptNumber}").FontSize(13).Bold();
                    row.AutoItem().AlignBottom().Text(d.PaidOn);
                });
                if (d.ReversedText is not null) col.Item().Text(d.ReversedText).Italic().FontColor(Colors.Red.Darken2);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(3);
                    });
                    void Row(string label, string? value)
                    {
                        if (string.IsNullOrWhiteSpace(value)) return;
                        table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(1.5f, Unit.Millimetre).Text(label).SemiBold();
                        table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(1.5f, Unit.Millimetre).Text(value);
                    }
                    Row("Patient", $"{d.PatientName} ({d.PatientNumber})");
                    Row("Bill", d.BillNumber);
                    Row("Amount received", d.Amount);
                    Row("Method", d.Method);
                    Row("Reference", d.Reference);
                    Row("Balance on the bill", d.BillBalance);
                });
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Colors.Grey.Medium));
                text.Span("Thank you.");
                if (d.ShowBranding) text.Span(" Powered by CareLink");
            });
        }));
    }

    /// <summary>The logo re-encoded as PNG (keeping transparency), or null when the bytes are not a readable image.</summary>
    private static byte[]? Usable(byte[]? image)
    {
        if (image is null or { Length: 0 }) return null;
        try
        {
            using var bitmap = SKBitmap.Decode(image);
            if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0) return null;
            using var encoded = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
            return encoded?.ToArray();
        }
        catch (Exception)
        {
            return null; // a broken logo leaves the letterhead without it rather than failing the prescription
        }
    }
}
