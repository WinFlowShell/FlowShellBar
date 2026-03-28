using FlowShellBar.App.Application;
using FlowShellBar.App.Application.Models;
using FlowShellBar.App.Diagnostics;

namespace FlowShellBar.App.Integrations;

public sealed class MockBarModelProvider : IBarModelProvider, IBarModelMutator, IAsyncDisposable
{
    private readonly IFlowShellCoreAdapter _flowShellCoreAdapter;
    private readonly IFlowtileWmAdapter _flowtileWmAdapter;
    private readonly ISystemMetricsAdapter _systemMetricsAdapter;
    private readonly IAppLogger _logger;
    private readonly object _modelLock = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private PeriodicTimer? _timer;
    private Task? _backgroundLoop;
    private BarModel _currentModel;
    private int _tickCount;

    public MockBarModelProvider(
        IFlowShellCoreAdapter flowShellCoreAdapter,
        IFlowtileWmAdapter flowtileWmAdapter,
        ISystemMetricsAdapter systemMetricsAdapter,
        IAppLogger logger)
    {
        _flowShellCoreAdapter = flowShellCoreAdapter;
        _flowtileWmAdapter = flowtileWmAdapter;
        _systemMetricsAdapter = systemMetricsAdapter;
        _logger = logger;

        _currentModel = BuildModel(activeWorkspaceId: 2);
    }

    public event EventHandler<BarModel>? ModelChanged;

    public BarModel GetCurrentModel()
    {
        lock (_modelLock)
        {
            return _currentModel;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Mock bar model provider started.");
        _logger.Info($"Adapter state: FlowShellCore={_flowShellCoreAdapter is not null}, FlowtileWM={_flowtileWmAdapter is not null}.");

        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _backgroundLoop = Task.Run(RunAsync, _cancellationTokenSource.Token);

        RaiseModelChanged();
        return Task.CompletedTask;
    }

    public Task SetActiveWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        if (workspaceId < 1 || workspaceId > 5)
        {
            _logger.Warning($"Mock workspace id out of range: {workspaceId}.");
            return Task.CompletedTask;
        }

        lock (_modelLock)
        {
            _currentModel = BuildModel(workspaceId);
        }

        RaiseModelChanged();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();

        if (_timer is not null)
        {
            _timer.Dispose();
        }

        if (_backgroundLoop is not null)
        {
            try
            {
                await _backgroundLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task RunAsync()
    {
        while (_timer is not null && await _timer.WaitForNextTickAsync(_cancellationTokenSource.Token))
        {
            _tickCount++;

            var activeWorkspaceId = GetCurrentModel().Workspaces.First(x => x.IsActive).Id;
            lock (_modelLock)
            {
                _currentModel = BuildModel(activeWorkspaceId);
            }

            RaiseModelChanged();
        }
    }

    private void RaiseModelChanged()
    {
        ModelChanged?.Invoke(this, GetCurrentModel());
    }

    private BarModel BuildModel(int activeWorkspaceId)
    {
        var hasNotifications = _tickCount % 12 >= 6;
        var activeWindowTitle = activeWorkspaceId switch
        {
            1 => "FlowShell Launcher Contract",
            2 => "FlowShellBar Runtime Skeleton",
            3 => "FlowtileWM Adapter Stub",
            4 => "Standalone Diagnostics View",
            _ => "Requirements Review Session",
        };

        var workspaces = Enumerable
            .Range(1, 5)
            .Select(index => new WorkspaceModel(
                Id: index,
                Label: index.ToString(),
                IsActive: index == activeWorkspaceId,
                IsOccupied: index <= 4))
            .ToArray();

        return new BarModel(
            RuntimeMode: RuntimeMode.Standalone,
            Workspaces: workspaces,
            ActiveWindowAppName: "FlowShellBar",
            ActiveWindowTitle: activeWindowTitle,
            CurrentTime: DateTime.Now.ToString("HH:mm"),
            CurrentDate: DateTime.Now.ToString("ddd, dd MMM"),
            StatusCluster: new StatusClusterModel(
                IsNetworkAvailable: _systemMetricsAdapter.IsNetworkAvailable,
                IsAudioAvailable: _systemMetricsAdapter.IsAudioAvailable,
                HasNotifications: hasNotifications));
    }
}
