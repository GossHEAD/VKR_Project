﻿using System.Globalization;
using System.Windows.Data;

namespace VRK_WPF.MVVM.Converters;

public class NullToFalseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}