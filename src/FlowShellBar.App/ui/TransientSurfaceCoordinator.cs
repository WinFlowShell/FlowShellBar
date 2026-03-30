using FlowShellBar.App.Application;
using FlowShellBar.App.Application.ViewModels;
using FlowShellBar.App.Diagnostics;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using Windows.Graphics;

namespace FlowShellBar.App.Ui;

internal sealed class TransientSurfaceCoordinator : IDisposable
{
    private readonly Window _ownerWindow;
    private readonly BarViewModel _viewModel;
    private readonly IAppLogger _logger;
    private readonly Func<BarPopupSurfaceKind, RectInt32?> _popupAnchorResolver;
    private readonly DispatcherQueueTimer? _transientPopupCloseTimer;

    private SidebarPanelWindow? _leftPanelWindow;
    private SidebarPanelWindow? _rightPanelWindow;
    private AnchoredPopupWindow? _popupWindow;
    private bool _isResourcesAnchorHovered;
    private bool _isWorkspaceAnchorHovered;
    private bool _isClockAnchorHovered;
    private bool _isPopupHovered;

    public TransientSurfaceCoordinator(
        Window ownerWindow,
        BarViewModel viewModel,
        IAppLogger logger,
        Func<BarPopupSurfaceKind, RectInt32?> popupAnchorResolver)
    {
        _ownerWindow = ownerWindow;
        _viewModel = viewModel;
        _logger = logger;
        _popupAnchorResolver = popupAnchorResolver;

        _transientPopupCloseTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (_transientPopupCloseTimer is not null)
        {
            _transientPopupCloseTimer.Interval = TimeSpan.FromMilliseconds(160);
            _transientPopupCloseTimer.Tick += OnTransientPopupCloseTimerTick;
        }
    }

    public void TogglePanel(BarPanelSurfaceKind panelKind)
    {
        CancelTransientPopupCloseEvaluation();
        _viewModel.TogglePanel(panelKind);
    }

    public LeftSidebarCommandSnapshot ExecuteLeftSidebarCommand(LeftSidebarCommandKind commandKind)
    {
        CancelTransientPopupCloseEvaluation();

        switch (commandKind)
        {
            case LeftSidebarCommandKind.Toggle:
                _viewModel.ToggleLeftSidebar();
                break;

            case LeftSidebarCommandKind.Open:
                _viewModel.OpenLeftSidebar();
                break;

            case LeftSidebarCommandKind.Close:
                _viewModel.CloseLeftSidebar();
                break;

            case LeftSidebarCommandKind.Detach:
                _viewModel.DetachLeftSidebar();
                break;

            case LeftSidebarCommandKind.Pin:
                _viewModel.PinLeftSidebar();
                break;

            case LeftSidebarCommandKind.Attach:
                _viewModel.AttachLeftSidebar();
                break;
        }

        return BuildLeftSidebarCommandSnapshot();
    }

    public void TogglePinnedPopup(BarPopupSurfaceKind popupKind)
    {
        CancelTransientPopupCloseEvaluation();
        _viewModel.TogglePopup(popupKind);
    }

    public void ClosePinnedPopupIfOpen()
    {
        if (_viewModel.ActivePopupSurface != BarPopupSurfaceKind.None && _viewModel.IsPopupPinned)
        {
            _viewModel.ClosePopup();
        }
    }

    public void OnAnchorPointerEntered(BarPopupSurfaceKind popupKind)
    {
        SetAnchorHoverState(popupKind, true);
        CancelTransientPopupCloseEvaluation();

        if (!_viewModel.IsPopupPinned || _viewModel.ActivePopupSurface == popupKind)
        {
            _viewModel.ShowPopup(popupKind, pinned: false);
        }
    }

    public void OnAnchorPointerExited(BarPopupSurfaceKind popupKind)
    {
        SetAnchorHoverState(popupKind, false);
        ScheduleTransientPopupCloseEvaluation();
    }

    public void CancelTransientPopupCloseEvaluation()
    {
        _transientPopupCloseTimer?.Stop();
    }

    public void EnsureShellSurfaceZOrder()
    {
        _leftPanelWindow?.EnsureShellSurfaceZOrder();
        _rightPanelWindow?.EnsureShellSurfaceZOrder();
        _popupWindow?.EnsureShellSurfaceZOrder();
    }

    public void SyncSurfaces()
    {
        SyncLeftPanelWindow();
        SyncRightPanelWindow();
        SyncPopupWindow();
    }

    public void Dispose()
    {
        if (_transientPopupCloseTimer is not null)
        {
            _transientPopupCloseTimer.Stop();
            _transientPopupCloseTimer.Tick -= OnTransientPopupCloseTimerTick;
        }

        ClosePopupWindow();
        DestroyLeftPanelWindow();
        DestroyRightPanelWindow();
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

    private void SyncLeftPanelWindow()
    {
        if (!_viewModel.IsLeftPanelOpen)
        {
            _leftPanelWindow?.HideSurface();
            return;
        }

        if (_leftPanelWindow is null)
        {
            _leftPanelWindow = new SidebarPanelWindow(
                _viewModel,
                _logger,
                BarPanelSurfaceKind.LeftSidebar,
                () => ShellSurfaceWindowing.GetWindowBounds(_ownerWindow),
                OnPanelDismissRequested,
                () => _viewModel.LeftSidebarMode,
                ExecuteLeftSidebarCommand);
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
            _rightPanelWindow?.HideSurface();
            return;
        }

        if (_rightPanelWindow is null)
        {
            _rightPanelWindow = new SidebarPanelWindow(
                _viewModel,
                _logger,
                BarPanelSurfaceKind.RightSidebar,
                () => ShellSurfaceWindowing.GetWindowBounds(_ownerWindow),
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

        if (_popupAnchorResolver(_viewModel.ActivePopupSurface) is null)
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
                () => ShellSurfaceWindowing.GetWindowBounds(_ownerWindow),
                () => _popupAnchorResolver(_viewModel.ActivePopupSurface),
                OnPopupDismissRequested,
                OnPopupHoverChanged);
            _popupWindow.Closed += OnPopupWindowClosed;
            _popupWindow.PrewarmShellSurface();
        }

        _popupWindow.ShowSurface();
        _popupWindow.RefreshPlacement();
        _popupWindow.EnsureShellSurfaceZOrder();
    }

    private void SetAnchorHoverState(BarPopupSurfaceKind popupKind, bool isHovered)
    {
        switch (popupKind)
        {
            case BarPopupSurfaceKind.Resources:
                _isResourcesAnchorHovered = isHovered;
                break;

            case BarPopupSurfaceKind.Workspaces:
                _isWorkspaceAnchorHovered = isHovered;
                break;

            case BarPopupSurfaceKind.Clock:
                _isClockAnchorHovered = isHovered;
                break;
        }
    }

    private void OnPanelDismissRequested(BarPanelSurfaceKind panelKind)
    {
        if (panelKind == BarPanelSurfaceKind.LeftSidebar)
        {
            _viewModel.CloseLeftSidebar();
            return;
        }

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
        _viewModel.CloseLeftSidebar();
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

    private LeftSidebarCommandSnapshot BuildLeftSidebarCommandSnapshot()
    {
        return new LeftSidebarCommandSnapshot(
            Mode: _viewModel.LeftSidebarModeLabel,
            IsOpen: _viewModel.IsLeftPanelOpen,
            IsAttached: _viewModel.IsLeftSidebarAttached,
            IsDetached: _viewModel.IsLeftSidebarDetached,
            IsPinned: _viewModel.IsLeftSidebarPinned);
    }
}
