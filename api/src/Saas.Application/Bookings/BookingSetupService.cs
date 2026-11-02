using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Auth;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Bookings;
using Saas.Domain.Core;

namespace Saas.Application.Bookings;

/// <summary>Sets up a new bookings business: profile, Mon-Sat 9-6 hours, and the owner as the first staff member.</summary>
public sealed class BookingsProvisioner(IAppDbContext db) : IProductProvisioner
{
    public string Product => Products.Bookings;

    public Task ProvisionAsync(Tenant tenant, SignupRequest request, Guid ownerUserId, CancellationToken ct)
    {
        db.BookingBusinesses.Add(new BookingBusiness
        {
            Name = tenant.Name, Slug = tenant.Slug, Timezone = tenant.Timezone, Currency = tenant.Currency,
            Email = request.Email.Trim().ToLowerInvariant(),
            Policies = "Please arrive 5 minutes early. You can cancel or reschedule up to 24 hours before your appointment.",
        });
        var schedule = new List<ScheduleInterval>();
        for (var day = 0; day < 7; day++)
        {
            var closed = day == 0;
            db.BusinessHours.Add(new BusinessHours { Weekday = day, Closed = closed });
            if (!closed) schedule.Add(new ScheduleInterval { Weekday = day, Start = new TimeOnly(9, 0), End = new TimeOnly(18, 0) });
        }
        db.StaffMembers.Add(new StaffMember
        {
            DisplayName = request.FullName.Trim().Split(' ')[0], UserId = ownerUserId, FeedToken = BookingCore.NewToken(),
            Schedule = schedule, Color = StaffColors.Palette[0],
        });
        return Task.CompletedTask;
    }
}

public static class StaffColors
{
    /// <summary>Muted tints that read well as calendar blocks with dark text.</summary>
    public static readonly string[] Palette = ["#7C5CBF", "#2F8F6E", "#C2255C", "#3D6FB6", "#B7791F", "#0E8A8A", "#8A4F7D", "#5C6B2F"];
}

public sealed class BookingsUsage(IAppDbContext db, BookingCore core) : IUsageProvider
{
    public string Product => Products.Bookings;

    public async Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct)
    {
        var business = await core.BusinessAsync(ct);
        var monthStart = core.MonthStart(business);
        return
        [
            new(LimitKeys.StaffMembers, "Staff", await db.StaffMembers.CountAsync(ct), plan.Limit(LimitKeys.StaffMembers)),
            new(LimitKeys.BookingsPerMonth, "Bookings this month",
                await db.Bookings.CountAsync(b => b.CreatedAt >= monthStart && b.Status != BookingStatus.Expired, ct),
                plan.Limit(LimitKeys.BookingsPerMonth)),
        ];
    }
}

public sealed class BookingSetupService(
    IAppDbContext db, BookingCore core, PlanService plans, IFileStorage storage, ISecretProtector secrets, ICurrentUser user, MerchantKeyRules keyRules)
{
    // Business profile

    public async Task<BookingBusinessDto> GetBusinessAsync(CancellationToken ct) => await ToDtoAsync(await core.BusinessAsync(ct), ct);

    public async Task<BookingBusinessDto> UpdateBusinessAsync(UpdateBookingBusinessRequest r, CancellationToken ct)
    {
        var b = await core.BusinessAsync(ct);
        var slug = r.Slug.Trim().ToLowerInvariant();
        Slugs.EnsureNotNewlyReserved(slug, b.Slug);
        if (slug != b.Slug && await db.BookingBusinesses.IgnoreQueryFilters().AnyAsync(x => x.Slug == slug, ct))
            throw BadRequestException.Field("slug", "That booking link is already taken.");
        var country = await db.Tenants.Where(t => t.Id == b.TenantId).Select(t => t.Country).FirstAsync(ct);
        string? whatsapp = null;
        if (!string.IsNullOrWhiteSpace(r.WhatsappNumber))
            whatsapp = PhoneNumbers.Normalize(r.WhatsappNumber, country) ?? throw BadRequestException.Field("whatsappNumber", "Enter a valid WhatsApp number.");

        b.Name = r.Name.Trim();
        b.Slug = slug;
        b.Category = r.Category;
        b.Description = Clean(r.Description);
        b.Address = Clean(r.Address);
        b.Phone = Clean(r.Phone);
        b.WhatsappNumber = whatsapp;
        b.Email = Clean(r.Email)?.ToLowerInvariant();
        b.Timezone = r.Timezone;
        b.BrandColor = r.BrandColor;
        b.Policies = Clean(r.Policies);
        b.CancellationHours = r.CancellationHours;
        b.SlotIntervalMinutes = r.SlotIntervalMinutes;
        b.MinNoticeMinutes = r.MinNoticeMinutes;
        b.MaxDaysAhead = r.MaxDaysAhead;
        b.BookingEnabled = r.BookingEnabled;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(b, ct);
    }

    public async Task<BookingBusinessDto> UploadImageAsync(Upload upload, bool cover, CancellationToken ct)
    {
        var b = await core.BusinessAsync(ct);
        var url = await SaveImageAsync(upload, $"{b.TenantId}/booking/{(cover ? "cover" : "logo")}", cover ? "booking-cover" : "booking-logo", ct);
        if (cover) b.CoverUrl = url; else b.LogoUrl = url;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(b, ct);
    }

    public async Task<BookingBusinessDto> SetPaystackKeyAsync(string secretKey, CancellationToken ct)
    {
        var key = keyRules.Validate(secretKey);
        var b = await core.BusinessAsync(ct);
        b.PaystackSecretKeyProtected = secrets.Protect(key);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(b, ct);
    }

    public async Task<BookingBusinessDto> RemovePaystackKeyAsync(CancellationToken ct)
    {
        var b = await core.BusinessAsync(ct);
        b.PaystackSecretKeyProtected = null;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(b, ct);
    }

    public async Task<IReadOnlyList<HoursDto>> GetHoursAsync(CancellationToken ct) =>
        (await db.BusinessHours.AsNoTracking().OrderBy(h => h.Weekday).ToListAsync(ct)).Select(BookingCore.ToDto).ToList();

    public async Task<IReadOnlyList<HoursDto>> SetHoursAsync(IReadOnlyList<HoursDto> hours, CancellationToken ct)
    {
        if (hours.Count != 7 || hours.Select(h => h.Weekday).Distinct().Count() != 7 || hours.Any(h => h.Weekday is < 0 or > 6))
            throw BadRequestException.Field("hours", "Send one row for each day of the week (0 = Sunday to 6 = Saturday).");
        var existing = await db.BusinessHours.ToDictionaryAsync(h => h.Weekday, ct);
        foreach (var h in hours)
        {
            var (open, close) = (ParseTime(h.Open, "open"), ParseTime(h.Close, "close"));
            if (!h.Closed && close <= open) throw BadRequestException.Field("hours", "Closing time must be after opening time.");
            if (!existing.TryGetValue(h.Weekday, out var row)) db.BusinessHours.Add(row = new BusinessHours { Weekday = h.Weekday });
            row.Open = open;
            row.Close = close;
            row.Closed = h.Closed;
        }
        await db.SaveChangesAsync(ct);
        return await GetHoursAsync(ct);
    }

    // Services

    public async Task<IReadOnlyList<ServiceDto>> ListServicesAsync(CancellationToken ct) =>
        (await db.BookableServices.AsNoTracking().OrderBy(s => s.Position).ThenBy(s => s.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<ServiceDto> SaveServiceAsync(Guid? id, SaveServiceRequest r, CancellationToken ct)
    {
        var staffIds = (r.StaffIds ?? []).Distinct().ToList();
        if (await db.StaffMembers.CountAsync(s => staffIds.Contains(s.Id), ct) != staffIds.Count)
            throw BadRequestException.Field("staffIds", "One of the staff members no longer exists.");
        BookableService service;
        if (id is null)
        {
            service = new BookableService { Name = r.Name.Trim() };
            db.BookableServices.Add(service);
        }
        else
        {
            service = await db.BookableServices.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Service");
        }
        service.Name = r.Name.Trim();
        service.Description = Clean(r.Description);
        service.DurationMinutes = r.DurationMinutes;
        service.BufferBeforeMinutes = r.BufferBeforeMinutes;
        service.BufferAfterMinutes = r.BufferAfterMinutes;
        service.PriceMinor = r.PriceMinor;
        service.DepositType = r.DepositType;
        service.DepositValue = r.DepositType == DepositType.None ? 0 : r.DepositValue;
        service.IsActive = r.IsActive;
        service.Position = r.Position;
        service.StaffIds = staffIds;
        await db.SaveChangesAsync(ct);
        return ToDto(service);
    }

    /// <summary>Past bookings keep the service name they were made with.</summary>
    public async Task DeleteServiceAsync(Guid id, CancellationToken ct)
    {
        var service = await db.BookableServices.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Service");
        var now = core.Now;
        if (await db.Bookings.AnyAsync(b => b.ServiceId == id && b.StartsAt > now && Booking.Holding.Contains(b.Status), ct))
            throw new ConflictException("This service has upcoming bookings. Turn it off instead, or move those bookings first.");
        db.BookableServices.Remove(service);
        await db.SaveChangesAsync(ct);
    }

    // Staff

    public async Task<IReadOnlyList<StaffDto>> ListStaffAsync(CancellationToken ct)
    {
        var staff = await db.StaffMembers.AsNoTracking().OrderBy(s => s.Position).ThenBy(s => s.DisplayName).ToListAsync(ct);
        var services = await db.BookableServices.AsNoTracking().Select(s => new { s.Id, s.StaffIds }).ToListAsync(ct);
        return staff.Select(s => ToDto(s, services.Where(x => x.StaffIds.Contains(s.Id)).Select(x => x.Id).ToList())).ToList();
    }

    public async Task<StaffDto> GetStaffAsync(Guid id, CancellationToken ct) =>
        (await ListStaffAsync(ct)).FirstOrDefault(s => s.Id == id) ?? throw new NotFoundException("Staff member");

    public async Task<StaffDto> SaveStaffAsync(Guid? id, SaveStaffRequest r, CancellationToken ct)
    {
        if (r.UserId is { } userId && !await db.Memberships.AnyAsync(m => m.UserId == userId && m.Status == MembershipStatus.Active, ct))
            throw BadRequestException.Field("userId", "That person is not on your team.");
        StaffMember staff;
        if (id is null)
        {
            await plans.EnsureCanAddAsync(LimitKeys.StaffMembers, await db.StaffMembers.CountAsync(ct), "Staff members", ct);
            var count = await db.StaffMembers.CountAsync(ct);
            var hours = await db.BusinessHours.AsNoTracking().Where(h => !h.Closed).ToListAsync(ct);
            staff = new StaffMember
            {
                DisplayName = r.DisplayName.Trim(), FeedToken = BookingCore.NewToken(),
                Color = StaffColors.Palette[count % StaffColors.Palette.Length],
                // New staff start on the business's opening hours; edit their schedule afterwards.
                Schedule = hours.Select(h => new ScheduleInterval { Weekday = h.Weekday, Start = h.Open, End = h.Close }).ToList(),
            };
            db.StaffMembers.Add(staff);
        }
        else
        {
            staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Staff member");
        }
        staff.DisplayName = r.DisplayName.Trim();
        staff.Bio = Clean(r.Bio);
        staff.Email = Clean(r.Email)?.ToLowerInvariant();
        if (r.Color is not null) staff.Color = r.Color;
        staff.IsBookable = r.IsBookable;
        staff.Position = r.Position;
        staff.UserId = r.UserId;

        if (r.ServiceIds is { } serviceIds)
        {
            var services = await db.BookableServices.ToListAsync(ct);
            foreach (var service in services)
            {
                var should = serviceIds.Contains(service.Id);
                var has = service.StaffIds.Contains(staff.Id);
                if (should && !has) service.StaffIds = [.. service.StaffIds, staff.Id];
                if (!should && has) service.StaffIds = service.StaffIds.Where(x => x != staff.Id).ToList();
            }
        }
        await db.SaveChangesAsync(ct);
        return await GetStaffAsync(staff.Id, ct);
    }

    public async Task DeleteStaffAsync(Guid id, CancellationToken ct)
    {
        var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Staff member");
        var now = core.Now;
        if (await db.Bookings.AnyAsync(b => b.StaffId == id && b.StartsAt > now && Booking.Holding.Contains(b.Status), ct))
            throw new ConflictException("This person has upcoming bookings. Move them to someone else first.");
        if (await db.StaffMembers.CountAsync(ct) == 1) throw new ConflictException("You need at least one staff member.");
        foreach (var service in await db.BookableServices.Where(s => s.StaffIds.Contains(id)).ToListAsync(ct))
            service.StaffIds = service.StaffIds.Where(x => x != id).ToList();
        await db.TimeOffs.Where(t => t.StaffId == id).ExecuteDeleteAsync(ct);
        db.StaffMembers.Remove(staff);
        await db.SaveChangesAsync(ct);
    }

    public async Task<StaffDto> UploadStaffPhotoAsync(Guid id, Upload upload, CancellationToken ct)
    {
        var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Staff member");
        staff.PhotoUrl = await SaveImageAsync(upload, $"{staff.TenantId}/staff/{staff.Id:N}", "staff-photo", ct);
        await db.SaveChangesAsync(ct);
        return await GetStaffAsync(id, ct);
    }

    public async Task<StaffDto> SetScheduleAsync(Guid id, IReadOnlyList<IntervalDto> intervals, CancellationToken ct)
    {
        var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Staff member");
        var parsed = intervals.Select(i =>
        {
            if (i.Weekday is < 0 or > 6) throw BadRequestException.Field("schedule", "Weekdays go from 0 (Sunday) to 6 (Saturday).");
            var (start, end) = (ParseTime(i.Start, "start"), ParseTime(i.End, "end"));
            if (end <= start) throw BadRequestException.Field("schedule", "Each working period must end after it starts.");
            return new ScheduleInterval { Weekday = i.Weekday, Start = start, End = end };
        }).OrderBy(i => i.Weekday).ThenBy(i => i.Start).ToList();
        foreach (var day in parsed.GroupBy(i => i.Weekday))
        {
            var list = day.ToList();
            for (var n = 1; n < list.Count; n++)
                if (list[n].Start < list[n - 1].End)
                    throw BadRequestException.Field("schedule", "Working periods on the same day can't overlap.");
        }
        staff.Schedule = parsed;
        await db.SaveChangesAsync(ct);
        return await GetStaffAsync(id, ct);
    }

    public async Task<IReadOnlyList<TimeOffDto>> ListTimeOffAsync(Guid staffId, CancellationToken ct) =>
        await db.TimeOffs.AsNoTracking().Where(t => t.StaffId == staffId && t.EndsAt > core.Now.AddDays(-30)).OrderBy(t => t.StartsAt)
            .Select(t => new TimeOffDto(t.Id, t.StaffId, t.StartsAt, t.EndsAt, t.Reason)).ToListAsync(ct);

    public async Task<TimeOffDto> AddTimeOffAsync(Guid staffId, SaveTimeOffRequest r, CancellationToken ct)
    {
        if (!await db.StaffMembers.AnyAsync(s => s.Id == staffId, ct)) throw new NotFoundException("Staff member");
        var timeOff = new TimeOff { StaffId = staffId, StartsAt = r.StartsAt.ToUniversalTime(), EndsAt = r.EndsAt.ToUniversalTime(), Reason = Clean(r.Reason) };
        db.TimeOffs.Add(timeOff);
        await db.SaveChangesAsync(ct);
        return new TimeOffDto(timeOff.Id, staffId, timeOff.StartsAt, timeOff.EndsAt, timeOff.Reason);
    }

    public async Task DeleteTimeOffAsync(Guid staffId, Guid id, CancellationToken ct)
    {
        var deleted = await db.TimeOffs.Where(t => t.Id == id && t.StaffId == staffId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Time off");
    }

    // Helpers

    private async Task<string> SaveImageAsync(Upload upload, string prefix, string purpose, CancellationToken ct)
    {
        var (buffer, ext, mime) = await ImageUploads.ReadAsync(upload, ct);
        await using var _ = buffer;
        var key = $"{prefix}-{Guid.CreateVersion7():N}{ext}";
        var url = await storage.SaveAsync(key, buffer, mime, ct);
        db.Files.Add(new StoredFile { Key = key, Url = url, MimeType = mime, SizeBytes = buffer.Length, Purpose = purpose });
        return url;
    }

    private async Task<BookingBusinessDto> ToDtoAsync(BookingBusiness b, CancellationToken ct)
    {
        var country = await db.Tenants.Where(t => t.Id == b.TenantId).Select(t => t.Country).FirstAsync(ct);
        // The same rule BookingCore.DepositsEnabledAsync applies when a customer books, so the app can say why none are taken.
        var inPlan = await plans.HasFeatureAsync(LimitKeys.Deposits, ct);
        var connected = b.PaystackSecretKeyProtected is not null;
        return new BookingBusinessDto(b.Name, b.Slug, b.Category, b.Description, b.LogoUrl, b.CoverUrl, b.Address, b.Phone,
            b.WhatsappNumber, b.Email, b.Timezone, b.Currency, country, b.BrandColor, b.Policies, b.CancellationHours,
            b.SlotIntervalMinutes, b.MinNoticeMinutes, b.MaxDaysAhead, b.BookingEnabled, connected,
            core.BookingPageUrl(b.Slug), connected && inPlan, inPlan);
    }

    private static ServiceDto ToDto(BookableService s) => new(s.Id, s.Name, s.Description, s.DurationMinutes, s.BufferBeforeMinutes,
        s.BufferAfterMinutes, s.PriceMinor, s.DepositType, s.DepositValue, s.IsActive, s.Position, s.StaffIds, s.DepositMinor);

    private StaffDto ToDto(StaffMember s, IReadOnlyList<Guid> serviceIds) => new(s.Id, s.DisplayName, s.Bio, s.Email, s.Color,
        s.IsBookable, s.Position, serviceIds, s.UserId, s.PhotoUrl,
        s.Schedule.OrderBy(i => i.Weekday).ThenBy(i => i.Start)
            .Select(i => new IntervalDto(i.Weekday, i.Start.ToString("HH:mm"), i.End.ToString("HH:mm"))).ToList(),
        user.IsSupportView ? "" : core.FeedUrl(s.FeedToken));

    public static TimeOnly ParseTime(string value, string field) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : throw BadRequestException.Field(field, "Use 24-hour time like 09:30.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
