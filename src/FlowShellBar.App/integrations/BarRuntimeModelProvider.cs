using FlowShellBar.App.Application;
using FlowShellBar.App.Application.Models;
using FlowShellBar.App.Diagnostics;

namespace FlowShellBar.App.Integrations;

public sealed class BarRuntimeModelProvider : IBarModelProvider, IBarModelMutator, IAsyncDisposable
{
    private readonly IFlowShellCoreAdapter _flowShellCoreAdapter;
    private readonly IFlowtileWmAdapter _flowtileWmAdapter;
    private readonly ISystemMetricsAdapter _systemMetricsAdapter;
    private readonly IAppLogger _logger;
    private readonly object _modelLock = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly TimeSpan _refreshInterval;
    private PeriodicTimer? _timer;
    private Task? _backgroundLoop;
    private BarModel _currentModel;
    private FlowtileBarProjection? _latestFlowtileProjection;
    private FlowShellCoreSessionProjection? _latestFlowShellCoreProjection;
    private int _fallbackActiveWorkspaceId = 2;
    private int _tickCount;

    public BarRuntimeModelProvider(
        IFlowShellCoreAdapter flowShellCoreAdapter,
        IFlowtileWmAdapter flowtileWmAdapter,
        ISystemMetricsAdapter systemMetricsAdapter,
        IAppLogger logger)
    {
        _flowShellCoreAdapter = flowShellCoreAdapter;
        _flowtileWmAdapter = flowtileWmAdapter;
        _systemMetricsAdapter = systemMetricsAdapter;
        _logger = logger;
        _refreshInterval = TimeSpan.FromMilliseconds(GetEnvironmentInt("FLOWSHELL_BAR_REFRESH_INTERVAL_MS", 1000));
        _currentModel = BuildModel(null, null);
    }

    public event EventHandler<BarModel>? ModelChanged;

    public BarModel GetCurrentModel()
    {
        lock (_modelLock)
        {
            return _currentModel;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Bar runtime model provider started.");

        await RefreshModelAsync(cancellationToken);

        _timer = new PeriodicTimer(_refreshInterval);
        _backgroundLoop = Task.Run(() => RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
    }

    public async Task SetActiveWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        if (workspaceId < 1)
        {
            _logger.Warning($"Workspace id is out of range: {workspaceId}.");
            return;
        }

        if (await _flowtileWmAdapter.SwitchWorkspaceAsync(workspaceId, cancellationToken))
        {
            _logger.Info($"Workspace switch routed through FlowtileWM: {workspaceId}.");
            await RefreshModelAsync(cancellationToken);
            return;
        }

        _logger.Warning($"Falling back to local workspace mutation for workspace {workspaceId}.");
        _fallbackActiveWorkspaceId = Math.Clamp(workspaceId, 1, 5);
        UpdateModel(BuildModel(null, _latestFlowShellCoreProjection));
    }

    public async Task ToggleOverviewAsync(CancellationToken cancellationToken = default)
    {
        if (await _flowtileWmAdapter.ToggleOverviewAsync(cancellationToken))
        {
            _logger.Info("Overview toggle routed through FlowtileWM.");
            await RefreshModelAsync(cancellationToken);
            return;
        }

        _logger.Info("Overview toggle remains diagnostic stub because FlowtileWM is unavailable.");
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

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_timer is not null && await _timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshModelAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshModelAsync(CancellationToken cancellationToken)
    {
        _tickCount++;

        var flowtileTask = RefreshFlowtileProjectionAsync(cancellationToken);
        var flowShellCoreTask = RefreshFlowShellCoreProjectionAsync(cancellationToken);

        await Task.WhenAll(flowtileTask, flowShellCoreTask);

        _latestFlowtileProjection = await flowtileTask;
        _latestFlowShellCoreProjection = await flowShellCoreTask;

        UpdateModel(BuildModel(_latestFlowtileProjection, _latestFlowShellCoreProjection));
    }

    private async Task<FlowtileBarProjection?> RefreshFlowtileProjectionAsync(CancellationToken cancellationToken)
    {
        var connected = await _flowtileWmAdapter.TryConnectAsync(cancellationToken);
        return connected
            ? await _flowtileWmAdapter.ReadProjectionAsync(cancellationToken)
            : null;
    }

    private async Task<FlowShellCoreSessionProjection?> RefreshFlowShellCoreProjectionAsync(CancellationToken cancellationToken)
    {
        var connected = await _flowShellCoreAdapter.TryConnectAsync(cancellationToken);
        return connected
            ? await _flowShellCoreAdapter.ReadSessionProjectionAsync(cancellationToken)
            : null;
    }

    private void UpdateModel(BarModel model)
    {
        BarModel previousModel;
        lock (_modelLock)
        {
            previousModel = _currentModel;
            _currentModel = model;
        }

        if (previousModel.ConnectionState != model.ConnectionState)
        {
            _logger.Info($"Bar connection state changed: {model.ConnectionState.ToString().ToLowerInvariant()}.");
        }

        ModelChanged?.Invoke(this, model);
    }

    private BarModel BuildModel(
        FlowtileBarProjection? flowtileProjection,
        FlowShellCoreSessionProjection? flowShellCoreProjection)
    {
        var runtimeMode = flowShellCoreProjection is null
            ? RuntimeMode.Standalone
            : RuntimeMode.Session;

        var connectionState = ComputeConnectionState(flowtileProjection, flowShellCoreProjection);
        var fallbackEnabled = flowtileProjection is null || flowShellCoreProjection is null;
        var activeWorkspaceId = flowtileProjection?.ActiveWorkspaceId ?? _fallbackActiveWorkspaceId;
        var hasNotifications = _tickCount % 12 >= 6;
        var surfacePlacement = flowtileProjection is null
            ? BarSurfacePlacementModel.Fallback
            : new BarSurfacePlacementModel(
                Source: BarSurfacePlacementSource.FlowtileWm,
                MonitorId: flowtileProjection.MonitorId,
                MonitorBinding: flowtileProjection.MonitorBinding,
                WorkAreaX: flowtileProjection.WorkArea?.X ?? 0,
                WorkAreaY: flowtileProjection.WorkArea?.Y ?? 0,
                WorkAreaWidth: flowtileProjection.WorkArea?.Width ?? 0,
                WorkAreaHeight: flowtileProjection.WorkArea?.Height ?? 0);

        var workspaces = flowtileProjection?.Workspaces
            .Select(workspace => new WorkspaceModel(
                Id: workspace.WorkspaceId,
                Label: workspace.Label,
                IsActive: workspace.IsActive,
                IsOccupied: workspace.IsOccupied))
            .ToArray()
            ?? BuildFallbackWorkspaces(activeWorkspaceId);
        var activeWorkspaceLabel = GetActiveWorkspaceDisplayLabel(workspaces, activeWorkspaceId);
        var resourceMetrics = new ResourceMetricsModel(
            MemoryUsagePercent: _systemMetricsAdapter.MemoryUsagePercent,
            CpuUsagePercent: _systemMetricsAdapter.CpuUsagePercent,
            TemperatureCelsius: _systemMetricsAdapter.TemperatureCelsius);

        return new BarModel(
            RuntimeMode: runtimeMode,
            ConnectionState: connectionState,
            Sources: new BarProjectionSourcesModel(
                FlowtileWm: flowtileProjection is null ? BarLiveSourceState.Disconnected : BarLiveSourceState.Connected,
                FlowShellCore: flowShellCoreProjection is null ? BarLiveSourceState.Disconnected : BarLiveSourceState.Connected,
                Fallback: fallbackEnabled ? BarFallbackState.Enabled : BarFallbackState.Disabled),
            SurfacePlacement: surfacePlacement,
            Workspaces: workspaces,
            ActiveWindowAppName: flowtileProjection?.ActiveWindowAppName ?? "FlowShellBar",
            ActiveWindowTitle: flowtileProjection?.ActiveWindowTitle ?? GetFallbackWindowTitle(activeWorkspaceId),
            ActiveWorkspaceLabel: activeWorkspaceLabel,
            ResourceMetrics: resourceMetrics,
            CurrentTime: DateTime.Now.ToString("HH:mm:ss"),
            CurrentDate: DateTime.Now.ToString("ddd, dd MMM"),
            StatusCluster: new StatusClusterModel(
                IsNetworkAvailable: _systemMetricsAdapter.IsNetworkAvailable,
                IsAudioAvailable: _systemMetricsAdapter.IsAudioAvailable,
                HasNotifications: hasNotifications));
    }

    private static BarConnectionState ComputeConnectionState(
        FlowtileBarProjection? flowtileProjection,
        FlowShellCoreSessionProjection? flowShellCoreProjection)
    {
        var hasFlowtile = flowtileProjection is not null;
        var hasFlowShellCore = flowShellCoreProjection is not null;

        if (!hasFlowtile && !hasFlowShellCore)
        {
            return BarConnectionState.Offline;
        }

        if (hasFlowtile
            && hasFlowShellCore
            && flowShellCoreProjection!.SessionMode == FlowShellCoreSessionMode.Normal
            && flowShellCoreProjection.IsBarReady)
        {
            return BarConnectionState.Live;
        }

        return BarConnectionState.Degraded;
    }

    private static WorkspaceModel[] BuildFallbackWorkspaces(int activeWorkspaceId)
    {
        var normalizedWorkspaceId = Math.Clamp(activeWorkspaceId, 1, 5);
        return Enumerable
            .Range(1, 5)
            .Select(index => new WorkspaceModel(
                Id: index,
                Label: index.ToString(),
                IsActive: index == normalizedWorkspaceId,
                IsOccupied: index <= 4))
            .ToArray();
    }

    private static string GetFallbackWindowTitle(int activeWorkspaceId)
    {
        return Math.Clamp(activeWorkspaceId, 1, 5) switch
        {
            1 => "FlowShell Launcher Contract",
            2 => "FlowShellBar Live Projection Baseline",
            3 => "FlowtileWM IPC Recovery Path",
            4 => "FlowShellCore Session Status",
            _ => "Requirements Review Session",
        };
    }

    private static string GetActiveWorkspaceDisplayLabel(IReadOnlyList<WorkspaceModel> workspaces, int activeWorkspaceId)
    {
        var label = workspaces
            .FirstOrDefault(workspace => workspace.IsActive)?.Label;

        if (string.IsNullOrWhiteSpace(label))
        {
            label = activeWorkspaceId.ToString();
        }

        return $"Workspace {label}";
    }

    private static int GetEnvironmentInt(string variableName, int fallback)
    {
        var rawValue = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(rawValue, out var value) ? value : fallback;
    }
}
