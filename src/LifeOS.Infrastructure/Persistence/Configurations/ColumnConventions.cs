using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared column conventions so every table uses the same precision and length rules.
/// Keeping them in one place prevents drift between modules.
/// </summary>
internal static class ColumnConventions
{
    public const int NameLength = 200;
    public const int ShortTextLength = 500;
    public const int MediumTextLength = 2000;
    public const int EnumLength = 40;
    public const int CurrencyLength = 3;
    public const int ColorLength = 9;
    public const int IconLength = 60;
    public const int EntityTypeLength = 100;
    public const int KeyLength = 200;

    /// <summary>Money: <c>decimal(18,2)</c>.</summary>
    public static PropertyBuilder<decimal> Money(this PropertyBuilder<decimal> property)
        => property.HasPrecision(18, 2);

    public static PropertyBuilder<decimal?> Money(this PropertyBuilder<decimal?> property)
        => property.HasPrecision(18, 2);

    /// <summary>Percentages in 0..100: <c>decimal(5,2)</c>.</summary>
    public static PropertyBuilder<decimal> Percentage(this PropertyBuilder<decimal> property)
        => property.HasPrecision(5, 2);

    public static PropertyBuilder<decimal?> Percentage(this PropertyBuilder<decimal?> property)
        => property.HasPrecision(5, 2);

    /// <summary>Hours: <c>decimal(9,2)</c>.</summary>
    public static PropertyBuilder<decimal> Hours(this PropertyBuilder<decimal> property)
        => property.HasPrecision(9, 2);

    public static PropertyBuilder<decimal?> Hours(this PropertyBuilder<decimal?> property)
        => property.HasPrecision(9, 2);

    /// <summary>Confidence in 0..1: <c>decimal(3,2)</c>.</summary>
    public static PropertyBuilder<decimal> Confidence(this PropertyBuilder<decimal> property)
        => property.HasPrecision(3, 2);

    /// <summary>UTC timestamps are stored at second precision.</summary>
    public static PropertyBuilder<DateTimeOffset> UtcTimestamp(this PropertyBuilder<DateTimeOffset> property)
        => property.HasPrecision(0);

    public static PropertyBuilder<DateTimeOffset?> UtcTimestamp(this PropertyBuilder<DateTimeOffset?> property)
        => property.HasPrecision(0);

    /// <summary>
    /// Enums are stored as strings. The database stays readable during debugging and adding a
    /// new member cannot silently shift the meaning of existing rows.
    /// </summary>
    public static PropertyBuilder<TEnum> EnumAsString<TEnum>(this PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum
        => property.HasConversion<string>().HasMaxLength(EnumLength);

    public static PropertyBuilder<TEnum?> EnumAsString<TEnum>(this PropertyBuilder<TEnum?> property)
        where TEnum : struct, Enum
        => property.HasConversion<string>().HasMaxLength(EnumLength);
}
