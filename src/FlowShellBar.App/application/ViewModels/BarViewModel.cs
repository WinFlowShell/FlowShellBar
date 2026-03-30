using System.Collections.ObjectModel;
using System.Windows.Input;

using FlowShellBar.App.Application;
using FlowShellBar.App.Application.Actions;
using FlowShellBar.App.Application.Models;
using FlowShellBar.App.Diagnostics;
using FlowShellBar.App.Integrations;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace FlowShellBar.App.Application.ViewModels;

public sealed class BarViewModel : BindableBase
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly IBarActionDispatcher _actionDispatcher;
    private readonly IAppLogger _logger;

    private string _activeWindowAppName = string.Empty;
    private string _activeWindowTitle = string.Empty;
    private string _activeWorkspaceLabel = string.Empty;
    private string _memoryUsageText = string.Empty;
    private string _cpuUsageText = string.Empty;
    private string _temperatureText = string.Empty;
    private string _currentTime = string.Empty;
    private string _currentDate = string.Empty;
    private string _runtimeModeLabel = string.Empty;
    private string _connectionStateLabel = string.Empty;
    private bool _isNetworkAvailable;
    private bool _isAudioAvailable;
    private bool _hasNotifications;
    private BarSurfacePlacementModel _surfacePlacement = BarSurfacePlacementModel.Fallback;
    private BarPanelSurfaceKind _activePanelSurface;
    private BarPopupSurfaceKind _activePopupSurface;
    private bool _isPopupPinned;
    private Brush _connectionStateBackground = CreateBrush("#202836");
    private Brush _connectionStateBorderBrush = CreateBrush("#303A4B");
    private Brush _connectionStateForeground = CreateBrush("#A1AEC4");

    public BarViewModel(
        DispatcherQueue dispatcherQueue,
        IBarModelProvider barModelProvider,
        IBarActionDispatcher actionDispatcher,
        IAppLogger logger)
    {
        _dispatcherQueue = dispatcherQueue;
        _actionDispatcher = actionDispatcher;
        _logger = logger;

        Workspaces = [];

        OpenLauncherCommand = new AsyncDelegateCommand(_ => _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.OpenLauncher)));
        OpenStatusPanelCommand = new AsyncDelegateCommand(_ => _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.OpenStatusPanel)));
        ToggleOverviewCommand = new AsyncDelegateCommand(_ => _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.ToggleOverview)));
        SwitchWorkspaceCommand = new AsyncDelegateCommand(async parameter =>
        {
            if (TryGetWorkspaceId(parameter, out var workspaceId))
            {
                await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.SwitchWorkspace, WorkspaceId: workspaceId));
            }
        });

        ApplyModel(barModelProvider.GetCurrentModel());
        barModelProvider.ModelChanged += OnModelChanged;
    }

    public ObservableCollection<WorkspaceItemViewModel> Workspaces { get; }

    public ICommand OpenLauncherCommand { get; }

    public ICommand OpenStatusPanelCommand { get; }

    public ICommand ToggleOverviewCommand { get; }

    public ICommand SwitchWorkspaceCommand { get; }

    public string ActiveWindowAppName
    {
        get => _activeWindowAppName;
        private set => SetProperty(ref _activeWindowAppName, value);
    }

    public string ActiveWindowTitle
    {
        get => _activeWindowTitle;
        private set => SetProperty(ref _activeWindowTitle, value);
    }

    public string ActiveWorkspaceLabel
    {
        get => _activeWorkspaceLabel;
        private set => SetProperty(ref _activeWorkspaceLabel, value);
    }

    public string MemoryUsageText
    {
        get => _memoryUsageText;
        private set => SetProperty(ref _memoryUsageText, value);
    }

    public string CpuUsageText
    {
        get => _cpuUsageText;
        private set => SetProperty(ref _cpuUsageText, value);
    }

    public string TemperatureText
    {
        get => _temperatureText;
        private set => SetProperty(ref _temperatureText, value);
    }

    public string CurrentTime
    {
        get => _currentTime;
        private set => SetProperty(ref _currentTime, value);
    }

    public string CurrentDate
    {
        get => _currentDate;
        private set => SetProperty(ref _currentDate, value);
    }

    public string RuntimeModeLabel
    {
        get => _runtimeModeLabel;
        private set
        {
            if (SetProperty(ref _runtimeModeLabel, value))
            {
                OnPropertyChanged(nameof(RuntimeModeShortLabel));
            }
        }
    }

    public string RuntimeModeShortLabel => RuntimeModeLabel switch
    {
        "SESSION" => "S",
        "STANDALONE" => "O",
        _ => RuntimeModeLabel.Length > 0 ? RuntimeModeLabel.Substring(0, 1) : string.Empty
    };

    public bool IsNetworkAvailable
    {
        get => _isNetworkAvailable;
        private set => SetProperty(ref _isNetworkAvailable, value);
    }

    public string ConnectionStateLabel
    {
        get => _connectionStateLabel;
        private set => SetProperty(ref _connectionStateLabel, value);
    }

    public Brush ConnectionStateBackground
    {
        get => _connectionStateBackground;
        private set => SetProperty(ref _connectionStateBackground, value);
    }

    public Brush ConnectionStateBorderBrush
    {
        get => _connectionStateBorderBrush;
        private set => SetProperty(ref _connectionStateBorderBrush, value);
    }

    public Brush ConnectionStateForeground
    {
        get => _connectionStateForeground;
        private set => SetProperty(ref _connectionStateForeground, value);
    }

    public bool IsAudioAvailable
    {
        get => _isAudioAvailable;
        private set => SetProperty(ref _isAudioAvailable, value);
    }

    public bool HasNotifications
    {
        get => _hasNotifications;
        private set => SetProperty(ref _hasNotifications, value);
    }

    public BarSurfacePlacementModel SurfacePlacement
    {
        get => _surfacePlacement;
        private set => SetProperty(ref _surfacePlacement, value);
    }

    public BarPanelSurfaceKind ActivePanelSurface
    {
        get => _activePanelSurface;
        private set
        {
            if (SetProperty(ref _activePanelSurface, value))
            {
                OnPropertyChanged(nameof(IsLeftPanelOpen));
                OnPropertyChanged(nameof(IsRightPanelOpen));
            }
        }
    }

    public bool IsLeftPanelOpen => ActivePanelSurface == BarPanelSurfaceKind.LeftSidebar;

    public bool IsRightPanelOpen => ActivePanelSurface == BarPanelSurfaceKind.RightSidebar;

    public BarPopupSurfaceKind ActivePopupSurface
    {
        get => _activePopupSurface;
        private set => SetProperty(ref _activePopupSurface, value);
    }

    public bool IsPopupPinned
    {
        get => _isPopupPinned;
        private set => SetProperty(ref _isPopupPinned, value);
    }

    public int VisibleWorkspaceCount => Workspaces.Count;

    public int OccupiedWorkspaceCount => Workspaces.Count(x => x.IsOccupied || x.IsActive);

    public string ActiveWorkspaceIdText
    {
        get
        {
            var activeWorkspace = Workspaces.FirstOrDefault(x => x.IsActive);
            return activeWorkspace is null ? "0" : activeWorkspace.Id.ToString();
        }
    }

    public void TogglePanel(BarPanelSurfaceKind panelKind)
    {
        if (panelKind == BarPanelSurfaceKind.None)
        {
            CloseAllTransientSurfaces();
            return;
        }

        if (ActivePanelSurface == panelKind)
        {
            ActivePanelSurface = BarPanelSurfaceKind.None;
            return;
        }

        ActivePopupSurface = BarPopupSurfaceKind.None;
        IsPopupPinned = false;
        ActivePanelSurface = panelKind;
    }

    public void ClosePanel(BarPanelSurfaceKind? onlyIfKind = null)
    {
        if (onlyIfKind is not null && ActivePanelSurface != onlyIfKind.Value)
        {
            return;
        }

        ActivePanelSurface = BarPanelSurfaceKind.None;
    }

    public void ShowPopup(BarPopupSurfaceKind popupKind, bool pinned)
    {
        if (popupKind == BarPopupSurfaceKind.None)
        {
            ClosePopup();
            return;
        }

        if (ActivePopupSurface == popupKind && IsPopupPinned && pinned)
        {
            ClosePopup();
            return;
        }

        if (ActivePanelSurface != BarPanelSurfaceKind.None)
        {
            ActivePanelSurface = BarPanelSurfaceKind.None;
        }

        ActivePopupSurface = popupKind;
        IsPopupPinned = pinned;
    }

    public void ClosePopup(BarPopupSurfaceKind? onlyIfKind = null)
    {
        if (onlyIfKind is not null && ActivePopupSurface != onlyIfKind.Value)
        {
            return;
        }

        ActivePopupSurface = BarPopupSurfaceKind.None;
        IsPopupPinned = false;
    }

    public void CloseTransientPopupIfNotPinned(BarPopupSurfaceKind popupKind)
    {
        if (ActivePopupSurface == popupKind && !IsPopupPinned)
        {
            ClosePopup();
        }
    }

    public void TogglePopup(BarPopupSurfaceKind popupKind)
    {
        if (ActivePopupSurface == popupKind && IsPopupPinned)
        {
            ClosePopup();
            return;
        }

        ShowPopup(popupKind, pinned: true);
    }

    public void CloseAllTransientSurfaces()
    {
        ActivePanelSurface = BarPanelSurfaceKind.None;
        ActivePopupSurface = BarPopupSurfaceKind.None;
        IsPopupPinned = false;
    }

    private void OnModelChanged(object? sender, BarModel model)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyModel(model);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyModel(model));
    }

    private void ApplyModel(BarModel model)
    {
        RuntimeModeLabel = model.RuntimeMode.ToString().ToUpperInvariant();
        ConnectionStateLabel = model.ConnectionState.ToString().ToUpperInvariant();
        ActiveWindowAppName = model.ActiveWindowAppName;
        ActiveWindowTitle = model.ActiveWindowTitle;
        ActiveWorkspaceLabel = model.ActiveWorkspaceLabel;
        MemoryUsageText = model.ResourceMetrics.MemoryUsagePercent.ToString();
        CpuUsageText = model.ResourceMetrics.CpuUsagePercent.ToString();
        TemperatureText = model.ResourceMetrics.TemperatureCelsius.ToString();
        CurrentTime = model.CurrentTime;
        CurrentDate = model.CurrentDate;
        SurfacePlacement = model.SurfacePlacement;
        IsNetworkAvailable = model.StatusCluster.IsNetworkAvailable;
        IsAudioAvailable = model.StatusCluster.IsAudioAvailable;
        HasNotifications = model.StatusCluster.HasNotifications;
        ApplyConnectionPalette(model.ConnectionState);

        SyncWorkspaces(model.Workspaces);
        _logger.Info($"Bar model applied. Active workspace: {model.Workspaces.FirstOrDefault(x => x.IsActive)?.Id ?? -1}; visible workspaces: {model.Workspaces.Count}.");
    }

    private void ApplyConnectionPalette(BarConnectionState connectionState)
    {
        switch (connectionState)
        {
            case BarConnectionState.Live:
                ConnectionStateBackground = CreateBrush("#183426");
                ConnectionStateBorderBrush = CreateBrush("#2D7050");
                ConnectionStateForeground = CreateBrush("#CBF7DC");
                break;

            case BarConnectionState.Degraded:
                ConnectionStateBackground = CreateBrush("#3A2C13");
                ConnectionStateBorderBrush = CreateBrush("#8A6B23");
                ConnectionStateForeground = CreateBrush("#FFE8A3");
                break;

            default:
                ConnectionStateBackground = CreateBrush("#342322");
                ConnectionStateBorderBrush = CreateBrush("#7A4741");
                ConnectionStateForeground = CreateBrush("#FFD8D3");
                break;
        }
    }

    private void SyncWorkspaces(IReadOnlyList<WorkspaceModel> workspaces)
    {
        while (Workspaces.Count < workspaces.Count)
        {
            Workspaces.Add(new WorkspaceItemViewModel());
        }

        while (Workspaces.Count > workspaces.Count)
        {
            Workspaces.RemoveAt(Workspaces.Count - 1);
        }

        for (var index = 0; index < workspaces.Count; index++)
        {
            var source = workspaces[index];
            var target = Workspaces[index];

            target.Id = source.Id;
            target.Label = source.Label;
            target.IsActive = source.IsActive;
            target.IsOccupied = source.IsOccupied;
        }

        OnPropertyChanged(nameof(VisibleWorkspaceCount));
        OnPropertyChanged(nameof(OccupiedWorkspaceCount));
        OnPropertyChanged(nameof(ActiveWorkspaceIdText));
    }

    private static bool TryGetWorkspaceId(object? parameter, out int workspaceId)
    {
        switch (parameter)
        {
            case int value:
                workspaceId = value;
                return true;

            case string text when int.TryParse(text, out var parsed):
                workspaceId = parsed;
                return true;

            default:
                workspaceId = 0;
                return false;
        }
    }

    private static Brush CreateBrush(string color)
    {
        var hex = color.TrimStart('#');
        if (hex.Length == 6)
        {
            hex = $"FF{hex}";
        }

        if (hex.Length != 8)
        {
            throw new ArgumentException("Expected #RRGGBB or #AARRGGBB color.", nameof(color));
        }

        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            a: Convert.ToByte(hex.Substring(0, 2), 16),
            r: Convert.ToByte(hex.Substring(2, 2), 16),
            g: Convert.ToByte(hex.Substring(4, 2), 16),
            b: Convert.ToByte(hex.Substring(6, 2), 16)));
    }
}
