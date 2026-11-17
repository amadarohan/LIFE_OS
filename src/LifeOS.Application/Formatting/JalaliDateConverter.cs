using System.Globalization;

namespace LifeOS.Application.Formatting;

/// <summary>A Jalali (Solar Hijri) calendar date. Display-only; storage stays Gregorian.</summary>
public readonly record struct JalaliDate(int Year, int Month, int Day)
{
    public override string ToString() => $"{Year:0000}/{Month:00}/{Day:00}";
}

/// <summary>
/// Converts between Gregorian and Jalali dates. Uses <see cref="PersianCalendar"/> so leap-year
/// rules are the platform's, not a hand-rolled approximation.
/// </summary>
public static class JalaliDateConverter
{
    private static readonly PersianCalendar Calendar = new();

    public static readonly string[] MonthNames =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];

    public static readonly string[] DayNames =
    [
        "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه"
    ];

    public static JalaliDate ToJalali(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return new JalaliDate(
            Calendar.GetYear(dateTime),
            Calendar.GetMonth(dateTime),
            Calendar.GetDayOfMonth(dateTime));
    }

    public static DateOnly ToGregorian(JalaliDate jalali)
    {
        if (jalali.Year < 1 || jalali.Month is < 1 or > 12 || jalali.Day < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(jalali), "تاریخ شمسی نامعتبر است.");
        }

        if (jalali.Day > Calendar.GetDaysInMonth(jalali.Year, jalali.Month))
        {
            throw new ArgumentOutOfRangeException(nameof(jalali), "روز در این ماه شمسی وجود ندارد.");
        }

        var dateTime = Calendar.ToDateTime(jalali.Year, jalali.Month, jalali.Day, 0, 0, 0, 0);
        return DateOnly.FromDateTime(dateTime);
    }

    public static string MonthName(int month)
        => month is >= 1 and <= 12 ? MonthNames[month - 1] : string.Empty;

    /// <summary>Persian week starts on Saturday, so the index is shifted accordingly.</summary>
    public static string DayName(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Saturday => "شنبه",
        DayOfWeek.Sunday => "یکشنبه",
        DayOfWeek.Monday => "دوشنبه",
        DayOfWeek.Tuesday => "سه‌شنبه",
        DayOfWeek.Wednesday => "چهارشنبه",
        DayOfWeek.Thursday => "پنجشنبه",
        DayOfWeek.Friday => "جمعه",
        _ => string.Empty
    };

    public static int DaysInMonth(int year, int month) => Calendar.GetDaysInMonth(year, month);
}
