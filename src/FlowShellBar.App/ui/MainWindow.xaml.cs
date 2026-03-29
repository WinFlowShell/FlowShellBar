using FlowShellBar.App.Application.Actions;
using FlowShellBar.App.Application.ViewModels;
using FlowShellBar.App.Diagnostics;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

using System.ComponentModel;
using System.Runtime.InteropServices;

using Windows.Graphics;

using WinRT.Interop;

namespace FlowShellBar.App.Ui;

public sealed partial class MainWindow : Window
{
    private static readonly NativeMethods.MonitorEnumProc MonitorBoundsLookupProc = OnMonitorBoundsLookup;

    private readonly BarViewModel _viewModel;
    private readonly IBarActionDispatcher _actionDispatcher;
    private readonly IAppLogger _logger;
    private DispatcherQueueTimer? _topmostEnforcerTimer;
    private bool _shellSurfacePrepared;

    public MainWindow(
        BarViewModel viewModel,
        IBarActionDispatcher actionDispatcher,
        IAppLogger logger)
    {
        _viewModel = viewModel;
        _actionDispatcher = actionDispatcher;
        _logger = logger;

        InitializeComponent();
        ShellRoot.DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
    }

    private async void OnOpenLauncherFlyoutClick(object sender, RoutedEventArgs e)
    {
        await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.OpenLauncher));
    }

    private async void OnToggleOverviewFlyoutClick(object sender, RoutedEventArgs e)
    {
        await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.ToggleOverview));
    }

    private async void OnOpenStatusPanelFlyoutClick(object sender, RoutedEventArgs e)
    {
        await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.OpenStatusPanel));
    }

    private async void OnLeftZoneTapped(object sender, TappedRoutedEventArgs e)
    {
        await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.OpenLauncher));
        e.Handled = true;
    }

    private async void OnLeftZonePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        await _actionDispatcher.DispatchAsync(new BarActionRequest(
            BarActionKind.AdjustBrightness,
            Delta: Math.Sign(delta)));
        e.Handled = true;
    }

    private async void OnRightZonePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        await _actionDispatcher.DispatchAsync(new BarActionRequest(
            BarActionKind.AdjustVolume,
            Delta: Math.Sign(delta)));
        e.Handled = true;
    }

    public void PrepareShellSurface()
    {
        if (_shellSurfacePrepared)
        {
            return;
        }

        ConfigureWindow();
        StartTopmostEnforcer();
        _shellSurfacePrepared = true;
    }

    public void EnsureShellSurfaceZOrder()
    {
        if (!_shellSurfacePrepared)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        ApplyCompanionShellSurfaceStyle(hwnd);
        ReassertTopmost(hwnd);
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = GetAppWindowForCurrentWindow();

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        appWindow.IsShownInSwitchers = false;
        var horizontalMargin = GetEnvironmentInt("FLOWSHELL_BAR_HORIZONTAL_MARGIN_PX", 0);
        var topMargin = GetEnvironmentInt("FLOWSHELL_BAR_TOP_MARGIN_PX", 0);
        var barHeight = GetBarHeight();
        var monitorBounds = ResolveShellAnchorBounds(windowId, barHeight, out var anchorSource);
        var minWidth = GetEnvironmentInt("FLOWSHELL_BAR_MIN_WIDTH_PX", 960);
        var availableWidth = Math.Max(320, monitorBounds.Width - (horizontalMargin * 2));
        var barWidth = Math.Min(Math.Max(minWidth, availableWidth), availableWidth);

        var bounds = new RectInt32(
            monitorBounds.X + horizontalMargin,
            monitorBounds.Y + topMargin,
            barWidth,
            barHeight);

        ApplyCompanionShellSurfaceStyle(hwnd);
        appWindow.MoveAndResize(bounds);
        appWindow.Title = "FlowShellBar";

        ReassertTopmost(hwnd);

        var adjustedBounds = GetOuterBoundsForClientArea(hwnd, bounds);

        ApplyTopmostBounds(hwnd, adjustedBounds);

        _logger.Info(
            $"MainWindow configured: anchor={anchorSource}; client {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y}), outer {adjustedBounds.Width}x{adjustedBounds.Height} at ({adjustedBounds.X},{adjustedBounds.Y}).");
    }

    private static int GetEnvironmentInt(string variableName, int fallback)
    {
        var rawValue = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(rawValue, out var value) ? value : fallback;
    }

    private static int GetBarHeight()
    {
        var configuredHeight = GetEnvironmentInt("FLOWSHELL_BAR_HEIGHT_PX", 37);
        return configuredHeight is 40 or 41 or 46 or 60 ? 37 : configuredHeight;
    }

    private RectInt32 ResolveShellAnchorBounds(Microsoft.UI.WindowId windowId, int barHeight, out string anchorSource)
    {
        if (TryResolveFlowtileAnchorBounds(barHeight, out var wmBounds, out anchorSource))
        {
            return wmBounds;
        }

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        anchorSource = "displayarea-fallback";
        return displayArea.OuterBounds;
    }

    private bool TryResolveFlowtileAnchorBounds(int barHeight, out RectInt32 bounds, out string anchorSource)
    {
        var placement = _viewModel.SurfacePlacement;
        if (!placement.IsFlowtileBound)
        {
            bounds = default;
            anchorSource = string.Empty;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(placement.MonitorBinding)
            && TryGetMonitorBoundsByBinding(placement.MonitorBinding, out bounds))
        {
            anchorSource = $"flowtilewm-binding:{placement.MonitorBinding}";
            return true;
        }

        if (placement.WorkAreaWidth <= 0 || placement.WorkAreaHeight <= 0)
        {
            bounds = default;
            anchorSource = string.Empty;
            return false;
        }

        var reservedTop = GetEnvironmentInt("FLOWSHELL_RESERVED_TOP_PX", barHeight);
        var top = placement.WorkAreaY >= reservedTop
            ? placement.WorkAreaY - reservedTop
            : placement.WorkAreaY;
        var height = Math.Max(barHeight, placement.WorkAreaHeight + Math.Max(0, placement.WorkAreaY - top));

        bounds = new RectInt32(
            placement.WorkAreaX,
            top,
            placement.WorkAreaWidth,
            height);
        anchorSource = "flowtilewm-work-area";
        return true;
    }

    private static bool TryGetMonitorBoundsByBinding(string monitorBinding, out RectInt32 bounds)
    {
        var state = new MonitorLookupState(monitorBinding);
        var handle = GCHandle.Alloc(state);

        try
        {
            NativeMethods.EnumDisplayMonitors(0, 0, MonitorBoundsLookupProc, GCHandle.ToIntPtr(handle));
            if (state.Bounds is RectInt32 resolvedBounds)
            {
                bounds = resolvedBounds;
                return true;
            }
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        bounds = default;
        return false;
    }

    private static bool OnMonitorBoundsLookup(
        nint monitorHandle,
        nint _,
        nint __,
        nint userData)
    {
        var handle = GCHandle.FromIntPtr(userData);
        if (handle.Target is not MonitorLookupState state)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFOEX
        {
            monitorInfo = new NativeMethods.MONITORINFO
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            },
            szDevice = string.Empty,
        };

        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
        {
            return true;
        }

        if (!string.Equals(info.szDevice, state.TargetBinding, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rect = info.monitorInfo.rcMonitor;
        state.Bounds = new RectInt32(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
        return false;
    }

    private void ApplyCompanionShellSurfaceStyle(nint hwnd)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle);
        style |= NativeMethods.WsPopup;
        style &= ~(NativeMethods.WsCaption
            | NativeMethods.WsSysmenu
            | NativeMethods.WsThickframe
            | NativeMethods.WsMinimizebox
            | NativeMethods.WsMaximizebox);

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, style);

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExstyle);
        exStyle |= NativeMethods.WsExToolwindow | NativeMethods.WsExNoactivate;
        exStyle |= NativeMethods.WsExTopmost;
        exStyle &= ~NativeMethods.WsExAppwindow;

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExstyle, exStyle);

        var cornerPreference = (int)NativeMethods.DwmWindowCornerPreference.DoNotRound;
        NativeMethods.DwmSetWindowAttributeInt(
            hwnd,
            NativeMethods.DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));

        var borderColor = NativeMethods.DwmColorNone;
        NativeMethods.DwmSetWindowAttributeUInt(
            hwnd,
            NativeMethods.DwmwaBorderColor,
            ref borderColor,
            sizeof(uint));
    }

    private static RectInt32 GetOuterBoundsForClientArea(nint hwnd, RectInt32 desiredClientBounds)
    {
        NativeMethods.GetWindowRect(hwnd, out var windowRect);
        NativeMethods.GetClientRect(hwnd, out var clientRect);

        var clientTopLeft = new NativeMethods.POINT(0, 0);
        var clientBottomRight = new NativeMethods.POINT(clientRect.Right, clientRect.Bottom);

        NativeMethods.ClientToScreen(hwnd, ref clientTopLeft);
        NativeMethods.ClientToScreen(hwnd, ref clientBottomRight);

        var leftInset = clientTopLeft.X - windowRect.Left;
        var topInset = clientTopLeft.Y - windowRect.Top;
        var rightInset = windowRect.Right - clientBottomRight.X;
        var bottomInset = windowRect.Bottom - clientBottomRight.Y;

        return new RectInt32(
            desiredClientBounds.X - leftInset,
            desiredClientBounds.Y - topInset,
            desiredClientBounds.Width + leftInset + rightInset,
            desiredClientBounds.Height + topInset + bottomInset);
    }

    private AppWindow GetAppWindowForCurrentWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private void StartTopmostEnforcer()
    {
        if (_topmostEnforcerTimer is not null)
        {
            return;
        }

        _topmostEnforcerTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (_topmostEnforcerTimer is null)
        {
            return;
        }

        _topmostEnforcerTimer.Interval = TimeSpan.FromSeconds(2);
        _topmostEnforcerTimer.Tick += (_, _) => EnsureShellSurfaceZOrder();
        _topmostEnforcerTimer.Start();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        EnsureShellSurfaceZOrder();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_shellSurfacePrepared || e.PropertyName != nameof(BarViewModel.SurfacePlacement))
        {
            return;
        }

        ConfigureWindow();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_topmostEnforcerTimer is not null)
        {
            _topmostEnforcerTimer.Stop();
            _topmostEnforcerTimer = null;
        }
    }

    private static void ReassertTopmost(nint hwnd)
    {
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNomove
            | NativeMethods.SwpNosize
            | NativeMethods.SwpNoactivate
            | NativeMethods.SwpNoownerzorder
            | NativeMethods.SwpShowwindow
            | NativeMethods.SwpFramechanged);
    }

    private static void ApplyTopmostBounds(nint hwnd, RectInt32 bounds)
    {
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoactivate
            | NativeMethods.SwpNoownerzorder
            | NativeMethods.SwpShowwindow
            | NativeMethods.SwpFramechanged);
    }

    private sealed class MonitorLookupState
    {
        public MonitorLookupState(string targetBinding)
        {
            TargetBinding = targetBinding;
        }

        public string TargetBinding { get; }

        public RectInt32? Bounds { get; set; }
    }
}
