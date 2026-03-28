using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace FlowShellBar.App.Ui.Converters;

public sealed class WorkspaceForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Colors.White);
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromArgb(255, 200, 206, 219));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
