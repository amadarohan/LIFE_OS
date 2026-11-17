namespace LifeOS.Application.Common;

/// <summary>
/// Raised when a write loses an optimistic-concurrency race. Infrastructure translates EF Core's
/// <c>DbUpdateConcurrencyException</c> into this type so the Application layer never references
/// EF Core.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when a write violates a database constraint (unique index, check constraint, FK).
/// Distinct from <see cref="ConcurrencyConflictException"/> because the user action differs:
/// a conflict means "fix the data", a concurrency conflict means "reload and retry".
/// </summary>
public sealed class PersistenceConflictException : Exception
{
    public PersistenceConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
