using FlowShellBar.App.Application.Actions;
using FlowShellBar.App.Application.ViewModels;
using FlowShellBar.App.Diagnostics;
using FlowShellBar.App.Integrations;

using Microsoft.UI.Dispatching;

namespace FlowShellBar.App.Host;

public sealed class AppBootstrapper : IAsyncDisposable
{
    private BarRuntimeModelProvider? _barModelProvider;

    public IAppLogger Logger { get; private set; } = new FileAppLogger();

    public async Task<BootstrapContext> InitializeAsync(DispatcherQueue dispatcherQueue)
    {
        Logger.Info("Bootstrapping FlowShellBar.App.");

        var flowShellCoreAdapter = new FlowShellCoreIpcAdapter(Logger);
        var flowtileWmAdapter = new FlowtileWmIpcAdapter(Logger);
        var systemMetricsAdapter = new WindowsSystemMetricsAdapter(Logger);

        await flowShellCoreAdapter.TryConnectAsync();
        await flowtileWmAdapter.TryConnectAsync();

        _barModelProvider = new BarRuntimeModelProvider(
            flowShellCoreAdapter,
            flowtileWmAdapter,
            systemMetricsAdapter,
            Logger);

        var actionDispatcher = new BarActionDispatcher(_barModelProvider, Logger);
        var viewModel = new BarViewModel(dispatcherQueue, _barModelProvider, actionDispatcher, Logger);

        await _barModelProvider.StartAsync();

        Logger.Info("Bootstrap completed.");

        return new BootstrapContext(viewModel, actionDispatcher, Logger);
    }

    public async ValueTask DisposeAsync()
    {
        if (_barModelProvider is not null)
        {
            await _barModelProvider.DisposeAsync();
            _barModelProvider = null;
        }

        Logger.Info("FlowShellBar.App stopped.");
        await Logger.DisposeAsync();
    }
}
