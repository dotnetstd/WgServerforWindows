using System;
using System.Globalization;
using System.Windows.Data;

namespace WgServerforWindows.Views
{
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string enumValue = value.ToString();
            string paramValue = parameter.ToString();
            return enumValue.Equals(paramValue, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return null;

            bool boolValue = (bool)value;
            if (boolValue)
            {
                string paramValue = parameter.ToString();
                return Enum.Parse(targetType, paramValue, true);
            }

            return null;
        }
    }
}