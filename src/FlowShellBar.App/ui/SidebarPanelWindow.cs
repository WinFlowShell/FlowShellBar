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

internal sealed class SidebarPanelWindow : Window
{
    private readonly BarViewModel _viewModel;
    private readonly IAppLogger _logger;
    private readonly BarPanelSurfaceKind _panelKind;
    private readonly Func<RectInt32> _barBoundsResolver;
    private readonly Action<BarPanelSurfaceKind> _dismissRequested;
    private readonly Func<LeftSidebarSurfaceMode>? _leftSidebarModeResolver;
    private readonly Func<LeftSidebarCommandKind, LeftSidebarCommandSnapshot>? _leftSidebarCommandRequested;
    private Border? _surfaceChrome;
    private bool _surfacePrepared;
    private bool _isVisible;
    private bool _suppressDismissRequests;

    public SidebarPanelWindow(
        BarViewModel viewModel,
        IAppLogger logger,
        BarPanelSurfaceKind panelKind,
        Func<RectInt32> barBoundsResolver,
        Action<BarPanelSurfaceKind> dismissRequested,
        Func<LeftSidebarSurfaceMode>? leftSidebarModeResolver = null,
        Func<LeftSidebarCommandKind, LeftSidebarCommandSnapshot>? leftSidebarCommandRequested = null)
    {
        _viewModel = viewModel;
        _logger = logger;
        _panelKind = panelKind;
        _barBoundsResolver = barBoundsResolver;
        _dismissRequested = dismissRequested;
        _leftSidebarModeResolver = leftSidebarModeResolver;
        _leftSidebarCommandRequested = leftSidebarCommandRequested;

        var content = BuildContent();
        content.DataContext = _viewModel;
        Content = content;

        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

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
        ShellSurfaceWindowing.ShowCompanionSurface(this, noActivate: false);
        _isVisible = true;
        Activate();
    }

    public void HideSurface()
    {
        if (!_surfacePrepared || !_isVisible)
        {
            return;
        }

        _suppressDismissRequests = true;
        try
        {
            ShellSurfaceWindowing.HideCompanionSurface(this);
            _isVisible = false;
        }
        finally
        {
            _suppressDismissRequests = false;
        }
    }

    public void EnsureShellSurfaceZOrder()
    {
        if (_surfacePrepared && _isVisible)
        {
            ShellSurfaceWindowing.EnsureTopmost(this, noActivate: false);
        }
    }

    private FrameworkElement BuildContent()
    {
        var root = ShellSurfaceVisualFactory.CreateRootSurfaceGrid();

        var escapeAccelerator = new KeyboardAccelerator
        {
            Key = VirtualKey.Escape,
        };
        escapeAccelerator.Invoked += OnEscapeAcceleratorInvoked;
        root.KeyboardAccelerators.Add(escapeAccelerator);

        var contentStack = new StackPanel
        {
            Spacing = 16,
        };

        var headerStack = new StackPanel
        {
            Spacing = 4,
        };

        headerStack.Children.Add(ShellSurfaceVisualFactory.CreateTextBlock(
            _panelKind == BarPanelSurfaceKind.LeftSidebar ? "LEFT SIDEBAR" : "RIGHT SIDEBAR",
            "#948F94",
            10,
            FontWeights.SemiBold,
            120));
        headerStack.Children.Add(ShellSurfaceVisualFactory.CreateTextBlock(
            _panelKind == BarPanelSurfaceKind.LeftSidebar ? "FlowShell Launcher" : "FlowShell Control",
            "#E7E1E7",
            18,
            FontWeights.SemiBold));

        if (_panelKind == BarPanelSurfaceKind.LeftSidebar)
        {
            headerStack.Children.Add(CreateLeftSidebarModeRow());
            headerStack.Children.Add(CreateLeftSidebarCommandRow());
        }

        contentStack.Children.Add(headerStack);

        contentStack.Children.Add(ShellSurfaceVisualFactory.CreateInfoCard("stub surface aligned to illogical-impulse shell vocabulary"));

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _panelKind == BarPanelSurfaceKind.LeftSidebar
                ? BuildLeftSidebarStub()
                : BuildRightSidebarStub(),
        };

        contentStack.Children.Add(scrollViewer);
        _surfaceChrome = ShellSurfaceVisualFactory.CreateSurfaceChrome(
            contentStack,
            GetWindowCornerRadius(),
            new Thickness(20, 18, 20, 18),
            28);
        _surfaceChrome.Child = contentStack;
        root.Children.Add(_surfaceChrome);
        return root;
    }

    private UIElement BuildLeftSidebarStub()
    {
        var stack = ShellSurfaceVisualFactory.CreateSectionStack();

        stack.Children.Add(CreateToolbarStrip("intelligence", "translator", "anime"));

        stack.Children.Add(ShellSurfaceVisualFactory.CreateCard(
            "search",
            [
                ShellSurfaceVisualFactory.CreateTextBlock("overview search / launcher surface", "#CBC5CA", 11, FontWeights.SemiBold),
                CreateSearchBarStub("search applications, actions, files"),
            ]));

        var quickstartGrid = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
        };
        quickstartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickstartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickstartGrid.Children.Add(CreateMiniCard("apps", "launcher", 0, false));
        quickstartGrid.Children.Add(CreateMiniCard("run", "command", 1, false));
        var secondRow = new Grid();
        secondRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        secondRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        secondRow.Children.Add(CreateMiniCard("recent", "surface", 0, false));
        secondRow.Children.Add(CreateMiniCard("clipboard", "stub", 1, false));
        Grid.SetRow(secondRow, 1);
        Grid.SetColumnSpan(secondRow, 2);
        quickstartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        quickstartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.Children.Add(ShellSurfaceVisualFactory.CreateSection("quick access", quickstartGrid, secondRow));

        stack.Children.Add(ShellSurfaceVisualFactory.CreateSection(
            "context",
            ShellSurfaceVisualFactory.CreateCard(
                "active workspace",
                [
                    ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.ActiveContext)}.{nameof(BarActiveContextSectionViewModel.ActiveWorkspaceLabel)}", "#E7E1E7", 13, FontWeights.SemiBold),
                    ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.ActiveContext)}.{nameof(BarActiveContextSectionViewModel.ActiveWindowAppName)}", "#CBC5CA", 10, FontWeights.SemiBold),
                ],
                "#2D2A2F"),
            ShellSurfaceVisualFactory.CreateCard(
                "focus",
                [
                    ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.ActiveContext)}.{nameof(BarActiveContextSectionViewModel.ActiveWindowTitle)}", "#E7E1E7", 12, FontWeights.SemiBold),
                    ShellSurfaceVisualFactory.CreateTextBlock("overview card / pinned actions placeholder", "#CBC5CA", 10, FontWeights.SemiBold),
                ])));

        stack.Children.Add(ShellSurfaceVisualFactory.CreateSection(
            "suggested",
            CreateListCard("terminal", "favorite session entry", "#CBC4CB"),
            CreateListCard("browser", "workspace launcher slot", "#D1C3C6"),
            CreateListCard("files", "recent or pinned target", "#CAC5C8")));

        return stack;
    }

    private UIElement BuildRightSidebarStub()
    {
        var stack = ShellSurfaceVisualFactory.CreateSectionStack();

        stack.Children.Add(CreateTopSystemRow());

        var controlsGrid = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
        };
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controlsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        controlsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        controlsGrid.Children.Add(CreateQuickToggleCard("audio", "mixer", 0, 0));
        controlsGrid.Children.Add(CreateQuickToggleCard("wifi", "network", 1, 0));
        controlsGrid.Children.Add(CreateQuickToggleCard("bluetooth", "devices", 0, 1));
        controlsGrid.Children.Add(CreateQuickToggleCard("night", "display", 1, 1));
        stack.Children.Add(ShellSurfaceVisualFactory.CreateSection("quick toggles", controlsGrid));

        stack.Children.Add(ShellSurfaceVisualFactory.CreateSection(
            "center widgets",
            ShellSurfaceVisualFactory.CreateCard(
                "session",
                [
                    ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.Status)}.{nameof(BarStatusSectionViewModel.RuntimeModeLabel)}", "#E7E1E7", 13, FontWeights.SemiBold),
                    ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.Status)}.{nameof(BarStatusSectionViewModel.ConnectionStateLabel)}", "#CBC5CA", 10, FontWeights.SemiBold),
                ],
                "#222124"),
            ShellSurfaceVisualFactory.CreateCard(
                "surface state",
                [
                    ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.Clock)}.{nameof(BarClockSectionViewModel.CurrentTime)}", "#E7E1E7", 12, FontWeights.SemiBold),
                    ShellSurfaceVisualFactory.CreateBoundTextBlock($"{nameof(BarViewModel.Clock)}.{nameof(BarClockSectionViewModel.CurrentDate)}", "#CBC5CA", 10, FontWeights.SemiBold),
                ])));

        stack.Children.Add(ShellSurfaceVisualFactory.CreateSection(
            "bottom widgets",
            CreateNotificationCard("shell update", "stub notification card"),
            CreateNotificationCard("session health", "runtime and diagnostics placeholder"),
            CreateNotificationCard("connectivity", "audio / network / notifications surface")));

        return stack;
    }

    private void ConfigureWindow(bool showWindow, bool offscreenWarmup)
    {
        var barHeight = ShellSurfaceWindowing.GetBarHeight();
        var monitorBounds = ShellSurfaceWindowing.ResolveShellAnchorBounds(
            this,
            _viewModel.SurfacePlacement,
            barHeight,
            out var anchorSource);
        var barBounds = _barBoundsResolver();
        var panelTop = barBounds.Y + barBounds.Height;
        var width = _panelKind == BarPanelSurfaceKind.LeftSidebar
            ? GetLeftSidebarWidth()
            : ShellSurfaceWindowing.GetRightSidebarWidth();
        var monitorBottom = monitorBounds.Y + monitorBounds.Height;
        var height = Math.Max(260, monitorBottom - panelTop);
        var y = panelTop;
        var x = _panelKind == BarPanelSurfaceKind.LeftSidebar
            ? monitorBounds.X
            : monitorBounds.X + monitorBounds.Width - width;

        if (_panelKind == BarPanelSurfaceKind.LeftSidebar)
        {
            switch (GetCurrentLeftSidebarMode())
            {
                case LeftSidebarSurfaceMode.Detached:
                    width = ShellSurfaceWindowing.GetLeftSidebarDetachedWidth();
                    height = Math.Min(Math.Max(420, monitorBounds.Height - 112), Math.Max(420, monitorBounds.Height - 72));
                    x = monitorBounds.X + 24;
                    y = monitorBounds.Y + barHeight + 24;
                    break;

                case LeftSidebarSurfaceMode.Pinned:
                    width = ShellSurfaceWindowing.GetLeftSidebarPinnedWidth();
                    height = monitorBounds.Height;
                    x = monitorBounds.X;
                    y = monitorBounds.Y;
                    break;
            }
        }

        if (offscreenWarmup)
        {
            x = -32000;
            y = -32000;
        }

        var bounds = new RectInt32(x, y, width, height);
        ShellSurfaceWindowing.PrepareCompanionSurface(
            this,
            _panelKind == BarPanelSurfaceKind.LeftSidebar ? "FlowShellBar.LeftSidebar" : "FlowShellBar.RightSidebar",
            bounds,
            noActivate: false,
            showWindow: showWindow);

        _logger.Info(
            $"SidebarPanelWindow configured: kind={_panelKind}; warmup={offscreenWarmup}; visible={_isVisible}; anchor={anchorSource}; client {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y}).");
    }

    private void OnEscapeAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _dismissRequested(_panelKind);
        args.Handled = true;
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_suppressDismissRequests)
        {
            return;
        }

        if (args.WindowActivationState == WindowActivationState.Deactivated
            && ShouldDismissOnDeactivation())
        {
            _dismissRequested(_panelKind);
            return;
        }

        EnsureShellSurfaceZOrder();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_surfacePrepared
            && (e.PropertyName == nameof(BarViewModel.SurfacePlacement)
                || (_panelKind == BarPanelSurfaceKind.LeftSidebar && e.PropertyName == nameof(BarViewModel.LeftSidebarMode))))
        {
            if (_surfaceChrome is not null)
            {
                _surfaceChrome.CornerRadius = GetWindowCornerRadius();
            }

            ConfigureWindow(showWindow: _isVisible, offscreenWarmup: false);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnWindowActivated;
        Closed -= OnWindowClosed;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void CompleteWarmupLayout()
    {
        if (Content is FrameworkElement frameworkElement)
        {
            frameworkElement.UpdateLayout();
        }
    }

    private static UIElement CreateSoftSlot(string text)
    {
        return ShellSurfaceVisualFactory.CreatePanel(
            ShellSurfaceVisualFactory.CreateTextBlock(text, "#CBC5CA", 10, FontWeights.SemiBold),
            "#252327",
            padding: new Thickness(10, 8, 10, 8),
            cornerRadius: new CornerRadius(10));
    }

    private UIElement CreateLeftSidebarModeRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                ShellSurfaceVisualFactory.CreateTextBlock("host", "#948F94", 10, FontWeights.SemiBold),
                ShellSurfaceVisualFactory.CreateBoundTextBlock(nameof(BarViewModel.LeftSidebarModeLabel), "#E7E1E7", 11, FontWeights.SemiBold),
            },
        };

        return ShellSurfaceVisualFactory.CreatePanel(
            row,
            background: "#1B1A1C",
            borderColor: "#343136",
            padding: new Thickness(12, 8, 12, 8),
            cornerRadius: new CornerRadius(14));
    }

    private UIElement CreateLeftSidebarCommandRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                CreateLeftCommandChip("ATT", LeftSidebarCommandKind.Attach),
                CreateLeftCommandChip("DET", LeftSidebarCommandKind.Detach),
                CreateLeftCommandChip("PIN", LeftSidebarCommandKind.Pin),
                CreateLeftCommandChip("X", LeftSidebarCommandKind.Close),
            },
        };

        return row;
    }

    private static UIElement CreateToolbarStrip(params string[] items)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };

        for (var index = 0; index < items.Length; index++)
        {
            row.Children.Add(new Border
            {
                Background = ShellSurfaceVisualFactory.CreateBrush(index == 0 ? "#2D2A2F" : "#242225"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 6, 12, 6),
                Child = ShellSurfaceVisualFactory.CreateTextBlock(items[index], index == 0 ? "#E7E1E7" : "#CBC5CA", 10, FontWeights.SemiBold),
            });
        }

        return ShellSurfaceVisualFactory.CreatePanel(
            row,
            borderColor: "#343136",
            padding: new Thickness(6),
            cornerRadius: new CornerRadius(18));
    }

    private UIElement CreateLeftCommandChip(string label, LeftSidebarCommandKind commandKind)
    {
        var border = new Border
        {
            Background = ShellSurfaceVisualFactory.CreateBrush("#1D1C1F"),
            BorderBrush = ShellSurfaceVisualFactory.CreateBrush("#343136"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 6, 10, 6),
            Child = ShellSurfaceVisualFactory.CreateTextBlock(label, "#CBC5CA", 10, FontWeights.SemiBold),
        };
        border.Tapped += (_, _) => _leftSidebarCommandRequested?.Invoke(commandKind);
        return border;
    }

    private static UIElement CreateSearchBarStub(string placeholder)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftGlyph = ShellSurfaceVisualFactory.CreateTextBlock("S", "#CBC5CA", 10, FontWeights.SemiBold);
        leftGlyph.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(leftGlyph);

        var placeholderText = ShellSurfaceVisualFactory.CreateTextBlock(placeholder, "#948F94", 10, FontWeights.SemiBold);
        placeholderText.Margin = new Thickness(10, 0, 10, 0);
        placeholderText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(placeholderText, 1);
        grid.Children.Add(placeholderText);

        var lensAction = new Border
        {
            Background = ShellSurfaceVisualFactory.CreateBrush("#2A282D"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Child = ShellSurfaceVisualFactory.CreateTextBlock("L", "#CBC5CA", 9, FontWeights.SemiBold),
        };
        Grid.SetColumn(lensAction, 2);
        grid.Children.Add(lensAction);

        var musicAction = new Border
        {
            Background = ShellSurfaceVisualFactory.CreateBrush("#2A282D"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Child = ShellSurfaceVisualFactory.CreateTextBlock("M", "#CBC5CA", 9, FontWeights.SemiBold),
        };
        Grid.SetColumn(musicAction, 3);
        grid.Children.Add(musicAction);

        var shortcut = new Border
        {
            Background = ShellSurfaceVisualFactory.CreateBrush("#2A282D"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Child = ShellSurfaceVisualFactory.CreateTextBlock("Ctrl K", "#CBC5CA", 9, FontWeights.SemiBold),
        };
        Grid.SetColumn(shortcut, 4);
        grid.Children.Add(shortcut);

        return ShellSurfaceVisualFactory.CreatePanel(
            grid,
            background: "#1B1A1C",
            borderColor: "#343136",
            padding: new Thickness(12, 10, 12, 10),
            cornerRadius: new CornerRadius(16));
    }

    private static UIElement CreateTopSystemRow()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var uptime = new Border
        {
            Background = ShellSurfaceVisualFactory.CreateBrush("#1D1C1F"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12, 7, 12, 7),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    ShellSurfaceVisualFactory.CreateTextBlock("Fs", "#E7E1E7", 11, FontWeights.SemiBold),
                    ShellSurfaceVisualFactory.CreateTextBlock("Up 04:12", "#CBC5CA", 10, FontWeights.SemiBold),
                },
            },
        };
        grid.Children.Add(uptime);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                CreateToolbarActionButton("reload"),
                CreateToolbarActionButton("settings"),
                CreateToolbarActionButton("power"),
            },
        };
        var actionGroup = ShellSurfaceVisualFactory.CreatePanel(
            actions,
            padding: new Thickness(6),
            cornerRadius: new CornerRadius(18));
        Grid.SetColumn(actionGroup, 2);
        grid.Children.Add(actionGroup);

        return grid;
    }

    private static UIElement CreateToolbarActionButton(string label)
    {
        var text = ShellSurfaceVisualFactory.CreateTextBlock(label.Substring(0, 1).ToUpperInvariant(), "#CBC5CA", 11, FontWeights.SemiBold);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;

        return new Border
        {
            Width = 32,
            Height = 32,
            Background = ShellSurfaceVisualFactory.CreateBrush("#1D1C1F"),
            BorderBrush = ShellSurfaceVisualFactory.CreateBrush("#343136"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = text,
        };
    }

    private static UIElement CreateQuickToggleCard(string title, string subtitle, int column, int row)
    {
        var border = new Border
        {
            Background = ShellSurfaceVisualFactory.CreateBrush("#1D1C1F"),
            BorderBrush = ShellSurfaceVisualFactory.CreateBrush("#343136"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10, 12, 10),
            Child = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    ShellSurfaceVisualFactory.CreateTextBlock(title, "#E7E1E7", 11, FontWeights.SemiBold),
                    ShellSurfaceVisualFactory.CreateTextBlock(subtitle, "#CBC5CA", 10, FontWeights.SemiBold),
                },
            },
        };

        Grid.SetColumn(border, column);
        Grid.SetRow(border, row);
        return border;
    }

    private static UIElement CreateMiniCard(string title, string subtitle, int column, bool withRightMargin)
    {
        var border = new Border
        {
            Background = ShellSurfaceVisualFactory.CreateBrush("#1D1C1F"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 12, 10, 12),
            Margin = withRightMargin ? new Thickness(0, 0, 8, 0) : new Thickness(0),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    ShellSurfaceVisualFactory.CreateTextBlock(title, "#E7E1E7", 12, FontWeights.SemiBold),
                    ShellSurfaceVisualFactory.CreateTextBlock(subtitle, "#CBC5CA", 10, FontWeights.SemiBold),
                },
            },
        };

        Grid.SetColumn(border, column);
        return border;
    }

    private static UIElement CreateListCard(string title, string subtitle, string dotColor)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = ShellSurfaceVisualFactory.CreateBrush(dotColor),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var textStack = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(10, 0, 0, 0),
            Children =
            {
                ShellSurfaceVisualFactory.CreateTextBlock(title, "#E7E1E7", 12, FontWeights.SemiBold),
                ShellSurfaceVisualFactory.CreateTextBlock(subtitle, "#CBC5CA", 10, FontWeights.SemiBold),
            },
        };

        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        return ShellSurfaceVisualFactory.CreatePanel(row);
    }

    private static UIElement CreateNotificationCard(string title, string subtitle)
    {
        return ShellSurfaceVisualFactory.CreatePanel(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                ShellSurfaceVisualFactory.CreateTextBlock(title, "#E7E1E7", 12, FontWeights.SemiBold),
                ShellSurfaceVisualFactory.CreateTextBlock(subtitle, "#CBC5CA", 10, FontWeights.SemiBold),
            },
        });
    }

    private LeftSidebarSurfaceMode GetCurrentLeftSidebarMode()
    {
        return _panelKind == BarPanelSurfaceKind.LeftSidebar && _leftSidebarModeResolver is not null
            ? _leftSidebarModeResolver()
            : LeftSidebarSurfaceMode.Hidden;
    }

    private bool ShouldDismissOnDeactivation()
    {
        if (_panelKind != BarPanelSurfaceKind.LeftSidebar)
        {
            return true;
        }

        return GetCurrentLeftSidebarMode() == LeftSidebarSurfaceMode.Attached;
    }

    private CornerRadius GetWindowCornerRadius()
    {
        if (_panelKind != BarPanelSurfaceKind.LeftSidebar)
        {
            return new CornerRadius(18, 0, 0, 18);
        }

        return GetCurrentLeftSidebarMode() switch
        {
            LeftSidebarSurfaceMode.Detached => new CornerRadius(18),
            LeftSidebarSurfaceMode.Pinned => new CornerRadius(0, 18, 18, 0),
            _ => new CornerRadius(0, 18, 18, 0),
        };
    }

    private int GetLeftSidebarWidth()
    {
        return GetCurrentLeftSidebarMode() switch
        {
            LeftSidebarSurfaceMode.Detached => ShellSurfaceWindowing.GetLeftSidebarDetachedWidth(),
            LeftSidebarSurfaceMode.Pinned => ShellSurfaceWindowing.GetLeftSidebarPinnedWidth(),
            _ => ShellSurfaceWindowing.GetLeftSidebarAttachedWidth(),
        };
    }
}
