using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NINA.PINS.Resources.Styles {

    /// <summary>
    /// Interaktionslogik für DewControl.xaml
    /// </summary>
    public partial class DewControl : UserControl {

        public DewControl() {
            InitializeComponent();
        }

        private void SetButton_Click(object sender, RoutedEventArgs e) {
            // Find the stepper control and read its current staged value
            var stepper = FindIntStepperControl(this);
            if (stepper != null) {
                // Apply the staged value to the actual port property
                System.Diagnostics.Debug.WriteLine($"Dew SetButton clicked: Setting PortSetPower to {stepper.Value}");
                PortSetPower = stepper.Value;
                System.Diagnostics.Debug.WriteLine($"PortTemp is now: {PortSetPower}");
            } else {
                System.Diagnostics.Debug.WriteLine("Dew SetButton clicked: Stepper control not found!");
            }
        }

        private CustomControlLibrary.IntStepperControl? FindIntStepperControl(DependencyObject obj) {
            if (obj is CustomControlLibrary.IntStepperControl stepper) {
                return stepper;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++) {
                var child = VisualTreeHelper.GetChild(obj, i);
                var result = FindIntStepperControl(child);
                if (result != null) {
                    return result;
                }
            }
            return null;
        }

        private static void onDewTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            // When PortText changes, update the binding source if it exists
            if (d is DewControl control && e.NewValue != e.OldValue) {
                // This will trigger the binding update back to the source
            }
        }

        public string PortName {
            get { return (string)GetValue(PortNameProperty); }
            set { SetValue(PortNameProperty, value); }
        }

        public static readonly DependencyProperty PortNameProperty =
            DependencyProperty.Register("PortName", typeof(string), typeof(DewControl), new PropertyMetadata(string.Empty));

        public string PortText {
            get { return (string)GetValue(PortTextProperty); }
            set { SetValue(PortTextProperty, value); }
        }

        public static readonly DependencyProperty PortTextProperty =
            DependencyProperty.Register("PortText", typeof(string), typeof(DewControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    onDewTextChanged));

        public bool PortState {
            get { return (bool)GetValue(PortStateProperty); }
            set { SetValue(PortStateProperty, value); }
        }

        public static readonly DependencyProperty PortStateProperty =
            DependencyProperty.Register("PortState", typeof(bool), typeof(DewControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string PortCurrent {
            get { return (string)GetValue(PortCurrentProperty); }
            set { SetValue(PortCurrentProperty, value); }
        }

        public static readonly DependencyProperty PortCurrentProperty =
            DependencyProperty.Register("PortCurrent", typeof(string), typeof(DewControl), new PropertyMetadata(string.Empty));

        public bool PortOvercurrent {
            get { return (bool)GetValue(PortOvercurrentProperty); }
            set { SetValue(PortOvercurrentProperty, value); }
        }

        public static readonly DependencyProperty PortOvercurrentProperty =
            DependencyProperty.Register("PortOvercurrent", typeof(bool), typeof(DewControl), new PropertyMetadata(false));

        public bool PortAutoDew {
            get { return (bool)GetValue(PortAutoDewProperty); }
            set { SetValue(PortAutoDewProperty, value); }
        }

        public static readonly DependencyProperty PortAutoDewProperty =
            DependencyProperty.Register("PortAutoDew", typeof(bool), typeof(DewControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int PortNumber {
            get { return (int)GetValue(PortNumberProperty); }
            set { SetValue(PortNumberProperty, value); }
        }

        public static readonly DependencyProperty PortNumberProperty =
            DependencyProperty.Register("PortNumber", typeof(int), typeof(DewControl), new PropertyMetadata(-1));

        public int PortSetPower {
            get { return (int)GetValue(PortSetPowerProperty); }
            set { SetValue(PortSetPowerProperty, value); }
        }

        public static readonly DependencyProperty PortSetPowerProperty =
            DependencyProperty.Register("PortSetPower", typeof(int), typeof(DewControl), 
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int PortValue {
            get { return (int)GetValue(PortValueProperty); }
            set { SetValue(PortValueProperty, value); }
        }

        public static readonly DependencyProperty PortValueProperty =
            DependencyProperty.Register("PortValue", typeof(int), typeof(DewControl), new PropertyMetadata(0));

        public int PortDutyCycle {
            get { return (int)GetValue(PortDutyCycleProperty); }
            set { SetValue(PortDutyCycleProperty, value); }
        }

        public static readonly DependencyProperty PortDutyCycleProperty =
            DependencyProperty.Register("PortDutyCycle", typeof(int), typeof(DewControl), new PropertyMetadata(1));

        public double PortTemperature {
            get { return (double)GetValue(PortTemperatureProperty); }
            set { SetValue(PortTemperatureProperty, value); }
        }

        public static readonly DependencyProperty PortTemperatureProperty =
            DependencyProperty.Register("PortTemperature", typeof(double), typeof(DewControl), new PropertyMetadata(127.0));

        public double PortDeltaT {
            get { return (double)GetValue(PortDeltaTProperty); }
            set { SetValue(PortDeltaTProperty, value); }
        }

        public static readonly DependencyProperty PortDeltaTProperty =
            DependencyProperty.Register("PortDeltaT", typeof(double), typeof(DewControl),
                new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    }
}