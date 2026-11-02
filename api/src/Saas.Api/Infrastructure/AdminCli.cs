using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.Api.Infrastructure;

/// <summary>
/// Break-glass recovery when nobody can get into the admin console (every super admin lost, or a config admin removed):
/// <c>dotnet Saas.Api.dll admin grant &lt;email&gt; &lt;role&gt;</c>. Needs server access, which is the right bar; audited.
/// The account must already exist (sign up first), so the command never creates logins.
/// </summary>
public static class AdminCli
{
    private const string Usage = "Usage: admin grant <email> <SuperAdmin|Moderator|Finance|Support>";

    /// <summary>0 when granted; 1 when there's no such account; 2 for a bad command.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, CancellationToken ct)
    {
        if (args is not ["admin", "grant", var email, var roleName]
            || !Enum.TryParse<PlatformRole>(roleName, ignoreCase: true, out var role) || !Enum.IsDefined(role) || int.TryParse(roleName, out _))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = email.Trim().ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null)
        {
            await output.WriteLineAsync($"No account uses {email}. Sign up with that email first, then run this again.");
            return 1;
        }

        var row = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        var before = row is null ? null : JsonSerializer.Serialize(new { role = row.Role.ToString(), removed = row.RemovedAt is not null });
        if (row is null) db.PlatformAdmins.Add(new PlatformAdmin { UserId = user.Id, Role = role });
        else
        {
            row.Role = role;
            row.RemovedAt = null;
            row.RemovedBy = null;
        }
        db.AuditLogs.Add(new AuditLog
        {
            Action = "admin.grant_cli", EntityType = nameof(PlatformAdmin), EntityId = user.Id.ToString(), BeforeJson = before,
            AfterJson = JsonSerializer.Serialize(new { role = role.ToString(), via = $"server command on {Environment.MachineName}" }),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        await output.WriteLineAsync($"{user.Email} is now an active {role} admin. They sign in to the console with their password and an emailed code.");
        return 0;
    }
}
