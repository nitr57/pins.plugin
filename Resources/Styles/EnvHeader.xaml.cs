using System.Windows;
using System.Windows.Controls;

namespace NINA.PINS.Resources.Styles {

    /// <summary>
    /// Interaktionslogik für EnvHeader.xaml
    /// </summary>
    public partial class EnvHeader : UserControl {

        public EnvHeader() {
            InitializeComponent();
        }

        public double EnvVoltage {
            get { return (double)GetValue(EnvVoltageProperty); }
            set { SetValue(EnvVoltageProperty, value); }
        }

        public static readonly DependencyProperty EnvVoltageProperty =
            DependencyProperty.Register("EnvVoltage", typeof(double), typeof(EnvHeader), new PropertyMetadata(0.0));

        public double EnvCurrent {
            get { return (double)GetValue(EnvCurrentProperty); }
            set { SetValue(EnvCurrentProperty, value); }
        }

        public static readonly DependencyProperty EnvCurrentProperty =
            DependencyProperty.Register("EnvCurrent", typeof(double), typeof(EnvHeader), new PropertyMetadata(0.0));

        public double EnvTemperature {
            get { return (double)GetValue(EnvTemperatureProperty); }
            set { SetValue(EnvTemperatureProperty, value); }
        }

        public static readonly DependencyProperty EnvTemperatureProperty =
            DependencyProperty.Register("EnvTemperature", typeof(double), typeof(EnvHeader), new PropertyMetadata(0.0));

        public double EnvHumidity {
            get { return (double)GetValue(EnvHumidityProperty); }
            set { SetValue(EnvHumidityProperty, value); }
        }

        public static readonly DependencyProperty EnvHumidityProperty =
            DependencyProperty.Register("EnvHumidity", typeof(double), typeof(EnvHeader), new PropertyMetadata(0.0));

        public double EnvDewPoint {
            get { return (double)GetValue(EnvDewPointProperty); }
            set { SetValue(EnvDewPointProperty, value); }
        }

        public static readonly DependencyProperty EnvDewPointProperty =
            DependencyProperty.Register("EnvDewPoint", typeof(double), typeof(EnvHeader), new PropertyMetadata(0.0));

        public string EnvUpTime {
            get { return (string)GetValue(EnvUpTimeProperty); }
            set { SetValue(EnvUpTimeProperty, value); }
        }

        public static readonly DependencyProperty EnvUpTimeProperty =
            DependencyProperty.Register("EnvUpTime", typeof(string), typeof(EnvHeader), new PropertyMetadata(string.Empty));
    }
}