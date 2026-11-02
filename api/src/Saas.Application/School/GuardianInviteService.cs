using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record GuardianInviteDto(string Link, string? WhatsAppUrl);
public sealed record InviteAllResult(int Invited);
public sealed record GuardianInvitePreview(string SchoolName, string GuardianName, IReadOnlyList<string> Children);

/// <summary>
/// Guardian invites: leadership sends a single-use link (emailed when the guardian has an address, and always offered as a WhatsApp
/// message staff send themselves). A signed-in parent account accepts it, which links the guardian record to the account.
/// </summary>
public sealed class GuardianInviteService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, ParentAccess parents, NotificationQueue notifications, SchoolFeed feed, AppUrls urls, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);
    public const string AlreadyUsed = "This invitation was already used.";
    public const string Expired = "This invitation has expired. Ask the school for a new one.";

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    public static string HashToken(string token) => Codes.Sha256($"school-invite:{token}");

    // ---------- Staff ----------

    public async Task<GuardianInviteDto> InviteAsync(Guid guardianId, CancellationToken ct)
    {
        var guardian = await db.Guardians.FirstOrDefaultAsync(g => g.Id == guardianId, ct) ?? throw new NotFoundException("Guardian");
        var children = (await ChildNamesAsync([guardianId], ct)).GetValueOrDefault(guardianId);
        if (children is not { Count: > 0 })
            throw new ConflictException("This guardian has no current students. Add them to a student first.", "guardian_has_no_students");
        var school = await SchoolNameAsync(ct);
        var result = await CreateAsync(guardian, school, children, ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Emails an invite to every guardian with an email address, no parent account and no unused link. Guardians without email are invited one by one (WhatsApp).</summary>
    public async Task<InviteAllResult> InviteAllAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var guardians = await db.Guardians
            .Where(g => g.UserId == null && g.Email != null
                && !db.GuardianInvites.Any(i => i.GuardianId == g.Id && i.AcceptedAt == null && i.ExpiresAt > now))
            .ToListAsync(ct);
        if (guardians.Count == 0) return new InviteAllResult(0);
        var names = await ChildNamesAsync(guardians.Select(g => g.Id).ToList(), ct);
        var school = await SchoolNameAsync(ct);
        var invited = 0;
        foreach (var guardian in guardians)
        {
            if (names.GetValueOrDefault(guardian.Id) is not { Count: > 0 } children) continue;
            await CreateAsync(guardian, school, children, ct);
            invited++;
        }
        await db.SaveChangesAsync(ct);
        return new InviteAllResult(invited);
    }

    /// <summary>Adds the invite (expiring earlier unused ones) and queues the email. Saved by the caller.</summary>
    private async Task<GuardianInviteDto> CreateAsync(Guardian guardian, string school, IReadOnlyList<string> children, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var earlier in await db.GuardianInvites.Where(i => i.GuardianId == guardian.Id && i.AcceptedAt == null && i.ExpiresAt > now).ToListAsync(ct))
            earlier.ExpiresAt = now;

        var token = StudentImport.NewToken(); // 32 random bytes, URL-safe
        db.GuardianInvites.Add(new GuardianInvite
        {
            TenantId = TenantId, GuardianId = guardian.Id, TokenHash = HashToken(token), ExpiresAt = now.Add(Lifetime), InvitedBy = user.UserId,
        });
        var link = $"{urls.Web(Products.School)}/invite/{token}";
        var child = JoinNames(children);
        if (guardian.Email is { } email)
            notifications.Email(email, "school.guardian_invite",
                new() { ["guardian"] = guardian.FullName, ["school"] = school, ["child"] = child, ["link"] = link }, TenantId);
        return new GuardianInviteDto(link, WhatsAppUrl(guardian.Phone, $"Hello {guardian.FullName}, {school} has set up SchoolLink for {child}. Join here: {link}"));
    }

    public static string? WhatsAppUrl(string? phone, string text)
    {
        var digits = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : $"https://wa.me/{digits}?text={Uri.EscapeDataString(text)}";
    }

    /// <summary>"Ada", "Ada and Tolu", "Ada, Tolu and Chidi".</summary>
    public static string JoinNames(IReadOnlyList<string> names) =>
        names.Count <= 1 ? string.Join("", names) : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    private async Task<string> SchoolNameAsync(CancellationToken ct) =>
        await db.SchoolProfiles.Select(p => p.Name).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");

    /// <summary>Full names of each guardian's active students, by first name. Runs under the current school's tenant filter.</summary>
    private async Task<Dictionary<Guid, List<string>>> ChildNamesAsync(List<Guid> guardianIds, CancellationToken ct)
    {
        var rows = await db.StudentGuardians.Where(l => guardianIds.Contains(l.GuardianId))
            .Join(db.Students.Where(s => s.Status == StudentStatus.Active), l => l.StudentId, s => s.Id,
                (l, s) => new { l.GuardianId, s.FirstName, s.MiddleName, s.LastName })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.GuardianId).ToDictionary(g => g.Key,
            g => g.OrderBy(r => r.FirstName).ThenBy(r => r.LastName).Select(r => ParentAccess.FullName(r.FirstName, r.MiddleName, r.LastName)).ToList());
    }

    // ---------- Public invite page and accept ----------

    /// <summary>The unused, unexpired invite behind a token, in an active school; then the request acts as that school.</summary>
    private async Task<GuardianInvite> OpenAsync(string token, CancellationToken ct)
    {
        var hash = HashToken(token ?? "");
        var invite = await db.GuardianInvites.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(i => i.TokenHash == hash, ct)
            ?? throw new NotFoundException("Invitation");
        if (!await db.Tenants.AnyAsync(t => t.Id == invite.TenantId && t.Status == TenantStatus.Active, ct))
            throw new ForbiddenException(ParentAccess.PortalUnavailable);
        if (invite.AcceptedAt is not null) throw new ConflictException(AlreadyUsed, "invite_used");
        if (invite.ExpiresAt <= clock.GetUtcNow()) throw new ConflictException(Expired, "invite_expired");
        if (tenant.TenantId is { } current && current != invite.TenantId) throw new ForbiddenException("Sign in to your parent account to accept this invitation.");
        if (tenant.TenantId is null) tenant.SetTenant(invite.TenantId);
        return invite;
    }

    public async Task<GuardianInvitePreview> PreviewAsync(string token, CancellationToken ct)
    {
        var invite = await OpenAsync(token, ct);
        var guardian = await db.Guardians.AsNoTracking().FirstOrDefaultAsync(g => g.Id == invite.GuardianId, ct) ?? throw new NotFoundException("Invitation");
        var children = (await ChildNamesAsync([guardian.Id], ct)).GetValueOrDefault(guardian.Id) ?? [];
        return new GuardianInvitePreview(await SchoolNameAsync(ct), guardian.FullName, children);
    }

    /// <summary>Links the guardian to the signed-in parent account. A parent already linked elsewhere just gains these children.</summary>
    public async Task<IReadOnlyList<ChildRef>> AcceptAsync(string token, CancellationToken ct)
    {
        var userId = parents.CustomerUserId();
        var invite = await OpenAsync(token, ct);

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // One accept per guardian at a time, so two parents racing with two links can't both end up "linked".
            await db.LockAsync(BitConverter.ToInt64(invite.GuardianId.ToByteArray(), 8), ct);
            var guardian = await db.Guardians.FirstOrDefaultAsync(g => g.Id == invite.GuardianId, ct) ?? throw new NotFoundException("Invitation");
            if (guardian.UserId is { } linked && linked != userId) throw new ConflictException(AlreadyUsed, "invite_used");

            // The conditional claim makes the link single use even when two accepts arrive together.
            var now = clock.GetUtcNow();
            var claimed = await db.GuardianInvites.Where(i => i.Id == invite.Id && i.AcceptedAt == null && i.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAt, now), ct);
            if (claimed == 0) throw new ConflictException(AlreadyUsed, "invite_used");

            guardian.UserId = userId;
            if (invite.InvitedBy is { } inviter)
            {
                var children = (await ChildNamesAsync([guardian.Id], ct)).GetValueOrDefault(guardian.Id) ?? [];
                feed.Notify(inviter, "school.guardian_joined", $"{guardian.FullName} joined the parent portal",
                    children.Count == 0 ? "They accepted your invitation." : $"They can now follow {string.Join(", ", children)}.", "/app/students");
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await parents.ChildrenAsync(ct);
    }
}
