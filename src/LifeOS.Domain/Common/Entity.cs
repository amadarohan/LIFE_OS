namespace LifeOS.Domain.Common;

/// <summary>
/// Base type for all persisted entities. Identifiers are UUIDv7 so they are
/// time-ordered (index friendly) and can be generated before persistence.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public override bool Equals(object? obj)
        => obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>Entity that records creation and last modification in UTC.</summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAtUtc { get; protected set; }
    public DateTimeOffset UpdatedAtUtc { get; protected set; }

    public void StampCreated(DateTimeOffset now)
    {
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void StampUpdated(DateTimeOffset now) => UpdatedAtUtc = now;
}

/// <summary>Entity protected by SQL Server <c>rowversion</c> optimistic concurrency.</summary>
public interface IConcurrencyTracked
{
    byte[] RowVersion { get; }
}
