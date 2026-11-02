using System.Text;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed record CategoryAmountDto(string Category, long AmountMinor);
public sealed record MonthDto(string Month, long IncomeMinor, long ExpensesMinor, long ProfitMinor);

public sealed record ProfitReportDto(
    string Currency, DateOnly From, DateOnly To, long IncomeMinor, long ExpensesMinor, long ProfitMinor, long InvoicedMinor,
    long OutstandingMinor, long TaxCollectedMinor, long TaxOnExpensesMinor, IReadOnlyList<CategoryAmountDto> ExpensesByCategory,
    IReadOnlyList<MonthDto> Months);

/// <summary>Cash-basis profit and loss in the business base currency, using the rates saved on each record.</summary>
public sealed class ReportService(IAppDbContext db, InvoiceWorkspace ws, PlanService plans)
{
    public async Task<ProfitReportDto> ProfitAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (!await plans.HasFeatureAsync(LimitKeys.Reports, ct))
            throw new PlanLimitException("The profit report is part of the Business plan. Upgrade to see your profit.");
        var today = await ws.TodayAsync(ct);
        var start = from ?? new DateOnly(today.Year, 1, 1);
        var end = to ?? new DateOnly(today.Year, 12, 31);
        if (end < start) throw BadRequestException.Field("to", "The end date can't be before the start date.");
        if (end.DayNumber - start.DayNumber > 3660) throw BadRequestException.Field("from", "Choose a period of 10 years or less.");
        var business = await ws.BusinessAsync(ct);

        var payments = await db.InvoicePayments.AsNoTracking().Where(p => p.PaidAt >= start && p.PaidAt <= end)
            .Select(p => new { p.PaidAt, p.BaseAmountMinor }).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.Date >= start && e.Date <= end)
            .Select(e => new { e.Date, e.Category, e.BaseAmountMinor, e.TaxMinor, e.ExchangeRate }).ToListAsync(ct);
        var invoiced = await db.Invoices.AsNoTracking()
            .Where(i => i.IssueDate >= start && i.IssueDate <= end && i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Void)
            .Select(i => new { i.TotalMinor, i.TaxTotalMinor, i.ExchangeRate }).ToListAsync(ct);
        var open = new[] { InvoiceStatus.Sent, InvoiceStatus.Viewed, InvoiceStatus.PartiallyPaid };
        var outstanding = (await db.Invoices.AsNoTracking().Where(i => open.Contains(i.Status) && i.BalanceMinor > 0)
            .Select(i => new { i.BalanceMinor, i.ExchangeRate }).ToListAsync(ct))
            .Sum(i => ExchangeRateService.ToBase(i.BalanceMinor, i.ExchangeRate));

        var income = payments.Sum(p => p.BaseAmountMinor);
        var spent = expenses.Sum(e => e.BaseAmountMinor);
        var months = new List<MonthDto>();
        for (var m = new DateOnly(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1))
        {
            var mIncome = payments.Where(p => p.PaidAt.Year == m.Year && p.PaidAt.Month == m.Month).Sum(p => p.BaseAmountMinor);
            var mSpent = expenses.Where(e => e.Date.Year == m.Year && e.Date.Month == m.Month).Sum(e => e.BaseAmountMinor);
            months.Add(new MonthDto($"{m.Year:D4}-{m.Month:D2}", mIncome, mSpent, mIncome - mSpent));
        }

        return new ProfitReportDto(business.Currency, start, end, income, spent, income - spent,
            invoiced.Sum(i => ExchangeRateService.ToBase(i.TotalMinor, i.ExchangeRate)), outstanding,
            invoiced.Sum(i => ExchangeRateService.ToBase(i.TaxTotalMinor, i.ExchangeRate)),
            expenses.Sum(e => ExchangeRateService.ToBase(e.TaxMinor, e.ExchangeRate)),
            expenses.GroupBy(e => e.Category).Select(g => new CategoryAmountDto(g.Key, g.Sum(e => e.BaseAmountMinor)))
                .OrderByDescending(c => c.AmountMinor).ToList(),
            months);
    }

    public async Task<byte[]> ProfitCsvAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var report = await ProfitAsync(from, to, ct);
        var sb = new StringBuilder($"Month,Income ({report.Currency}),Expenses ({report.Currency}),Profit ({report.Currency})\r\n");
        foreach (var m in report.Months)
            sb.Append($"{m.Month},{Csv.Major(m.IncomeMinor)},{Csv.Major(m.ExpensesMinor)},{Csv.Major(m.ProfitMinor)}\r\n");
        sb.Append($"Total,{Csv.Major(report.IncomeMinor)},{Csv.Major(report.ExpensesMinor)},{Csv.Major(report.ProfitMinor)}\r\n");
        return Csv.Bytes(sb);
    }
}
