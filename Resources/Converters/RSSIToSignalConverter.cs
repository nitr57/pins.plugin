using System;
using System.Globalization;
using System.Windows.Data;

namespace NINA.PINS.Resources.Converters {

    public class RSSIToSignalConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            int rssi = (int)value;

            if (rssi >= -50) {
                return "strong";
            } else if (rssi >= -70) {
                return "moderate";
            } else {
                return "weak";
            }
        }

        public object ConvertBack(object value, Type targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}