namespace LifeOS.Application.Abstractions.Ai;

/// <summary>
/// Supplies the currently configured providers. Options live in the settings table, not in
/// appsettings, so the user can change endpoint, model or API key without a restart. Resolving them
/// through this abstraction keeps the router free of persistence concerns and testable.
/// </summary>
public interface IAiProviderOptionsProvider
{
    Task<IReadOnlyList<AiProviderOptions>> GetAsync(CancellationToken cancellationToken);
}
