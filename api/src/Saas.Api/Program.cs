using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Saas.Api.Endpoints;
using Saas.Api.Infrastructure;
using Saas.Application;
using Saas.Application.Common;
using Saas.Infrastructure;
using Saas.Infrastructure.Auth;
using Saas.Infrastructure.Persistence;
using Saas.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ForbiddenMessageHandler>();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddOpenApi();

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (jwt.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters. Set it in user secrets or an environment variable.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = jwt.Key(),
        NameClaimType = "name",
        RoleClaimType = AppClaims.Role,
        ClockSkew = TimeSpan.FromSeconds(30),
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Owner, p => p.RequireRole("Owner"))
    .AddPolicy(Policies.Admin, p => p.RequireRole("Owner", "Admin"))
    .AddPolicy(Policies.Reports, p => p.RequireRole("Owner", "Admin", "Accountant"))
    .AddPolicy(Policies.OrdersProduct, p => p.RequireClaim(AppClaims.Product, Saas.Application.Billing.Products.WhatsAppOrders))
    .AddPolicy(Policies.InvoicesProduct, p => p.RequireClaim(AppClaims.Product, Saas.Application.Billing.Products.Invoices))
    .AddPolicy(Policies.BookingsProduct, p => p.RequireClaim(AppClaims.Product, Saas.Application.Billing.Products.Bookings))
    .AddPolicy(Policies.CoursesProduct, p => p.RequireClaim(AppClaims.Product, Saas.Application.Billing.Products.Courses))
    .AddPolicy(Policies.PropertyProduct, p => p.RequireClaim(AppClaims.Product, Saas.Application.Billing.Products.Property))
    .AddPolicy(Policies.SchoolProduct, p => p.RequireClaim(AppClaims.Product, Saas.Application.Billing.Products.School))
    .AddPolicy(Policies.CareProduct, p => p.RequireClaim(AppClaims.Product, Saas.Application.Billing.Products.Care))
    .AddPolicy(Policies.PlatformAdmin, p => p.AddRequirements(new PlatformRoleRequirement()))
    .AddPolicy(Policies.PlatformModerator, p => p.AddRequirements(new PlatformRoleRequirement(Saas.Domain.Core.PlatformRole.Moderator)))
    .AddPolicy(Policies.PlatformFinance, p => p.AddRequirements(new PlatformRoleRequirement(Saas.Domain.Core.PlatformRole.Finance)))
    .AddPolicy(Policies.PlatformSupport, p => p.AddRequirements(new PlatformRoleRequirement(Saas.Domain.Core.PlatformRole.Support)))
    .AddPolicy(Policies.PlatformSupportOrFinance, p => p.AddRequirements(new PlatformRoleRequirement(Saas.Domain.Core.PlatformRole.Support, Saas.Domain.Core.PlatformRole.Finance)))
    .AddPolicy(Policies.PlatformSession, p => p.RequireClaim(AppClaims.Amr, "otp"))
    .AddPolicy(Policies.PlatformSuper, p => p.AddRequirements(new PlatformRoleRequirement(Saas.Domain.Core.PlatformRole.SuperAdmin)));
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PlatformRoleHandler>();

var authPerMinute = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 20);
var publicWritesPerMinute = builder.Configuration.GetValue("RateLimits:PublicWritesPerMinute", 10);
var webhooksPerMinute = builder.Configuration.GetValue("RateLimits:WebhooksPerMinute", 600);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    o.AddPolicy(RateLimits.Auth, c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RateLimits.Otp, c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:OtpPer10Minutes", 5), Window = TimeSpan.FromMinutes(10) }));
    o.AddPolicy(RateLimits.Preview, c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:PreviewPerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
    // Per signed-in admin, so a leaked admin token can't bulk-export or bulk-change data quickly.
    o.AddPolicy(RateLimits.Admin, c => RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirst("sub")?.Value ?? Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:AdminPerMinute", 300), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RateLimits.PublicWrite, c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = publicWritesPerMinute, Window = TimeSpan.FromMinutes(1) }));
    // "I've sent the transfer": per business (else per user), so a script can't flood the claims list or the admins' inboxes.
    o.AddPolicy(RateLimits.TransferClaim, c => RateLimitPartition.GetFixedWindowLimiter(
        $"transfer:{c.User.FindFirst(Saas.Infrastructure.Auth.AppClaims.Tenant)?.Value ?? c.User.FindFirst("sub")?.Value ?? Ip(c)}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:TransferClaimsPerHour", 5), Window = TimeSpan.FromHours(1) }));
    // Paystack sends every school's webhooks from a few IPs, so this limit only stops floods.
    o.AddPolicy(RateLimits.Webhook, c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = webhooksPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3700"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddHealthChecks();

var app = builder.Build();

// Break-glass: `dotnet Saas.Api.dll admin grant <email> <role>` runs the command and exits instead of serving.
if (args is ["admin", ..])
{
    Environment.ExitCode = await AdminCli.RunAsync(app.Services, args, Console.Out, CancellationToken.None);
    return;
}

app.UseForwardedHeaders();
app.UseSerilogRequestLogging(); // outside the exception handler so it logs the final status code
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db);
}

var storage = app.Configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
if (!string.Equals(storage.Provider, "R2", StringComparison.OrdinalIgnoreCase))
{
    var uploads = Path.Combine(app.Environment.ContentRootPath, storage.LocalPath);
    Directory.CreateDirectory(uploads);
    // Verification documents live under uploads/private and are only read through the admin API.
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/uploads/private", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next();
    });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(uploads), RequestPath = "/uploads" });
}

app.UseCors();
app.UseAuthentication();
// After authentication, so the per-user and per-business policies (Admin, TransferClaim) see who is signed in; the others are per IP.
app.UseRateLimiter();
app.UseMiddleware<SessionMiddleware>();
app.UseMiddleware<ReadOnlyTokenMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<TenantStatusMiddleware>();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("/docs");
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Redirect("/docs")).ExcludeFromDescription();

app.MapCoreEndpoints();
app.MapOrderSystemEndpoints();
app.MapInvoiceEndpoints();
app.MapBookingEndpoints();
app.MapCourseEndpoints();
app.MapPropertyEndpoints();
app.MapSchoolEndpoints();
app.MapCareEndpoints();
app.MapCarePortalEndpoints();
app.MapParentEndpoints();
app.MapStudentEndpoints();
app.MapPlatformEndpoints();

app.Run();

public partial class Program;
