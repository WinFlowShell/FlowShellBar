using System.Numerics;
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
            Background = CreateBrush("#0F0E0E"),
        };

        var escapeAccelerator = new KeyboardAccelerator
        {
            Key = VirtualKey.Escape,
        };
        escapeAccelerator.Invoked += OnEscapeAcceleratorInvoked;
        root.KeyboardAccelerators.Add(escapeAccelerator);

        var chrome = new Border
        {
            Background = CreateBrush("#151416"),
            BorderBrush = CreateBrush("#302D31"),
            BorderThickness = new Thickness(1),
            CornerRadius = _panelKind == BarPanelSurfaceKind.LeftSidebar
                ? new CornerRadius(0, 18, 18, 0)
                : new CornerRadius(18, 0, 0, 18),
            Padding = new Thickness(20, 18, 20, 18),
            Shadow = new ThemeShadow(),
            Translation = new Vector3(0, 0, 28),
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
                CreateTextBlock(_panelKind == BarPanelSurfaceKind.LeftSidebar ? "LEFT SIDEBAR" : "RIGHT SIDEBAR", "#948F94", 10, FontWeights.SemiBold, 120),
                CreateTextBlock(_panelKind == BarPanelSurfaceKind.LeftSidebar ? "FlowShell Launcher" : "FlowShell Control", "#E7E1E7", 18, FontWeights.SemiBold),
            },
        });

        contentStack.Children.Add(CreateInfoCard("stub surface aligned to illogical-impulse shell vocabulary"));

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

        stack.Children.Add(CreateToolbarStrip("intelligence", "translator", "anime"));

        stack.Children.Add(CreateCard(
            "search",
            [
                CreateTextBlock("overview search / launcher surface", "#CBC5CA", 11, FontWeights.SemiBold),
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
        stack.Children.Add(CreateSection("quick access", quickstartGrid, secondRow));

        stack.Children.Add(CreateSection(
            "context",
            CreateCard(
                "active workspace",
                [
                    CreateBoundTextBlock(nameof(BarViewModel.ActiveWorkspaceLabel), "#E7E1E7", 13, FontWeights.SemiBold),
                    CreateBoundTextBlock(nameof(BarViewModel.ActiveWindowAppName), "#CBC5CA", 10, FontWeights.SemiBold),
                ],
                "#2D2A2F"),
            CreateCard(
                "focus",
                [
                    CreateBoundTextBlock(nameof(BarViewModel.ActiveWindowTitle), "#E7E1E7", 12, FontWeights.SemiBold),
                    CreateTextBlock("overview card / pinned actions placeholder", "#CBC5CA", 10, FontWeights.SemiBold),
                ])));

        stack.Children.Add(CreateSection(
            "suggested",
            CreateListCard("terminal", "favorite session entry", "#CBC4CB"),
            CreateListCard("browser", "workspace launcher slot", "#D1C3C6"),
            CreateListCard("files", "recent or pinned target", "#CAC5C8")));

        return stack;
    }

    private UIElement BuildRightSidebarStub()
    {
        var stack = CreateSectionStack();

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
        stack.Children.Add(CreateSection("quick toggles", controlsGrid));

        stack.Children.Add(CreateSection(
            "center widgets",
            CreateCard(
                "session",
                [
                    CreateBoundTextBlock(nameof(BarViewModel.RuntimeModeLabel), "#E7E1E7", 13, FontWeights.SemiBold),
                    CreateBoundTextBlock(nameof(BarViewModel.ConnectionStateLabel), "#CBC5CA", 10, FontWeights.SemiBold),
                ],
                "#222124"),
            CreateCard(
                "surface state",
                [
                    CreateBoundTextBlock(nameof(BarViewModel.CurrentTime), "#E7E1E7", 12, FontWeights.SemiBold),
                    CreateBoundTextBlock(nameof(BarViewModel.CurrentDate), "#CBC5CA", 10, FontWeights.SemiBold),
                ])));

        stack.Children.Add(CreateSection(
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

        var width = 460;
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

        stack.Children.Add(CreateTextBlock(title, "#CBC5CA", 11, FontWeights.SemiBold));
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
            Background = CreateBrush(background ?? "#1D1C1F"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12),
        };

        var stack = new StackPanel
        {
            Spacing = 8,
        };

        stack.Children.Add(CreateTextBlock(title, "#CBC5CA", 11, FontWeights.SemiBold));
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

        grid.Children.Add(CreateTextBlock(">", "#CBC5CA", 10, FontWeights.SemiBold));

        var textBlock = CreateTextBlock(text, "#CBC5CA", 10, FontWeights.SemiBold);
        textBlock.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(textBlock, 1);
        grid.Children.Add(textBlock);

        return new Border
        {
            Background = CreateBrush("#1D1C1F"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid,
        };
    }

    private static UIElement CreateSoftSlot(string text)
    {
        return new Border
        {
            Background = CreateBrush("#252327"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Child = CreateTextBlock(text, "#CBC5CA", 10, FontWeights.SemiBold),
        };
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
                Background = CreateBrush(index == 0 ? "#2D2A2F" : "#242225"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 6, 12, 6),
                Child = CreateTextBlock(items[index], index == 0 ? "#E7E1E7" : "#CBC5CA", 10, FontWeights.SemiBold),
            });
        }

        return new Border
        {
            Background = CreateBrush("#1D1C1F"),
            BorderBrush = CreateBrush("#343136"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(6),
            Child = row,
        };
    }

    private static UIElement CreateSearchBarStub(string placeholder)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftGlyph = CreateTextBlock("S", "#CBC5CA", 10, FontWeights.SemiBold);
        leftGlyph.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(leftGlyph);

        var placeholderText = CreateTextBlock(placeholder, "#948F94", 10, FontWeights.SemiBold);
        placeholderText.Margin = new Thickness(10, 0, 10, 0);
        placeholderText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(placeholderText, 1);
        grid.Children.Add(placeholderText);

        var lensAction = new Border
        {
            Background = CreateBrush("#2A282D"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Child = CreateTextBlock("L", "#CBC5CA", 9, FontWeights.SemiBold),
        };
        Grid.SetColumn(lensAction, 2);
        grid.Children.Add(lensAction);

        var musicAction = new Border
        {
            Background = CreateBrush("#2A282D"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Child = CreateTextBlock("M", "#CBC5CA", 9, FontWeights.SemiBold),
        };
        Grid.SetColumn(musicAction, 3);
        grid.Children.Add(musicAction);

        var shortcut = new Border
        {
            Background = CreateBrush("#2A282D"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Child = CreateTextBlock("Ctrl K", "#CBC5CA", 9, FontWeights.SemiBold),
        };
        Grid.SetColumn(shortcut, 4);
        grid.Children.Add(shortcut);

        return new Border
        {
            Background = CreateBrush("#1B1A1C"),
            BorderBrush = CreateBrush("#343136"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid,
        };
    }

    private static UIElement CreateTopSystemRow()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var uptime = new Border
        {
            Background = CreateBrush("#1D1C1F"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12, 7, 12, 7),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    CreateTextBlock("Fs", "#E7E1E7", 11, FontWeights.SemiBold),
                    CreateTextBlock("Up 04:12", "#CBC5CA", 10, FontWeights.SemiBold),
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
        var actionGroup = new Border
        {
            Background = CreateBrush("#1D1C1F"),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(6),
            Child = actions,
        };
        Grid.SetColumn(actionGroup, 2);
        grid.Children.Add(actionGroup);

        return grid;
    }

    private static UIElement CreateToolbarActionButton(string label)
    {
        var text = CreateTextBlock(label.Substring(0, 1).ToUpperInvariant(), "#CBC5CA", 11, FontWeights.SemiBold);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;

        return new Border
        {
            Width = 32,
            Height = 32,
            Background = CreateBrush("#1D1C1F"),
            BorderBrush = CreateBrush("#343136"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = text,
        };
    }

    private static UIElement CreateQuickToggleCard(string title, string subtitle, int column, int row)
    {
        var border = new Border
        {
            Background = CreateBrush("#1D1C1F"),
            BorderBrush = CreateBrush("#343136"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10, 12, 10),
            Child = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    CreateTextBlock(title, "#E7E1E7", 11, FontWeights.SemiBold),
                    CreateTextBlock(subtitle, "#CBC5CA", 10, FontWeights.SemiBold),
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
            Background = CreateBrush("#1D1C1F"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 12, 10, 12),
            Margin = withRightMargin ? new Thickness(0, 0, 8, 0) : new Thickness(0),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    CreateTextBlock(title, "#E7E1E7", 12, FontWeights.SemiBold),
                    CreateTextBlock(subtitle, "#CBC5CA", 10, FontWeights.SemiBold),
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
                CreateTextBlock(title, "#E7E1E7", 12, FontWeights.SemiBold),
                CreateTextBlock(subtitle, "#CBC5CA", 10, FontWeights.SemiBold),
            },
        };

        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        return new Border
        {
            Background = CreateBrush("#1D1C1F"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Child = row,
        };
    }

    private static UIElement CreateNotificationCard(string title, string subtitle)
    {
        return new Border
        {
            Background = CreateBrush("#1D1C1F"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    CreateTextBlock(title, "#E7E1E7", 12, FontWeights.SemiBold),
                    CreateTextBlock(subtitle, "#CBC5CA", 10, FontWeights.SemiBold),
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
