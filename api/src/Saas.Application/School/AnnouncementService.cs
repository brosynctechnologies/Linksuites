using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record PostAnnouncementRequest(
    string? Title, string? Body, AnnouncementAudience Audience, List<Guid>? LevelIds, List<Guid>? ClassIds, bool EmailGuardians);
/// <summary><c>WhatsAppText</c> is filled only in the response to posting: a message staff can share with the school's WhatsApp groups.</summary>
public sealed record AnnouncementDto(
    Guid Id, string Title, string Body, AnnouncementAudience Audience, IReadOnlyList<Guid> LevelIds, IReadOnlyList<Guid> ClassIds,
    bool EmailGuardians, Guid PostedBy, string? PostedByName, DateTimeOffset PostedAt, string? AttachmentName, string? AttachmentUrl, string? WhatsAppText);
public sealed record PortalAnnouncementDto(Guid Id, string Title, string Body, DateTimeOffset PostedAt, string? AttachmentName, string? AttachmentUrl);
public sealed record AnnouncementFile(Stream? Content, string? RedirectUrl, string ContentType, string FileName);

/// <summary>
/// Announcements to the whole school, the staff, chosen levels or chosen classes. Owner, Admin and the bursar post to any audience; a teacher
/// only to classes they teach. Posting tells the audience in the app (members for School and Staff; linked parents and students otherwise);
/// when <c>EmailGuardians</c> is set it queues one email per guardian (never for Staff), which the broadcast job sends in batches
/// (<see cref="SendPendingBroadcastsAsync"/>). Staff announcements never appear in the parent or student portals.
/// </summary>
public sealed class AnnouncementService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, SchoolAccess access, ParentAccess parents, StudentAccess students,
    SchoolFeed feed, NotificationQueue notifications, IFileStorage storage, IStudentPhotoProcessor images, SchoolPhotoSigner signer, AppUrls urls, TimeProvider clock)
{
    public const int MaxTitleLength = 150;
    public const int MaxBodyLength = 5000;
    public const int AttachmentMaxSide = 1600;
    public const int WhatsAppBodyLength = 600;
    private const int PortalPageSize = 50;
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Staff ----------

    public async Task<IReadOnlyList<AnnouncementDto>> ListAsync(int? skip, int? take, CancellationToken ct)
    {
        var rows = await db.Announcements.AsNoTracking().OrderByDescending(a => a.PostedAt)
            .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 50, 1, 200)).ToListAsync(ct);
        var posterIds = rows.Select(a => a.PostedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => posterIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(a => Dto(a, names.GetValueOrDefault(a.PostedBy), null)).ToList();
    }

    public async Task<AnnouncementDto> PostAsync(PostAnnouncementRequest r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenException();
        var title = (r.Title ?? "").Trim();
        if (title.Length == 0) throw BadRequestException.Field("title", "Enter a title.");
        if (title.Length > MaxTitleLength) throw BadRequestException.Field("title", $"Use {MaxTitleLength} characters or fewer for the title.");
        var body = (r.Body ?? "").Trim();
        if (body.Length == 0) throw BadRequestException.Field("body", "Write the announcement.");
        if (body.Length > MaxBodyLength) throw BadRequestException.Field("body", $"Use {MaxBodyLength} characters or fewer.");

        var sessionId = await db.AcademicSessions.Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        var levelIds = (r.LevelIds ?? []).Distinct().ToList();
        var classIds = (r.ClassIds ?? []).Distinct().ToList();
        switch (r.Audience)
        {
            case AnnouncementAudience.School:
            case AnnouncementAudience.Staff:
                levelIds = [];
                classIds = [];
                break;
            case AnnouncementAudience.Levels:
                classIds = [];
                if (levelIds.Count == 0) throw BadRequestException.Field("levelIds", "Choose at least one level.");
                if (await db.Levels.CountAsync(l => levelIds.Contains(l.Id), ct) != levelIds.Count)
                    throw BadRequestException.Field("levelIds", "Choose levels of this school.");
                break;
            case AnnouncementAudience.Classes:
                levelIds = [];
                if (classIds.Count == 0) throw BadRequestException.Field("classIds", "Choose at least one class.");
                if (await db.SchoolClasses.CountAsync(c => classIds.Contains(c.Id) && c.SessionId == sessionId, ct) != classIds.Count)
                    throw BadRequestException.Field("classIds", "Choose classes of the current session.");
                break;
            default:
                throw BadRequestException.Field("audience", "Choose who the announcement is for: the whole school, the staff, levels or classes.");
        }

        // Teachers post to their own classes only; leadership and the bursar post to anyone, including the staff.
        if (!access.IsLeadership && !access.IsBursar)
        {
            if (r.Audience != AnnouncementAudience.Classes) throw new ForbiddenException("Teachers can post announcements to their own classes only.");
            foreach (var classId in classIds)
                if (!await access.TeachesClassAsync(classId, ct)) throw new ForbiddenException("Teachers can post announcements to their own classes only.");
        }

        var now = clock.GetUtcNow();
        var announcement = new Announcement
        {
            TenantId = TenantId, Title = title, Body = body, Audience = r.Audience, LevelIds = levelIds, ClassIds = classIds,
            EmailGuardians = r.EmailGuardians && r.Audience != AnnouncementAudience.Staff, PostedBy = userId, PostedAt = now,
        };
        db.Announcements.Add(announcement);
        var school = await db.SchoolProfiles.AsNoTracking().Select(p => p.Name).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        await QueueRecipientsAsync(announcement, sessionId, school, ct);
        await db.SaveChangesAsync(ct);

        var poster = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return Dto(announcement, poster, WhatsAppText(school, title, body, r.Audience == AnnouncementAudience.Staff ? "/app/announcements" : "/parent/announcements"));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var announcement = await EditableAsync(id, ct);
        var key = announcement.AttachmentKey;
        db.Announcements.Remove(announcement); // pending emails go with it
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(key, ct);
    }

    /// <summary>One attachment per announcement: a PDF, or an image (re-encoded as WebP). Replacing it deletes the old file.</summary>
    public async Task<AnnouncementDto> UploadAttachmentAsync(Guid id, Upload upload, CancellationToken ct)
    {
        var announcement = await EditableAsync(id, ct);
        if (upload.Length <= 0) throw BadRequestException.Field("file", "Choose a file to attach.");
        if (upload.Length > ImageUploads.MaxBytes) throw BadRequestException.Field("file", "Attachments must be 5 MB or smaller.");
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        if (buffer.Length > ImageUploads.MaxBytes) throw BadRequestException.Field("file", "Attachments must be 5 MB or smaller.");

        byte[] bytes;
        string contentType, extension;
        if (buffer.Length >= 5 && buffer.GetBuffer().AsSpan(0, 5).SequenceEqual("%PDF-"u8))
        {
            bytes = buffer.ToArray();
            (contentType, extension) = ("application/pdf", ".pdf");
        }
        else
        {
            buffer.Position = 0;
            try
            {
                bytes = images.ToWebp(buffer, AttachmentMaxSide);
            }
            catch (InvalidDataException)
            {
                throw BadRequestException.Field("file", "Attach a PDF or a JPEG, PNG or WebP image.");
            }
            (contentType, extension) = ("image/webp", ".webp");
        }

        var key = $"private/school/{TenantId}/announcements/{announcement.Id:N}-{Guid.NewGuid():N}{extension}";
        await using (var stream = new MemoryStream(bytes)) await storage.SaveAsync(key, stream, contentType, ct);
        var old = announcement.AttachmentKey;
        announcement.AttachmentKey = key;
        announcement.AttachmentType = contentType;
        announcement.AttachmentName = AttachmentName(upload.FileName, extension);
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
        var poster = await db.Users.AsNoTracking().Where(u => u.Id == announcement.PostedBy).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return Dto(announcement, poster, null);
    }

    /// <summary>The attachment behind a valid signed link, or null. Reads across schools: the signature is the only gate.</summary>
    public async Task<AnnouncementFile?> AttachmentByLinkAsync(Guid id, long expires, string signature, CancellationToken ct)
    {
        if (!signer.IsAnnouncementFileValid(id, expires, signature)) throw new ForbiddenException("This link has expired. Open the announcement again.");
        var row = await db.Announcements.IgnoreQueryFilters().AsNoTracking().Where(a => a.Id == id)
            .Select(a => new { a.AttachmentKey, a.AttachmentType, a.AttachmentName }).FirstOrDefaultAsync(ct);
        if (row?.AttachmentKey is null) return null;
        var type = row.AttachmentType ?? "application/octet-stream";
        var name = row.AttachmentName ?? "attachment";
        if (storage.TemporaryUrl(row.AttachmentKey, SchoolPhotoSigner.Lifetime) is { } url) return new AnnouncementFile(null, url, type, name);
        var stream = await storage.OpenReadAsync(row.AttachmentKey, ct);
        return stream is null ? null : new AnnouncementFile(stream, null, type, name);
    }

    // ---------- Portals ----------

    /// <summary>The announcements that reach the child today: the whole school, the child's level and the child's class.</summary>
    public async Task<IReadOnlyList<PortalAnnouncementDto>> ForChildAsync(Guid studentId, CancellationToken ct)
    {
        await parents.RequireChildAsync(studentId, ct);
        return await ForStudentAsync(studentId, ct);
    }

    public async Task<IReadOnlyList<PortalAnnouncementDto>> ForSelfAsync(CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        return await ForStudentAsync(me.StudentId, ct);
    }

    private async Task<IReadOnlyList<PortalAnnouncementDto>> ForStudentAsync(Guid studentId, CancellationToken ct)
    {
        var current = await db.Enrolments.AsNoTracking()
            .Where(e => e.StudentId == studentId && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent))
            .Join(db.SchoolClasses, e => e.ClassId, c => c.Id, (e, c) => new { c.Id, c.LevelId }).FirstOrDefaultAsync(ct);
        // A child with no class this session sees only the whole-school announcements (Guid.Empty matches nothing).
        var classId = current?.Id ?? Guid.Empty;
        var levelId = current?.LevelId ?? Guid.Empty;
        var rows = await db.Announcements.AsNoTracking()
            .Where(a => a.Audience == AnnouncementAudience.School
                || (a.Audience == AnnouncementAudience.Levels && a.LevelIds.Contains(levelId))
                || (a.Audience == AnnouncementAudience.Classes && a.ClassIds.Contains(classId)))
            .OrderByDescending(a => a.PostedAt).Take(PortalPageSize).ToListAsync(ct);
        return rows.Select(a => new PortalAnnouncementDto(a.Id, a.Title, a.Body, a.PostedAt, a.AttachmentName,
            a.AttachmentKey is null ? null : signer.AnnouncementFileUrl(a.Id))).ToList();
    }

    // ---------- Broadcast emails ----------

    /// <summary>
    /// Sends up to <paramref name="batchSize"/> of the school's pending announcement emails, oldest first, and returns how many were sent.
    /// Called by the broadcast job for each school until it returns 0. Each email is queued and its row marked sent in one save.
    /// </summary>
    public async Task<int> SendPendingBroadcastsAsync(int batchSize, CancellationToken ct)
    {
        var tenantId = TenantId;
        var batch = await db.AnnouncementDeliveries.Where(d => d.Status == AnnouncementDeliveryStatus.Pending)
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).Take(Math.Clamp(batchSize, 1, 1000)).ToListAsync(ct);
        if (batch.Count == 0) return 0;

        var announcementIds = batch.Select(d => d.AnnouncementId).Distinct().ToList();
        var announcements = await db.Announcements.AsNoTracking().Where(a => announcementIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var guardianIds = batch.Select(d => d.GuardianId).Distinct().ToList();
        var guardians = await db.Guardians.AsNoTracking().Where(g => guardianIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => new { g.FullName, g.UserId }, ct);
        var school = await db.SchoolProfiles.AsNoTracking().Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "";
        var link = $"{urls.Web(Products.School)}/parent/announcements";
        var now = clock.GetUtcNow();

        foreach (var delivery in batch)
        {
            var a = announcements[delivery.AnnouncementId];
            var guardian = guardians.GetValueOrDefault(delivery.GuardianId);
            notifications.Email(delivery.Email, "school.announcement", new()
            {
                ["guardian"] = guardian?.FullName ?? "", ["school"] = school, ["title"] = a.Title, ["body"] = a.Body, ["link"] = link,
            }, tenantId, guardian?.UserId);
            delivery.Status = AnnouncementDeliveryStatus.Sent;
            delivery.SentAt = now;
        }
        await db.SaveChangesAsync(ct);
        return batch.Count;
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Tells the audience in the app: every active member for School and Staff; the linked parents and the students for School, Levels
    /// and Classes, whose guardians' emails are queued too. Saved by the caller.
    /// </summary>
    private async Task QueueRecipientsAsync(Announcement a, Guid? sessionId, string school, CancellationToken ct)
    {
        var title = $"{school}: {a.Title}";
        var body = Excerpt(a.Body, 140);
        if (a.Audience is AnnouncementAudience.School or AnnouncementAudience.Staff)
            feed.Notify(await feed.MembersAsync(ct, Enum.GetValues<MemberRole>()), "school.announcement", title, body, "/app/announcements");
        if (a.Audience == AnnouncementAudience.Staff || sessionId is not { } session) return;

        var studentIds = AudienceStudentIds(a.Audience, a.LevelIds, a.ClassIds, session);
        var guardians = await db.StudentGuardians.AsNoTracking().Where(l => studentIds.Contains(l.StudentId))
            .Join(db.Guardians.AsNoTracking(), l => l.GuardianId, g => g.Id, (l, g) => new { g.Id, g.UserId, g.Email, l.StudentId })
            .ToListAsync(ct);
        var studentUsers = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id) && s.UserId != null)
            .Select(s => s.UserId!.Value).ToListAsync(ct);

        var parentUsers = guardians.Where(g => g.UserId != null).Select(g => g.UserId!.Value).Distinct().ToList();
        // One notification per parent; its link opens a child of theirs in the audience (the first by id, so it is stable).
        foreach (var parent in guardians.Where(g => g.UserId != null).GroupBy(g => g.UserId!.Value))
            feed.Notify(parent.Key, "school.announcement", title, body, SchoolFeed.ParentLink("announcements", parent.Min(g => g.StudentId)));
        feed.Notify(studentUsers.Except(parentUsers), "school.announcement", title, body, "/student/announcements");

        if (!a.EmailGuardians) return;
        foreach (var g in guardians.Where(g => g.Email != null).DistinctBy(g => g.Id).ToList())
            db.AnnouncementDeliveries.Add(new AnnouncementDelivery { TenantId = a.TenantId, AnnouncementId = a.Id, GuardianId = g.Id, Email = g.Email! });
    }

    /// <summary>Active students enrolled in the current session in the audience's classes or levels (every class for the whole school).</summary>
    private IQueryable<Guid> AudienceStudentIds(AnnouncementAudience audience, List<Guid> levelIds, List<Guid> classIds, Guid sessionId)
    {
        var classes = db.SchoolClasses.Where(c => c.SessionId == sessionId);
        if (audience == AnnouncementAudience.Levels) classes = classes.Where(c => levelIds.Contains(c.LevelId));
        if (audience == AnnouncementAudience.Classes) classes = classes.Where(c => classIds.Contains(c.Id));
        return db.Enrolments.Where(e => e.SessionId == sessionId && classes.Any(c => c.Id == e.ClassId))
            .Join(db.Students.Where(s => s.Status == StudentStatus.Active), e => e.StudentId, s => s.Id, (e, s) => s.Id);
    }

    /// <summary>The poster and leadership change an announcement; nobody else.</summary>
    private async Task<Announcement> EditableAsync(Guid id, CancellationToken ct)
    {
        var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Announcement");
        if (!access.IsLeadership && announcement.PostedBy != user.UserId)
            throw new ForbiddenException("Only the person who posted this announcement or the school's leadership can change it.");
        return announcement;
    }

    private string WhatsAppText(string school, string title, string body, string path) =>
        $"{school}: {title}\n\n{Excerpt(body, WhatsAppBodyLength)}\n\nRead it on SchoolLink: {urls.Web(Products.School)}{path}";

    private static string Excerpt(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private static string AttachmentName(string? fileName, string extension)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? "").Trim();
        var safe = new string(stem.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "attachment";
        if (safe.Length > 100) safe = safe[..100];
        return safe + extension;
    }

    private AnnouncementDto Dto(Announcement a, string? posterName, string? whatsAppText) =>
        new(a.Id, a.Title, a.Body, a.Audience, a.LevelIds, a.ClassIds, a.EmailGuardians, a.PostedBy, posterName, a.PostedAt, a.AttachmentName,
            a.AttachmentKey is null ? null : signer.AnnouncementFileUrl(a.Id), whatsAppText);

    private async Task DeleteQuietlyAsync(string? key, CancellationToken ct)
    {
        if (key is null) return;
        try
        {
            await storage.DeleteAsync(key, ct);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            // The old file is orphaned, not harmful; the announcement already points elsewhere.
        }
    }
}
