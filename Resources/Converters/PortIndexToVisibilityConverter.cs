using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NINA.PINS.Resources.Converters {
    /// <summary>
    /// Shows the row of a port (ConverterParameter = port index) only if the box has that
    /// port (value = number of ports, 0 = none). All rows are shown while the number is not
    /// known yet (negative).
    /// </summary>
    public class PortIndexToVisibilityConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is int count && count >= 0 &&
                int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) {
                return index < count ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return Binding.DoNothing;
        }
    }
}
