using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VRK_WPF.MVVM.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = false;
        if (value is bool b)
        {
            boolValue = b;
        }

        bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        if (inverse)
            boolValue = !boolValue;


        Visibility falseVisibility = Visibility.Collapsed; 
        if (parameter is string s2 && s2.Equals("Hidden", StringComparison.OrdinalIgnoreCase))
            falseVisibility = Visibility.Hidden; 
        if (parameter is string s3) 
            if (s3.Contains("Hidden", StringComparison.OrdinalIgnoreCase)) falseVisibility = Visibility.Hidden;


        return boolValue ? Visibility.Visible : falseVisibility;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isVisible = false;
        if (value is Visibility v)
        {
            isVisible = (v == Visibility.Visible);
        }

        bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        if (inverse)
        {
            isVisible = !isVisible;
        }

        return isVisible;
    }
}


[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}