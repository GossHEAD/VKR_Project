using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VRK_WPF.MVVM.Converters;

/// <summary>
    /// Converts a boolean value to a System.Windows.Visibility value.
    /// True typically maps to Visible, False maps to Collapsed.
    /// Can be customized using ConverterParameter.
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BooleanToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Converts a boolean to a Visibility value.
        /// </summary>
        /// <param name="value">The boolean value to convert.</param>
        /// <param name="targetType">The type of the binding target property (expected to be Visibility).</param>
        /// <param name="parameter">Optional parameter. If "Inverse", reverses the conversion. If "Hidden", uses Hidden instead of Collapsed for False.</param>
        /// <param name="culture">The culture to use in the converter (not used).</param>
        /// <returns>Visible if value is true, Collapsed/Hidden if value is false.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = false;
            if (value is bool b)
            {
                boolValue = b;
            }

            // Check for inverse parameter
            bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
            if (inverse)
            {
                boolValue = !boolValue;
            }

            // Determine visibility for False state
            Visibility falseVisibility = Visibility.Collapsed; // Default to Collapsed
            if (parameter is string s2 && s2.Equals("Hidden", StringComparison.OrdinalIgnoreCase))
            {
                falseVisibility = Visibility.Hidden; // Use Hidden if specified
            }
             // Allow combining Inverse and Hidden, e.g., parameter="Inverse,Hidden"
             if (parameter is string s3) {
                 if (s3.Contains("Hidden", StringComparison.OrdinalIgnoreCase)) falseVisibility = Visibility.Hidden;
                 // Inverse logic already applied above
             }


            return boolValue ? Visibility.Visible : falseVisibility;
        }

        /// <summary>
        /// Converts a Visibility value back to a boolean (typically not needed for one-way bindings).
        /// </summary>
        /// <param name="value">The Visibility value to convert.</param>
        /// <param name="targetType">The type to convert to (expected to be boolean).</param>
        /// <param name="parameter">Optional parameter (e.g., "Inverse").</param>
        /// <param name="culture">The culture to use in the converter (not used).</param>
        /// <returns>True if value is Visible, false otherwise.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = false;
            if (value is Visibility v)
            {
                isVisible = (v == Visibility.Visible);
            }

            // Check for inverse parameter
            bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
            if (inverse)
            {
                isVisible = !isVisible;
            }

            return isVisible;
            // Or throw new NotSupportedException("Cannot convert back from Visibility to Boolean");
        }
    }
    
    
    /// <summary>
    /// Converts a boolean value to its inverse (true to false, false to true).
    /// Useful for binding scenarios where the opposite boolean state is needed.
    /// </summary>
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBooleanConverter : IValueConverter
    {
        /// <summary>
        /// Converts a boolean value to its inverse.
        /// </summary>
        /// <param name="value">The boolean value to convert.</param>
        /// <param name="targetType">The type of the binding target property (expected to be boolean).</param>
        /// <param name="parameter">Converter parameter (not used).</param>
        /// <param name="culture">The culture to use in the converter (not used).</param>
        /// <returns>The inverse of the input boolean value.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            // Return false or Binding.DoNothing if the value is not a boolean?
            // Returning false is often safer for IsEnabled bindings.
            return false;
        }

        /// <summary>
        /// Converts the inverse boolean value back to the original.
        /// </summary>
        /// <param name="value">The inverse boolean value.</param>
        /// <param name="targetType">The type to convert to (expected to be boolean).</param>
        /// <param name="parameter">Converter parameter (not used).</param>
        /// <param name="culture">The culture to use in the converter (not used).</param>
        /// <returns>The original boolean value.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
            // Or throw new NotSupportedException("Cannot convert back from non-boolean.");
        }
    }