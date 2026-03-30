using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FlowShellBar.App.Ui.Controls;

public sealed partial class ResourceRingView : UserControl
{
    public static readonly DependencyProperty ProgressValueProperty = DependencyProperty.Register(
        nameof(ProgressValue),
        typeof(int),
        typeof(ResourceRingView),
        new PropertyMetadata(0));

    public static readonly DependencyProperty IndicatorBrushProperty = DependencyProperty.Register(
        nameof(IndicatorBrush),
        typeof(Brush),
        typeof(ResourceRingView),
        new PropertyMetadata(new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE7, 0xE1, 0xE7))));

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText),
        typeof(string),
        typeof(ResourceRingView),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
        nameof(IconKind),
        typeof(ResourceRingIconKind),
        typeof(ResourceRingView),
        new PropertyMetadata(ResourceRingIconKind.Memory, OnIconKindChanged));

    public ResourceRingView()
    {
        InitializeComponent();
        UpdateIconVisibility();
    }

    public int ProgressValue
    {
        get => (int)GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }

    public Brush IndicatorBrush
    {
        get => (Brush)GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public ResourceRingIconKind IconKind
    {
        get => (ResourceRingIconKind)GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    private static void OnIconKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ResourceRingView)d).UpdateIconVisibility();
    }

    private void UpdateIconVisibility()
    {
        if (MemoryIcon is null || TemperatureIcon is null || CpuIcon is null)
        {
            return;
        }

        MemoryIcon.Visibility = IconKind == ResourceRingIconKind.Memory ? Visibility.Visible : Visibility.Collapsed;
        TemperatureIcon.Visibility = IconKind == ResourceRingIconKind.Temperature ? Visibility.Visible : Visibility.Collapsed;
        CpuIcon.Visibility = IconKind == ResourceRingIconKind.Cpu ? Visibility.Visible : Visibility.Collapsed;
    }
}
