using LifeOS.Application.Abstractions;

namespace LifeOS.Infrastructure.Services;

/// <summary>
/// Resolves the configured timezone. The default is the machine's local zone, but the user can
/// override it in settings; storage is always UTC and only display converts.
/// </summary>
public sealed class UserTimeZone : IUserTimeZone
{
    public UserTimeZone(string? timeZoneId)
    {
        TimeZone = Resolve(timeZoneId);
    }

    public TimeZoneInfo TimeZone { get; }

    public DateOnly ToLocalDate(DateTimeOffset utcInstant)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcInstant, TimeZone).DateTime);

    public DateTimeOffset ToUtc(DateOnly localDate, TimeOnly localTime)
    {
        var local = localDate.ToDateTime(localTime);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = TimeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    public DateOnly TodayLocal => ToLocalDate(DateTimeOffset.UtcNow);

    private static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall through to local zone rather than failing startup.
            }
            catch (InvalidTimeZoneException)
            {
                // Same: a corrupt zone database must not prevent the app from running.
            }
        }

        return TimeZoneInfo.Local;
    }
}
