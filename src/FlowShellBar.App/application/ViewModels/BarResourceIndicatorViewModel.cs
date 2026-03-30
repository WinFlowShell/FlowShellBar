using Microsoft.UI.Xaml.Media;

namespace FlowShellBar.App.Application.ViewModels;

public sealed class BarResourceIndicatorViewModel : BindableBase
{
    private int _progressValue;
    private string _valueText = string.Empty;
    private Brush? _indicatorBrush;

    public int ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public Brush? IndicatorBrush
    {
        get => _indicatorBrush;
        set => SetProperty(ref _indicatorBrush, value);
    }
}
