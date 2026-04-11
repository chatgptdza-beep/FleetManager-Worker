using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FleetManager.Desktop.Converters;

public class EqualityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return DependencyProperty.UnsetValue;
        
        bool isEqual = value.ToString() == parameter.ToString();
        
        // Return #143A33 if equal (active), else #162532 (inactive)
        return isEqual 
            ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#143A33"))
            : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#162532"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class EqualityToBorderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return DependencyProperty.UnsetValue;
        
        bool isEqual = value.ToString() == parameter.ToString();
        
        // Return #47C1A8 if equal (active), else #2C3A43 (inactive)
        return isEqual 
            ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#47C1A8"))
            : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2C3A43"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
