using System.ComponentModel;

using FlowShellBar.App.Application;
using FlowShellBar.App.Application.ViewModels;
using FlowShellBar.App.Diagnostics;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Text;

using Windows.Graphics;
using Windows.System;

namespace FlowShellBar.App.Ui;

public sealed class AnchoredPopupWindow : Window
{
    private readonly BarViewModel _viewModel;
    private readonly IAppLogger _logger;
    private readonly BarPopupSurfaceKind _popupKind;
    private readonly bool _isPinned;
    private readonly Func<RectInt32> _barBoundsResolver;
    private readonly Func<RectInt32?> _anchorResolver;
    private readonly Action<BarPopupSurfaceKind> _dismissRequested;
    private readonly Action<bool> _hoverChanged;
    private bool _surfacePrepared;
    private bool _isVisible;
    private bool _suppressDismissRequests;

    public AnchoredPopupWindow(
        BarViewModel viewModel,
        IAppLogger logger,
        BarPopupSurfaceKind popupKind,
        bool isPinned,
        Func<RectInt32> barBoundsResolver,
        Func<RectInt32?> anchorResolver,
        Action<BarPopupSurfaceKind> dismissRequested,
        Action<bool> hoverChanged)
    {
        _viewModel = viewModel;
        _logger = logger;
        _popupKind = popupKind;
        _isPinned = isPinned;
        _barBoundsResolver = barBoundsResolver;
        _anchorResolver = anchorResolver;
        _dismissRequested = dismissRequested;
        _hoverChanged = hoverChanged;

        var content = BuildContent();
        content.DataContext = _viewModel;
        Content = content;

        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public BarPopupSurfaceKind PopupKind => _popupKind;

    public bool IsPinned => _isPinned;

    public void PrewarmShellSurface()
    {
        if (_surfacePrepared)
        {
            return;
        }

        ConfigureWindow(showWindow: true, offscreenWarmup: true);
        _surfacePrepared = true;

        _suppressDismissRequests = true;
        try
        {
            Activate();
            CompleteWarmupLayout();
            ShellSurfaceWindowing.HideCompanionSurface(this);
            _isVisible = false;
        }
        finally
        {
            _suppressDismissRequests = false;
        }
    }

    public void ShowSurface()
    {
        if (!_surfacePrepared)
        {
            PrewarmShellSurface();
        }

        ConfigureWindow(showWindow: false, offscreenWarmup: false);
        ShellSurfaceWindowing.ShowCompanionSurface(this, noActivate: !_isPinned);
        _isVisible = true;

        if (_isPinned)
        {
            Activate();
        }
    }

    public void RefreshPlacement()
    {
        if (_surfacePrepared)
        {
            ConfigureWindow(showWindow: _isVisible, offscreenWarmup: false);
        }
    }

    public void EnsureShellSurfaceZOrder()
    {
        if (_surfacePrepared && _isVisible)
        {
            ShellSurfaceWindowing.EnsureTopmost(this, noActivate: !_isPinned);
        }
    }

    private FrameworkElement BuildContent()
    {
        var root = ShellSurfaceVisualFactory.CreateRootSurfaceGrid();

        root.PointerEntered += OnRootPointerEntered;
        root.PointerExited += OnRootPointerExited;

        var escapeAccelerator = new KeyboardAccelerator
        {
            Key = VirtualKey.Escape,
        };
        escapeAccelerator.Invoked += OnEscapeAcceleratorInvoked;
        root.KeyboardAccelerators.Add(escapeAccelerator);

        var border = ShellSurfaceVisualFactory.CreateSurfaceChrome(
            BuildPopupContent(),
            new CornerRadius(14),
            new Thickness(12, 12, 12, 12),
            22);

        root.Children.Add(border);
        return root;
    }

    private UIElement BuildPopupContent()
    {
        return _popupKind switch
        {
            BarPopupSurfaceKind.Resources => BuildResourcesPopup(),
            BarPopupSurfaceKind.Workspaces => BuildWorkspacesPopup(),
            _ => BuildClockPopup(),
        };
    }

    private UIElement BuildResourcesPopup()
    {
        var stack = new StackPanel
        {
            Spacing = 10,
        };

        stack.Children.Add(ShellSurfaceVisualFactory.CreateTextBlock("RESOURCES", "#948F94", 10, FontWeights.SemiBold, 120));
        var grid = new Grid
        {
            ColumnSpacing = 10,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var ramColumn = ShellSurfaceVisualFactory.CreatePanel(CreateResourcesColumn(
            "RAM",
            ("Used:", $"{nameof(BarViewModel.Resources)}.{nameof(BarResourceSectionViewModel.Popup)}.{nameof(BarResourcePopupSectionViewModel.RamUsedText)}"),
            ("Free:", $"{nameof(BarViewModel.Resources)}.{nameof(BarResourceSectionViewModel.Popup)}.{nameof(BarResourcePopupSectionViewModel.RamFreeText)}"),
            ("Total:", $"{nameof(BarViewModel.Resources)}.{nameof(BarResourceSectionViewModel.Popup)}.{nameof(BarResourcePopupSectionViewModel.RamTotalText)}")));
        grid.Children.Add(ramColumn);

        var temperatureColumn = ShellSurfaceVisualFactory.CreatePanel(CreateResourcesColumn(
            "Temperature",
            ("CPU:", $"{nameof(BarViewModel.Resources)}.{nameof(BarResourceSectionViewModel.Popup)}.{nameof(BarResourcePopupSectionViewModel.CpuTemperatureText)}"),
            ("GPU:", $"{nameof(BarViewModel.Resources)}.{nameof(BarResourceSectionViewModel.Popup)}.{nameof(BarResourcePopupSectionViewModel.GpuTemperatureText)}")));
        Grid.SetColumn(temperatureColumn, 1);
        grid.Children.Add(temperatureColumn);

        var cpuColumn = ShellSurfaceVisualFactory.CreatePanel(CreateResourcesColumn(
            "CPU",
            ("Load:", $"{nameof(BarViewModel.Resources)}.{nameof(BarResourceSectionViewModel.Popup)}.{nameof(BarResourcePopupSectionViewModel.CpuLoadText)}"),
            ("GPU:", $"{nameof(BarViewModel.Resources)}.{nameof(BarResourceSectionViewModel.Popup)}.{nameof(BarResourcePopupSectionViewModel.GpuLoadText)}")));
        Grid.SetColumn(cpuColumn, 2);
        grid.Children.Add(cpuColumn);

        stack.Children.Add(grid);

        return stack;
    }

    private UIElement BuildClockPopup()
    {
        var stack = new StackPanel
        {
            Spacing = 10,
        };

        stack.Children.Add(ShellSurfaceVisualFactory.CreateTextBlock("CLOCK", "#948F94", 10, FontWeights.SemiBold, 120));

        stack.Children.Add(ShellSurfaceVisualFactory.CreatePanel(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.Clock)}.{nameof(BarClockSectionViewModel.CurrentTime)}", "#E7E1E7", 24, FontWeights.SemiBold),
                ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.Clock)}.{nameof(BarClockSectionViewModel.CurrentDate)}", "#E7E1E7", 12, FontWeights.SemiBold),
            },
        }));

        stack.Children.Add(ShellSurfaceVisualFactory.CreatePanel(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.ActiveContext)}.{nameof(BarActiveContextSectionViewModel.ActiveWorkspaceLabel)}", "#E7E1E7", 12, FontWeights.SemiBold),
                ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.ActiveContext)}.{nameof(BarActiveContextSectionViewModel.ActiveWindowAppName)}", "#CBC5CA", 10, FontWeights.SemiBold),
            },
        }));

        stack.Children.Add(ShellSurfaceVisualFactory.CreateTextBlock(
            "click keeps this popup anchored until a different transient surface opens",
            "#CBC5CA",
            10,
            FontWeights.SemiBold));

        return stack;
    }

    private UIElement BuildWorkspacesPopup()
    {
        var stack = new StackPanel
        {
            Spacing = 10,
        };

        stack.Children.Add(ShellSurfaceVisualFactory.CreateTextBlock("WORKSPACES", "#948F94", 10, FontWeights.SemiBold, 120));

        stack.Children.Add(ShellSurfaceVisualFactory.CreatePanel(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.ActiveContext)}.{nameof(BarActiveContextSectionViewModel.ActiveWorkspaceLabel)}", "#E7E1E7", 13, FontWeights.SemiBold),
                ShellSurfaceVisualFactory.CreateSummaryRow("active id", nameof(BarViewModel.ActiveWorkspaceIdText)),
                ShellSurfaceVisualFactory.CreateSummaryRow("visible strip", nameof(BarViewModel.VisibleWorkspaceCount)),
                ShellSurfaceVisualFactory.CreateSummaryRow("occupied", nameof(BarViewModel.OccupiedWorkspaceCount)),
            },
        }));

        stack.Children.Add(ShellSurfaceVisualFactory.CreateInfoRow("left click", "switch workspace through FlowtileWM"));
        stack.Children.Add(ShellSurfaceVisualFactory.CreateInfoRow("right click", "open overview intent"));
        stack.Children.Add(ShellSurfaceVisualFactory.CreateInfoRow("wheel", "cycle visible workspaces"));

        return stack;
    }

    private FrameworkElement CreateResourcesColumn(string title, params (string Label, string BindingPath)[] rows)
    {
        var stack = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
        };

        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = ShellSurfaceVisualFactory.CreateBrush("#CBC4CB"),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                ShellSurfaceVisualFactory.CreateTextBlock(title, "#CBC5CA", 11, FontWeights.SemiBold),
            },
        });

        var rowsStack = new StackPanel
        {
            Spacing = 4,
        };

        foreach (var row in rows)
        {
            rowsStack.Children.Add(CreateResourcePopupRow(row.Label, row.BindingPath));
        }

        stack.Children.Add(rowsStack);
        return stack;
    }

    private FrameworkElement CreateResourcePopupRow(string label, string bindingPath)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(ShellSurfaceVisualFactory.CreateTextBlock(label, "#CBC5CA", 10, FontWeights.SemiBold));

        var valueBlock = ShellSurfaceVisualFactory.CreateBoundTextBlock(bindingPath, "#E7E1E7", 10, FontWeights.SemiBold);
        valueBlock.Margin = new Thickness(12, 0, 0, 0);
        valueBlock.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);

        return grid;
    }

    private void ConfigureWindow(bool showWindow, bool offscreenWarmup)
    {
        var anchorBounds = _anchorResolver();
        if (anchorBounds is null)
        {
            _dismissRequested(_popupKind);
            return;
        }

        var barHeight = ShellSurfaceWindowing.GetBarHeight();
        var monitorBounds = ShellSurfaceWindowing.ResolveShellAnchorBounds(
            this,
            _viewModel.SurfacePlacement,
            barHeight,
            out var anchorSource);
        var barBounds = _barBoundsResolver();

        var (width, height) = _popupKind switch
        {
            BarPopupSurfaceKind.Resources => (468, 168),
            BarPopupSurfaceKind.Workspaces => (304, 188),
            _ => (276, 188),
        };

        var horizontalInset = 10;
        var x = _popupKind is BarPopupSurfaceKind.Clock or BarPopupSurfaceKind.Workspaces
            ? anchorBounds.Value.X + ((anchorBounds.Value.Width - width) / 2)
            : anchorBounds.Value.X;

        var monitorLeft = monitorBounds.X + horizontalInset;
        var monitorRight = monitorBounds.X + monitorBounds.Width - horizontalInset - width;
        x = Math.Max(monitorLeft, Math.Min(x, monitorRight));

        var y = barBounds.Y + barBounds.Height;
        var bottomLimit = monitorBounds.Y + monitorBounds.Height - horizontalInset - height;

        if (y > bottomLimit)
        {
            y = Math.Max(monitorBounds.Y + horizontalInset, barBounds.Y - height);
        }

        if (offscreenWarmup)
        {
            x = -32000;
            y = -32000;
        }

        var bounds = new RectInt32(x, y, width, height);
        ShellSurfaceWindowing.PrepareCompanionSurface(
            this,
            _popupKind switch
            {
                BarPopupSurfaceKind.Resources => "FlowShellBar.Popup.Resources",
                BarPopupSurfaceKind.Workspaces => "FlowShellBar.Popup.Workspaces",
                _ => "FlowShellBar.Popup.Clock",
            },
            bounds,
            noActivate: !_isPinned,
            showWindow: showWindow);

        _logger.Info(
            $"AnchoredPopupWindow configured: kind={_popupKind}; pinned={_isPinned}; warmup={offscreenWarmup}; visible={_isVisible}; anchor={anchorSource}; client {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y}).");
    }

    private void OnEscapeAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isPinned)
        {
            _dismissRequested(_popupKind);
            args.Handled = true;
        }
    }

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hoverChanged(true);
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hoverChanged(false);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_suppressDismissRequests)
        {
            return;
        }

        if (_isPinned && args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _dismissRequested(_popupKind);
            return;
        }

        EnsureShellSurfaceZOrder();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_surfacePrepared && e.PropertyName == nameof(BarViewModel.SurfacePlacement))
        {
            ConfigureWindow(showWindow: _isVisible, offscreenWarmup: false);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnWindowActivated;
        Closed -= OnWindowClosed;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _hoverChanged(false);
    }

    private void CompleteWarmupLayout()
    {
        if (Content is FrameworkElement frameworkElement)
        {
            frameworkElement.UpdateLayout();
        }
    }

}
