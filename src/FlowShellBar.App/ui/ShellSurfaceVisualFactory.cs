using System.Collections.Generic;
using System.Numerics;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace FlowShellBar.App.Ui;

internal static class ShellSurfaceVisualFactory
{
    public static Grid CreateRootSurfaceGrid()
    {
        return new Grid
        {
            Background = CreateBrush("#0F0E0E"),
        };
    }

    public static Border CreateSurfaceChrome(
        UIElement child,
        CornerRadius cornerRadius,
        Thickness padding,
        float elevation)
    {
        return new Border
        {
            Background = CreateBrush("#151416"),
            BorderBrush = CreateBrush("#302D31"),
            BorderThickness = new Thickness(1),
            CornerRadius = cornerRadius,
            Padding = padding,
            Shadow = new ThemeShadow(),
            Translation = new Vector3(0, 0, elevation),
            Child = child,
        };
    }

    public static StackPanel CreateSectionStack(double spacing = 18)
    {
        return new StackPanel
        {
            Spacing = spacing,
        };
    }

    public static UIElement CreateSection(string title, params UIElement[] body)
    {
        var stack = new StackPanel
        {
            Spacing = 10,
        };

        stack.Children.Add(CreateTextBlock(title, "#CBC5CA", 11, Microsoft.UI.Text.FontWeights.SemiBold));
        foreach (var child in body)
        {
            stack.Children.Add(child);
        }

        return stack;
    }

    public static Border CreatePanel(
        UIElement child,
        string background = "#1D1C1F",
        string? borderColor = null,
        Thickness? padding = null,
        CornerRadius? cornerRadius = null)
    {
        var border = new Border
        {
            Background = CreateBrush(background),
            CornerRadius = cornerRadius ?? new CornerRadius(12),
            Padding = padding ?? new Thickness(12, 10, 12, 10),
            Child = child,
        };

        if (!string.IsNullOrWhiteSpace(borderColor))
        {
            border.BorderBrush = CreateBrush(borderColor);
            border.BorderThickness = new Thickness(1);
        }

        return border;
    }

    public static UIElement CreateCard(string title, IEnumerable<UIElement> body, string? background = null)
    {
        var stack = new StackPanel
        {
            Spacing = 8,
        };

        stack.Children.Add(CreateTextBlock(title, "#CBC5CA", 11, Microsoft.UI.Text.FontWeights.SemiBold));
        foreach (var child in body)
        {
            stack.Children.Add(child);
        }

        return CreatePanel(
            stack,
            background ?? "#1D1C1F",
            padding: new Thickness(14, 12, 14, 12),
            cornerRadius: new CornerRadius(14));
    }

    public static UIElement CreateInfoCard(string text)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(CreateTextBlock(">", "#CBC5CA", 10, Microsoft.UI.Text.FontWeights.SemiBold));

        var textBlock = CreateTextBlock(text, "#CBC5CA", 10, Microsoft.UI.Text.FontWeights.SemiBold);
        textBlock.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(textBlock, 1);
        grid.Children.Add(textBlock);

        return CreatePanel(grid);
    }

    public static UIElement CreateInfoRow(string title, string body)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var titleBlock = CreateTextBlock(title, "#E7E1E7", 11, Microsoft.UI.Text.FontWeights.SemiBold);
        grid.Children.Add(titleBlock);

        var bodyBlock = CreateTextBlock(body, "#CBC5CA", 10, Microsoft.UI.Text.FontWeights.SemiBold);
        bodyBlock.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(bodyBlock, 1);
        grid.Children.Add(bodyBlock);

        return CreatePanel(grid);
    }

    public static UIElement CreateSummaryRow(string title, string bindingPath)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(CreateTextBlock(title, "#CBC5CA", 10, Microsoft.UI.Text.FontWeights.SemiBold));

        var valueBlock = CreateBoundTextBlock(bindingPath, "#E7E1E7", 11, Microsoft.UI.Text.FontWeights.SemiBold);
        valueBlock.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);

        return grid;
    }

    public static TextBlock CreateTextBlock(string text, string color, double size, Windows.UI.Text.FontWeight weight, int characterSpacing = 0)
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

    public static TextBlock CreateBoundTextBlock(string path, string color, double size, Windows.UI.Text.FontWeight weight)
    {
        var textBlock = CreateTextBlock(string.Empty, color, size, weight);
        textBlock.SetBinding(TextBlock.TextProperty, new Binding
        {
            Path = new PropertyPath(path),
        });
        return textBlock;
    }

    public static SolidColorBrush CreateBrush(string color)
    {
        var hex = color.TrimStart('#');
        if (hex.Length == 6)
        {
            hex = $"FF{hex}";
        }

        return new SolidColorBrush(ColorHelper.FromArgb(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16),
            Convert.ToByte(hex.Substring(6, 2), 16)));
    }
}
