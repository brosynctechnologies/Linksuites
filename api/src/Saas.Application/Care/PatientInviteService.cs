using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>Who to send the invite to. Both are optional and default to the patient's own email and phone.</summary>
public sealed record PatientInviteRequest(string? Email, string? Phone);

/// <summary>The invite link, and a WhatsApp link (when there is a phone number) for staff to send it themselves.</summary>
public sealed record PatientInviteDto(string Link, string? WhatsAppUrl, DateTimeOffset ExpiresAt);

/// <summary><c>Relationship</c> is "self", "parent" or "guardian".</summary>
public sealed record AcceptPatientInviteRequest(string? Relationship);

/// <summary>The public invite page: the clinic and the patient's first name only.</summary>
public sealed record PatientInvitePreview(string ClinicName, string PatientFirstName, DateTimeOffset ExpiresAt);

/// <summary>A pending invite as front desk and leadership see it: the masked address it went to, who sent it and when it expires. Never the token.</summary>
public sealed record PendingInviteDto(Guid Id, string? Email, string? Phone, string? InvitedByName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>A portal account linked to a patient, as front desk and leadership see it.</summary>
public sealed record PortalLinkDto(Guid Id, string Name, string? Email, PatientLinkRelationship Relationship, bool Active, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

/// <summary>
/// Patient portal invites (Task 15), the way SchoolLink invites guardians (<c>GuardianInviteService</c>): front desk or leadership sends a
/// single-use link valid for 14 days, stored only as its hash; it is emailed when there is an address and always offered as a WhatsApp
/// message staff send themselves. The messages carry the clinic, the patient's first name and the link, never anything clinical. A signed-in
/// customer account (emailed code or password) accepts it, which links the account to the patient as self, parent or guardian.
/// </summary>
public sealed class PatientInviteService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, CareAccess access, PatientPortalAccess portal, CarePortalGate gate,
    CareNotices notices, AppUrls urls, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);
    public const string AlreadyUsed = "This invitation was already used.";
    public const string Expired = "This invitation has expired. Ask the clinic for a new one.";
    public const string ChooseRelationship = "Choose self, parent or guardian.";
    public const string StaffRefused = "Staff accounts can't be linked to patients. Use a personal account.";
    public const string OtherEmail = "This invitation was sent to another email address.";
    public const string LinkOnlyLeadership =
        "Only the medical director or an admin can create an invitation without an email. Add the patient's email to send one.";
    public const string DeskEmailOnFile = "Front desk can send the invitation only to the email on the patient's file.";
    public const string EmailRecentlyChanged =
        "You changed this patient's email in the last 24 hours, so you can't invite them. Ask the medical director or an admin to send the invitation.";

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    public static string HashToken(string token) => Codes.Sha256($"care-invite:{token}");

    /// <summary>The per-patient lock every accept takes (a transaction-scoped advisory lock).</summary>
    public static long AcceptLockKey(Guid patientId) => BitConverter.ToInt64(patientId.ToByteArray(), 8) ^ 0x0CA4E1E5;

    /// <summary>
    /// Front desk (clinical role FrontDesk) and leadership send portal invites and manage portal links, as they register and edit patients.
    /// Doctors, nurses and the cashier don't; support's read-only view never writes.
    /// </summary>
    public static void EnsureInviter(CareCaller c)
    {
        if (c.IsSupportView || !(c.IsLeadership || (c.MemberRole == MemberRole.Staff && c.ClinicalRole == ClinicalRole.FrontDesk)))
            throw new ForbiddenException();
    }

    // ---------- Staff ----------

    public async Task<PatientInviteDto> InviteAsync(Guid patientId, PatientInviteRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        EnsureInviter(caller);
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct) ?? throw new NotFoundException("Patient");
        if (patient.Status == PatientStatus.Deceased) throw new ConflictException(AppointmentService.PatientDeceased, "patient_deceased");

        var email = StudentService.NormaliseEmail(r.Email) ?? patient.Email;
        if (email is not null && (email.Length > 200 || !email.Contains('@'))) throw BadRequestException.Field("email", "Enter a valid email address.");
        if (!StudentService.TryNormalisePhone(r.Phone, out var phone)) throw BadRequestException.Field("phone", "Enter a valid phone number.");
        phone ??= patient.Phone;

        var now = clock.GetUtcNow();
        if (!caller.IsLeadership)
        {
            // Final review C1 (ruling): a link-only invite (no email to match at accept) lets whoever holds the link reach the record, so
            // only leadership makes one. Front desk sends emailed invites, to the email on the patient's file and no other address.
            if (email is null) throw new ForbiddenException(LinkOnlyLeadership);
            if (!string.Equals(email, patient.Email?.Trim(), StringComparison.OrdinalIgnoreCase)) throw new ForbiddenException(DeskEmailOnFile);
            email = patient.Email!.Trim();
            // A clerk who put an address on the file (their own, say) and then invites it: refused for 24 hours after their change.
            if (await ChangedEmailRecentlyAsync(patientId, caller.UserId, now, ct))
                throw new ConflictException(EmailRecentlyChanged, "invite_email_recently_changed");
        }
        var token = StudentImport.NewToken(); // 32 random bytes, URL-safe; only its hash is stored
        var invite = new PatientInvite
        {
            TenantId = TenantId, PatientId = patientId, TokenHash = HashToken(token), Email = email, Phone = phone, ExpiresAt = now.Add(Lifetime),
            InvitedBy = caller.UserId,
        };
        db.PatientInvites.Add(invite);
        var link = $"{urls.Web(Products.Care)}/portal/invite/{token}";
        var clinic = await db.ClinicProfiles.AsNoTracking().Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "the clinic";
        if (email is not null) await notices.PatientInviteAsync(email, patient.FirstName, link, ct);
        db.AuditLogs.Add(Audit("patient.portal_invite", patientId, new { inviteId = invite.Id }, now));
        await db.SaveChangesAsync(ct);
        // The WhatsApp message: the clinic, the first name and the link. Never anything clinical.
        var whatsApp = GuardianInviteService.WhatsAppUrl(phone, $"Hello, {CareNotices.PatientInviteText(clinic, patient.FirstName)} Join here: {link}");
        return new PatientInviteDto(link, whatsApp, invite.ExpiresAt);
    }

    /// <summary>The portal accounts linked to the patient (active and revoked), newest first.</summary>
    public async Task<IReadOnlyList<PortalLinkDto>> LinksAsync(Guid patientId, CancellationToken ct)
    {
        EnsureInviter(await access.CallerAsync(ct));
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient");
        return await (from l in db.PatientLinks.AsNoTracking()
                      where l.PatientId == patientId
                      join u in db.Users.AsNoTracking() on l.UserId equals u.Id
                      orderby l.CreatedAt descending
                      select new PortalLinkDto(l.Id, u.FullName, u.Email, l.Relationship, l.Active, l.CreatedAt, l.RevokedAt)).ToListAsync(ct);
    }

    /// <summary>
    /// Revokes a portal link: the account no longer reaches the patient and gets no more notices. Ruling (fix round 1, I2): it also expires
    /// every pending invite for the patient, so a revoked parent can't come back through another unused link. Audited with ids and a count.
    /// </summary>
    public async Task RevokeAsync(Guid patientId, Guid linkId, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        EnsureInviter(caller);
        var link = await db.PatientLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.PatientId == patientId, ct) ?? throw new NotFoundException("Portal link");
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Under the accept lock, so an accept in flight either finished before the revoke or sees its invite expired.
        await db.LockAsync(AcceptLockKey(patientId), ct);
        var expired = await ExpirePendingAsync(patientId, now, ct);
        if (link.Active)
        {
            link.Active = false;
            link.RevokedAt = now;
            link.RevokedByUserId = caller.UserId;
        }
        db.AuditLogs.Add(Audit("patient.portal_link_revoked", patientId, new { linkId, userId = link.UserId, invitesExpired = expired }, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>The patient's pending invites (unused, unexpired), newest first: who they went to (masked) and when they expire. Never the token.</summary>
    public async Task<IReadOnlyList<PendingInviteDto>> PendingAsync(Guid patientId, CancellationToken ct)
    {
        EnsureInviter(await access.CallerAsync(ct));
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient");
        var now = clock.GetUtcNow();
        var rows = await (from i in db.PatientInvites.AsNoTracking()
                          where i.PatientId == patientId && i.AcceptedAt == null && i.ExpiresAt > now
                          orderby i.CreatedAt descending
                          select new { i.Id, i.Email, i.Phone, i.CreatedAt, i.ExpiresAt, InvitedBy = db.Users.Where(u => u.Id == i.InvitedBy).Select(u => u.FullName).FirstOrDefault() })
            .ToListAsync(ct);
        return rows.Select(i => new PendingInviteDto(i.Id, MaskEmail(i.Email), MaskPhone(i.Phone), i.InvitedBy, i.CreatedAt, i.ExpiresAt)).ToList();
    }

    /// <summary>Withdraws one pending invite (a mistyped address, a link sent to the wrong number): it expires now. Audited with ids only.</summary>
    public async Task CancelInviteAsync(Guid patientId, Guid inviteId, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        EnsureInviter(caller);
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(AcceptLockKey(patientId), ct);
        var invite = await db.PatientInvites.FirstOrDefaultAsync(i => i.Id == inviteId && i.PatientId == patientId, ct) ?? throw new NotFoundException("Invitation");
        if (invite.AcceptedAt is null && invite.ExpiresAt > now)
        {
            invite.ExpiresAt = now;
            db.AuditLogs.Add(Audit("patient.portal_invite_cancelled", patientId, new { inviteId }, now));
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// True when <paramref name="userId"/> changed the patient's email in the 24 hours before <paramref name="now"/>, read from the
    /// explicit <c>patient.updated</c> audit rows (they list the changed field names, never values).
    /// </summary>
    private async Task<bool> ChangedEmailRecentlyAsync(Guid patientId, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddHours(-24);
        var entity = patientId.ToString();
        // The column is jsonb: read the few recent rows and look at the changed-field list here.
        var changes = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == TenantId && a.Action == "patient.updated" && a.EntityId == entity && a.ActorId == userId && a.CreatedAt >= since)
            .Select(a => a.AfterJson).ToListAsync(ct);
        return changes.Any(json => json is not null && JsonDocument.Parse(json).RootElement.TryGetProperty("changed", out var changed)
            && changed.ValueKind == JsonValueKind.Array && changed.EnumerateArray().Any(f => f.GetString() == "email"));
    }

    private Task<int> ExpirePendingAsync(Guid patientId, DateTimeOffset now, CancellationToken ct) =>
        db.PatientInvites.Where(i => i.PatientId == patientId && i.AcceptedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, now), ct);

    /// <summary>"a•••@example.com": enough for the desk to recognise the address, not to read it off the screen.</summary>
    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return null;
        var at = email.IndexOf('@');
        return at <= 0 ? "•••" : $"{email[0]}•••{email[at..]}";
    }

    /// <summary>"•••4567".</summary>
    public static string? MaskPhone(string? phone) =>
        string.IsNullOrEmpty(phone) ? null : $"•••{phone[Math.Max(0, phone.Length - 4)..]}";

    // ---------- Public invite page and accept ----------

    /// <summary>
    /// The unused, unexpired invite behind a token, in an open clinic; then the request acts as that clinic. 404 for an unknown token, 403
    /// "This clinic's portal is unavailable." for a suspended clinic, 409 for a used or expired invite.
    /// </summary>
    private async Task<PatientInvite> OpenAsync(string token, CancellationToken ct)
    {
        var hash = HashToken(token ?? "");
        var invite = await db.PatientInvites.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(i => i.TokenHash == hash, ct)
            ?? throw new NotFoundException("Invitation");
        await gate.EnsureOpenAsync(invite.TenantId, ct);
        if (invite.AcceptedAt is not null) throw new ConflictException(AlreadyUsed, "invite_used");
        if (invite.ExpiresAt <= clock.GetUtcNow()) throw new ConflictException(Expired, "invite_expired");
        if (tenant.TenantId is { } current && current != invite.TenantId) throw new ForbiddenException(PatientPortalAccess.CustomerOnly);
        if (tenant.TenantId is null) tenant.SetTenant(invite.TenantId);
        return invite;
    }

    public async Task<PatientInvitePreview> PreviewAsync(string token, CancellationToken ct)
    {
        var invite = await OpenAsync(token, ct);
        var first = await db.Patients.AsNoTracking().Where(p => p.Id == invite.PatientId).Select(p => p.FirstName).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Invitation");
        var clinic = await db.ClinicProfiles.AsNoTracking().Select(c => c.Name).FirstAsync(ct);
        return new PatientInvitePreview(clinic, first, invite.ExpiresAt);
    }

    /// <summary>
    /// Links the signed-in customer account to the invite's patient with the relationship given, and returns the account's patients. The
    /// conditional claim makes the invite single use even when two accepts arrive together. An account already linked (or revoked earlier)
    /// is linked again with the new relationship.
    /// </summary>
    public async Task<IReadOnlyList<PortalPatientRef>> AcceptAsync(string token, AcceptPatientInviteRequest r, CancellationToken ct)
    {
        var userId = portal.CustomerUserId();
        // R1 (fix round 2): a session issued before the account's email was confirmed belongs to whoever set the account up first.
        await portal.EnsureFreshSessionAsync(userId, ct);
        var relationship = ParseRelationship(r.Relationship);
        var invite = await OpenAsync(token, ct);

        // Ruling (fix round 1, I1): the clinic's own staff can't become portal users of its patients with any login, since the portal is
        // not logged as staff access. A member in any role or state is refused; a staff member who is also a patient uses a personal account.
        if (await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.TenantId == invite.TenantId && m.UserId == userId, ct))
            throw new ForbiddenException(StaffRefused);
        // An emailed invite belongs to that address: only an account whose email is confirmed (the emailed code) and matches may accept it.
        // A link-only invite (WhatsApp, no email) accepts from any non-member account.
        if (invite.Email is { } invited)
        {
            var account = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Email, u.EmailConfirmed }).FirstOrDefaultAsync(ct);
            if (account is null || !account.EmailConfirmed || !string.Equals(account.Email?.Trim(), invited.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ConflictException(OtherEmail, "invite_other_email");
        }

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // One accept per patient at a time, so two accounts racing with two links each end with one row per account.
            await db.LockAsync(AcceptLockKey(invite.PatientId), ct);
            // Then the patient's admission lock (a Died discharge marks the death under it; the order is accept lock then patient lock, and
            // the discharge takes only the patient lock, so no cycle). An invite sent while the patient was alive is not accepted after the
            // death, and isn't used up.
            await db.LockAsync(AdmissionService.PatientLockKey(invite.TenantId, invite.PatientId), ct);
            var patient = await db.Patients.AsNoTracking().Where(p => p.Id == invite.PatientId).Select(p => new { p.Status, p.Number, p.Email }).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Invitation");
            if (patient.Status == PatientStatus.Deceased) throw new ConflictException(AppointmentService.PatientDeceased, "patient_deceased");
            var now = clock.GetUtcNow();
            var claimed = await db.PatientInvites.Where(i => i.Id == invite.Id && i.AcceptedAt == null && i.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAt, now).SetProperty(i => i.AcceptedByUserId, userId), ct);
            if (claimed == 0) throw new ConflictException(AlreadyUsed, "invite_used");

            var link = await db.PatientLinks.FirstOrDefaultAsync(l => l.PatientId == invite.PatientId && l.UserId == userId, ct);
            var linkedNow = link is null || !link.Active;
            if (link is null)
            {
                link = new PatientLink { TenantId = invite.TenantId, PatientId = invite.PatientId, UserId = userId };
                db.PatientLinks.Add(link);
            }
            if (linkedNow)
            {
                // The director hears of every new or re-activated link: the patient number, relationship and inviter only.
                var inviter = invite.InvitedBy is { } by
                    ? await db.Users.AsNoTracking().Where(u => u.Id == by).Select(u => u.FullName).FirstOrDefaultAsync(ct)
                    : null;
                // An emailed invite to an address that isn't the patient's recorded email is pointed out (a clerk typing their own address).
                var offFile = invite.Email is { } sentTo && !string.Equals(sentTo.Trim(), patient.Email?.Trim(), StringComparison.OrdinalIgnoreCase);
                await notices.PortalLinkedAsync(invite.PatientId, patient.Number, relationship, inviter ?? "a staff member", offFile, ct);
            }
            link.Relationship = relationship;
            link.Active = true;
            link.RevokedAt = null;
            link.RevokedByUserId = null;
            link.InviteId = invite.Id;
            db.AuditLogs.Add(Audit("patient.portal_linked", invite.PatientId, new { inviteId = invite.Id, linkId = link.Id }, now));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await portal.PatientsAsync(ct);
    }

    private static PatientLinkRelationship ParseRelationship(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value) || value.Any(char.IsDigit) || !Enum.TryParse<PatientLinkRelationship>(value, ignoreCase: true, out var relationship)
            || !Enum.IsDefined(relationship))
            throw BadRequestException.Field("relationship", ChooseRelationship);
        return relationship;
    }

    /// <summary>An explicit audit row: ids only, never contact details or the token.</summary>
    private AuditLog Audit(string action, Guid patientId, object after, DateTimeOffset at) => new()
    {
        TenantId = tenant.TenantId, ActorId = user.UserId, Action = action, EntityType = nameof(Patient), EntityId = patientId.ToString(),
        AfterJson = JsonSerializer.Serialize(after), Ip = user.IpAddress, CreatedAt = at,
    };
}
