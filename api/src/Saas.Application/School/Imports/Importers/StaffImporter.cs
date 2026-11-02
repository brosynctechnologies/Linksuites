using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Team;
using Saas.Domain.Core;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Staff, one per row (kind <c>staff</c>). Leadership only. Someone already on the team has their profile (job title, staff number, phone)
/// updated through <see cref="StaffService.UpdateAsync"/>; anyone else is invited through <see cref="TeamService.InviteAsync"/> as Principal,
/// Bursar or Teacher. The proprietor can't be imported. Invites must fit the plan's team seats, counted as the team screen counts them (active
/// members plus pending invitations); a row that would go over is an error with the team screen's sentence. Invitation emails go to the
/// outbox inside the import's transaction, so none is sent unless the import commits.
/// </summary>
public sealed class StaffImporter(
    IAppDbContext db, SchoolAccess access, StaffService staffService, TeamService team, PlanService plans, TimeProvider clock)
    : IImporter, IImportWarnings, IImportNotice
{
    public const string KindName = "staff";
    public const string BadEmail = "Enter a valid email address.";
    public const string BadRole = "Enter the role: Principal, Bursar or Teacher.";
    public const string ProprietorRefused = "The proprietor can't be added from a file. Use Principal, Bursar or Teacher.";
    public const string EmailTwice = "This email appears more than once in the file.";
    public const string StaffNoTwice = "This staff number appears more than once in the file.";
    public const string AlreadyInvited = "This person already has an invitation. Resend it instead.";
    public const string ProfileOnInvite =
        "Job title, staff number and phone are saved only for people already on the team. Set them on the staff page after they accept the invitation.";
    public const string RoleNotChanged =
        "The roles of people already on the team are not changed by an import. Change a role on the staff page.";

    public string Kind => KindName;
    public string SheetName => "Staff";
    public string NoticeTitle => "Staff import finished";
    public string NoticeLink => "/app/staff";

    private static readonly ImportColumns AllColumns = new(
    [
        new("email", "email", true, "The person's email. They sign in with it."),
        new("role", "role", true, "Principal, Bursar or Teacher. Needed to invite someone new; the proprietor can't be imported."),
        new("job_title", "job_title", false, "Optional. What the school calls them, for example Class teacher. Up to 80 characters."),
        new("staff_no", "staff_no", false, "Optional. The school's staff number, up to 30 characters. Each staff number belongs to one person."),
        new("phone", "phone", false, "Optional. Up to 30 characters."),
    ]);

    private static readonly string[] Notes =
    [
        "One person per row on the Staff sheet. Delete nothing from the heading row.",
        "Someone already on the team: their job title, staff number and phone are updated. A blank cell keeps what is saved. Their role is not changed.",
        "Anyone else is sent an invitation by email as Principal, Bursar or Teacher once the import is confirmed. The proprietor can't be imported.",
        "New invitations must fit the staff seats on your plan, as on the staff page. Pending invitations take a seat.",
        "Job title, staff number and phone of someone invited are set on the staff page after they accept.",
        "Each email can appear once in the file.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    private enum Action { None, Update, Invite }

    private sealed record Plan(Action Action, string Email, MemberRole? Role, Guid? MembershipId, SaveStaffRequest? Profile, bool ProfileIgnored, bool RoleIgnored);

    private sealed record Checked(ImportRowData Row, string Label, string? Error, Plan? Plan, string? StaffNo);

    private static readonly InviteRequestValidator EmailRule = new();

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct) => SetupImport.EnsureLeadership(access, "staff");

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(SheetName, AllColumns.All, [], Notes));

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // Every active member, with or without a staff profile: a member without one is still on the team (the staff screen makes the profile
        // with the role's default job title the first time it is opened, and so does the update below).
        var members = (await db.Memberships.AsNoTracking().Where(m => m.Status == MembershipStatus.Active)
            .Select(m => new
            {
                MembershipId = m.Id, m.User!.Email, m.Role,
                Profile = db.StaffProfiles.Where(p => p.MembershipId == m.Id).Select(p => new { p.Id, p.JobTitle, p.StaffNo, p.Phone }).FirstOrDefault(),
            })
            .ToListAsync(ct))
            .Select(m => new
            {
                m.MembershipId, m.Email, m.Role, ProfileId = m.Profile?.Id,
                JobTitle = m.Profile?.JobTitle ?? StaffService.DefaultJobTitle(m.Role), StaffNo = m.Profile?.StaffNo, Phone = m.Profile?.Phone,
            })
            .ToList();
        // Staff numbers are unique across every profile of the school, disabled members included, as on the staff screen and the unique index.
        var staffNumbers = await db.StaffProfiles.AsNoTracking().Where(p => p.StaffNo != null).Select(p => new { p.Id, p.StaffNo }).ToListAsync(ct);
        var byEmail = members.Where(m => m.Email != null).GroupBy(m => m.Email!.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var pending = (await db.Invitations.AsNoTracking().Where(i => i.AcceptedAt == null && i.ExpiresAt > now).Select(i => i.Email).ToListAsync(ct))
            .Select(e => e.ToLowerInvariant()).ToHashSet();

        var checkedRows = rows.Select(row =>
        {
            var cells = row.Cells;
            var email = (cells.GetValueOrDefault("email") ?? "").Trim().ToLowerInvariant();
            var label = email.Length > 0 ? email : "";
            var roleText = (cells.GetValueOrDefault("role") ?? "").Trim();
            var jobCell = cells.GetValueOrDefault("job_title") ?? "";
            var staffNoCell = cells.GetValueOrDefault("staff_no") ?? "";
            var phoneCell = cells.GetValueOrDefault("phone") ?? "";

            if (email.Length == 0 || !EmailRule.Validate(new InviteRequest(email, MemberRole.Staff)).IsValid)
                return new Checked(row, label, BadEmail, null, null);

            var lowerRole = roleText.ToLowerInvariant();
            if (lowerRole is "proprietor" or "proprietress" or "owner") return new Checked(row, label, ProprietorRefused, null, null);
            MemberRole? role = lowerRole switch
            {
                "principal" => MemberRole.Admin,
                "bursar" => MemberRole.Accountant,
                "teacher" => MemberRole.Staff,
                _ => null,
            };
            if (role is null && lowerRole.Length > 0) return new Checked(row, label, BadRole, null, null);

            if (byEmail.TryGetValue(email, out var member))
            {
                // A blank cell keeps what is saved.
                var request = new SaveStaffRequest(
                    jobCell.Length > 0 ? jobCell : member.JobTitle,
                    staffNoCell.Length > 0 ? staffNoCell : member.StaffNo,
                    phoneCell.Length > 0 ? phoneCell : member.Phone);
                (string JobTitle, string? StaffNo, string? Phone) clean;
                try
                {
                    clean = StaffService.CleanProfile(request);
                }
                catch (BadRequestException ex)
                {
                    return new Checked(row, label, ex.Message, null, null);
                }
                if (clean.StaffNo is { } no && staffNumbers.Any(p => p.Id != member.ProfileId && p.StaffNo!.Equals(no, StringComparison.OrdinalIgnoreCase)))
                    return new Checked(row, label, StaffService.StaffNoTaken, null, null);
                // A member without a profile always updates: saving makes their profile.
                var same = member.ProfileId is not null && clean.JobTitle == member.JobTitle && clean.StaffNo == member.StaffNo && clean.Phone == member.Phone;
                var roleIgnored = role is { } r && r != member.Role;
                return new Checked(row, label, null,
                    new Plan(same ? Action.None : Action.Update, email, null, member.MembershipId, new SaveStaffRequest(clean.JobTitle, clean.StaffNo, clean.Phone), false, roleIgnored),
                    staffNoCell.Length > 0 ? clean.StaffNo : null);
            }

            if (role is null) return new Checked(row, label, BadRole, null, null);
            if (pending.Contains(email)) return new Checked(row, label, AlreadyInvited, null, null);
            var profileIgnored = jobCell.Length > 0 || staffNoCell.Length > 0 || phoneCell.Length > 0;
            return new Checked(row, label, null, new Plan(Action.Invite, email, role, null, null, profileIgnored, false), null);
        }).ToList();

        var emailsTwice = checkedRows.Where(r => r.Label.Length > 0).GroupBy(r => r.Label).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var numbersTwice = checkedRows.Where(r => r.StaffNo is not null).GroupBy(r => r.StaffNo!.ToLowerInvariant())
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        // Seats: counted as the team screen counts them (active members plus pending invitations), in file order, with the team screen's
        // rule and sentence. Once one invite doesn't fit, none after it does.
        var used = await db.Memberships.CountAsync(m => m.Status == MembershipStatus.Active, ct)
            + await db.Invitations.CountAsync(i => i.AcceptedAt == null && i.ExpiresAt > now, ct);
        string? full = null;

        var results = new List<ImportRowResult>(checkedRows.Count);
        foreach (var r in checkedRows)
        {
            var error = r.Error;
            if (error is null && r.Label.Length > 0 && emailsTwice.Contains(r.Label)) error = EmailTwice;
            if (error is null && r.StaffNo is not null && numbersTwice.Contains(r.StaffNo.ToLowerInvariant())) error = StaffNoTwice;
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
                ? new ImportRowResult(r.Row.Row, ImportRowStatus.Error, r.Label, error, null)
                : new ImportRowResult(r.Row.Row, r.Plan!.Action switch
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
        var plans = rows.Select(r => r.Plan).OfType<Plan>().ToList();
        var warnings = new List<string>();
        if (plans.Any(p => p.ProfileIgnored)) warnings.Add(ProfileOnInvite);
        if (plans.Any(p => p.RoleIgnored)) warnings.Add(RoleNotChanged);
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
                    await staffService.UpdateAsync(plan.MembershipId!.Value, plan.Profile!, ct);
                    updated++;
                    break;
                case Action.Invite:
                    // The email is queued in the outbox with this transaction; the dispatcher sends it only after the import commits.
                    await team.InviteAsync(new InviteRequest(plan.Email, plan.Role!.Value), ct);
                    created++;
                    break;
            }
        }
        return new ImportApplied(created, updated);
    }
}
