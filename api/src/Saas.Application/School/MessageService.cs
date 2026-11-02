using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary><c>To</c> is "class-teacher" or "office".</summary>
public sealed record StartThreadRequest(string? To, string? Body);
public sealed record SendMessageRequest(string? Body);
public sealed record ThreadDto(
    Guid Id, Guid StudentId, string StudentName, Guid GuardianId, string GuardianName, Guid? StaffId, string? StaffName, bool IsOffice,
    DateTimeOffset LastMessageAt, string? LastMessage, int Unread);
public sealed record MessageDto(
    Guid Id, Guid ThreadId, Guid SenderUserId, string SenderName, bool FromParent, string Body, string? ImageUrl, DateTimeOffset SentAt, DateTimeOffset? ReadAt);

/// <summary>
/// Conversations between a guardian and the school about one student: with the class teacher, or with the office (Owner and Admin; the
/// bursar reads and answers office threads too). Parents reach a thread only through <see cref="ParentAccess"/> and only their own guardian
/// record's threads; any other id is 404. Teachers see the threads addressed to them and those of the classes they are class teacher of.
/// A new message tells the other side in the app and by email. A message is text and at most one private image.
/// </summary>
public sealed class MessageService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, SchoolAccess access, ParentAccess parents, SchoolFeed feed,
    NotificationQueue notifications, IFileStorage storage, IStudentPhotoProcessor images, SchoolPhotoSigner signer, AppUrls urls,
    IDbErrors dbErrors, TimeProvider clock)
{
    public const int MaxBodyLength = 2000;
    public const int ImageMaxSide = 1600;
    private const int ListSize = 200;
    public const string NoClassTeacher = "This class has no class teacher yet. Message the school office instead.";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Parents ----------

    public async Task<IReadOnlyList<ThreadDto>> ParentThreadsAsync(Guid studentId, CancellationToken ct)
    {
        var userId = parents.CustomerUserId();
        await parents.RequireChildAsync(studentId, ct);
        var guardianIds = MyGuardianIds(userId, studentId);
        return await ThreadsAsync(db.MessageThreads.Where(t => t.StudentId == studentId && guardianIds.Contains(t.GuardianId)), forParent: true, ct);
    }

    /// <summary>Adds the message to the parent's thread with the class teacher or the office about this child, starting the thread when needed.</summary>
    public async Task<ThreadDto> StartAsync(Guid studentId, StartThreadRequest r, CancellationToken ct)
    {
        var userId = parents.CustomerUserId();
        await parents.RequireChildAsync(studentId, ct);
        var office = (r.To ?? "").Trim().ToLowerInvariant() switch
        {
            "office" => true,
            "class-teacher" => false,
            _ => throw BadRequestException.Field("to", "Choose who to message: the class teacher or the school office."),
        };
        var body = CleanBody(r.Body, hasImage: false);
        var guardianId = await MyGuardianIds(userId, studentId).FirstAsync(ct);

        Guid? staffId = null;
        if (!office)
        {
            staffId = await db.Enrolments.Where(e => e.StudentId == studentId && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent))
                .Join(db.SchoolClasses, e => e.ClassId, c => c.Id, (e, c) => c.ClassTeacherStaffId).FirstOrDefaultAsync(ct);
            if (staffId is null) throw new ConflictException(NoClassTeacher, "no_class_teacher");
        }

        var thread = await FindOrStartThreadAsync(guardianId, studentId, staffId, office, ct);
        await AddAsync(thread, body, null, fromParent: true, ct);
        return (await ThreadsAsync(db.MessageThreads.Where(t => t.Id == thread.Id), forParent: true, ct)).Single();
    }

    /// <summary>The thread's messages, oldest first. Opening the thread marks the school's messages read.</summary>
    public async Task<IReadOnlyList<MessageDto>> ParentMessagesAsync(Guid threadId, CancellationToken ct)
    {
        var thread = await ParentThreadAsync(threadId, ct);
        await MarkReadAsync(thread.Id, fromParent: false, ct);
        return await MessagesAsync(thread, ct);
    }

    public async Task<MessageDto> ParentSendAsync(Guid threadId, string? body, Upload? image, CancellationToken ct)
    {
        var thread = await ParentThreadAsync(threadId, ct);
        return await AddAsync(thread, CleanBody(body, image is not null), image, fromParent: true, ct);
    }

    // ---------- Staff ----------

    /// <summary>
    /// Threads the member may see, latest first. <paramref name="mine"/>: only those addressed to the member (for leadership, also the office's).
    /// </summary>
    public async Task<IReadOnlyList<ThreadDto>> ListAsync(bool mine, CancellationToken ct) =>
        await ThreadsAsync(await VisibleAsync(mine, ct), forParent: false, ct);

    /// <summary>
    /// The thread's messages, oldest first. The parent's messages are marked read only when the addressed side opens the thread: the
    /// thread's staff member, or the office (Owner, Admin, the bursar) for an office thread. Leadership looking in on a teacher's thread leaves them unread.
    /// </summary>
    public async Task<IReadOnlyList<MessageDto>> StaffMessagesAsync(Guid threadId, CancellationToken ct)
    {
        var thread = await StaffThreadAsync(threadId, ct);
        if (await IsAddressedAsync(thread, ct)) await MarkReadAsync(thread.Id, fromParent: true, ct);
        return await MessagesAsync(thread, ct);
    }

    /// <summary>
    /// True when the member is who the parent wrote to. A teacher's thread whose staff member has left is the office's again, the same
    /// people its new messages notify.
    /// </summary>
    private async Task<bool> IsAddressedAsync(MessageThread thread, CancellationToken ct)
    {
        var office = access.IsLeadership || access.IsBursar;
        if (thread.IsOffice) return office;
        var me = await access.StaffIdAsync(ct);
        if (thread.StaffId is { } staffId && me == staffId) return true;
        var staffActive = thread.StaffId is { } id && await db.StaffProfiles.AnyAsync(
            p => p.Id == id && db.Memberships.Any(m => m.Id == p.MembershipId && m.Status == MembershipStatus.Active), ct);
        return !staffActive && access.IsLeadership;
    }

    public async Task<MessageDto> StaffSendAsync(Guid threadId, string? body, Upload? image, CancellationToken ct)
    {
        var thread = await StaffThreadAsync(threadId, ct);
        return await AddAsync(thread, CleanBody(body, image is not null), image, fromParent: false, ct);
    }

    /// <summary>The image behind a valid signed link, or null. Reads across schools: the signature is the only gate.</summary>
    public async Task<StudentPhotoFile?> ImageByLinkAsync(Guid messageId, long expires, string signature, CancellationToken ct)
    {
        if (!signer.IsMessageImageValid(messageId, expires, signature)) throw new ForbiddenException("This image link has expired. Open the conversation again.");
        var key = await db.Messages.IgnoreQueryFilters().AsNoTracking().Where(m => m.Id == messageId).Select(m => m.AttachmentKey).FirstOrDefaultAsync(ct);
        if (key is null) return null;
        if (storage.TemporaryUrl(key, SchoolPhotoSigner.Lifetime) is { } url) return new StudentPhotoFile(null, url);
        var stream = await storage.OpenReadAsync(key, ct);
        return stream is null ? null : new StudentPhotoFile(stream, null);
    }

    // ---------- Access ----------

    /// <summary>The guardian records of this account linked to this child (normally one).</summary>
    private IQueryable<Guid> MyGuardianIds(Guid userId, Guid studentId) =>
        db.StudentGuardians.Where(l => l.StudentId == studentId)
            .Join(db.Guardians.Where(g => g.UserId == userId), l => l.GuardianId, g => g.Id, (l, g) => new { g.Id, g.CreatedAt })
            .OrderBy(g => g.CreatedAt).Select(g => g.Id);

    /// <summary>
    /// The parent's own thread; then the request acts as its school. 404 for a thread of any other guardian (or school), and once the
    /// account is no longer linked to the child.
    /// </summary>
    private async Task<MessageThread> ParentThreadAsync(Guid threadId, CancellationToken ct)
    {
        var userId = parents.CustomerUserId();
        // A parent token carries no school, so the thread is found across schools and must belong to one of this account's guardian records.
        var owner = await (from t in db.MessageThreads.IgnoreQueryFilters()
                           where t.Id == threadId
                           join g in db.Guardians.IgnoreQueryFilters() on new { G = t.GuardianId, T = t.TenantId } equals new { G = g.Id, T = g.TenantId }
                           where g.UserId == userId
                           select new { t.StudentId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Conversation");
        await parents.RequireChildAsync(owner.StudentId, ct);
        // From here on the tenant filter applies.
        return await db.MessageThreads.FirstOrDefaultAsync(t => t.Id == threadId, ct) ?? throw new NotFoundException("Conversation");
    }

    private async Task<MessageThread> StaffThreadAsync(Guid threadId, CancellationToken ct)
    {
        var visible = await VisibleAsync(mine: false, ct);
        return await visible.FirstOrDefaultAsync(t => t.Id == threadId, ct) ?? throw new NotFoundException("Conversation");
    }

    /// <summary>
    /// Leadership: every thread (<paramref name="mine"/>: the office's and their own). The bursar: the office's. A teacher: threads addressed
    /// to them and, unless <paramref name="mine"/>, the threads of students in the classes they are class teacher of. Never the office's.
    /// </summary>
    private async Task<IQueryable<MessageThread>> VisibleAsync(bool mine, CancellationToken ct)
    {
        var threads = db.MessageThreads.AsQueryable();
        var me = await access.StaffIdAsync(ct);
        if (access.IsLeadership)
        {
            if (!mine) return threads;
            return me is { } own ? threads.Where(t => t.IsOffice || t.StaffId == own) : threads.Where(t => t.IsOffice);
        }
        if (access.IsBursar) return threads.Where(t => t.IsOffice);
        if (me is not { } staffId) return threads.Where(t => false);
        if (mine) return threads.Where(t => !t.IsOffice && t.StaffId == staffId);
        var myStudents = db.Enrolments
            .Where(e => db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent)
                && db.SchoolClasses.Any(c => c.Id == e.ClassId && c.ClassTeacherStaffId == staffId))
            .Select(e => e.StudentId);
        return threads.Where(t => !t.IsOffice && (t.StaffId == staffId || myStudents.Contains(t.StudentId)));
    }

    // ---------- Writing ----------

    private async Task<MessageThread> FindOrStartThreadAsync(Guid guardianId, Guid studentId, Guid? staffId, bool office, CancellationToken ct)
    {
        IQueryable<MessageThread> Existing() => db.MessageThreads.Where(t => t.GuardianId == guardianId && t.StudentId == studentId
            && (office ? t.IsOffice : !t.IsOffice && t.StaffId == staffId));
        if (await Existing().FirstOrDefaultAsync(ct) is { } found) return found;

        var thread = new MessageThread
        {
            TenantId = TenantId, GuardianId = guardianId, StudentId = studentId, StaffId = office ? null : staffId, IsOffice = office,
            LastMessageAt = clock.GetUtcNow(),
        };
        db.MessageThreads.Add(thread);
        try
        {
            await db.SaveChangesAsync(ct);
            return thread;
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex))
        {
            // The parent's other tab started the same thread first: use that one.
            db.ChangeTracker.Entries<MessageThread>().Single(e => e.Entity == thread).State = EntityState.Detached;
            return await Existing().FirstAsync(ct);
        }
    }

    private string CleanBody(string? raw, bool hasImage)
    {
        var body = (raw ?? "").Trim();
        if (body.Length == 0 && !hasImage) throw BadRequestException.Field("body", "Write a message.");
        if (body.Length > MaxBodyLength) throw BadRequestException.Field("body", $"Use {MaxBodyLength} characters or fewer.");
        return body;
    }

    private async Task<MessageDto> AddAsync(MessageThread thread, string body, Upload? image, bool fromParent, CancellationToken ct)
    {
        var senderId = user.UserId ?? throw new ForbiddenException();
        var message = new Message { TenantId = thread.TenantId, ThreadId = thread.Id, SenderUserId = senderId, FromParent = fromParent, Body = body };
        if (image is not null) message.AttachmentKey = await SaveImageAsync(message.Id, image, ct);

        var now = clock.GetUtcNow();
        message.CreatedAt = now;
        thread.LastMessageAt = now;
        db.Messages.Add(message);
        var names = await NotifyOtherSideAsync(thread, message, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(message, names.Sender);
    }

    private async Task<string> SaveImageAsync(Guid messageId, Upload upload, CancellationToken ct)
    {
        var (buffer, _, _) = await ImageUploads.ReadAsync(upload, ct);
        byte[] webp;
        using (buffer)
        {
            try
            {
                webp = images.ToWebp(buffer, ImageMaxSide);
            }
            catch (InvalidDataException)
            {
                throw BadRequestException.Field("file", "We couldn't read this image. Upload a JPEG, PNG or WebP image.");
            }
        }
        // Under "private/": never served from /uploads.
        var key = $"private/school/{TenantId}/messages/{messageId:N}.webp";
        await using var stream = new MemoryStream(webp);
        await storage.SaveAsync(key, stream, "image/webp", ct);
        return key;
    }

    /// <summary>
    /// A parent's message goes to the thread's staff member (or, for the office or when that member has left, to Owner and Admin); the school's
    /// reply goes to the guardian. In the app and by email, saved by the caller.
    /// </summary>
    private async Task<(string Sender, string Child)> NotifyOtherSideAsync(MessageThread thread, Message message, CancellationToken ct)
    {
        var school = await db.SchoolProfiles.AsNoTracking().Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "";
        var child = await db.Students.AsNoTracking().Where(s => s.Id == thread.StudentId)
            .Select(s => s.FirstName + " " + s.LastName).FirstAsync(ct);
        var guardian = await db.Guardians.AsNoTracking().Where(g => g.Id == thread.GuardianId)
            .Select(g => new { g.FullName, g.Email, g.UserId }).FirstAsync(ct);
        var excerpt = message.Body.Length == 0 ? "Sent an image." : message.Body.Length <= 140 ? message.Body : message.Body[..139].TrimEnd() + "…";

        if (message.FromParent)
        {
            var recipients = new List<Guid>();
            if (!thread.IsOffice && thread.StaffId is { } staffId)
                recipients = await db.StaffProfiles.AsNoTracking()
                    .Where(p => p.Id == staffId && db.Memberships.Any(m => m.Id == p.MembershipId && m.Status == MembershipStatus.Active))
                    .Select(p => p.UserId).ToListAsync(ct);
            if (recipients.Count == 0) recipients = await feed.MembersAsync(ct, MemberRole.Owner, MemberRole.Admin);

            var title = $"New message from {guardian.FullName} about {child}";
            feed.Notify(recipients, "school.message", title, excerpt, $"/app/messages/{thread.Id}");
            var link = $"{urls.Web(Products.School)}/app/messages/{thread.Id}";
            var staff = await db.Users.AsNoTracking().Where(u => recipients.Contains(u.Id) && u.Email != null)
                .Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct);
            foreach (var member in staff)
                notifications.Email(member.Email!, "school.message", new()
                {
                    ["name"] = member.FullName, ["sender"] = guardian.FullName, ["child"] = child, ["school"] = school, ["message"] = excerpt, ["link"] = link,
                }, TenantId, member.Id);
            return (guardian.FullName, child);
        }

        var sender = await db.Users.AsNoTracking().Where(u => u.Id == message.SenderUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? school;
        if (guardian.UserId is { } parentId)
            feed.Notify(parentId, "school.message", $"{sender} replied about {child}", excerpt, $"/parent/messages/{thread.Id}");
        var to = guardian.Email ?? (guardian.UserId is { } id
            ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(ct)
            : null);
        if (to is not null && !StudentLogins.IsStudentEmail(to))
            notifications.Email(to, "school.message", new()
            {
                ["name"] = guardian.FullName, ["sender"] = $"{sender} ({school})", ["child"] = child, ["school"] = school, ["message"] = excerpt,
                ["link"] = $"{urls.Web(Products.School)}/parent/messages/{thread.Id}",
            }, TenantId, guardian.UserId);
        return (sender, child);
    }

    /// <summary>
    /// Marks the other side's messages read and, for the reader, this thread's unread bell notifications (the staff link for staff,
    /// the parent link for a parent), so the bell agrees with the Messages list.
    /// </summary>
    private async Task MarkReadAsync(Guid threadId, bool fromParent, CancellationToken ct)
    {
        // Support's view-as reads without leaving read receipts.
        if (user.IsSupportView) return;
        var now = clock.GetUtcNow();
        await db.Messages.Where(m => m.ThreadId == threadId && m.FromParent == fromParent && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now), ct);
        if (user.UserId is not { } readerId) return;
        var link = fromParent ? $"/app/messages/{threadId}" : $"/parent/messages/{threadId}";
        var withQuery = link + "?";
        await db.UserNotifications
            .Where(n => n.UserId == readerId && n.Kind == "school.message" && n.ReadAt == null && (n.Link == link || n.Link.StartsWith(withQuery)))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    // ---------- Reading ----------

    private async Task<IReadOnlyList<ThreadDto>> ThreadsAsync(IQueryable<MessageThread> query, bool forParent, CancellationToken ct)
    {
        var unreadFromParent = !forParent;
        var rows = await query.AsNoTracking().OrderByDescending(t => t.LastMessageAt).Take(ListSize)
            .Select(t => new
            {
                Thread = t,
                Student = db.Students.Where(s => s.Id == t.StudentId).Select(s => s.FirstName + " " + s.LastName).FirstOrDefault(),
                Guardian = db.Guardians.Where(g => g.Id == t.GuardianId).Select(g => g.FullName).FirstOrDefault(),
                Staff = db.StaffProfiles.Where(p => p.Id == t.StaffId).Join(db.Users, p => p.UserId, u => u.Id, (p, u) => u.FullName).FirstOrDefault(),
                Last = db.Messages.Where(m => m.ThreadId == t.Id).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Body).FirstOrDefault(),
                Unread = db.Messages.Count(m => m.ThreadId == t.Id && m.ReadAt == null && m.FromParent == unreadFromParent),
            })
            .ToListAsync(ct);
        return rows.Select(x => new ThreadDto(x.Thread.Id, x.Thread.StudentId, x.Student ?? "", x.Thread.GuardianId, x.Guardian ?? "",
            x.Thread.StaffId, x.Staff, x.Thread.IsOffice, x.Thread.LastMessageAt, x.Last, x.Unread)).ToList();
    }

    private async Task<IReadOnlyList<MessageDto>> MessagesAsync(MessageThread thread, CancellationToken ct)
    {
        var rows = await db.Messages.AsNoTracking().Where(m => m.ThreadId == thread.Id).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var guardian = await db.Guardians.AsNoTracking().Where(g => g.Id == thread.GuardianId).Select(g => g.FullName).FirstOrDefaultAsync(ct) ?? "";
        var staffIds = rows.Where(m => !m.FromParent).Select(m => m.SenderUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => staffIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(m => ToDto(m, m.FromParent ? guardian : names.GetValueOrDefault(m.SenderUserId) ?? "")).ToList();
    }

    private MessageDto ToDto(Message m, string senderName) =>
        new(m.Id, m.ThreadId, m.SenderUserId, senderName, m.FromParent, m.Body,
            m.AttachmentKey is null ? null : signer.MessageImageUrl(m.Id), m.CreatedAt, m.ReadAt);
}
