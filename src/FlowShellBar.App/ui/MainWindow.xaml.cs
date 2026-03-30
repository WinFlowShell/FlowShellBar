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
    private readonly TransientSurfaceCoordinator _transientSurfaceCoordinator;
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
        _transientSurfaceCoordinator = new TransientSurfaceCoordinator(this, _viewModel, _logger, ResolvePopupAnchorBounds);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
    }

    private void OnOpenLauncherFlyoutClick(object sender, RoutedEventArgs e)
    {
        _transientSurfaceCoordinator.TogglePanel(BarPanelSurfaceKind.LeftSidebar);
    }

    private async void OnToggleOverviewFlyoutClick(object sender, RoutedEventArgs e)
    {
        await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.ToggleOverview));
    }

    private void OnOpenStatusPanelFlyoutClick(object sender, RoutedEventArgs e)
    {
        _transientSurfaceCoordinator.TogglePanel(BarPanelSurfaceKind.RightSidebar);
    }

    private void OnLeftZoneTapped(object sender, TappedRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.TogglePanel(BarPanelSurfaceKind.LeftSidebar);
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
        _transientSurfaceCoordinator.OnAnchorPointerEntered(BarPopupSurfaceKind.Resources);
    }

    private void OnResourcesAnchorPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.OnAnchorPointerExited(BarPopupSurfaceKind.Resources);
    }

    private void OnResourcesAnchorTapped(object sender, TappedRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.TogglePinnedPopup(BarPopupSurfaceKind.Resources);
        e.Handled = true;
    }

    private void OnClockAnchorPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.OnAnchorPointerEntered(BarPopupSurfaceKind.Clock);
    }

    private void OnClockAnchorPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.OnAnchorPointerExited(BarPopupSurfaceKind.Clock);
    }

    private void OnClockAnchorTapped(object sender, TappedRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.TogglePinnedPopup(BarPopupSurfaceKind.Clock);
        e.Handled = true;
    }

    private void OnWorkspaceAnchorPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.OnAnchorPointerEntered(BarPopupSurfaceKind.Workspaces);
    }

    private void OnWorkspaceAnchorPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.OnAnchorPointerExited(BarPopupSurfaceKind.Workspaces);
    }

    private void OnWorkspaceAnchorTapped(object sender, TappedRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.ClosePinnedPopupIfOpen();
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
        _transientSurfaceCoordinator.CancelTransientPopupCloseEvaluation();
        await _actionDispatcher.DispatchAsync(new BarActionRequest(BarActionKind.ToggleOverview));
        e.Handled = true;
    }

    private void OnRightZoneTapped(object sender, TappedRoutedEventArgs e)
    {
        _transientSurfaceCoordinator.TogglePanel(BarPanelSurfaceKind.RightSidebar);
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
        _transientSurfaceCoordinator.EnsureShellSurfaceZOrder();
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
            _transientSurfaceCoordinator.SyncSurfaces();
            return;
        }

        if (e.PropertyName == nameof(BarViewModel.ActivePanelSurface)
            || e.PropertyName == nameof(BarViewModel.ActivePopupSurface)
            || e.PropertyName == nameof(BarViewModel.IsPopupPinned))
        {
            _transientSurfaceCoordinator.SyncSurfaces();
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

        _transientSurfaceCoordinator.Dispose();
    }
}
