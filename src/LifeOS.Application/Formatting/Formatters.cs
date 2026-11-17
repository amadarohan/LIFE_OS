using System.Globalization;
using System.Text;
using LifeOS.Application.Abstractions;

namespace LifeOS.Application.Formatting;

/// <summary>Formats instants and dates for display in the Persian UI.</summary>
public interface IDateTimeFormatter
{
    /// <summary>Jalali date, e.g. ۱۴۰۳/۰۸/۲۶.</summary>
    string FormatDate(DateOnly date);

    /// <summary>Jalali date with month name, e.g. ۲۶ آبان ۱۴۰۳.</summary>
    string FormatDateLong(DateOnly date);

    /// <summary>Jalali date and local time, e.g. ۲۶ آبان ۱۴۰۳ - ۱۴:۳۰.</summary>
    string FormatDateTime(DateTimeOffset utcInstant);

    /// <summary>Local time only, e.g. ۱۴:۳۰.</summary>
    string FormatTime(DateTimeOffset utcInstant);

    /// <summary>Human relative distance, e.g. ۳ روز پیش / ۲ ساعت دیگر.</summary>
    string FormatRelative(DateOnly date, DateOnly today);

    string FormatRelative(DateTimeOffset utcInstant, DateTimeOffset nowUtc);

    /// <summary>Weekday name in Persian.</summary>
    string FormatDayName(DateOnly date);
}

/// <summary>Formats monetary amounts with the configured currency.</summary>
public interface IMoneyFormatter
{
    string Format(decimal amount, string currencyCode);

    /// <summary>Compact form for dashboards, e.g. ۱۲.۵ میلیون.</summary>
    string FormatCompact(decimal amount, string currencyCode);
}

/// <summary>Formats plain numbers, percentages and durations.</summary>
public interface INumberFormatter
{
    string Format(decimal value, int decimals = 0);
    string Format(int value);
    string FormatPercent(decimal value, int decimals = 1);
    string FormatMinutes(int minutes);
}

/// <summary>
/// Persian digit conversion. Kept as a single helper so every formatter produces the same glyphs.
/// </summary>
public static class PersianDigits
{
    private const string Western = "0123456789";
    private const string Persian = "۰۱۲۳۴۵۶۷۸۹";

    public static string ToPersian(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var index = Western.IndexOf(ch);
            builder.Append(index >= 0 ? Persian[index] : ch);
        }

        return builder.ToString();
    }

    public static string ToWestern(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var index = Persian.IndexOf(ch);
            builder.Append(index >= 0 ? Western[index] : ch);
        }

        return builder.ToString();
    }
}

public sealed class PersianDateTimeFormatter : IDateTimeFormatter
{
    private readonly IUserTimeZone _timeZone;

    public PersianDateTimeFormatter(IUserTimeZone timeZone)
    {
        _timeZone = timeZone;
    }

    public string FormatDate(DateOnly date)
    {
        var jalali = JalaliDateConverter.ToJalali(date);
        return PersianDigits.ToPersian(
            $"{jalali.Year:0000}/{jalali.Month:00}/{jalali.Day:00}");
    }

    public string FormatDateLong(DateOnly date)
    {
        var jalali = JalaliDateConverter.ToJalali(date);
        return PersianDigits.ToPersian($"{jalali.Day} {JalaliDateConverter.MonthName(jalali.Month)} {jalali.Year}");
    }

    public string FormatDateTime(DateTimeOffset utcInstant)
    {
        var local = TimeZoneInfo.ConvertTime(utcInstant, _timeZone.TimeZone);
        var date = DateOnly.FromDateTime(local.DateTime);
        return $"{FormatDateLong(date)} - {FormatTime(utcInstant)}";
    }

    public string FormatTime(DateTimeOffset utcInstant)
    {
        var local = TimeZoneInfo.ConvertTime(utcInstant, _timeZone.TimeZone);
        return PersianDigits.ToPersian(local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    public string FormatRelative(DateOnly date, DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;

        return days switch
        {
            0 => "امروز",
            1 => "فردا",
            -1 => "دیروز",
            > 1 and < 7 => PersianDigits.ToPersian($"{days} روز دیگر"),
            > 0 => PersianDigits.ToPersian($"{days} روز دیگر"),
            > -7 => PersianDigits.ToPersian($"{Math.Abs(days)} روز پیش"),
            _ => PersianDigits.ToPersian($"{Math.Abs(days)} روز پیش")
        };
    }

    public string FormatRelative(DateTimeOffset utcInstant, DateTimeOffset nowUtc)
    {
        var delta = nowUtc - utcInstant;
        var minutes = (int)Math.Round(Math.Abs(delta.TotalMinutes));
        var isPast = delta.TotalMinutes >= 0;

        if (minutes < 1)
        {
            return "همین حالا";
        }

        if (minutes < 60)
        {
            return PersianDigits.ToPersian(isPast ? $"{minutes} دقیقه پیش" : $"{minutes} دقیقه دیگر");
        }

        var hours = minutes / 60;
        if (hours < 24)
        {
            return PersianDigits.ToPersian(isPast ? $"{hours} ساعت پیش" : $"{hours} ساعت دیگر");
        }

        var days = hours / 24;
        return PersianDigits.ToPersian(isPast ? $"{days} روز پیش" : $"{days} روز دیگر");
    }

    public string FormatDayName(DateOnly date) => JalaliDateConverter.DayName(date.DayOfWeek);
}

public sealed class PersianMoneyFormatter : IMoneyFormatter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public string Format(decimal amount, string currencyCode)
    {
        var rounded = decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("N0", Invariant);
        return PersianDigits.ToPersian($"{text} {CurrencyLabel(currencyCode)}");
    }

    public string FormatCompact(decimal amount, string currencyCode)
    {
        var absolute = Math.Abs(amount);
        var label = CurrencyLabel(currencyCode);

        if (absolute >= 1_000_000_000m)
        {
            return PersianDigits.ToPersian($"{decimal.Round(amount / 1_000_000_000m, 1, MidpointRounding.AwayFromZero)} میلیارد {label}");
        }

        if (absolute >= 1_000_000m)
        {
            return PersianDigits.ToPersian($"{decimal.Round(amount / 1_000_000m, 1, MidpointRounding.AwayFromZero)} میلیون {label}");
        }

        if (absolute >= 1_000m)
        {
            return PersianDigits.ToPersian($"{decimal.Round(amount / 1_000m, 1, MidpointRounding.AwayFromZero)} هزار {label}");
        }

        return Format(amount, currencyCode);
    }

    private static string CurrencyLabel(string currencyCode) => currencyCode?.ToUpperInvariant() switch
    {
        "IRR" => "ریال",
        "IRT" => "تومان",
        "USD" => "دلار",
        "EUR" => "یورو",
        null or "" => "ریال",
        _ => currencyCode!
    };
}

public sealed class PersianNumberFormatter : INumberFormatter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public string Format(decimal value, int decimals = 0)
        => PersianDigits.ToPersian(decimal.Round(value, decimals, MidpointRounding.AwayFromZero)
            .ToString($"N{decimals}", Invariant));

    public string Format(int value) => PersianDigits.ToPersian(value.ToString("N0", Invariant));

    public string FormatPercent(decimal value, int decimals = 1)
        => PersianDigits.ToPersian($"{decimal.Round(value, decimals, MidpointRounding.AwayFromZero).ToString($"N{decimals}", Invariant)}٪");

    public string FormatMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return "۰ دقیقه";
        }

        var hours = minutes / 60;
        var remainder = minutes % 60;

        if (hours == 0)
        {
            return PersianDigits.ToPersian($"{remainder} دقیقه");
        }

        return remainder == 0
            ? PersianDigits.ToPersian($"{hours} ساعت")
            : PersianDigits.ToPersian($"{hours} ساعت و {remainder} دقیقه");
    }
}
