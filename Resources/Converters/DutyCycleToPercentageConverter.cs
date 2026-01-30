using System;
using System.Globalization;
using System.Windows.Data;

namespace NINA.PINS.Resources.Converters {
    public class DutyCycleToPercentageConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length == 2 && 
                values[0] is int currentValue && 
                values[1] is int maxValue && 
                maxValue > 0) {
                double percentage = (currentValue / (double)maxValue) * 100.0;
                return $"{Math.Round(percentage, 1)}%";
            }
            return "0%";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
