using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed class RecurringService(IAppDbContext db, InvoiceWorkspace ws, PlanService plans)
{
    public async Task<IReadOnlyList<RecurringDto>> ListAsync(CancellationToken ct)
    {
        var schedules = await db.RecurringSchedules.AsNoTracking().OrderBy(s => s.NextRunDate).ToListAsync(ct);
        var clientIds = schedules.Select(s => s.ClientId).Distinct().ToList();
        var names = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var taxIds = schedules.SelectMany(s => s.Lines).Where(l => l.TaxRateId is not null).Select(l => l.TaxRateId!.Value).Distinct().ToList();
        var taxes = await db.TaxRates.AsNoTracking().Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.RatePercent, ct);
        return schedules.Select(s => ToDto(s, names.GetValueOrDefault(s.ClientId, "(deleted client)"), taxes)).ToList();
    }

    public async Task<RecurringDto> SaveAsync(Guid? id, SaveRecurringRequest r, CancellationToken ct)
    {
        if (!await plans.HasFeatureAsync(LimitKeys.Recurring, ct))
            throw new PlanLimitException("Recurring invoices are part of the Pro plan. Upgrade to use them.");
        var business = await ws.BusinessAsync(ct);
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ClientId, ct)
            ?? throw BadRequestException.Field("clientId", "That client does not exist.");
        // Validates tax rates and premium templates the same way as a normal invoice.
        await ws.BuildLinesAsync<InvoiceItem>(r.Items, ct);
        var template = await ws.ResolveTemplateAsync(r.Template, business, ct);
        var today = await ws.TodayAsync(ct);

        RecurringSchedule schedule;
        if (id is null)
        {
            schedule = new RecurringSchedule { Currency = r.Currency };
            db.RecurringSchedules.Add(schedule);
        }
        else
        {
            schedule = await db.RecurringSchedules.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Recurring invoice");
        }
        var timingChanged = id is null || schedule.StartDate != r.StartDate || schedule.Frequency != r.Frequency;
        schedule.ClientId = client.Id;
        schedule.Frequency = r.Frequency;
        schedule.StartDate = r.StartDate;
        schedule.EndDate = r.EndDate;
        schedule.DueInDays = r.DueInDays;
        schedule.AutoSend = r.AutoSend;
        schedule.IsActive = r.IsActive;
        schedule.Currency = r.Currency.ToUpperInvariant();
        schedule.Template = template;
        schedule.ShippingMinor = r.ShippingMinor;
        schedule.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        schedule.Terms = string.IsNullOrWhiteSpace(r.Terms) ? null : r.Terms.Trim();
        schedule.Lines = r.Items.Select(l => new RecurringLine
        {
            ItemId = l.ItemId, Description = l.Description.Trim(), Quantity = l.Quantity, UnitPriceMinor = l.UnitPriceMinor,
            DiscountPercent = l.DiscountPercent, TaxRateId = l.TaxRateId,
        }).ToList();
        if (timingChanged)
            schedule.NextRunDate = r.StartDate >= today ? r.StartDate : Schedule.NextOnOrAfter(r.StartDate, r.Frequency, today);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(s => s.Id == schedule.Id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var schedule = await db.RecurringSchedules.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Recurring invoice");
        db.RecurringSchedules.Remove(schedule);
        await db.SaveChangesAsync(ct);
    }

    private static RecurringDto ToDto(RecurringSchedule s, string clientName, IReadOnlyDictionary<Guid, decimal> taxes)
    {
        var results = s.Lines.Select(l => InvoiceMath.Line(new LineInput(l.Quantity, l.UnitPriceMinor, l.DiscountPercent,
            l.TaxRateId is { } t ? taxes.GetValueOrDefault(t) : 0)));
        var total = InvoiceMath.Totals(results, s.ShippingMinor).TotalMinor;
        return new RecurringDto(s.Id, s.ClientId, clientName, s.Frequency, s.StartDate, s.EndDate, s.DueInDays, s.AutoSend, s.IsActive,
            s.Currency, s.Template,
            s.Lines.Select(l => new LineRequest(l.ItemId, l.Description, l.Quantity, l.UnitPriceMinor, l.DiscountPercent, l.TaxRateId)).ToList(),
            s.ShippingMinor, s.Notes, s.Terms, s.NextRunDate, s.LastRunDate, s.InvoicesCreated, total);
    }
}

public static class Schedule
{
    /// <summary>The n-th occurrence counted from the start date, so monthly runs on the 31st don't drift to the 28th.</summary>
    public static DateOnly Occurrence(DateOnly start, RecurringFrequency frequency, int n) => frequency switch
    {
        RecurringFrequency.Weekly => start.AddDays(7 * n),
        RecurringFrequency.Monthly => start.AddMonths(n),
        RecurringFrequency.Quarterly => start.AddMonths(3 * n),
        RecurringFrequency.Yearly => start.AddYears(n),
        _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
    };

    public static DateOnly NextOnOrAfter(DateOnly start, RecurringFrequency frequency, DateOnly date)
    {
        var n = 0;
        while (Occurrence(start, frequency, n) < date) n++;
        return Occurrence(start, frequency, n);
    }

    public static DateOnly NextAfter(DateOnly start, RecurringFrequency frequency, DateOnly date)
    {
        var n = 0;
        while (Occurrence(start, frequency, n) <= date) n++;
        return Occurrence(start, frequency, n);
    }
}

/// <summary>
/// Background work for one tenant: create due recurring invoices and send payment reminders.
/// The caller sets the tenant; both steps are idempotent, so running twice in a day is harmless.
/// </summary>
public sealed class InvoiceJobs(
    IAppDbContext db,
    InvoiceWorkspace ws,
    InvoiceService invoices,
    PlanService plans,
    NotificationQueue notifications,
    ILogger<InvoiceJobs> logger)
{
    public async Task<(int Created, int Reminded)> RunAsync(CancellationToken ct)
    {
        var today = await ws.TodayAsync(ct);
        int created;
        try
        {
            created = await RunRecurringAsync(today, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Reminders are independent of recurring invoices: a failure there must not stop them.
            logger.LogError(ex, "Recurring invoices failed; sending reminders anyway");
            db.ChangeTracker.Clear();
            created = 0;
        }
        return (created, await SendRemindersAsync(today, ct));
    }

    public async Task<int> RunRecurringAsync(DateOnly today, CancellationToken ct)
    {
        if (!await plans.HasFeatureAsync(LimitKeys.Recurring, ct)) return 0;
        var due = await db.RecurringSchedules.Where(s => s.IsActive && s.NextRunDate <= today).ToListAsync(ct);
        var created = 0;
        foreach (var s in due)
        {
            if (s.EndDate is { } end && s.NextRunDate > end)
            {
                s.IsActive = false;
                continue;
            }
            if (!await db.Clients.AnyAsync(c => c.Id == s.ClientId, ct))
            {
                s.IsActive = false;
                logger.LogWarning("Stopped recurring schedule {Id}: its client was deleted", s.Id);
                continue;
            }

            // One invoice per run, dated today, even if the server was down for several periods.
            InvoiceDto invoice;
            try
            {
                invoice = await invoices.CreateAsync(new SaveInvoiceRequest
                {
                    ClientId = s.ClientId,
                    IssueDate = today,
                    DueDate = today.AddDays(s.DueInDays),
                    Currency = s.Currency,
                    Template = s.Template,
                    Items = s.Lines.Select(l => new LineRequest(l.ItemId, l.Description, l.Quantity, l.UnitPriceMinor, l.DiscountPercent,
                        l.TaxRateId)).ToList(),
                    ShippingMinor = s.ShippingMinor,
                    Notes = s.Notes,
                    Terms = s.Terms,
                }, ct);
            }
            catch (AppException ex)
            {
                // One bad schedule (a missing tax rate, a plan limit...) is skipped and retried on the next run;
                // the schedules after it and the reminders still go out.
                logger.LogWarning("Skipped recurring schedule {Id}: {Reason}", s.Id, ex.Message);
                foreach (var entry in db.ChangeTracker.Entries<Invoice>().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
                continue;
            }
            var entity = await db.Invoices.FirstAsync(i => i.Id == invoice.Id, ct);
            entity.RecurringScheduleId = s.Id;
            s.LastRunDate = today;
            s.InvoicesCreated++;
            s.NextRunDate = Schedule.NextAfter(s.StartDate, s.Frequency, today);
            if (s.EndDate is { } last && s.NextRunDate > last) s.IsActive = false;
            await db.SaveChangesAsync(ct);
            created++;

            if (s.AutoSend && !string.IsNullOrWhiteSpace(invoice.Client.Email))
                await invoices.SendAsync(invoice.Id, new SendRequest(null, null), ct);
        }
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<int> SendRemindersAsync(DateOnly today, CancellationToken ct)
    {
        var business = await ws.BusinessAsync(ct);
        if (!business.RemindersEnabled || !await plans.HasFeatureAsync(LimitKeys.Reminders, ct)) return 0;

        var dueDates = business.ReminderDaysAfter.Select(d => today.AddDays(-d)).ToList();
        if (business.ReminderDaysBefore is { } before) dueDates.Add(today.AddDays(before));
        var open = new[] { InvoiceStatus.Sent, InvoiceStatus.Viewed, InvoiceStatus.PartiallyPaid };

        var candidates = await db.Invoices
            .Where(i => open.Contains(i.Status) && i.BalanceMinor > 0 && dueDates.Contains(i.DueDate)
                && i.Client.Email != null && (i.LastReminderOn == null || i.LastReminderOn < today))
            .ToListAsync(ct);
        foreach (var invoice in candidates)
        {
            var overdue = invoice.DueDate < today;
            notifications.Email(invoice.Client.Email!, "invoice.reminder", new()
            {
                ["business"] = business.LegalName,
                ["client"] = invoice.Client.Name,
                ["number"] = invoice.Number,
                ["balance"] = Money.Format(invoice.BalanceMinor, invoice.Currency),
                ["due"] = InvoiceWorkspace.Date(invoice.DueDate),
                ["overdue"] = overdue ? "yes" : "no",
                ["link"] = ws.InvoiceUrl(invoice.PublicToken),
            }, invoice.TenantId);
            invoice.LastReminderOn = today;
            db.InvoiceEvents.Add(new InvoiceEvent
            {
                InvoiceId = invoice.Id,
                Type = InvoiceEventType.Reminded,
                Note = overdue ? $"Overdue reminder sent to {invoice.Client.Email}" : $"Upcoming reminder sent to {invoice.Client.Email}",
            });
        }
        await db.SaveChangesAsync(ct);
        return candidates.Count;
    }
}
