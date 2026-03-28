using FlowShellBar.App.Diagnostics;

namespace FlowShellBar.App.Integrations;

public sealed class FlowShellCoreAdapterStub : IFlowShellCoreAdapter
{
    private readonly IAppLogger _logger;

    public FlowShellCoreAdapterStub(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("FlowShellCore adapter stub initialized. Connection skipped.");
        return Task.FromResult(false);
    }
}
