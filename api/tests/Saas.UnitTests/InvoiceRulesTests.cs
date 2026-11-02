using Saas.Application.Invoices;
using Saas.Domain.Invoices;

namespace Saas.UnitTests;

public class InvoiceMathTests
{
    [Fact]
    public void Line_applies_discount_then_tax_rounding_each_step()
    {
        // 2.5 x 12,500.50 = 31,251.25; 10% off = 3,125.125 -> 3,125.13; tax 7.5% of 28,126.12 = 2,109.459 -> 2,109.46
        var r = InvoiceMath.Line(new LineInput(2.5m, 1_250_050, 10m, 7.5m));
        Assert.Equal(3_125_125, r.GrossMinor);
        Assert.Equal(312_513, r.DiscountMinor);
        Assert.Equal(2_812_612, r.LineTotalMinor);
        Assert.Equal(210_946, r.TaxMinor);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    [InlineData(-0.5, -1)]
    public void Rounds_half_away_from_zero(decimal value, long expected) => Assert.Equal(expected, InvoiceMath.Round(value));

    [Fact]
    public void Totals_add_tax_and_shipping_after_discount()
    {
        var lines = new[]
        {
            InvoiceMath.Line(new LineInput(1, 15_000_000, 10, 7.5m)),
            InvoiceMath.Line(new LineInput(2.5m, 1_250_050, 0, 0)),
        };
        var t = InvoiceMath.Totals(lines, shippingMinor: 500_000);
        Assert.Equal(18_125_125, t.SubtotalMinor);
        Assert.Equal(1_500_000, t.DiscountTotalMinor);
        Assert.Equal(1_012_500, t.TaxTotalMinor);
        Assert.Equal(18_137_625, t.TotalMinor);
    }

    [Fact]
    public void Payments_move_invoice_between_partially_paid_and_paid()
    {
        var invoice = new Invoice { Number = "INV-0001", Currency = "NGN", PublicToken = "t", Status = InvoiceStatus.Sent, TotalMinor = 10_000 };
        var now = DateTimeOffset.UtcNow;

        invoice.Payments.Add(new InvoicePayment { AmountMinor = 4_000, ReceiptNumber = "RCT-0001" });
        InvoiceMath.ApplyPayments(invoice, now);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(6_000, invoice.BalanceMinor);

        invoice.Payments.Add(new InvoicePayment { AmountMinor = 6_000, ReceiptNumber = "RCT-0002" });
        InvoiceMath.ApplyPayments(invoice, now);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.NotNull(invoice.PaidAt);

        invoice.Payments.RemoveAt(1);
        InvoiceMath.ApplyPayments(invoice, now);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Null(invoice.PaidAt);
    }

    [Fact]
    public void Draft_and_void_invoices_keep_their_status_when_paid()
    {
        var invoice = new Invoice { Number = "1", Currency = "NGN", PublicToken = "t", Status = InvoiceStatus.Void, TotalMinor = 100 };
        invoice.Payments.Add(new InvoicePayment { AmountMinor = 100, ReceiptNumber = "R" });
        InvoiceMath.ApplyPayments(invoice, DateTimeOffset.UtcNow);
        Assert.Equal(InvoiceStatus.Void, invoice.Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Sent, -1, 100, InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.PartiallyPaid, -1, 100, InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Sent, 0, 100, InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Draft, -10, 100, InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Paid, -10, 0, InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void, -10, 100, InvoiceStatus.Void)]
    public void Overdue_is_derived_from_due_date_and_balance(InvoiceStatus stored, int dueInDays, long balance, InvoiceStatus expected)
    {
        var today = new DateOnly(2026, 10, 12);
        var invoice = new Invoice
        {
            Number = "1", Currency = "NGN", PublicToken = "t", Status = stored, DueDate = today.AddDays(dueInDays), BalanceMinor = balance,
        };
        Assert.Equal(expected, invoice.DisplayStatus(today));
    }
}

public class ScheduleTests
{
    [Fact]
    public void Monthly_schedule_on_the_31st_does_not_drift()
    {
        var start = new DateOnly(2026, 1, 31);
        Assert.Equal(new DateOnly(2026, 2, 28), Schedule.Occurrence(start, RecurringFrequency.Monthly, 1));
        Assert.Equal(new DateOnly(2026, 3, 31), Schedule.Occurrence(start, RecurringFrequency.Monthly, 2));
    }

    [Fact]
    public void Next_after_skips_missed_periods()
    {
        var start = new DateOnly(2026, 1, 15);
        Assert.Equal(new DateOnly(2026, 11, 15), Schedule.NextAfter(start, RecurringFrequency.Monthly, new DateOnly(2026, 10, 15)));
        Assert.Equal(new DateOnly(2026, 10, 15), Schedule.NextOnOrAfter(start, RecurringFrequency.Monthly, new DateOnly(2026, 10, 15)));
        Assert.Equal(new DateOnly(2027, 1, 15), Schedule.NextAfter(start, RecurringFrequency.Quarterly, new DateOnly(2026, 10, 15)));
        Assert.Equal(new DateOnly(2026, 1, 22), Schedule.NextAfter(start, RecurringFrequency.Weekly, start));
    }

    [Fact]
    public void Today_uses_the_business_time_zone()
    {
        // 23:30 UTC is already the next day in Lagos (UTC+1).
        Assert.Equal(new DateOnly(2026, 11, 1), InvoiceWorkspace.Today(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero), "Africa/Lagos"));
    }
}
