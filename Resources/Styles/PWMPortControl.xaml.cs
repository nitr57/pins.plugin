using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NINA.PINS.Resources.Styles {

    /// <summary>
    /// Interaktionslogik für PWMPortControl.xaml
    /// </summary>
    public partial class PWMPortControl : UserControl {

        public PWMPortControl() {
            InitializeComponent();
        }

        private void SetButton_Click(object sender, RoutedEventArgs e) {
            // Find the stepper control and read its current staged value
            var stepper = FindIntStepperControl(this);
            if (stepper != null) {
                // Apply the staged value to the actual port property
                System.Diagnostics.Debug.WriteLine($"SetButton clicked: Setting PortAdjValue to {stepper.Value}");
                PortAdjValue = stepper.Value;
                System.Diagnostics.Debug.WriteLine($"PortAdjValue is now: {PortAdjValue}");
            } else {
                System.Diagnostics.Debug.WriteLine("SetButton clicked: Stepper control not found!");
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

        private static void onPortTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            // When PortText changes, update the binding source if it exists
            if (d is PWMPortControl control && e.NewValue != e.OldValue) {
                // This will trigger the binding update back to the source
            }
        }

        public string PortName {
            get { return (string)GetValue(PortNameProperty); }
            set { SetValue(PortNameProperty, value); }
        }

        public static readonly DependencyProperty PortNameProperty =
            DependencyProperty.Register("PortName", typeof(string), typeof(PWMPortControl), new PropertyMetadata(string.Empty));

        public string PortText {
            get { return (string)GetValue(PortTextProperty); }
            set { SetValue(PortTextProperty, value); }
        }

        public static readonly DependencyProperty PortTextProperty =
            DependencyProperty.Register("PortText", typeof(string), typeof(PWMPortControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    onPortTextChanged));

        public bool PortState {
            get { return (bool)GetValue(PortStateProperty); }
            set { SetValue(PortStateProperty, value); }
        }

        public static readonly DependencyProperty PortStateProperty =
            DependencyProperty.Register("PortState", typeof(bool), typeof(PWMPortControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string PortCurrent {
            get { return (string)GetValue(PortCurrentProperty); }
            set { SetValue(PortCurrentProperty, value); }
        }

        public static readonly DependencyProperty PortCurrentProperty =
            DependencyProperty.Register("PortCurrent", typeof(string), typeof(PWMPortControl), new PropertyMetadata(string.Empty));

        public bool PortOvercurrent {
            get { return (bool)GetValue(PortOvercurrentProperty); }
            set { SetValue(PortOvercurrentProperty, value); }
        }

        public static readonly DependencyProperty PortOvercurrentProperty =
            DependencyProperty.Register("PortOvercurrent", typeof(bool), typeof(PWMPortControl), new PropertyMetadata(false));

        public bool PortBootstrap {
            get { return (bool)GetValue(PortBootstrapProperty); }
            set { SetValue(PortBootstrapProperty, value); }
        }

        public static readonly DependencyProperty PortBootstrapProperty =
            DependencyProperty.Register("PortBootstrap", typeof(bool), typeof(PWMPortControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int PortNumber {
            get { return (int)GetValue(PortNumberProperty); }
            set { SetValue(PortNumberProperty, value); }
        }

        public static readonly DependencyProperty PortNumberProperty =
            DependencyProperty.Register("PortNumber", typeof(int), typeof(PWMPortControl), new PropertyMetadata(-1));

        public int PortAdjValue {
            get { return (int)GetValue(PortAdjValueProperty); }
            set { SetValue(PortAdjValueProperty, value); }
        }

        public static readonly DependencyProperty PortAdjValueProperty =
            DependencyProperty.Register("PortAdjValue", typeof(int), typeof(PWMPortControl), new PropertyMetadata(0));

        public int PortValue {
            get { return (int)GetValue(PortValueProperty); }
            set { SetValue(PortValueProperty, value); }
        }

        public static readonly DependencyProperty PortValueProperty =
            DependencyProperty.Register("PortValue", typeof(int), typeof(PWMPortControl), new PropertyMetadata(0));

        public int PortDutyCycle {
            get { return (int)GetValue(PortDutyCycleProperty); }
            set { SetValue(PortDutyCycleProperty, value); }
        }

        public static readonly DependencyProperty PortDutyCycleProperty =
            DependencyProperty.Register("PortDutyCycle", typeof(int), typeof(PWMPortControl), new PropertyMetadata(1));
    }
}