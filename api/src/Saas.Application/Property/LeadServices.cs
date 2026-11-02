using Saas.Domain.Core;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Application.Property;

// Enquiries

public sealed record EnquiryRequest(string Name, string Phone, string? Email, string? Message);
public sealed record EnquiryDto(Guid Id, Guid ListingId, string ListingTitle, string Name, string Phone, string? Email, string Message,
    EnquiryStatus Status, DateTimeOffset CreatedAt);
public sealed record SetEnquiryStatusRequest(EnquiryStatus Status);
public sealed record MyEnquiryDto(Guid Id, Guid ListingId, string ListingTitle, string ListingUrl, string AgencyName, string Message,
    EnquiryStatus Status, DateTimeOffset CreatedAt);

public sealed class EnquiryRequestValidator : AbstractValidator<EnquiryRequest>
{
    public EnquiryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30).Matches(@"^\+?[0-9 ()-]{7,}$").WithMessage("Enter a phone number we can call.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Message).MaximumLength(2000);
    }
}

/// <summary>Messages from seekers to the listing's agent.</summary>
public sealed class LeadService(IAppDbContext db, ICurrentUser user, PropertyCore core, NotificationQueue notifications, TimeProvider clock)
{
    public async Task SendAsync(Guid listingId, EnquiryRequest r, CancellationToken ct)
    {
        var listing = await db.Listings.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Id == listingId && l.Status == ListingStatus.Active && db.Tenants.Any(t => t.Id == l.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Listing");
        core.UseTenantOf(listing.TenantId);
        var enquiry = new Enquiry
        {
            ListingId = listing.Id, AgentProfileId = listing.AgentProfileId, UserId = user.UserId, Name = r.Name.Trim(), Phone = r.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim().ToLowerInvariant(), Message = r.Message?.Trim() ?? "",
        };
        db.Enquiries.Add(enquiry);
        var agentEmail = await (from a in db.AgentProfiles join u in db.Users on a.UserId equals u.Id where a.Id == listing.AgentProfileId select u.Email)
            .FirstOrDefaultAsync(ct);
        if (agentEmail is not null)
            notifications.Email(agentEmail, "enquiry.new", new()
            {
                ["listing"] = listing.Title, ["name"] = enquiry.Name, ["phone"] = enquiry.Phone, ["email"] = enquiry.Email ?? "",
                ["message"] = enquiry.Message, ["link"] = $"{core.Web}/studio/enquiries",
            }, listing.TenantId);
        await db.SaveChangesAsync(ct);
        await db.AddListingStatAsync(listing.TenantId, listing.Id, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), "enquiries", ct);
    }

    public async Task<IReadOnlyList<EnquiryDto>> InboxAsync(EnquiryStatus? status, CancellationToken ct) =>
        await (from e in db.Enquiries.AsNoTracking()
               join l in db.Listings on e.ListingId equals l.Id
               where status == null || e.Status == status
               orderby e.CreatedAt descending
               select new EnquiryDto(e.Id, l.Id, l.Title, e.Name, e.Phone, e.Email, e.Message, e.Status, e.CreatedAt)).Take(500).ToListAsync(ct);

    public async Task SetStatusAsync(Guid id, SetEnquiryStatusRequest r, CancellationToken ct)
    {
        var enquiry = await db.Enquiries.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Enquiry");
        enquiry.Status = r.Status;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MyEnquiryDto>> MineAsync(CancellationToken ct)
    {
        var me = user.UserId ?? throw new ForbiddenException();
        var rows = await (from e in db.Enquiries.IgnoreQueryFilters().AsNoTracking()
                          join l in db.Listings.IgnoreQueryFilters() on e.ListingId equals l.Id
                          join a in db.AgencyProfiles.IgnoreQueryFilters() on e.TenantId equals a.TenantId
                          where e.UserId == me
                          orderby e.CreatedAt descending
                          select new { E = e, l.Title, ListingSlug = l.Slug, AgencySlug = a.Slug, AgencyName = a.Name }).Take(200).ToListAsync(ct);
        return rows.Select(x => new MyEnquiryDto(x.E.Id, x.E.ListingId, x.Title, core.ListingUrl(x.AgencySlug, x.ListingSlug), x.AgencyName,
            x.E.Message, x.E.Status, x.E.CreatedAt)).ToList();
    }
}

// Inspections

public sealed record SlotInput(DayOfWeek Weekday, string Start, string End, int SlotMinutes);
public sealed record SaveSlotsRequest(IReadOnlyList<SlotInput> Slots);
public sealed record SlotDto(DayOfWeek Weekday, string Start, string End, int SlotMinutes);
public sealed record InspectionRequest(DateTimeOffset At, string? Phone, string? Notes);

public sealed class InspectionRequestValidator : AbstractValidator<InspectionRequest>
{
    public InspectionRequestValidator()
    {
        RuleFor(x => x.Phone).MaximumLength(30).WithMessage("Enter a phone number of up to 30 characters.");
        RuleFor(x => x.Notes).MaximumLength(1000).WithMessage("Keep notes to 1,000 characters.");
    }
}
public sealed record InspectionDto(Guid Id, Guid ListingId, string ListingTitle, string AreaName, string SeekerName, string? SeekerPhone,
    string AgentName, string? AgentPhone, DateTimeOffset ScheduledAt, InspectionStatus Status, string? Notes);
public sealed record SetInspectionStatusRequest(InspectionStatus Status, DateTimeOffset? ProposedAt);

/// <summary>Agents publish weekly viewing windows; seekers book a free time; the agent confirms. No payment, ever.</summary>
public sealed class InspectionService(IAppDbContext db, ICurrentUser user, PropertyCore core, LocationTree locations, IDbErrors errors,
    NotificationQueue notifications)
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    public const int DaysAhead = 14;
    public static readonly TimeSpan LeadTime = TimeSpan.FromHours(2);

    public async Task<IReadOnlyList<SlotDto>> SlotsAsync(CancellationToken ct)
    {
        var agent = await core.MyAgentAsync(ct);
        return (await db.InspectionSlots.AsNoTracking().Where(s => s.AgentProfileId == agent.Id).OrderBy(s => s.Weekday).ThenBy(s => s.Start).ToListAsync(ct))
            .Select(s => new SlotDto(s.Weekday, s.Start.ToString("HH:mm"), s.End.ToString("HH:mm"), s.SlotMinutes)).ToList();
    }

    public async Task<IReadOnlyList<SlotDto>> SaveSlotsAsync(SaveSlotsRequest r, CancellationToken ct)
    {
        var agent = await core.MyAgentAsync(ct);
        if (r.Slots.Count > 28) throw BadRequestException.Field("slots", "Add up to 28 viewing windows.");
        var parsed = r.Slots.Select((s, i) =>
        {
            if (!TimeOnly.TryParseExact(s.Start, "HH:mm", out var start) || !TimeOnly.TryParseExact(s.End, "HH:mm", out var end) || end <= start)
                throw BadRequestException.Field($"slots[{i}]", "Use times like 09:00 and 17:00, with the end after the start.");
            if (s.SlotMinutes is < 15 or > 240) throw BadRequestException.Field($"slots[{i}].slotMinutes", "Viewings last 15 minutes to 4 hours.");
            return new InspectionSlot { AgentProfileId = agent.Id, Weekday = s.Weekday, Start = start, End = end, SlotMinutes = s.SlotMinutes };
        }).ToList();
        db.InspectionSlots.RemoveRange(await db.InspectionSlots.Where(s => s.AgentProfileId == agent.Id).ToListAsync(ct));
        db.InspectionSlots.AddRange(parsed);
        await db.SaveChangesAsync(ct);
        return await SlotsAsync(ct);
    }

    /// <summary>Free viewing times for a listing over the next two weeks, at least two hours from now.</summary>
    public async Task<IReadOnlyList<DateTimeOffset>> TimesAsync(Guid listingId, CancellationToken ct)
    {
        var listing = await LiveListingAsync(listingId, ct);
        var grid = await GridAsync(listing.AgentProfileId, ct);
        var booked = await BookedAsync(listing.AgentProfileId, ct);
        return grid.Where(t => !booked.Contains(t)).ToList();
    }

    public async Task<InspectionDto> RequestAsync(Guid listingId, InspectionRequest r, CancellationToken ct)
    {
        var me = user.UserId ?? throw new ForbiddenException();
        var listing = await LiveListingAsync(listingId, ct);
        var at = r.At.ToUniversalTime();
        if (await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.TenantId == listing.TenantId && m.UserId == me, ct))
            throw new ForbiddenException("You can't book a viewing of your own listing.");
        var grid = await GridAsync(listing.AgentProfileId, ct);
        if (!grid.Contains(at)) throw BadRequestException.Field("at", "Choose one of the available times.");
        if ((await BookedAsync(listing.AgentProfileId, ct)).Contains(at)) throw new ConflictException("That time was just booked. Choose another.");

        core.UseTenantOf(listing.TenantId);
        var name = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync(ct);
        var inspection = new Inspection
        {
            ListingId = listing.Id, AgentProfileId = listing.AgentProfileId, SeekerUserId = me, SeekerName = name,
            SeekerPhone = string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone.Trim(), ScheduledAt = at, Notes = r.Notes?.Trim(),
        };
        db.Inspections.Add(inspection);
        await NotifyAgentAsync(inspection, listing.Title, "inspection.requested", ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (errors.IsUniqueViolation(ex))
        {
            throw new ConflictException("That time was just booked. Choose another.");
        }
        return (await ToDtosAsync([inspection], ct))[0];
    }

    public async Task<IReadOnlyList<InspectionDto>> StudioAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var start = (from ?? core.Now.AddDays(-7)).ToUniversalTime();
        var end = (to ?? core.Now.AddDays(60)).ToUniversalTime();
        var rows = await db.Inspections.AsNoTracking().Where(i => i.ScheduledAt >= start && i.ScheduledAt < end).OrderBy(i => i.ScheduledAt).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<InspectionDto> SetStatusAsync(Guid id, SetInspectionStatusRequest r, CancellationToken ct)
    {
        var inspection = await db.Inspections.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Viewing");
        if (r.ProposedAt is { } proposed)
        {
            var at = proposed.ToUniversalTime();
            if (at <= core.Now) throw BadRequestException.Field("proposedAt", "Choose a time in the future.");
            if ((await BookedAsync(inspection.AgentProfileId, ct)).Contains(at)) throw new ConflictException("You already have a viewing then.");
            inspection.ScheduledAt = at;
            inspection.ReminderSent = false;
        }
        inspection.Status = r.Status;
        var title = await db.Listings.Where(l => l.Id == inspection.ListingId).Select(l => l.Title).FirstAsync(ct);
        if (r.Status is InspectionStatus.Confirmed or InspectionStatus.Cancelled)
        {
            var seekerEmail = await db.Users.Where(u => u.Id == inspection.SeekerUserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
            if (seekerEmail is not null)
                notifications.Email(seekerEmail, r.Status == InspectionStatus.Confirmed ? "inspection.confirmed" : "inspection.cancelled", new()
                {
                    ["listing"] = title, ["when"] = When(inspection.ScheduledAt), ["link"] = $"{core.Web}/me/viewings",
                }, inspection.TenantId);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (errors.IsUniqueViolation(ex))
        {
            throw new ConflictException("You already have a viewing then.");
        }
        return (await ToDtosAsync([inspection], ct))[0];
    }

    public async Task<IReadOnlyList<InspectionDto>> MineAsync(CancellationToken ct)
    {
        var me = user.UserId ?? throw new ForbiddenException();
        var rows = await db.Inspections.IgnoreQueryFilters().AsNoTracking().Where(i => i.SeekerUserId == me).OrderByDescending(i => i.ScheduledAt).Take(100).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task CancelMineAsync(Guid id, CancellationToken ct)
    {
        var me = user.UserId ?? throw new ForbiddenException();
        var tenantId = await db.Inspections.IgnoreQueryFilters().Where(i => i.Id == id && i.SeekerUserId == me).Select(i => (Guid?)i.TenantId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Viewing");
        core.UseTenantOf(tenantId);
        var inspection = await db.Inspections.FirstAsync(i => i.Id == id, ct);
        if (inspection.Status is not (InspectionStatus.Requested or InspectionStatus.Confirmed)) throw new ConflictException("This viewing can't be cancelled.");
        inspection.Status = InspectionStatus.Cancelled;
        var title = await db.Listings.Where(l => l.Id == inspection.ListingId).Select(l => l.Title).FirstAsync(ct);
        await NotifyAgentAsync(inspection, title, "inspection.cancelled", ct);
        await db.SaveChangesAsync(ct);
    }

    public static string When(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone).ToString("ddd d MMM, h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    private async Task NotifyAgentAsync(Inspection inspection, string title, string template, CancellationToken ct)
    {
        var email = await (from a in db.AgentProfiles.IgnoreQueryFilters() join u in db.Users on a.UserId equals u.Id
                           where a.Id == inspection.AgentProfileId select u.Email).FirstOrDefaultAsync(ct);
        if (email is not null)
            notifications.Email(email, template, new()
            {
                ["listing"] = title, ["when"] = When(inspection.ScheduledAt), ["name"] = inspection.SeekerName, ["phone"] = inspection.SeekerPhone ?? "",
                ["link"] = $"{core.Web}/studio/viewings",
            }, inspection.TenantId);
    }

    private async Task<Listing> LiveListingAsync(Guid listingId, CancellationToken ct) =>
        await db.Listings.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Id == listingId && l.Status == ListingStatus.Active && db.Tenants.Any(t => t.Id == l.TenantId && t.Status == TenantStatus.Active), ct)
        ?? throw new NotFoundException("Listing");

    private async Task<List<DateTimeOffset>> GridAsync(Guid agentId, CancellationToken ct)
    {
        var slots = await db.InspectionSlots.IgnoreQueryFilters().AsNoTracking().Where(s => s.AgentProfileId == agentId).ToListAsync(ct);
        var earliest = core.Now + LeadTime;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(core.Now, Zone).DateTime);
        var times = new List<DateTimeOffset>();
        for (var d = 0; d < DaysAhead; d++)
        {
            var date = today.AddDays(d);
            foreach (var slot in slots.Where(s => s.Weekday == date.DayOfWeek).OrderBy(s => s.Start))
                for (var t = slot.Start; t.AddMinutes(slot.SlotMinutes) <= slot.End && t >= slot.Start; t = t.AddMinutes(slot.SlotMinutes))
                {
                    var local = date.ToDateTime(t);
                    var at = new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
                    if (at >= earliest) times.Add(at);
                    if (t.AddMinutes(slot.SlotMinutes) < t) break; // wrapped past midnight
                }
        }
        return times.Distinct().Order().ToList();
    }

    private async Task<HashSet<DateTimeOffset>> BookedAsync(Guid agentId, CancellationToken ct) =>
        (await db.Inspections.IgnoreQueryFilters().Where(i => i.AgentProfileId == agentId && i.ScheduledAt >= core.Now
                && (i.Status == InspectionStatus.Requested || i.Status == InspectionStatus.Confirmed))
            .Select(i => i.ScheduledAt).ToListAsync(ct)).Select(t => t.ToUniversalTime()).ToHashSet();

    private async Task<List<InspectionDto>> ToDtosAsync(IReadOnlyList<Inspection> rows, CancellationToken ct)
    {
        var listingIds = rows.Select(r => r.ListingId).Distinct().ToList();
        var agentIds = rows.Select(r => r.AgentProfileId).Distinct().ToList();
        var listings = await db.Listings.IgnoreQueryFilters().Where(l => listingIds.Contains(l.Id)).Select(l => new { l.Id, l.Title, l.LocationId }).ToDictionaryAsync(l => l.Id, ct);
        var agents = await db.AgentProfiles.IgnoreQueryFilters().Where(a => agentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var tree = await locations.GetAsync(ct);
        return rows.Select(i =>
        {
            var l = listings.GetValueOrDefault(i.ListingId);
            var a = agents.GetValueOrDefault(i.AgentProfileId);
            return new InspectionDto(i.Id, i.ListingId, l?.Title ?? "Listing removed", l is null ? "" : tree.Find(l.LocationId)?.Name ?? "", i.SeekerName, i.SeekerPhone, a?.DisplayName ?? "",
                a?.Phone, i.ScheduledAt, i.Status, i.Notes);
        }).ToList();
    }
}
