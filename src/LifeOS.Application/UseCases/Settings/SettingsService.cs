using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.System;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Settings;

public sealed record SettingView(
    Guid Id,
    string Key,
    string Value,
    string ValueType,
    string? Description,
    bool IsSensitive,
    bool HasValue);

/// <summary>
/// Well-known setting keys. Keeping them in one place prevents typo-driven silent misconfiguration.
/// </summary>
public static class SettingKeys
{
    public const string AiActiveProvider = "ai.active-provider";
    public const string AiLocalEndpoint = "ai.local.endpoint";
    public const string AiLocalModel = "ai.local.model";
    public const string AiCloudEndpoint = "ai.cloud.endpoint";
    public const string AiCloudModel = "ai.cloud.model";
    public const string AiCloudApiKey = "ai.cloud.api-key";
    public const string AiAllowSensitiveToCloud = "ai.cloud.allow-sensitive";
    public const string AiMaxContextCharacters = "ai.context.max-characters";
    public const string AiProposalExpiryMinutes = "ai.proposal.expiry-minutes";
    public const string AiRequestTimeoutSeconds = "ai.request.timeout-seconds";

    public const string PlanningWeeklyCapacityHours = "planning.weekly-capacity-hours";
    public const string PlanningBufferRatio = "planning.buffer-ratio";
    public const string PlanningNearCapacityThreshold = "planning.near-capacity-threshold";

    public const string FinanceDefaultCurrency = "finance.default-currency";
    public const string FinanceMonthStartDay = "finance.month-start-day";

    public const string UiTimeZone = "ui.time-zone";
    public const string UiCalendar = "ui.calendar";

    public const string BackupDirectory = "backup.directory";
    public const string BackupRetentionCount = "backup.retention-count";
}

/// <summary>
/// Application settings. Sensitive values are encrypted at rest through
/// <see cref="ISecretProtector"/> and are never returned in clear text by the list API.
/// </summary>
public sealed class SettingsService
{
    private const string MaskedValue = "••••••••";

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public SettingsService(
        IApplicationDbContext db,
        ISecretProtector protector,
        IAuditWriter audit,
        IClock clock)
    {
        _db = db;
        _protector = protector;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SettingView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _db.ToListAsync(
            _db.AppSettings.OrderBy(s => s.Key),
            cancellationToken);

        return settings
            .Select(s => new SettingView(
                s.Id,
                s.Key,
                s.IsSensitive && !string.IsNullOrEmpty(s.Value) ? MaskedValue : s.Value,
                s.ValueType,
                s.Description,
                s.IsSensitive,
                !string.IsNullOrEmpty(s.Value)))
            .ToList();
    }

    /// <summary>Reads a setting and decrypts it when sensitive. Returns null when absent.</summary>
    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _db.FirstOrDefaultAsync(
            _db.AppSettings.Where(s => s.Key == key),
            cancellationToken);

        if (setting is null)
        {
            return null;
        }

        return setting.IsSensitive ? _protector.Unprotect(setting.Value) : setting.Value;
    }

    public async Task<T> GetValueAsync<T>(string key, T fallback, CancellationToken cancellationToken = default)
    {
        var raw = await GetValueAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        try
        {
            return (T)Convert.ChangeType(raw, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return fallback;
        }
    }

    public Task<Result> SetValueAsync(
        string key,
        string value,
        string valueType = "string",
        string? description = null,
        bool isSensitive = false,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new DomainRuleException("AppSetting.KeyRequired", "کلید تنظیمات الزامی است.");
            }

            var stored = isSensitive ? _protector.Protect(value) : value;

            var setting = await _db.FirstOrDefaultAsync(
                _db.AppSettings.Where(s => s.Key == key),
                cancellationToken);

            if (setting is null)
            {
                setting = new AppSetting(key.Trim(), stored, valueType, description, isSensitive);
                _db.Add(setting);
            }
            else
            {
                setting.SetValue(stored);
                setting.SetDescription(description ?? setting.Description);
            }

            await _audit.WriteAsync(
                nameof(AppSetting),
                setting.Id,
                AuditActionType.Updated,
                null,
                isSensitive ? null : $"{{\"{key}\":\"{value}\"}}",
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var setting = await _db.FirstOrDefaultAsync(
                _db.AppSettings.Where(s => s.Key == key),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(AppSetting), Guid.Empty);

            _db.Remove(setting);
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>
    /// Seeds the defaults the application needs to boot. Existing values are never overwritten,
    /// so a user's configuration survives upgrades.
    /// </summary>
    public Task<Result<int>> EnsureDefaultsAsync(CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var existing = await _db.ToListAsync(
                _db.AppSettings.Select(s => s.Key),
                cancellationToken);

            var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var defaults = new (string Key, string Value, string Type, string Description, bool Sensitive)[]
            {
                (SettingKeys.AiActiveProvider, "local", "string", "ارائه‌دهنده فعال هوش مصنوعی (local یا cloud)", false),
                (SettingKeys.AiLocalEndpoint, "http://localhost:11434", "string", "آدرس سرویس محلی Ollama", false),
                (SettingKeys.AiLocalModel, "llama3.1", "string", "مدل محلی پیش‌فرض", false),
                (SettingKeys.AiCloudEndpoint, string.Empty, "string", "آدرس سرویس ابری سازگار با OpenAI", false),
                (SettingKeys.AiCloudModel, string.Empty, "string", "مدل ابری پیش‌فرض", false),
                (SettingKeys.AiCloudApiKey, string.Empty, "string", "کلید API سرویس ابری", true),
                (SettingKeys.AiAllowSensitiveToCloud, "false", "bool", "اجازه ارسال داده حساس به سرویس ابری", false),
                (SettingKeys.AiMaxContextCharacters, "6000", "int", "حداکثر حجم زمینه ارسالی به مدل", false),
                (SettingKeys.AiProposalExpiryMinutes, "1440", "int", "مدت اعتبار پیشنهادهای هوش مصنوعی", false),
                (SettingKeys.AiRequestTimeoutSeconds, "120", "int", "مهلت پاسخ سرویس هوش مصنوعی به ثانیه", false),
                (SettingKeys.PlanningWeeklyCapacityHours, "40", "decimal", "ظرفیت هفتگی واقعی به ساعت", false),
                (SettingKeys.PlanningBufferRatio, "0.15", "decimal", "نسبت بافر ظرفیت", false),
                (SettingKeys.PlanningNearCapacityThreshold, "85", "decimal", "آستانه هشدار نزدیک شدن به ظرفیت", false),
                (SettingKeys.FinanceDefaultCurrency, "IRR", "string", "واحد پول پیش‌فرض", false),
                (SettingKeys.FinanceMonthStartDay, "1", "int", "روز شروع ماه مالی", false),
                (SettingKeys.UiTimeZone, "Iran Standard Time", "string", "منطقه زمانی نمایش", false),
                (SettingKeys.UiCalendar, "jalali", "string", "تقویم نمایشی", false),
                (SettingKeys.BackupDirectory, "backups", "string", "پوشه پشتیبان‌گیری", false),
                (SettingKeys.BackupRetentionCount, "10", "int", "تعداد نسخه‌های پشتیبان نگهداری‌شده", false)
            };

            var added = 0;
            foreach (var (key, value, type, description, sensitive) in defaults)
            {
                if (known.Contains(key))
                {
                    continue;
                }

                var stored = sensitive && !string.IsNullOrEmpty(value)
                    ? _protector.Protect(value)
                    : value;

                _db.Add(new AppSetting(key, stored, type, description, sensitive));
                added++;
            }

            if (added > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Result.Success(added);
        });
}
