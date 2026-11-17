using LifeOS.Application.Abstractions;

namespace LifeOS.Infrastructure.Services;

/// <summary>
/// The single source of "now" for the whole application. Nothing else may call
/// <c>DateTimeOffset.UtcNow</c>, so tests can control time deterministically.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
}

/// <summary>Fixed clock for tests and for deterministic replay of AI proposals.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; set; }

    public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
