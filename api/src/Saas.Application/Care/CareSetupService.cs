using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

public sealed record ClinicDto(
    string Name, string Slug, string? Address, string? Phone, string PatientPrefix, bool HasLogo, bool HasPaystackKey, bool DispensesDrugs,
    long MinDepositMinor, long LowDepositThresholdMinor, Guid? RegistrationPriceItemId, Guid? ConsultationPriceItemId);
public sealed record CareChecklistItemDto(string Key, string Label, bool Done);
public sealed record CareSetupDto(ClinicDto Clinic, IReadOnlyList<CareChecklistItemDto> Checklist, CareCaller Caller);
public sealed record SaveClinicProfileRequest(
    string? Name, string? Address, string? Phone, string? PatientPrefix, bool? DispensesDrugs, long? MinDepositMinor, long? LowDepositThresholdMinor,
    Guid? RegistrationPriceItemId = null, Guid? ConsultationPriceItemId = null);

/// <summary>What the clinic app shows first (the clinic, what is left to set up, who you are) and the clinic profile settings.</summary>
public sealed partial class CareSetupService(IAppDbContext db, CareAccess access)
{
    public async Task<CareSetupDto> GetAsync(CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        var profile = await db.ClinicProfiles.AsNoTracking().FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Clinic");

        // Profile is done once the address and phone are in; price list once an item is active; wards once a ward has an active bed.
        // Staff is done once someone besides the medical director has joined.
        var checklist = new List<CareChecklistItemDto>
        {
            new("profile", "Complete the clinic profile", profile.Address is not null && profile.Phone is not null),
            new("price-list", "Set up the price list", await db.PriceItems.AnyAsync(i => i.Active, ct)),
            new("wards", "Add wards and beds", await db.Beds.AnyAsync(b => b.Active, ct)),
            new("staff", "Add staff", await db.Memberships.AnyAsync(m => m.Role != MemberRole.Owner && m.Status == MembershipStatus.Active, ct)),
        };
        return new CareSetupDto(ToDto(profile), checklist, caller);
    }

    public async Task<ClinicDto> SaveProfileAsync(SaveClinicProfileRequest r, CancellationToken ct)
    {
        access.EnsureLeadership(await access.CallerAsync(ct));
        var profile = await db.ClinicProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Clinic");

        var name = (r.Name ?? "").Trim();
        if (name.Length == 0) throw BadRequestException.Field("name", "Enter the clinic's name.");
        if (name.Length > 120) throw BadRequestException.Field("name", "The name can have at most 120 characters.");
        var address = Clean(r.Address);
        if (address is { Length: > 300 }) throw BadRequestException.Field("address", "The address can have at most 300 characters.");
        var phone = Clean(r.Phone);
        if (phone is { Length: > 30 }) throw BadRequestException.Field("phone", "The phone number can have at most 30 characters.");
        var prefix = Clean(r.PatientPrefix)?.ToUpperInvariant() ?? profile.PatientPrefix;
        if (!PrefixPattern().IsMatch(prefix))
            throw BadRequestException.Field("patientPrefix", "Use 1 to 6 letters or digits for the patient number prefix, such as CL.");
        if (r.MinDepositMinor is < 0) throw BadRequestException.Field("minDepositMinor", "The minimum deposit can't be negative.");
        if (r.LowDepositThresholdMinor is < 0) throw BadRequestException.Field("lowDepositThresholdMinor", "The low-deposit threshold can't be negative.");
        // Charged automatically at check-in (Task 5): a newly chosen item must be an active item of this clinic in its own category.
        // An unchanged id isn't re-checked (ruling), so deactivating the configured item doesn't block other profile edits. Null clears it.
        if (r.RegistrationPriceItemId != profile.RegistrationPriceItemId)
            await CheckPriceItemAsync(r.RegistrationPriceItemId, PriceCategory.Registration, "registrationPriceItemId",
                "Choose an active Registration item from the price list.", ct);
        if (r.ConsultationPriceItemId != profile.ConsultationPriceItemId)
            await CheckPriceItemAsync(r.ConsultationPriceItemId, PriceCategory.Consultation, "consultationPriceItemId",
                "Choose an active Consultation item from the price list.", ct);

        profile.Name = name;
        profile.Address = address;
        profile.Phone = phone;
        profile.PatientPrefix = prefix;
        if (r.DispensesDrugs is { } dispenses) profile.DispensesDrugs = dispenses;
        if (r.MinDepositMinor is { } min) profile.MinDepositMinor = min;
        if (r.LowDepositThresholdMinor is { } low) profile.LowDepositThresholdMinor = low;
        profile.RegistrationPriceItemId = r.RegistrationPriceItemId;
        profile.ConsultationPriceItemId = r.ConsultationPriceItemId;
        await db.SaveChangesAsync(ct);
        return ToDto(profile);
    }

    private async Task CheckPriceItemAsync(Guid? id, PriceCategory category, string field, string message, CancellationToken ct)
    {
        if (id is null) return;
        // The tenant filter means another clinic's item is simply not found.
        if (!await db.PriceItems.AnyAsync(i => i.Id == id && i.Category == category && i.Active, ct)) throw BadRequestException.Field(field, message);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex("^[A-Z0-9]{1,6}$")]
    private static partial Regex PrefixPattern();

    private static ClinicDto ToDto(ClinicProfile p) => new(
        p.Name, p.Slug, p.Address, p.Phone, p.PatientPrefix, p.LogoKey is not null, p.PaystackSecretKeyProtected is not null, p.DispensesDrugs,
        p.MinDepositMinor, p.LowDepositThresholdMinor, p.RegistrationPriceItemId, p.ConsultationPriceItemId);
}
