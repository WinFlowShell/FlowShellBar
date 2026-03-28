using FlowShellBar.App.Diagnostics;

namespace FlowShellBar.App.Integrations;

public sealed class FlowtileWmAdapterStub : IFlowtileWmAdapter
{
    private readonly IAppLogger _logger;

    public FlowtileWmAdapterStub(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("FlowtileWM adapter stub initialized. Connection skipped.");
        return Task.FromResult(false);
    }
}
