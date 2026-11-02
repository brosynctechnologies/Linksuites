using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed record LineInput(decimal Quantity, long UnitPriceMinor, decimal DiscountPercent, decimal TaxRatePercent);

public sealed record LineResult(long GrossMinor, long DiscountMinor, long LineTotalMinor, long TaxMinor);

public sealed record TaxBreakdown(string Name, decimal RatePercent, long AmountMinor);

public sealed record DocumentTotals(long SubtotalMinor, long DiscountTotalMinor, long TaxTotalMinor, long ShippingMinor, long TotalMinor);

/// <summary>
/// The one place invoice and quote amounts are calculated. Every line is rounded to whole minor units
/// (half away from zero) before summing, so totals always equal the sum of what is printed.
/// </summary>
public static class InvoiceMath
{
    public static long Round(decimal value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    public static LineResult Line(LineInput l)
    {
        var gross = Round(l.Quantity * l.UnitPriceMinor);
        var discount = Round(gross * Math.Clamp(l.DiscountPercent, 0, 100) / 100m);
        var net = gross - discount;
        var tax = Round(net * l.TaxRatePercent / 100m);
        return new LineResult(gross, discount, net, tax);
    }

    public static DocumentTotals Totals(IEnumerable<LineResult> lines, long shippingMinor)
    {
        var list = lines.ToList();
        var subtotal = list.Sum(l => l.GrossMinor);
        var discount = list.Sum(l => l.DiscountMinor);
        var tax = list.Sum(l => l.TaxMinor);
        return new DocumentTotals(subtotal, discount, tax, shippingMinor, subtotal - discount + tax + shippingMinor);
    }

    /// <summary>Tax grouped by name and rate, as printed under the subtotal.</summary>
    public static IReadOnlyList<TaxBreakdown> Taxes(IEnumerable<DocumentLine> lines) =>
        lines.Where(l => l.TaxMinor != 0 || l.TaxRatePercent != 0)
            .GroupBy(l => (Name: l.TaxName ?? "Tax", l.TaxRatePercent))
            .Select(g => new TaxBreakdown(g.Key.Name, g.Key.TaxRatePercent, g.Sum(l => l.TaxMinor)))
            .OrderBy(t => t.Name).ToList();

    /// <summary>Recomputes every line and the document totals in place.</summary>
    public static void Apply(Document doc, IList<DocumentLine> lines)
    {
        var results = new List<LineResult>(lines.Count);
        foreach (var line in lines)
        {
            var r = Line(new LineInput(line.Quantity, line.UnitPriceMinor, line.DiscountPercent, line.TaxRatePercent));
            line.LineTotalMinor = r.LineTotalMinor;
            line.TaxMinor = r.TaxMinor;
            results.Add(r);
        }
        var t = Totals(results, doc.ShippingMinor);
        doc.SubtotalMinor = t.SubtotalMinor;
        doc.DiscountTotalMinor = t.DiscountTotalMinor;
        doc.TaxTotalMinor = t.TaxTotalMinor;
        doc.TotalMinor = t.TotalMinor;
    }

    /// <summary>Sets amount paid, balance and the paid/partially-paid status from the recorded payments.</summary>
    public static void ApplyPayments(Invoice invoice, DateTimeOffset now)
    {
        invoice.AmountPaidMinor = invoice.Payments.Sum(p => p.AmountMinor);
        // Never negative: an overpayment (AmountPaid above Total) shows as nothing left to pay, not a negative balance.
        invoice.BalanceMinor = Math.Max(0, invoice.TotalMinor - invoice.AmountPaidMinor);
        if (invoice.Status is InvoiceStatus.Void or InvoiceStatus.Draft) return;

        if (invoice.BalanceMinor <= 0 && invoice.TotalMinor > 0)
        {
            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAt ??= now;
        }
        else
        {
            invoice.PaidAt = null;
            invoice.Status = invoice.AmountPaidMinor > 0
                ? InvoiceStatus.PartiallyPaid
                : invoice.ViewedAt is not null ? InvoiceStatus.Viewed : InvoiceStatus.Sent;
        }
    }
}
