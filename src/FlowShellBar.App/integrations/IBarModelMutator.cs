namespace FlowShellBar.App.Integrations;

public interface IBarModelMutator
{
    Task SetActiveWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default);
}
