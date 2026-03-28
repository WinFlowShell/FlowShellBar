using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace FlowShellBar.App.Ui.Converters;

public sealed class WorkspaceOccupiedBorderConverter : IValueConverter
{
    private static readonly SolidColorBrush OccupiedBrush = new(Color.FromArgb(255, 56, 67, 84));
    private static readonly SolidColorBrush EmptyBrush = new(Color.FromArgb(255, 35, 41, 52));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? OccupiedBrush : EmptyBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
