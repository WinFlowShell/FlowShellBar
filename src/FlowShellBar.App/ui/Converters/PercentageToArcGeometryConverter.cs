using System.Globalization;

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;

namespace FlowShellBar.App.Ui.Converters;

public sealed class PercentageToArcGeometryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var percentage = value switch
        {
            int intValue => intValue,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0d,
        };

        var size = parameter switch
        {
            double doubleSize => doubleSize,
            int intSize => intSize,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 16d,
        };

        return BuildGeometry(size, Math.Clamp(percentage, 0d, 100d) / 100d);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static Geometry BuildGeometry(double size, double progress)
    {
        var geometry = new PathGeometry();

        if (progress <= 0d || size <= 0d)
        {
            return geometry;
        }

        var strokeInset = 1.45d;
        var radius = Math.Max(0d, (size / 2d) - strokeInset);
        var center = size / 2d;
        var startPoint = new Point(center, center - radius);

        if (progress >= 0.9999d)
        {
            var fullFigure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false,
                IsFilled = false,
            };

            fullFigure.Segments.Add(new ArcSegment
            {
                Point = new Point(center, center + radius),
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = false,
            });

            fullFigure.Segments.Add(new ArcSegment
            {
                Point = startPoint,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = false,
            });

            geometry.Figures.Add(fullFigure);
            return geometry;
        }

        var endAngleDegrees = -90d + (progress * 360d);
        var endAngleRadians = (Math.PI / 180d) * endAngleDegrees;
        var endPoint = new Point(
            center + (radius * Math.Cos(endAngleRadians)),
            center + (radius * Math.Sin(endAngleRadians)));

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false,
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = progress >= 0.5d,
        });

        geometry.Figures.Add(figure);
        return geometry;
    }
}
