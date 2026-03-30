using System.ComponentModel;

using FlowShellBar.App.Application;
using FlowShellBar.App.Application.ViewModels;
using FlowShellBar.App.Diagnostics;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Text;

using Windows.Graphics;
using Windows.System;

namespace FlowShellBar.App.Ui;

public sealed class SidebarPanelWindow : Window
{
    private readonly BarViewModel _viewModel;
    private readonly IAppLogger _logger;
    private readonly BarPanelSurfaceKind _panelKind;
    private readonly Func<RectInt32> _barBoundsResolver;
    private readonly Action<BarPanelSurfaceKind> _dismissRequested;
    private bool _surfacePrepared;
    private bool _isVisible;
    private bool _suppressDismissRequests;

    public SidebarPanelWindow(
        BarViewModel viewModel,
        IAppLogger logger,
        BarPanelSurfaceKind panelKind,
        Func<RectInt32> barBoundsResolver,
        Action<BarPanelSurfaceKind> dismissRequested)
    {
        _viewModel = viewModel;
        _logger = logger;
        _panelKind = panelKind;
        _barBoundsResolver = barBoundsResolver;
        _dismissRequested = dismissRequested;

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
        var root = new Grid
        {
            Background = CreateBrush("#12100F"),
        };

        var escapeAccelerator = new KeyboardAccelerator
        {
            Key = VirtualKey.Escape,
        };
        escapeAccelerator.Invoked += OnEscapeAcceleratorInvoked;
        root.KeyboardAccelerators.Add(escapeAccelerator);

        var chrome = new Border
        {
            Background = CreateBrush("#171412"),
            BorderBrush = CreateBrush("#2B2521"),
            BorderThickness = new Thickness(1),
            CornerRadius = _panelKind == BarPanelSurfaceKind.LeftSidebar
                ? new CornerRadius(0, 18, 18, 0)
                : new CornerRadius(18, 0, 0, 18),
            Padding = new Thickness(20, 18, 20, 18),
        };

        var contentStack = new StackPanel
        {
            Spacing = 16,
        };

        contentStack.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                CreateTextBlock(_panelKind == BarPanelSurfaceKind.LeftSidebar ? "LEFT SIDEBAR" : "RIGHT SIDEBAR", "#877C74", 10, FontWeights.SemiBold, 120),
                CreateTextBlock(_panelKind == BarPanelSurfaceKind.LeftSidebar ? "FlowShell Launcher" : "FlowShell Control", "#F1E9E3", 18, FontWeights.SemiBold),
            },
        });

        contentStack.Children.Add(CreateInfoCard("stub surface aligned to iNiR window mass"));

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _panelKind == BarPanelSurfaceKind.LeftSidebar
                ? BuildLeftSidebarStub()
                : BuildRightSidebarStub(),
        };

        contentStack.Children.Add(scrollViewer);
        chrome.Child = contentStack;
        root.Children.Add(chrome);
        return root;
    }

    private UIElement BuildLeftSidebarStub()
    {
        var stack = CreateSectionStack();

        stack.Children.Add(CreateCard(
            "launcher",
            [
                CreateTextBlock("quick search / command palette stub", "#F1E9E3", 12, FontWeights.SemiBold),
                CreateSoftSlot("type to search applications, files, commands"),
            ]));

        var quickstartGrid = new Grid();
        quickstartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickstartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickstartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickstartGrid.Children.Add(CreateMiniCard("apps", "launcher", 0, true));
        quickstartGrid.Children.Add(CreateMiniCard("run", "shell action", 1, true));
        quickstartGrid.Children.Add(CreateMiniCard("recent", "stub items", 2, false));
        stack.Children.Add(CreateSection("quickstart", quickstartGrid));

        stack.Children.Add(CreateSection(
            "pinned",
            CreateListCard("terminal", "favorite session entry", "#E8DDD5"),
            CreateListCard("browser", "workspace launcher slot", "#D8D0C8"),
            CreateListCard("files", "recent or pinned target", "#C5BEB7")));

        stack.Children.Add(CreateCard(
            "session",
            [
                CreateBoundTextBlock(nameof(BarViewModel.ActiveWorkspaceLabel), "#F1E9E3", 12, FontWeights.SemiBold),
                CreateBoundTextBlock(nameof(BarViewModel.ActiveWindowAppName), "#B8ADA4", 10, FontWeights.SemiBold),
            ],
            "#1E3A2D"));

        return stack;
    }

    private UIElement BuildRightSidebarStub()
    {
        var stack = CreateSectionStack();

        stack.Children.Add(CreateCard(
            "overview",
            [
                CreateBoundTextBlock(nameof(BarViewModel.CurrentTime), "#F1E9E3", 12, FontWeights.SemiBold),
                CreateBoundTextBlock(nameof(BarViewModel.CurrentDate), "#B8ADA4", 10, FontWeights.SemiBold),
            ]));

        var controlsGrid = new Grid();
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controlsGrid.Children.Add(CreateMiniCard("audio", "mixer stub", 0, true));
        controlsGrid.Children.Add(CreateMiniCard("network", "connectivity stub", 1, false));
        stack.Children.Add(CreateSection("controls", controlsGrid));

        stack.Children.Add(CreateSection(
            "notifications",
            CreateNotificationCard("shell update", "stub notification card"),
            CreateNotificationCard("session health", "runtime and diagnostics placeholder")));

        stack.Children.Add(CreateCard(
            "status",
            [
                CreateBoundTextBlock(nameof(BarViewModel.RuntimeModeLabel), "#F1E9E3", 12, FontWeights.SemiBold),
                CreateBoundTextBlock(nameof(BarViewModel.ConnectionStateLabel), "#B8ADA4", 10, FontWeights.SemiBold),
            ],
            "#221D19"));

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

        var width = _panelKind == BarPanelSurfaceKind.LeftSidebar ? 372 : 392;
        var monitorBottom = monitorBounds.Y + monitorBounds.Height;
        var height = Math.Max(260, monitorBottom - panelTop);
        var y = panelTop;
        var x = _panelKind == BarPanelSurfaceKind.LeftSidebar
            ? monitorBounds.X
            : monitorBounds.X + monitorBounds.Width - width;

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

        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _dismissRequested(_panelKind);
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
    }

    private void CompleteWarmupLayout()
    {
        if (Content is FrameworkElement frameworkElement)
        {
            frameworkElement.UpdateLayout();
        }
    }

    private static StackPanel CreateSectionStack()
    {
        return new StackPanel
        {
            Spacing = 18,
        };
    }

    private static UIElement CreateSection(string title, params UIElement[] body)
    {
        var stack = new StackPanel
        {
            Spacing = 10,
        };

        stack.Children.Add(CreateTextBlock(title, "#B8ADA4", 11, FontWeights.SemiBold));
        foreach (var child in body)
        {
            stack.Children.Add(child);
        }

        return stack;
    }

    private static UIElement CreateCard(string title, IEnumerable<UIElement> body, string? background = null)
    {
        var border = new Border
        {
            Background = CreateBrush(background ?? "#1F1A17"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12),
        };

        var stack = new StackPanel
        {
            Spacing = 8,
        };

        stack.Children.Add(CreateTextBlock(title, "#B8ADA4", 11, FontWeights.SemiBold));
        foreach (var child in body)
        {
            stack.Children.Add(child);
        }

        border.Child = stack;
        return border;
    }

    private static UIElement CreateInfoCard(string text)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(CreateTextBlock(">", "#B8ADA4", 10, FontWeights.SemiBold));

        var textBlock = CreateTextBlock(text, "#B8ADA4", 10, FontWeights.SemiBold);
        textBlock.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(textBlock, 1);
        grid.Children.Add(textBlock);

        return new Border
        {
            Background = CreateBrush("#1F1A17"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid,
        };
    }

    private static UIElement CreateSoftSlot(string text)
    {
        return new Border
        {
            Background = CreateBrush("#2C2522"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Child = CreateTextBlock(text, "#B8ADA4", 10, FontWeights.SemiBold),
        };
    }

    private static UIElement CreateMiniCard(string title, string subtitle, int column, bool withRightMargin)
    {
        var border = new Border
        {
            Background = CreateBrush("#1F1A17"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 12, 10, 12),
            Margin = withRightMargin ? new Thickness(0, 0, 8, 0) : new Thickness(0),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    CreateTextBlock(title, "#F1E9E3", 12, FontWeights.SemiBold),
                    CreateTextBlock(subtitle, "#B8ADA4", 10, FontWeights.SemiBold),
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
            Fill = CreateBrush(dotColor),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var textStack = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(10, 0, 0, 0),
            Children =
            {
                CreateTextBlock(title, "#F1E9E3", 12, FontWeights.SemiBold),
                CreateTextBlock(subtitle, "#B8ADA4", 10, FontWeights.SemiBold),
            },
        };

        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        return new Border
        {
            Background = CreateBrush("#1F1A17"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Child = row,
        };
    }

    private static UIElement CreateNotificationCard(string title, string subtitle)
    {
        return new Border
        {
            Background = CreateBrush("#1F1A17"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    CreateTextBlock(title, "#F1E9E3", 12, FontWeights.SemiBold),
                    CreateTextBlock(subtitle, "#B8ADA4", 10, FontWeights.SemiBold),
                },
            },
        };
    }

    private static TextBlock CreateTextBlock(string text, string color, double size, Windows.UI.Text.FontWeight weight, int characterSpacing = 0)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = CreateBrush(color),
            FontFamily = new FontFamily("JetBrains Mono"),
            FontSize = size,
            FontWeight = weight,
            CharacterSpacing = characterSpacing,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private static TextBlock CreateBoundTextBlock(string path, string color, double size, Windows.UI.Text.FontWeight weight)
    {
        var textBlock = CreateTextBlock(string.Empty, color, size, weight);
        textBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath(path),
        });
        return textBlock;
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var hex = color.TrimStart('#');
        if (hex.Length == 6)
        {
            hex = $"FF{hex}";
        }

        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16),
            Convert.ToByte(hex.Substring(6, 2), 16)));
    }
}
