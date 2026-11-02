using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Auth;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.School;

/// <summary>"Book a demo" on the SchoolLink site. <c>Students</c> is the school's rough head count.</summary>
public sealed record SchoolDemoRequest(string? SchoolName, string? ContactName, string? Email, string? Phone, int? Students);

public sealed class SchoolDemoRequestValidator : AbstractValidator<SchoolDemoRequest>
{
    public const int MaxStudents = 100_000;

    public SchoolDemoRequestValidator()
    {
        // These go into an email's subject and body, so no control characters (line breaks included).
        RuleFor(x => x.SchoolName).NotEmpty().WithMessage("Enter the school's name.").MaximumLength(120)
            .Must(NoControlCharacters).WithMessage("Write the school's name on one line.");
        RuleFor(x => x.ContactName).NotEmpty().WithMessage("Enter your name.").MaximumLength(100)
            .Must(NoControlCharacters).WithMessage("Write your name on one line.");
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter your email address.").MaximumLength(254)
            .Must(NoControlCharacters).WithMessage("Write your email address on one line.")
            .EmailAddress().WithMessage("Enter a valid email address.");
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Enter a phone number.").MaximumLength(30)
            .Must(NoControlCharacters).WithMessage("Write the phone number on one line.")
            .Matches(@"^[0-9 +\-()]+$").WithMessage("Use only digits, spaces, +, - and brackets in the phone number.")
            .Must(p => PhoneNumbers.Normalize(p, "NG") is not null).WithMessage("Enter a valid phone number.");
        RuleFor(x => x.Students).NotNull().WithMessage("Enter roughly how many students the school has.").InclusiveBetween(1, MaxStudents).WithMessage("Enter roughly how many students the school has.");
    }

    private static bool NoControlCharacters(string? value) => value is null || !value.Any(char.IsControl);
}

/// <summary>
/// Emails a demo request to every active super admin. Nothing else is stored: the outbox row is the record. With no active super admin
/// it goes to the configured platform emails (<c>Platform:AdminEmails</c>) and is logged as an error, so it is never silently lost.
/// </summary>
public sealed class SchoolDemoService(IAppDbContext db, NotificationQueue notifications, PlatformOptions platform, ILogger<SchoolDemoService> logger)
{
    public async Task RequestAsync(SchoolDemoRequest r, CancellationToken ct)
    {
        var team = await (from a in db.PlatformAdmins join u in db.Users on a.UserId equals u.Id
                          where a.RemovedAt == null && a.Role == PlatformRole.SuperAdmin && u.Email != null
                          select u.Email!).Distinct().ToListAsync(ct);
        if (team.Count == 0)
        {
            team = platform.AdminEmails.Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            logger.LogError("No active super admin to receive the SchoolLink demo request from {School} ({ContactEmail}). {Outcome}",
                r.SchoolName!.Trim(), r.Email!.Trim(),
                team.Count > 0 ? $"Sent it to the {team.Count} address(es) in Platform:AdminEmails." : "Platform:AdminEmails is empty, so nobody was emailed.");
        }
        var payload = new Dictionary<string, string>
        {
            ["school"] = r.SchoolName!.Trim(), ["contact"] = r.ContactName!.Trim(), ["email"] = r.Email!.Trim(),
            ["phone"] = r.Phone!.Trim(), ["students"] = r.Students!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var email in team) notifications.Email(email, "school.demo_request", new(payload));
        await db.SaveChangesAsync(ct);
    }
}
