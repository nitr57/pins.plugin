using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NINA.PINS.Resources.Converters {
    /// <summary>
    /// Shows the row of a port only if the box has that port. Single binding: value = number of
    /// ports (0 = none), ConverterParameter = port index. Multi binding: values = { port index,
    /// number of ports }. All rows are shown while the number is not known yet (negative) or a
    /// value is not available (e.g. during item generation).
    /// </summary>
    public class PortIndexToVisibilityConverter : IValueConverter, IMultiValueConverter {
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

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values != null && values.Length >= 2 &&
                values[0] is int index && values[1] is int count && count >= 0) {
                return index < count ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            return null;
        }
    }
}
