// InverseBooleanConverter.cs (for WinUI 3)
using Microsoft.UI.Xaml.Data; // Use WinUI 3 namespace for IValueConverter
using System;

// Ensure this namespace matches your WinUI 3 project structure
namespace WinUIAppSqlRecommendation
{
    public class InverseBooleanConverter : IValueConverter
    {
        // Converts true to false and false to true.
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            // Default or fallback if the input is not a boolean
            // Depending on requirements, you might return DependencyProperty.UnsetValue, true, false, or throw an exception.
            // Returning 'true' matches the original WPF version's implicit behavior for non-bools.
            return true;
        }

        // ConvertBack is typically not needed for one-way bindings like IsEnabled.
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // This matches the original WPF version
            throw new NotImplementedException("InverseBooleanConverter ConvertBack is not implemented.");
        }
    }
}