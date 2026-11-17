using System.Collections.Concurrent;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Abstractions.Ai;
using LifeOS.Domain.Enums;

namespace LifeOS.Infrastructure.Ai;

/// <summary>
/// Tracks recent provider failures so the router can skip a provider that just failed instead of
/// making every request wait for the same timeout. In-memory by design: this is a single-user
/// local app, and a stale health flag must never outlive the process.
/// </summary>
public sealed class AiProviderHealthTracker : IAiProviderHealthTracker
{
    private static readonly TimeSpan FailureWindow = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastFailure = new(StringComparer.OrdinalIgnoreCase);

    public void RecordFailure(string providerId, DateTimeOffset now)
        => _lastFailure[providerId] = now;

    public void RecordSuccess(string providerId)
        => _lastFailure.TryRemove(providerId, out _);

    public bool IsHealthy(string providerId, DateTimeOffset now)
        => !_lastFailure.TryGetValue(providerId, out var failedAt) || now - failedAt > FailureWindow;
}

/// <summary>
/// Resolves which provider should serve a request. The order is deliberate: an explicit override
/// wins, then the privacy gate, then category preference, then health, then fallback. The router
/// never silently downgrades privacy — if nothing survives the gate it fails loudly.
/// </summary>
public sealed class AiProviderRouter : IAiProviderRouter
{
    private readonly IAiProviderOptionsProvider _optionsProvider;
    private readonly IAiProviderHealthTracker _health;
    private readonly IClock _clock;

    public AiProviderRouter(
        IAiProviderOptionsProvider optionsProvider,
        IAiProviderHealthTracker health,
        IClock clock)
    {
        _optionsProvider = optionsProvider;
        _health = health;
        _clock = clock;
    }

    public async Task<AiRoutingDecision> ResolveAsync(
        AiTaskCategory category,
        SensitivityLevel sensitivity,
        string? explicitProviderId,
        CancellationToken cancellationToken)
    {
        var providers = await _optionsProvider.GetAsync(cancellationToken);
        var now = _clock.UtcNow;
        var rejected = new List<string>();

        if (providers.Count == 0)
        {
            return new AiRoutingDecision(
                false,
                null,
                "هیچ سرویس هوش مصنوعی تنظیم نشده است. در تنظیمات، مدل محلی یا ابری را پیکربندی کنید.",
                rejected);
        }

        // 1. Explicit override still has to pass the privacy gate.
        if (!string.IsNullOrWhiteSpace(explicitProviderId))
        {
            var explicitProvider = providers.FirstOrDefault(p =>
                string.Equals(p.ProviderId, explicitProviderId, StringComparison.OrdinalIgnoreCase));

            if (explicitProvider is null)
            {
                rejected.Add($"{explicitProviderId} (ناشناخته)");
            }
            else if (!PassesPrivacyGate(explicitProvider, sensitivity))
            {
                rejected.Add($"{explicitProvider.ProviderId} (مجاز به داده حساس نیست)");
            }
            else
            {
                return new AiRoutingDecision(
                    true,
                    explicitProvider,
                    "سرویس انتخابی کاربر.",
                    rejected);
            }
        }

        // 2. Privacy gate: sensitive payloads may only go to a provider authorized for them.
        var candidates = new List<AiProviderOptions>();
        foreach (var provider in providers)
        {
            if (PassesPrivacyGate(provider, sensitivity))
            {
                candidates.Add(provider);
            }
            else
            {
                rejected.Add($"{provider.ProviderId} (مجاز به داده حساس نیست)");
            }
        }

        if (candidates.Count == 0)
        {
            return new AiRoutingDecision(
                false,
                null,
                "این درخواست شامل داده حساس است و هیچ سرویس مجازی برای پردازش آن تنظیم نشده است. " +
                "یک مدل محلی پیکربندی کنید یا اجازه ارسال داده حساس به سرویس ابری را فعال کنید.",
                rejected);
        }

        // 3. Category preference.
        var preferredKind = PreferredKind(category);

        var preferred = candidates
            .Where(p => p.Kind == preferredKind)
            .OrderBy(p => p.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 4. Health: skip providers that failed within the window.
        var healthyPreferred = preferred.Where(p => _health.IsHealthy(p.ProviderId, now)).ToList();
        if (healthyPreferred.Count > 0)
        {
            return new AiRoutingDecision(
                true,
                healthyPreferred[0],
                $"سرویس ترجیحی برای دسته «{CategoryLabel(category)}».",
                rejected);
        }

        foreach (var provider in preferred)
        {
            rejected.Add($"{provider.ProviderId} (اخیراً ناموفق بود)");
        }

        // 5. Fallback, still inside the privacy gate.
        var healthyFallback = candidates
            .Where(p => _health.IsHealthy(p.ProviderId, now))
            .OrderBy(p => p.Kind == AiProviderKind.Local ? 0 : 1)
            .ThenBy(p => p.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (healthyFallback.Count > 0)
        {
            return new AiRoutingDecision(
                true,
                healthyFallback[0],
                "سرویس جانشین پس از ناموفق بودن سرویس ترجیحی.",
                rejected);
        }

        return new AiRoutingDecision(
            false,
            null,
            "همه سرویس‌های هوش مصنوعی در دسترس نیستند. برنامه بدون هوش مصنوعی کار می‌کند.",
            rejected);
    }

    /// <summary>
    /// Sensitive data may only reach a provider that explicitly allows it. Local providers are
    /// treated as allowed because the data never leaves the machine.
    /// </summary>
    private static bool PassesPrivacyGate(AiProviderOptions provider, SensitivityLevel sensitivity)
        => sensitivity != SensitivityLevel.Sensitive
           || provider.Kind == AiProviderKind.Local
           || provider.AllowSensitiveData;

    private static AiProviderKind PreferredKind(AiTaskCategory category) => category switch
    {
        AiTaskCategory.SimpleClassification => AiProviderKind.Local,
        AiTaskCategory.Extraction => AiProviderKind.Local,
        AiTaskCategory.Summarization => AiProviderKind.Local,
        AiTaskCategory.SensitiveAnalysis => AiProviderKind.Local,
        AiTaskCategory.StrategicAnalysis => AiProviderKind.Cloud,
        AiTaskCategory.ScenarioEvaluation => AiProviderKind.Cloud,
        _ => AiProviderKind.Local
    };

    private static string CategoryLabel(AiTaskCategory category) => category switch
    {
        AiTaskCategory.SimpleClassification => "دسته‌بندی ساده",
        AiTaskCategory.Extraction => "استخراج اطلاعات",
        AiTaskCategory.Summarization => "خلاصه‌سازی",
        AiTaskCategory.SensitiveAnalysis => "تحلیل حساس",
        AiTaskCategory.StrategicAnalysis => "تحلیل راهبردی",
        AiTaskCategory.ScenarioEvaluation => "ارزیابی سناریو",
        _ => category.ToString()
    };
}
