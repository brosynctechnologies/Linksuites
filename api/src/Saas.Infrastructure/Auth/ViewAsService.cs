using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Saas.Application.Common;
using Saas.Application.Platform;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Auth;

public sealed record ViewAsDto(string Token, DateTimeOffset ExpiresAt, string Product, string WebUrl, Guid SessionId);

/// <summary>
/// Support's read-only look at a business: a 30-minute token for the business owner's view, marked read-only (every write is
/// refused by <c>ReadOnlyTokenMiddleware</c>) and carrying the admin's id. Each session is audited and shown to the business.
/// </summary>
public sealed class ViewAsService(AppDbContext db, PlatformAudit audit, AuthSessions sessions, AppUrls urls, IOptions<JwtOptions> jwtOptions, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    public const string StartAction = "support.view_as.start";
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<ViewAsDto> StartAsync(Guid tenantId, ReasonRequest r, CancellationToken ct)
    {
        var reason = Reasons.Require(r.Reason);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct) ?? throw new NotFoundException("Business");
        var owner = await db.Memberships.IgnoreQueryFilters().Include(m => m.User)
            .Where(m => m.TenantId == tenantId && m.Role == MemberRole.Owner).OrderBy(m => m.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw new ConflictException("This business has no owner to view as.");

        var now = clock.GetUtcNow();
        var expires = now + Lifetime;
        var sessionId = Guid.CreateVersion7();
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_jwt.Key(), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, owner.UserId.ToString()), new Claim(JwtRegisteredClaimNames.Email, owner.User!.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Name, owner.User.FullName), new Claim(JwtRegisteredClaimNames.Jti, sessionId.ToString()),
                new Claim(AppClaims.Tenant, tenant.Id.ToString()), new Claim(AppClaims.Role, MemberRole.Owner.ToString()),
                new Claim(AppClaims.Product, tenant.Product), new Claim(AppClaims.ViewAs, audit.ActorId.ToString()), new Claim(AppClaims.ReadOnly, "1"),
            ]),
        });
        sessions.Start(sessionId, audit.ActorId, AuthSessionKind.SupportView, expires, tenant.Id);
        audit.Log(StartAction, nameof(Tenant), tenant.Id.ToString(), tenant.Id, after: new { sessionId, reason, expiresAt = expires });
        await db.SaveChangesAsync(ct);
        return new ViewAsDto(token, expires, tenant.Product, $"{urls.Web(tenant.Product)}/support#token={token}", sessionId);
    }
}

public sealed record AccountActivityDto(Guid SessionId, string AdminName, string Reason, DateTimeOffset StartedAt, DateTimeOffset EndsAt, DateTimeOffset? EndedAt);

/// <summary>What support did in this business: read-only views in the last 90 days. Any member can see it.</summary>
public sealed class AccountActivityService(AppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    private sealed record StartPayload(Guid SessionId, string Reason, DateTimeOffset ExpiresAt);

    public async Task<IReadOnlyList<AccountActivityDto>> ListAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        var since = clock.GetUtcNow().AddDays(-90);
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Action == ViewAsService.StartAction && a.CreatedAt >= since)
            .OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);
        var actorIds = rows.Where(a => a.ActorId != null).Select(a => a.ActorId!.Value).Distinct().ToList();
        var names = await db.Users.Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var ended = await db.AuthSessions.Where(x => x.TenantId == tenantId && x.Kind == AuthSessionKind.SupportView && x.EndedAt != null)
            .ToDictionaryAsync(x => x.Id, x => x.EndedAt, ct);
        return rows.Select(a =>
        {
            var p = JsonSerializer.Deserialize<StartPayload>(a.AfterJson ?? "{}", options);
            return new AccountActivityDto(p?.SessionId ?? Guid.Empty, a.ActorId is { } id ? names.GetValueOrDefault(id, "Support") : "Support",
                p?.Reason ?? "", a.CreatedAt, p?.ExpiresAt ?? a.CreatedAt, p is null ? null : ended.GetValueOrDefault(p.SessionId));
        }).ToList();
    }
}
