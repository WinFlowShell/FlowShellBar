using System.ComponentModel;

using FlowShellBar.App.Application;
using FlowShellBar.App.Application.Actions;
using FlowShellBar.App.Application.ViewModels;
using FlowShellBar.App.Diagnostics;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.Graphics;

namespace FlowShellBar.App.Ui;

public sealed partial class MainWindow : Window
{
    private readonly BarViewModel _viewModel;
    private readonly IBarActionDispatcher _actionDispatcher;
    private readonly IAppLogger _logger;
    private DispatcherQueueTimer? _topmostEnforcerTimer;
    private DispatcherQueueTimer? _transientPopupCloseTimer;
    private SidebarPanelWindow? _leftPanelWindow;
    private SidebarPanelWindow? _rightPanelWindow;
    private AnchoredPopupWindow? _popupWindow;
    private bool _shellSurfacePrepared;
    private bool _isResourcesAnchorHovered;
    private bool _isWorkspaceAnchorHovered;
    private bool _isClockAnchorHovered;
    private bool _isPopupHovered;

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

        InitializeTransientPopupTimer();
    }

    private void OnOpenLauncherFlyoutClick(object sender, RoutedEventArgs e)
    {
        CancelTransientPopupCloseEvaluation();
        _viewModel.TogglePanel(BarPanelSurfaceKind.LeftSidebar);
    }

    private async void OnToggleOverviewFlyoutClick(object sender, RoutedEventArgs e)
    {
        await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.ToggleOverview));
    }

    private void OnOpenStatusPanelFlyoutClick(object sender, RoutedEventArgs e)
    {
        CancelTransientPopupCloseEvaluation();
        _viewModel.TogglePanel(BarPanelSurfaceKind.RightSidebar);
    }

    private void OnLeftZoneTapped(object sender, TappedRoutedEventArgs e)
    {
        CancelTransientPopupCloseEvaluation();
        _viewModel.TogglePanel(BarPanelSurfaceKind.LeftSidebar);
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

    private void OnResourcesAnchorPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isResourcesAnchorHovered = true;
        CancelTransientPopupCloseEvaluation();

        if (!_viewModel.IsPopupPinned || _viewModel.ActivePopupSurface == BarPopupSurfaceKind.Resources)
        {
            _viewModel.ShowPopup(BarPopupSurfaceKind.Resources, pinned: false);
        }
    }

    private void OnResourcesAnchorPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isResourcesAnchorHovered = false;
        ScheduleTransientPopupCloseEvaluation();
    }

    private void OnResourcesAnchorTapped(object sender, TappedRoutedEventArgs e)
    {
        CancelTransientPopupCloseEvaluation();
        _viewModel.TogglePopup(BarPopupSurfaceKind.Resources);
        e.Handled = true;
    }

    private void OnClockAnchorPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isClockAnchorHovered = true;
        CancelTransientPopupCloseEvaluation();

        if (!_viewModel.IsPopupPinned || _viewModel.ActivePopupSurface == BarPopupSurfaceKind.Clock)
        {
            _viewModel.ShowPopup(BarPopupSurfaceKind.Clock, pinned: false);
        }
    }

    private void OnClockAnchorPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isClockAnchorHovered = false;
        ScheduleTransientPopupCloseEvaluation();
    }

    private void OnClockAnchorTapped(object sender, TappedRoutedEventArgs e)
    {
        CancelTransientPopupCloseEvaluation();
        _viewModel.TogglePopup(BarPopupSurfaceKind.Clock);
        e.Handled = true;
    }

    private void OnWorkspaceAnchorPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isWorkspaceAnchorHovered = true;
        CancelTransientPopupCloseEvaluation();

        if (!_viewModel.IsPopupPinned || _viewModel.ActivePopupSurface == BarPopupSurfaceKind.Workspaces)
        {
            _viewModel.ShowPopup(BarPopupSurfaceKind.Workspaces, pinned: false);
        }
    }

    private void OnWorkspaceAnchorPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isWorkspaceAnchorHovered = false;
        ScheduleTransientPopupCloseEvaluation();
    }

    private void OnWorkspaceAnchorTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_viewModel.ActivePopupSurface != BarPopupSurfaceKind.None && _viewModel.IsPopupPinned)
        {
            _viewModel.ClosePopup();
        }
    }

    private async void OnWorkspaceAnchorPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        if (!TryResolveAdjacentWorkspaceId(delta, out var workspaceId))
        {
            return;
        }

        await _actionDispatcher.DispatchAsync(new BarActionRequest(
            BarActionKind.SwitchWorkspace,
            WorkspaceId: workspaceId));
        e.Handled = true;
    }

    private async void OnWorkspaceAnchorRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        CancelTransientPopupCloseEvaluation();
        await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.ToggleOverview));
        e.Handled = true;
    }

    private void OnRightZoneTapped(object sender, TappedRoutedEventArgs e)
    {
        CancelTransientPopupCloseEvaluation();
        _viewModel.TogglePanel(BarPanelSurfaceKind.RightSidebar);
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

        ShellSurfaceWindowing.EnsureTopmost(this, noActivate: true);
        _leftPanelWindow?.EnsureShellSurfaceZOrder();
        _rightPanelWindow?.EnsureShellSurfaceZOrder();
        _popupWindow?.EnsureShellSurfaceZOrder();
    }

    private void ConfigureWindow()
    {
        var horizontalMargin = ShellSurfaceWindowing.GetEnvironmentInt("FLOWSHELL_BAR_HORIZONTAL_MARGIN_PX", 0);
        var topMargin = ShellSurfaceWindowing.GetEnvironmentInt("FLOWSHELL_BAR_TOP_MARGIN_PX", 0);
        var barHeight = ShellSurfaceWindowing.GetBarHeight();
        var monitorBounds = ShellSurfaceWindowing.ResolveShellAnchorBounds(this, _viewModel.SurfacePlacement, barHeight, out var anchorSource);
        var minWidth = ShellSurfaceWindowing.GetEnvironmentInt("FLOWSHELL_BAR_MIN_WIDTH_PX", 960);
        var availableWidth = Math.Max(320, monitorBounds.Width - (horizontalMargin * 2));
        var barWidth = Math.Min(Math.Max(minWidth, availableWidth), availableWidth);

        var bounds = new RectInt32(
            monitorBounds.X + horizontalMargin,
            monitorBounds.Y + topMargin,
            barWidth,
            barHeight);

        ShellSurfaceWindowing.PrepareCompanionSurface(this, "FlowShellBar", bounds, noActivate: true);

        _logger.Info(
            $"MainWindow configured: anchor={anchorSource}; client {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y}).");
    }

    private void InitializeTransientPopupTimer()
    {
        _transientPopupCloseTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (_transientPopupCloseTimer is null)
        {
            return;
        }

        _transientPopupCloseTimer.Interval = TimeSpan.FromMilliseconds(160);
        _transientPopupCloseTimer.Tick += OnTransientPopupCloseTimerTick;
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

    private void ScheduleTransientPopupCloseEvaluation()
    {
        if (_transientPopupCloseTimer is null)
        {
            return;
        }

        _transientPopupCloseTimer.Stop();
        _transientPopupCloseTimer.Start();
    }

    private void CancelTransientPopupCloseEvaluation()
    {
        _transientPopupCloseTimer?.Stop();
    }

    private void OnTransientPopupCloseTimerTick(object? sender, object e)
    {
        _transientPopupCloseTimer?.Stop();

        if (_viewModel.IsPopupPinned)
        {
            return;
        }

        if (_isResourcesAnchorHovered || _isWorkspaceAnchorHovered || _isClockAnchorHovered || _isPopupHovered)
        {
            return;
        }

        _viewModel.ClosePopup();
    }

    private void SyncAuxiliarySurfaces()
    {
        SyncLeftPanelWindow();
        SyncRightPanelWindow();
        SyncPopupWindow();
    }

    private void SyncLeftPanelWindow()
    {
        if (_viewModel.ActivePanelSurface != BarPanelSurfaceKind.LeftSidebar)
        {
            HideLeftPanelWindow();
            return;
        }

        if (_leftPanelWindow is null)
        {
            _leftPanelWindow = new SidebarPanelWindow(
                _viewModel,
                _logger,
                BarPanelSurfaceKind.LeftSidebar,
                () => ShellSurfaceWindowing.GetWindowBounds(this),
                OnPanelDismissRequested);
            _leftPanelWindow.Closed += OnLeftPanelWindowClosed;
            _leftPanelWindow.PrewarmShellSurface();
        }

        _leftPanelWindow.ShowSurface();
        _leftPanelWindow.EnsureShellSurfaceZOrder();
    }

    private void SyncRightPanelWindow()
    {
        if (_viewModel.ActivePanelSurface != BarPanelSurfaceKind.RightSidebar)
        {
            HideRightPanelWindow();
            return;
        }

        if (_rightPanelWindow is null)
        {
            _rightPanelWindow = new SidebarPanelWindow(
                _viewModel,
                _logger,
                BarPanelSurfaceKind.RightSidebar,
                () => ShellSurfaceWindowing.GetWindowBounds(this),
                OnPanelDismissRequested);
            _rightPanelWindow.Closed += OnRightPanelWindowClosed;
            _rightPanelWindow.PrewarmShellSurface();
        }

        _rightPanelWindow.ShowSurface();
        _rightPanelWindow.EnsureShellSurfaceZOrder();
    }

    private void SyncPopupWindow()
    {
        if (_viewModel.ActivePopupSurface == BarPopupSurfaceKind.None)
        {
            ClosePopupWindow();
            return;
        }

        if (ResolvePopupAnchorBounds(_viewModel.ActivePopupSurface) is null)
        {
            _viewModel.ClosePopup();
            return;
        }

        if (_popupWindow is null
            || _popupWindow.PopupKind != _viewModel.ActivePopupSurface
            || _popupWindow.IsPinned != _viewModel.IsPopupPinned)
        {
            ClosePopupWindow();

            _popupWindow = new AnchoredPopupWindow(
                _viewModel,
                _logger,
                _viewModel.ActivePopupSurface,
                _viewModel.IsPopupPinned,
                () => ShellSurfaceWindowing.GetWindowBounds(this),
                () => ResolvePopupAnchorBounds(_viewModel.ActivePopupSurface),
                OnPopupDismissRequested,
                OnPopupHoverChanged);
            _popupWindow.Closed += OnPopupWindowClosed;
            _popupWindow.PrewarmShellSurface();
        }

        _popupWindow.ShowSurface();
        _popupWindow.RefreshPlacement();
        _popupWindow.EnsureShellSurfaceZOrder();
    }

    private RectInt32? ResolvePopupAnchorBounds(BarPopupSurfaceKind popupKind)
    {
        FrameworkElement? anchor = popupKind switch
        {
            BarPopupSurfaceKind.Resources => ResourcesAnchor,
            BarPopupSurfaceKind.Workspaces => WorkspaceAnchor,
            BarPopupSurfaceKind.Clock => ClockAnchor,
            _ => null,
        };

        if (anchor is null)
        {
            return null;
        }

        return ShellSurfaceWindowing.TryGetElementScreenBounds(this, anchor, out var bounds)
            ? bounds
            : null;
    }

    private bool TryResolveAdjacentWorkspaceId(int wheelDelta, out int workspaceId)
    {
        if (_viewModel.Workspaces.Count == 0)
        {
            workspaceId = 0;
            return false;
        }

        var activeIndex = -1;
        for (var index = 0; index < _viewModel.Workspaces.Count; index++)
        {
            if (_viewModel.Workspaces[index].IsActive)
            {
                activeIndex = index;
                break;
            }
        }

        if (activeIndex < 0)
        {
            workspaceId = _viewModel.Workspaces[0].Id;
            return true;
        }

        var direction = Math.Sign(wheelDelta);
        if (direction == 0)
        {
            workspaceId = 0;
            return false;
        }

        var targetIndex = direction < 0
            ? activeIndex + 1
            : activeIndex - 1;

        if (targetIndex < 0)
        {
            targetIndex = _viewModel.Workspaces.Count - 1;
        }
        else if (targetIndex >= _viewModel.Workspaces.Count)
        {
            targetIndex = 0;
        }

        workspaceId = _viewModel.Workspaces[targetIndex].Id;
        return true;
    }

    private void OnPanelDismissRequested(BarPanelSurfaceKind panelKind)
    {
        _viewModel.ClosePanel(panelKind);
    }

    private void OnPopupDismissRequested(BarPopupSurfaceKind popupKind)
    {
        _viewModel.ClosePopup(popupKind);
    }

    private void OnPopupHoverChanged(bool isPointerOver)
    {
        _isPopupHovered = isPointerOver;

        if (isPointerOver)
        {
            CancelTransientPopupCloseEvaluation();
        }
        else
        {
            ScheduleTransientPopupCloseEvaluation();
        }
    }

    private void HideLeftPanelWindow()
    {
        _leftPanelWindow?.HideSurface();
    }

    private void HideRightPanelWindow()
    {
        _rightPanelWindow?.HideSurface();
    }

    private void DestroyLeftPanelWindow()
    {
        if (_leftPanelWindow is null)
        {
            return;
        }

        _leftPanelWindow.Closed -= OnLeftPanelWindowClosed;
        _leftPanelWindow.Close();
        _leftPanelWindow = null;
    }

    private void DestroyRightPanelWindow()
    {
        if (_rightPanelWindow is null)
        {
            return;
        }

        _rightPanelWindow.Closed -= OnRightPanelWindowClosed;
        _rightPanelWindow.Close();
        _rightPanelWindow = null;
    }

    private void ClosePopupWindow()
    {
        if (_popupWindow is null)
        {
            return;
        }

        _popupWindow.Closed -= OnPopupWindowClosed;
        _popupWindow.Close();
        _popupWindow = null;
        _isPopupHovered = false;
    }

    private void OnLeftPanelWindowClosed(object sender, WindowEventArgs args)
    {
        _leftPanelWindow = null;
        _viewModel.ClosePanel(BarPanelSurfaceKind.LeftSidebar);
    }

    private void OnRightPanelWindowClosed(object sender, WindowEventArgs args)
    {
        _rightPanelWindow = null;
        _viewModel.ClosePanel(BarPanelSurfaceKind.RightSidebar);
    }

    private void OnPopupWindowClosed(object sender, WindowEventArgs args)
    {
        _popupWindow = null;
        _isPopupHovered = false;
        _viewModel.ClosePopup();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        EnsureShellSurfaceZOrder();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_shellSurfacePrepared)
        {
            return;
        }

        if (e.PropertyName == nameof(BarViewModel.SurfacePlacement))
        {
            ConfigureWindow();
            SyncAuxiliarySurfaces();
            return;
        }

        if (e.PropertyName == nameof(BarViewModel.ActivePanelSurface)
            || e.PropertyName == nameof(BarViewModel.ActivePopupSurface)
            || e.PropertyName == nameof(BarViewModel.IsPopupPinned))
        {
            SyncAuxiliarySurfaces();
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_topmostEnforcerTimer is not null)
        {
            _topmostEnforcerTimer.Stop();
            _topmostEnforcerTimer = null;
        }

        if (_transientPopupCloseTimer is not null)
        {
            _transientPopupCloseTimer.Stop();
            _transientPopupCloseTimer = null;
        }

        ClosePopupWindow();
        DestroyLeftPanelWindow();
        DestroyRightPanelWindow();
    }
}
