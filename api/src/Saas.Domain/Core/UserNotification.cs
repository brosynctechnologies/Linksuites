using Saas.Domain.Common;

namespace Saas.Domain.Core;

/// <summary>
/// An in-app notification for one signed-in user (the bell). Not a tenant entity: it belongs to the user and is filtered by
/// <see cref="UserId"/>. <see cref="TenantId"/> only records which business it came from, so a business user sees this school's.
/// </summary>
public class UserNotification : Entity
{
    public Guid UserId { get; set; }
    public Guid? TenantId { get; set; }
    public required string Product { get; set; }
    /// <summary>Dotted like the email templates, e.g. "school.results_published".</summary>
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    /// <summary>A path inside the product's web app.</summary>
    public required string Link { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
