using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Team;

public sealed record MemberDto(Guid Id, Guid UserId, string FullName, string Email, MemberRole Role, bool IsYou, DateTimeOffset JoinedAt);
public sealed record InviteDto(Guid Id, string Email, MemberRole Role, DateTimeOffset InvitedAt, DateTimeOffset ExpiresAt, ClinicalRole? ClinicalRole = null);
public sealed record SeatsDto(int Used, long Limit);
public sealed record TeamDto(IReadOnlyList<MemberDto> Members, IReadOnlyList<InviteDto> Invites, SeatsDto Seats);
/// <summary><paramref name="ClinicalRole"/> is CareLink only: the clinical role the new member's staff profile starts with.</summary>
public sealed record InviteRequest(string Email, MemberRole Role, ClinicalRole? ClinicalRole = null);
public sealed record ChangeRoleRequest(MemberRole Role);

public sealed class InviteRequestValidator : AbstractValidator<InviteRequest>
{
    public InviteRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Role).IsInEnum().NotEqual(MemberRole.Owner).WithMessage("Invite someone as Admin, Staff or Accountant.");
        RuleFor(x => x.ClinicalRole).IsInEnum();
        RuleFor(x => x.ClinicalRole).Must(c => c is null or ClinicalRole.None).When(x => x.Role == MemberRole.Accountant)
            .WithMessage(Care.CareStaff.CashierRefused);
    }
}

public sealed class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator() =>
        RuleFor(x => x.Role).IsInEnum().NotEqual(MemberRole.Owner).WithMessage("Choose Admin, Staff or Accountant.");
}

/// <summary>Members and invitations for the current business. Works for every product.</summary>
public sealed class TeamService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, PlanService plans, NotificationQueue notifications, AppUrls urls,
    TimeProvider clock)
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    public async Task<TeamDto> GetAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var members = await db.Memberships.AsNoTracking().Include(m => m.User)
            .Where(m => m.Status == MembershipStatus.Active).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var invites = await PendingInvites(now).OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        var plan = await plans.GetCurrentPlanAsync(ct);
        return new TeamDto(
            members.Select(m => new MemberDto(m.Id, m.UserId, m.User!.FullName, m.User.Email!, m.Role, m.UserId == user.UserId, m.CreatedAt)).ToList(),
            invites.Select(ToDto).ToList(),
            new SeatsDto(members.Count + invites.Count, plan.Limit(LimitKeys.TeamSeats)));
    }

    public async Task<InviteDto> InviteAsync(InviteRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        var now = clock.GetUtcNow();
        if (await db.Memberships.AnyAsync(m => m.Status == MembershipStatus.Active && m.User!.Email == email, ct))
            throw new ConflictException("This person is already on your team.");
        if (await PendingInvites(now).AnyAsync(i => i.Email == email, ct))
            throw new ConflictException("This person already has an invitation. Resend it instead.");
        var used = await db.Memberships.CountAsync(m => m.Status == MembershipStatus.Active, ct) + await PendingInvites(now).CountAsync(ct);
        await plans.EnsureCanAddAsync(LimitKeys.TeamSeats, used, "Team seats", ct);

        // A clinical role means something only to a CareLink clinic; elsewhere it is dropped.
        var tenantId = tenant.TenantId!.Value;
        var product = await db.Tenants.Where(x => x.Id == tenantId).Select(x => x.Product).FirstAsync(ct);
        var clinicalRole = product == Products.Care
            ? Care.CareStaff.CheckInvite(user.Role ?? MemberRole.Staff, r.Role, r.ClinicalRole)
            : null;

        var invitation = new Invitation { Email = email, Role = r.Role, TokenHash = "", InvitedBy = user.UserId, ClinicalRole = clinicalRole };
        var token = Issue(invitation, now);
        db.Invitations.Add(invitation);
        await QueueEmailAsync(invitation, token, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(invitation);
    }

    public async Task<InviteDto> ResendAsync(Guid id, CancellationToken ct)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id && i.AcceptedAt == null, ct)
            ?? throw new NotFoundException("Invitation");
        var token = Issue(invitation, clock.GetUtcNow());
        await QueueEmailAsync(invitation, token, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(invitation);
    }

    public async Task CancelInviteAsync(Guid id, CancellationToken ct)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id && i.AcceptedAt == null, ct)
            ?? throw new NotFoundException("Invitation");
        db.Invitations.Remove(invitation);
        await db.SaveChangesAsync(ct);
    }

    public async Task<MemberDto> ChangeRoleAsync(Guid membershipId, ChangeRoleRequest r, CancellationToken ct)
    {
        var member = await LoadMemberAsync(membershipId, ct);
        if (member.Role == MemberRole.Owner) throw new ConflictException("The owner's role can't be changed.");
        var previousRole = member.Role;
        member.Role = r.Role;
        await Care.CareStaff.OnRoleChangedAsync(db, member, previousRole, user.Role, user.UserId, ct);
        await db.SaveChangesAsync(ct);
        // The new role reaches their token on its next refresh (within 15 minutes).
        return new MemberDto(member.Id, member.UserId, member.User!.FullName, member.User.Email!, member.Role, member.UserId == user.UserId, member.CreatedAt);
    }

    public async Task RemoveAsync(Guid membershipId, CancellationToken ct)
    {
        var member = await LoadMemberAsync(membershipId, ct);
        if (member.Role == MemberRole.Owner) throw new ConflictException("The owner can't be removed.");
        var tenantId = tenant.TenantId!.Value;
        db.Memberships.Remove(member);
        await db.SaveChangesAsync(ct);
        // End their sessions for this business now; access tokens they hold expire within 15 minutes.
        await db.RefreshTokens.Where(t => t.UserId == member.UserId && t.TenantId == tenantId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);
    }

    public static string HashToken(string token) => Codes.Sha256($"invite:{token}");

    private IQueryable<Invitation> PendingInvites(DateTimeOffset now) =>
        db.Invitations.Where(i => i.AcceptedAt == null && i.ExpiresAt > now);

    private static string Issue(Invitation invitation, DateTimeOffset now)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');
        invitation.TokenHash = HashToken(token);
        invitation.ExpiresAt = now.Add(InviteLifetime);
        return token;
    }

    private async Task QueueEmailAsync(Invitation invitation, string token, CancellationToken ct)
    {
        var tenantId = tenant.TenantId!.Value;
        var t = await db.Tenants.Where(x => x.Id == tenantId).Select(x => new { x.Name, x.Product }).FirstAsync(ct);
        var inviter = await db.Users.Where(u => u.Id == user.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "A teammate";
        notifications.Email(invitation.Email, "team.invite", new()
        {
            ["business"] = t.Name,
            ["inviter"] = inviter,
            ["role"] = invitation.Role.ToString(),
            ["link"] = $"{urls.Web(t.Product)}/invite/{token}",
        }, tenantId, user.UserId);
    }

    private async Task<Membership> LoadMemberAsync(Guid id, CancellationToken ct) =>
        await db.Memberships.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new NotFoundException("Team member");

    private static InviteDto ToDto(Invitation i) => new(i.Id, i.Email, i.Role, i.CreatedAt == default ? DateTimeOffset.UtcNow : i.CreatedAt, i.ExpiresAt, i.ClinicalRole);
}

/// <summary>Adds "Team seats" to a product's billing usage.</summary>
public sealed class ProductTeamUsage(string product, TeamUsage team) : IUsageProvider
{
    public string Product => product;
    public Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct) => team.GetAsync(plan, ct);
}

/// <summary>Team seats used, shared by any product that has teams.</summary>
public sealed class TeamUsage(IAppDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var used = await db.Memberships.CountAsync(m => m.Status == MembershipStatus.Active, ct)
            + await db.Invitations.CountAsync(i => i.AcceptedAt == null && i.ExpiresAt > now, ct);
        return [new UsageItemDto(LimitKeys.TeamSeats, "Team seats", used, plan.Limit(LimitKeys.TeamSeats))];
    }
}
