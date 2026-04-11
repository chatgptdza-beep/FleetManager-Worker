using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FleetManager.Desktop.Converters;

internal static class BrushCache
{
    internal static SolidColorBrush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

public class EqualityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = BrushCache.Frozen("#143A33");
    private static readonly SolidColorBrush InactiveBrush = BrushCache.Frozen("#162532");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return DependencyProperty.UnsetValue;
        return value.ToString() == parameter.ToString() ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EqualityToBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = BrushCache.Frozen("#47C1A8");
    private static readonly SolidColorBrush InactiveBrush = BrushCache.Frozen("#2C3A43");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return DependencyProperty.UnsetValue;
        return value.ToString() == parameter.ToString() ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EqualityToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = BrushCache.Frozen("#F8F5F1");
    private static readonly SolidColorBrush InactiveBrush = BrushCache.Frozen("#B9C4CD");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return DependencyProperty.UnsetValue;
        return value.ToString() == parameter.ToString() ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
