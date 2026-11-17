namespace LifeOS.Domain.Common;

/// <summary>
/// Raised when a domain invariant is violated. Application layer maps this to a
/// user-facing validation failure; it must never surface as a raw exception in the UI.
/// </summary>
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string rule, string message) : base(message)
    {
        Rule = rule;
    }

    public string Rule { get; }
}

/// <summary>Raised when an entity is not found by identifier.</summary>
public sealed class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entityType, Guid id)
        : base($"{entityType} with id '{id}' was not found.")
    {
        EntityType = entityType;
        EntityId = id;
    }

    public string EntityType { get; }
    public Guid EntityId { get; }
}
