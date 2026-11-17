namespace LifeOS.Application.Abstractions.Ai;

/// <summary>
/// Tracks recent provider failures so the router can skip a provider that just failed instead of
/// making every request wait for a timeout. The window is short (60 s) so recovery is automatic.
/// </summary>
public interface IAiProviderHealthTracker
{
    void RecordFailure(string providerId, DateTimeOffset now);

    void RecordSuccess(string providerId);

    bool IsHealthy(string providerId, DateTimeOffset now);
}
