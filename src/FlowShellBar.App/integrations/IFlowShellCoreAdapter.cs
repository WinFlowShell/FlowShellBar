namespace FlowShellBar.App.Integrations;

public interface IFlowShellCoreAdapter
{
    Task<bool> TryConnectAsync(CancellationToken cancellationToken = default);

    Task<FlowShellCoreSessionProjection?> ReadSessionProjectionAsync(CancellationToken cancellationToken = default);
}
