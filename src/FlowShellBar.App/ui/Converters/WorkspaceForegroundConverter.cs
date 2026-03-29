using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace FlowShellBar.App.Ui.Converters;

public sealed class WorkspaceForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromArgb(255, 244, 238, 234));
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromArgb(255, 153, 141, 133));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
