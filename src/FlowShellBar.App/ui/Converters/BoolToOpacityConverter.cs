using Microsoft.UI.Xaml.Data;

namespace FlowShellBar.App.Ui.Converters;

public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? 1.0 : 0.42;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
