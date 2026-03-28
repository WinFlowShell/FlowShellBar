namespace FlowShellBar.App.Integrations;

public interface IFlowtileWmAdapter
{
    Task<bool> TryConnectAsync(CancellationToken cancellationToken = default);
}
