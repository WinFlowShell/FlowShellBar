using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace FlowShellBar.App.Ui.Converters;

public sealed class WorkspaceActiveToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromArgb(255, 85, 126, 250));
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromArgb(255, 27, 33, 43));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
