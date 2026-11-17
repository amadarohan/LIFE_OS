using LifeOS.Application.Abstractions.Ai;
using LifeOS.Application.UseCases.Settings;
using LifeOS.Domain.Enums;

namespace LifeOS.Infrastructure.Ai;

/// <summary>
/// Reads provider configuration from the settings table on every call. Settings are user-editable
/// at runtime, so caching them here would mean a stale endpoint or API key after a change. The
/// query is a single indexed lookup on a tiny table — cheap enough to repeat per request.
/// </summary>
public sealed class SettingsAiProviderOptionsProvider : IAiProviderOptionsProvider
{
    private const string LocalProviderId = "local";
    private const string CloudProviderId = "cloud";

    private readonly SettingsService _settings;

    public SettingsAiProviderOptionsProvider(SettingsService settings) => _settings = settings;

    public async Task<IReadOnlyList<AiProviderOptions>> GetAsync(CancellationToken cancellationToken)
    {
        var timeoutSeconds = await _settings.GetValueAsync(
            SettingKeys.AiRequestTimeoutSeconds,
            120,
            cancellationToken);

        var allowSensitiveToCloud = await _settings.GetValueAsync(
            SettingKeys.AiAllowSensitiveToCloud,
            false,
            cancellationToken);

        var options = new List<AiProviderOptions>(2);

        var localEndpoint = await _settings.GetValueAsync(SettingKeys.AiLocalEndpoint, cancellationToken);
        var localModel = await _settings.GetValueAsync(SettingKeys.AiLocalModel, cancellationToken);

        if (!string.IsNullOrWhiteSpace(localEndpoint) && !string.IsNullOrWhiteSpace(localModel))
        {
            options.Add(new AiProviderOptions(
                LocalProviderId,
                AiProviderKind.Local,
                localEndpoint.Trim(),
                localModel.Trim(),
                null,
                timeoutSeconds,
                AllowSensitiveData: true));
        }

        var cloudEndpoint = await _settings.GetValueAsync(SettingKeys.AiCloudEndpoint, cancellationToken);
        var cloudModel = await _settings.GetValueAsync(SettingKeys.AiCloudModel, cancellationToken);
        var cloudApiKey = await _settings.GetValueAsync(SettingKeys.AiCloudApiKey, cancellationToken);

        if (!string.IsNullOrWhiteSpace(cloudEndpoint) && !string.IsNullOrWhiteSpace(cloudModel))
        {
            options.Add(new AiProviderOptions(
                CloudProviderId,
                AiProviderKind.Cloud,
                cloudEndpoint.Trim(),
                cloudModel.Trim(),
                string.IsNullOrWhiteSpace(cloudApiKey) ? null : cloudApiKey,
                timeoutSeconds,
                allowSensitiveToCloud));
        }

        return options;
    }
}
