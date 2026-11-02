namespace Saas.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Rows owned by one tenant. A global query filter limits every query to the current tenant.</summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

public abstract class TenantEntity : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
}

/// <summary>Changes to entities with this attribute are written to the audit log.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditedAttribute : Attribute;

/// <summary>On a property of an <see cref="AuditedAttribute"/> entity: never copied into audit rows (clinical values, references to clinical records).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class AuditIgnoreAttribute : Attribute;
