using System.Collections.ObjectModel;
using System.Globalization;
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
    private int _memoryUsagePercentValue;
    private int _cpuUsagePercentValue;
    private int _temperatureIndicatorValue;
    private string _memoryUsageText = string.Empty;
    private string _cpuUsageText = string.Empty;
    private string _temperatureText = string.Empty;
    private string _ramUsedPopupText = string.Empty;
    private string _ramFreePopupText = string.Empty;
    private string _ramTotalPopupText = string.Empty;
    private string _cpuTemperaturePopupText = string.Empty;
    private string _gpuTemperaturePopupText = string.Empty;
    private string _cpuLoadPopupText = string.Empty;
    private string _gpuLoadPopupText = string.Empty;
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
    private Brush _memoryIndicatorBrush = CreateBrush("#D7CFC7");
    private Brush _temperatureIndicatorBrush = CreateBrush("#D7CFC7");
    private Brush _cpuIndicatorBrush = CreateBrush("#D7CFC7");
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
        ActiveContext = new BarActiveContextSectionViewModel();
        Resources = new BarResourceSectionViewModel();
        Clock = new BarClockSectionViewModel();
        Status = new BarStatusSectionViewModel
        {
            ConnectionStateBackground = _connectionStateBackground,
            ConnectionStateBorderBrush = _connectionStateBorderBrush,
            ConnectionStateForeground = _connectionStateForeground,
        };

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

    public BarActiveContextSectionViewModel ActiveContext { get; }

    public BarResourceSectionViewModel Resources { get; }

    public BarClockSectionViewModel Clock { get; }

    public BarStatusSectionViewModel Status { get; }

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

    public int MemoryUsagePercentValue
    {
        get => _memoryUsagePercentValue;
        private set => SetProperty(ref _memoryUsagePercentValue, value);
    }

    public int CpuUsagePercentValue
    {
        get => _cpuUsagePercentValue;
        private set => SetProperty(ref _cpuUsagePercentValue, value);
    }

    public int TemperatureIndicatorValue
    {
        get => _temperatureIndicatorValue;
        private set => SetProperty(ref _temperatureIndicatorValue, value);
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

    public string RamUsedPopupText
    {
        get => _ramUsedPopupText;
        private set => SetProperty(ref _ramUsedPopupText, value);
    }

    public string RamFreePopupText
    {
        get => _ramFreePopupText;
        private set => SetProperty(ref _ramFreePopupText, value);
    }

    public string RamTotalPopupText
    {
        get => _ramTotalPopupText;
        private set => SetProperty(ref _ramTotalPopupText, value);
    }

    public string CpuTemperaturePopupText
    {
        get => _cpuTemperaturePopupText;
        private set => SetProperty(ref _cpuTemperaturePopupText, value);
    }

    public string GpuTemperaturePopupText
    {
        get => _gpuTemperaturePopupText;
        private set => SetProperty(ref _gpuTemperaturePopupText, value);
    }

    public string CpuLoadPopupText
    {
        get => _cpuLoadPopupText;
        private set => SetProperty(ref _cpuLoadPopupText, value);
    }

    public string GpuLoadPopupText
    {
        get => _gpuLoadPopupText;
        private set => SetProperty(ref _gpuLoadPopupText, value);
    }

    public Brush MemoryIndicatorBrush
    {
        get => _memoryIndicatorBrush;
        private set => SetProperty(ref _memoryIndicatorBrush, value);
    }

    public Brush TemperatureIndicatorBrush
    {
        get => _temperatureIndicatorBrush;
        private set => SetProperty(ref _temperatureIndicatorBrush, value);
    }

    public Brush CpuIndicatorBrush
    {
        get => _cpuIndicatorBrush;
        private set => SetProperty(ref _cpuIndicatorBrush, value);
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
        ActiveContext.ActiveWindowAppName = ActiveWindowAppName;
        ActiveContext.ActiveWindowTitle = ActiveWindowTitle;
        ActiveContext.ActiveWorkspaceLabel = ActiveWorkspaceLabel;
        MemoryUsagePercentValue = Math.Clamp(model.ResourceMetrics.MemoryUsagePercent, 0, 100);
        CpuUsagePercentValue = Math.Clamp(model.ResourceMetrics.CpuUsagePercent, 0, 100);
        TemperatureIndicatorValue = BuildTemperatureIndicatorValue(model.ResourceMetrics);
        MemoryUsageText = model.ResourceMetrics.MemoryUsagePercent.ToString(CultureInfo.InvariantCulture);
        CpuUsageText = model.ResourceMetrics.CpuUsagePercent.ToString(CultureInfo.InvariantCulture);
        TemperatureText = BuildTemperatureIndicatorText(model.ResourceMetrics);
        MemoryIndicatorBrush = BuildIndicatorBrush(MemoryUsagePercentValue, cautionThreshold: null, warningThreshold: 90);
        TemperatureIndicatorBrush = BuildIndicatorBrush(
            GetMaxAvailableTemperature(model.ResourceMetrics),
            cautionThreshold: 65,
            warningThreshold: 80);
        CpuIndicatorBrush = BuildIndicatorBrush(CpuUsagePercentValue, cautionThreshold: null, warningThreshold: 90);
        Resources.Memory.ProgressValue = MemoryUsagePercentValue;
        Resources.Memory.ValueText = MemoryUsageText;
        Resources.Memory.IndicatorBrush = MemoryIndicatorBrush;
        Resources.Temperature.ProgressValue = TemperatureIndicatorValue;
        Resources.Temperature.ValueText = TemperatureText;
        Resources.Temperature.IndicatorBrush = TemperatureIndicatorBrush;
        Resources.Cpu.ProgressValue = CpuUsagePercentValue;
        Resources.Cpu.ValueText = CpuUsageText;
        Resources.Cpu.IndicatorBrush = CpuIndicatorBrush;
        RamUsedPopupText = FormatGigabytes(model.ResourceMetrics.MemoryUsedBytes);
        RamFreePopupText = FormatGigabytes(model.ResourceMetrics.MemoryAvailableBytes);
        RamTotalPopupText = FormatGigabytes(model.ResourceMetrics.MemoryTotalBytes);
        CpuTemperaturePopupText = FormatTemperature(model.ResourceMetrics.CpuTemperatureCelsius);
        GpuTemperaturePopupText = FormatTemperature(model.ResourceMetrics.GpuTemperatureCelsius);
        CpuLoadPopupText = BuildLoadLevelText(model.ResourceMetrics.CpuUsagePercent);
        GpuLoadPopupText = BuildLoadLevelText(model.ResourceMetrics.GpuUsagePercent);
        Resources.Popup.RamUsedText = RamUsedPopupText;
        Resources.Popup.RamFreeText = RamFreePopupText;
        Resources.Popup.RamTotalText = RamTotalPopupText;
        Resources.Popup.CpuTemperatureText = CpuTemperaturePopupText;
        Resources.Popup.GpuTemperatureText = GpuTemperaturePopupText;
        Resources.Popup.CpuLoadText = CpuLoadPopupText;
        Resources.Popup.GpuLoadText = GpuLoadPopupText;
        CurrentTime = model.CurrentTime;
        CurrentDate = model.CurrentDate;
        Clock.CurrentTime = CurrentTime;
        Clock.CurrentDate = CurrentDate;
        SurfacePlacement = model.SurfacePlacement;
        IsNetworkAvailable = model.StatusCluster.IsNetworkAvailable;
        IsAudioAvailable = model.StatusCluster.IsAudioAvailable;
        HasNotifications = model.StatusCluster.HasNotifications;
        Status.RuntimeModeLabel = RuntimeModeLabel;
        Status.ConnectionStateLabel = ConnectionStateLabel;
        Status.IsNetworkAvailable = IsNetworkAvailable;
        Status.IsAudioAvailable = IsAudioAvailable;
        Status.HasNotifications = HasNotifications;
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
                Status.ConnectionStateBackground = ConnectionStateBackground;
                Status.ConnectionStateBorderBrush = ConnectionStateBorderBrush;
                Status.ConnectionStateForeground = ConnectionStateForeground;
                break;

            case BarConnectionState.Degraded:
                ConnectionStateBackground = CreateBrush("#3A2C13");
                ConnectionStateBorderBrush = CreateBrush("#8A6B23");
                ConnectionStateForeground = CreateBrush("#FFE8A3");
                Status.ConnectionStateBackground = ConnectionStateBackground;
                Status.ConnectionStateBorderBrush = ConnectionStateBorderBrush;
                Status.ConnectionStateForeground = ConnectionStateForeground;
                break;

            default:
                ConnectionStateBackground = CreateBrush("#342322");
                ConnectionStateBorderBrush = CreateBrush("#7A4741");
                ConnectionStateForeground = CreateBrush("#FFD8D3");
                Status.ConnectionStateBackground = ConnectionStateBackground;
                Status.ConnectionStateBorderBrush = ConnectionStateBorderBrush;
                Status.ConnectionStateForeground = ConnectionStateForeground;
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

    private static string BuildTemperatureIndicatorText(ResourceMetricsModel resourceMetrics)
    {
        var maxTemperature = GetMaxAvailableTemperature(resourceMetrics);
        return maxTemperature?.ToString(CultureInfo.InvariantCulture) ?? "--";
    }

    private static int BuildTemperatureIndicatorValue(ResourceMetricsModel resourceMetrics)
    {
        return GetMaxAvailableTemperature(resourceMetrics) is int maxTemperature
            ? Math.Clamp(maxTemperature, 0, 100)
            : 0;
    }

    private static int? GetMaxAvailableTemperature(ResourceMetricsModel resourceMetrics)
    {
        return (resourceMetrics.CpuTemperatureCelsius, resourceMetrics.GpuTemperatureCelsius) switch
        {
            (int cpuTemperature, int gpuTemperature) => Math.Max(cpuTemperature, gpuTemperature),
            (int cpuTemperature, null) => cpuTemperature,
            (null, int gpuTemperature) => gpuTemperature,
            _ => null,
        };
    }

    private static string FormatGigabytes(ulong bytes)
    {
        if (bytes == 0)
        {
            return "n/a";
        }

        var gigabytes = bytes / 1024d / 1024d / 1024d;
        return string.Create(CultureInfo.InvariantCulture, $"{gigabytes:0.0} GB");
    }

    private static string FormatTemperature(int? temperatureCelsius)
    {
        return temperatureCelsius is int value
            ? string.Create(CultureInfo.InvariantCulture, $"{value}°C")
            : "n/a";
    }

    private static string BuildLoadLevelText(int? usagePercent)
    {
        if (usagePercent is not int value)
        {
            return "n/a";
        }

        var level = value switch
        {
            >= 80 => "High",
            >= 40 => "Medium",
            _ => "Low",
        };

        return string.Create(CultureInfo.InvariantCulture, $"{level} ({value}%)");
    }

    private static Brush BuildIndicatorBrush(int? value, int? cautionThreshold, int warningThreshold)
    {
        if (value is not int metricValue)
        {
            return CreateBrush("#A99F97");
        }

        if (metricValue >= warningThreshold)
        {
            return CreateBrush("#B97862");
        }

        if (cautionThreshold is int cautionValue && metricValue >= cautionValue)
        {
            return CreateBrush("#C59A57");
        }

        return CreateBrush("#D7CFC7");
    }
}
