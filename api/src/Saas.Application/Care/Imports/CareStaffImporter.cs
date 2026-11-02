using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Application.School.Imports;
using Saas.Application.Team;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care.Imports;

/// <summary>
/// Clinic staff, one per row (kind <c>care-staff</c>): <c>email</c>, <c>role</c> and <c>title</c>. Leadership only, as on the staff screen.
/// <list type="bullet">
/// <item>Someone already on the team has their title updated through <see cref="CareStaffService.UpdateAsync"/> (a blank cell keeps it). An
/// import never changes anyone's role or clinical role (a preview note says so), and a non-director can't edit their own profile.</item>
/// <item>Anyone else is invited through <see cref="TeamService.InviteAsync"/> in a clinic role: Admin, Doctor, Nurse, Front desk or Cashier,
/// or "Admin (doctor)" / "Admin (nurse)", which only the medical director may give (<see cref="CareStaff.CheckInvite"/>). The medical
/// director can't be imported. Invites must fit the plan's team seats, counted as the team screen counts them; the first that doesn't fit
/// and every invite after it are errors with the team screen's sentence. Invitation emails wait in the outbox for the import's commit.</item>
/// </list>
/// </summary>
public sealed class CareStaffImporter(IAppDbContext db, CareAccess access, CareStaffService staff, TeamService team, PlanService plans, TimeProvider clock)
    : IImporter, IImportWarnings, IImportNotice, IImportDeclaredColumnsOnly
{
    public const string KindName = "care-staff";
    public const string BadEmail = "Enter a valid email address.";
    public const string BadRole = "Enter the role: Admin, Doctor, Nurse, Front desk or Cashier.";
    public const string DirectorRefused = "The medical director can't be added from a file. Use Admin, Doctor, Nurse, Front desk or Cashier.";
    public const string EmailTwice = "This email appears more than once in the file.";
    public const string AlreadyInvited = "This person already has an invitation. Resend it instead.";
    public const string TitleOnInvite = "Titles are saved only for people already on the team. Set them on the staff page after they accept the invitation.";
    public const string RoleNotChanged = "The roles of people already on the team are not changed by an import. Change a role on the staff page.";
    public const string TitleTooLong = "The title can have at most 80 characters.";

    public string Kind => KindName;
    public string Product => Billing.Products.Care;
    public string SheetName => "Staff";
    public string NoticeTitle => "Staff import finished";
    public string NoticeLink => "/app/staff";

    private static readonly ImportColumns AllColumns = new(
    [
        new("email", "email", true, "The person's email. They sign in with it."),
        new("role", "role", true, "Admin, Doctor, Nurse, Front desk or Cashier. Needed to invite someone new. Only the medical director can invite \"Admin (doctor)\" or \"Admin (nurse)\"."),
        new("title", "title", false, "Optional, for example Consultant physician. Up to 80 characters. Saved for people already on the team."),
    ]);

    private static readonly string[] Notes =
    [
        "One person per row on the Staff sheet. Delete nothing from the heading row.",
        "Someone already on the team: their title is updated. A blank cell keeps what is saved. Their role is not changed.",
        "Anyone else is sent an invitation by email once the import is confirmed, as Admin, Doctor, Nurse, Front desk or Cashier.",
        "Only the medical director can invite an admin with a clinical role: Admin (doctor) or Admin (nurse). The medical director can't be imported.",
        "New invitations must fit the team seats on your plan, as on the team page. Pending invitations take a seat.",
        "Each email can appear once in the file.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    private enum Action { None, Update, Invite }

    private sealed record Plan(Action Action, string Email, MemberRole? Role, ClinicalRole? ClinicalRole, Guid? UserId, SaveCareStaffRequest? Profile,
        bool TitleIgnored, bool RoleIgnored);

    private sealed record Checked(int Row, string Label, string? Error, Plan? Plan);

    private static readonly InviteRequestValidator EmailRule = new();

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct) => access.EnsureLeadership(await access.CallerAsync(ct));

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(SheetName, AllColumns.All, [], Notes));

    public IReadOnlyList<string> DroppedColumnWarnings(IReadOnlyList<string> dropped) => CareImports.Warnings(dropped);

    /// <summary>A role cell as (member role, clinical role), "director" for the medical director, or null when it is no role.</summary>
    private static (MemberRole Role, ClinicalRole? Clinical)? ParseRole(string cell, out bool director)
    {
        var words = new string(cell.ToLowerInvariant().Select(c => char.IsAsciiLetter(c) ? c : ' ').ToArray());
        var key = string.Join(' ', words.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        director = key is "medical director" or "director" or "owner";
        return key switch
        {
            "admin" => (MemberRole.Admin, ClinicalRole.None),
            "admin doctor" => (MemberRole.Admin, ClinicalRole.Doctor),
            "admin nurse" => (MemberRole.Admin, ClinicalRole.Nurse),
            "doctor" => (MemberRole.Staff, ClinicalRole.Doctor),
            "nurse" => (MemberRole.Staff, ClinicalRole.Nurse),
            "front desk" or "frontdesk" or "receptionist" => (MemberRole.Staff, ClinicalRole.FrontDesk),
            "cashier" => (MemberRole.Accountant, null),
            _ => null,
        };
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        var now = clock.GetUtcNow();
        var members = await db.Memberships.AsNoTracking().Where(m => m.Status == MembershipStatus.Active)
            .Select(m => new
            {
                m.Id, m.UserId, m.User!.Email, m.Role,
                Profile = db.CareStaffProfiles.Where(p => p.MembershipId == m.Id).Select(p => new { p.ClinicalRole, p.Title, p.LicenceNumber }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var byEmail = members.Where(m => m.Email != null).GroupBy(m => m.Email!.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var pending = (await db.Invitations.AsNoTracking().Where(i => i.AcceptedAt == null && i.ExpiresAt > now).Select(i => i.Email).ToListAsync(ct))
            .Select(e => e.ToLowerInvariant()).ToHashSet();

        var checkedRows = rows.Select(row =>
        {
            var email = (row.Cells.GetValueOrDefault("email") ?? "").Trim().ToLowerInvariant();
            var roleCell = (row.Cells.GetValueOrDefault("role") ?? "").Trim();
            var title = string.Join(' ', (row.Cells.GetValueOrDefault("title") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            Checked Error(string message) => new(row.Row, email, message, null);

            if (email.Length == 0 || !EmailRule.Validate(new InviteRequest(email, MemberRole.Staff)).IsValid) return Error(BadEmail);
            var role = ParseRole(roleCell, out var director);
            if (director) return Error(DirectorRefused);
            if (role is null && roleCell.Length > 0) return Error(BadRole);
            if (title.Length > 80) return Error(TitleTooLong);

            if (byEmail.TryGetValue(email, out var member))
            {
                var clinical = member.Profile?.ClinicalRole ?? CareStaff.DefaultClinicalRole(member.Role, null);
                var roleIgnored = role is { } r && (r.Role != member.Role || (r.Clinical ?? ClinicalRole.None) != clinical);
                var newTitle = title.Length > 0 ? title : member.Profile?.Title;
                var same = newTitle == member.Profile?.Title;
                if (same) return new Checked(row.Row, email, null, new Plan(Action.None, email, null, null, member.UserId, null, false, roleIgnored));
                // The staff screen's rule: nobody but the medical director edits their own clinic profile.
                if (member.UserId == caller.UserId && caller.MemberRole != MemberRole.Owner) return Error(CareStaff.SelfRoleRefused);
                return new Checked(row.Row, email, null, new Plan(Action.Update, email, null, null, member.UserId,
                    new SaveCareStaffRequest(clinical, newTitle, member.Profile?.LicenceNumber), false, roleIgnored));
            }

            if (role is not { } invite) return Error(BadRole);
            if (invite.Role == MemberRole.Admin && invite.Clinical is not (null or ClinicalRole.None) && caller.MemberRole != MemberRole.Owner)
                return Error(CareStaff.AdminRoleRefused);
            if (pending.Contains(email)) return Error(AlreadyInvited);
            return new Checked(row.Row, email, null, new Plan(Action.Invite, email, invite.Role, invite.Clinical, null, null, title.Length > 0, false));
        }).ToList();

        var twice = checkedRows.Where(r => r.Label.Length > 0).GroupBy(r => r.Label).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        // Seats, as the team screen counts them (active members plus pending invitations), in file order. Once one invite doesn't fit, none after it does.
        var used = await db.Memberships.CountAsync(m => m.Status == MembershipStatus.Active, ct)
            + await db.Invitations.CountAsync(i => i.AcceptedAt == null && i.ExpiresAt > now, ct);
        string? full = null;
        var results = new List<ImportRowResult>(checkedRows.Count);
        foreach (var r in checkedRows)
        {
            var error = r.Error;
            if (error is null && twice.Contains(r.Label)) error = EmailTwice;
            if (error is null && r.Plan is { Action: Action.Invite })
            {
                if (full is null)
                {
                    try
                    {
                        await plans.EnsureCanAddAsync(LimitKeys.TeamSeats, used, "Team seats", ct);
                        used++;
                    }
                    catch (PlanLimitException ex)
                    {
                        full = ex.Message;
                    }
                }
                error = full;
            }
            results.Add(error is not null
                ? new ImportRowResult(r.Row, ImportRowStatus.Error, r.Label, error, null)
                : new ImportRowResult(r.Row, r.Plan!.Action switch
                {
                    Action.Invite => ImportRowStatus.New,
                    Action.Update => ImportRowStatus.Update,
                    _ => ImportRowStatus.Unchanged,
                }, r.Label, null, r.Plan));
        }
        return results;
    }

    public Task<IReadOnlyList<string>> WarningsAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var all = rows.Select(r => r.Plan).OfType<Plan>().ToList();
        var warnings = new List<string>();
        if (all.Any(p => p.TitleIgnored)) warnings.Add(TitleOnInvite);
        if (all.Any(p => p.RoleIgnored)) warnings.Add(RoleNotChanged);
        return Task.FromResult<IReadOnlyList<string>>(warnings);
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            if (row.Status == ImportRowStatus.Error || row.Plan is not Plan plan) continue;
            switch (plan.Action)
            {
                case Action.Update:
                    await staff.UpdateAsync(plan.UserId!.Value, plan.Profile!, ct);
                    updated++;
                    break;
                case Action.Invite:
                    // CheckInvite runs again inside, and the email is queued in the outbox with this transaction.
                    await team.InviteAsync(new InviteRequest(plan.Email, plan.Role!.Value, plan.ClinicalRole), ct);
                    created++;
                    break;
            }
        }
        return new ImportApplied(created, updated);
    }
}
