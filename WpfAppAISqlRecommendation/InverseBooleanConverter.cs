namespace SqlPerformanceAiAdvisor
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true; // Default if not a bool
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException(); // Not needed for one-way binding
        }
    }
}