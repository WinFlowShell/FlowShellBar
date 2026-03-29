namespace FlowShellBar.App.Integrations;

public interface IFlowtileWmAdapter
{
    Task<bool> TryConnectAsync(CancellationToken cancellationToken = default);

    Task<FlowtileBarProjection?> ReadProjectionAsync(CancellationToken cancellationToken = default);

    Task<bool> SwitchWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default);

    Task<bool> ToggleOverviewAsync(CancellationToken cancellationToken = default);
}
