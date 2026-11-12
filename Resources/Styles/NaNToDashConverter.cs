using System;
using System.Globalization;
using System.Windows.Data;

namespace NINA.PINS.Resources.Styles {
    /// <summary>
    /// Shows a measured value with its unit (ConverterParameter), or "--" if the value is
    /// unknown (NaN, null or empty; also the text of a NaN double bound to a string
    /// property, "NaN" or the NaN symbol of the culture), e.g. a USB port without a valid
    /// current measurement.
    /// </summary>
    public class NaNToDashConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            string unit = parameter as string;
            string text;
            if (value == null ||
                (value is double d && double.IsNaN(d)) ||
                (value is float f && float.IsNaN(f)) ||
                (value is string s && (s.Length == 0 || s == "NaN" ||
                                       s == NumberFormatInfo.GetInstance(culture).NaNSymbol))) {
                text = "--";
            } else {
                text = System.Convert.ToString(value, culture);
            }
            return string.IsNullOrEmpty(unit) ? text : $"{text} {unit}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return Binding.DoNothing;
        }
    }
}
