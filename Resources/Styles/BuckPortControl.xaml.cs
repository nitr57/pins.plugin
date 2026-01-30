using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NINA.PINS.Resources.Styles {

    /// <summary>
    /// Interaktionslogik für BuckPortControl.xaml
    /// </summary>
    public partial class BuckPortControl : UserControl {

        public BuckPortControl() {
            InitializeComponent();
        }

        private void SetButton_Click(object sender, RoutedEventArgs e) {
            // Find the stepper control and read its current staged value
            var stepper = FindStepperControl(this);
            if (stepper != null) {
                // Apply the staged value to the actual port property
                System.Diagnostics.Debug.WriteLine($"SetButton clicked: Setting PortAdjValue to {stepper.Value}");
                PortAdjValue = stepper.Value;
                System.Diagnostics.Debug.WriteLine($"PortAdjValue is now: {PortAdjValue}");
            } else {
                System.Diagnostics.Debug.WriteLine("SetButton clicked: Stepper control not found!");
            }
        }

        private NINA.CustomControlLibrary.StepperControl? FindStepperControl(DependencyObject obj) {
            if (obj is NINA.CustomControlLibrary.StepperControl stepper) {
                return stepper;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++) {
                var child = VisualTreeHelper.GetChild(obj, i);
                var result = FindStepperControl(child);
                if (result != null) {
                    return result;
                }
            }
            return null;
        }

        private static void onPortTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            // When PortText changes, update the binding source if it exists
            if (d is BuckPortControl control && e.NewValue != e.OldValue) {
                // This will trigger the binding update back to the source
            }
        }

        public string PortName {
            get { return (string)GetValue(PortNameProperty); }
            set { SetValue(PortNameProperty, value); }
        }

        public static readonly DependencyProperty PortNameProperty =
            DependencyProperty.Register("PortName", typeof(string), typeof(BuckPortControl), new PropertyMetadata(string.Empty));

        public string PortText {
            get { return (string)GetValue(PortTextProperty); }
            set { SetValue(PortTextProperty, value); }
        }

        public static readonly DependencyProperty PortTextProperty =
            DependencyProperty.Register("PortText", typeof(string), typeof(BuckPortControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    onPortTextChanged));

        public bool PortState {
            get { return (bool)GetValue(PortStateProperty); }
            set { SetValue(PortStateProperty, value); }
        }

        public static readonly DependencyProperty PortStateProperty =
            DependencyProperty.Register("PortState", typeof(bool), typeof(BuckPortControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string PortCurrent {
            get { return (string)GetValue(PortCurrentProperty); }
            set { SetValue(PortCurrentProperty, value); }
        }

        public static readonly DependencyProperty PortCurrentProperty =
            DependencyProperty.Register("PortCurrent", typeof(string), typeof(BuckPortControl), new PropertyMetadata(string.Empty));

        public bool PortOvercurrent {
            get { return (bool)GetValue(PortOvercurrentProperty); }
            set { SetValue(PortOvercurrentProperty, value); }
        }

        public static readonly DependencyProperty PortOvercurrentProperty =
            DependencyProperty.Register("PortOvercurrent", typeof(bool), typeof(BuckPortControl), new PropertyMetadata(false));

        public bool PortBootstrap {
            get { return (bool)GetValue(PortBootstrapProperty); }
            set { SetValue(PortBootstrapProperty, value); }
        }

        public static readonly DependencyProperty PortBootstrapProperty =
            DependencyProperty.Register("PortBootstrap", typeof(bool), typeof(BuckPortControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int PortNumber {
            get { return (int)GetValue(PortNumberProperty); }
            set { SetValue(PortNumberProperty, value); }
        }

        public static readonly DependencyProperty PortNumberProperty =
            DependencyProperty.Register("PortNumber", typeof(int), typeof(BuckPortControl), new PropertyMetadata(-1));

        public double PortAdjValue {
            get { return (double)GetValue(PortAdjValueProperty); }
            set { SetValue(PortAdjValueProperty, value); }
        }

        public static readonly DependencyProperty PortAdjValueProperty =
            DependencyProperty.Register("PortAdjValue", typeof(double), typeof(BuckPortControl), 
                new PropertyMetadata(0.0));

        public double PortValue {
            get { return (double)GetValue(PortValueProperty); }
            set { SetValue(PortValueProperty, value); }
        }

        public static readonly DependencyProperty PortValueProperty =
            DependencyProperty.Register("PortValue", typeof(double), typeof(BuckPortControl), new PropertyMetadata(0.0));

        public double PortMinValue {
            get { return (double)GetValue(PortMinValueProperty); }
            set { SetValue(PortMinValueProperty, value); }
        }

        public static readonly DependencyProperty PortMinValueProperty =
            DependencyProperty.Register("PortMinValue", typeof(double), typeof(BuckPortControl), new PropertyMetadata(0.0));

        public double PortMaxValue {
            get { return (double)GetValue(PortMaxValueProperty); }
            set { SetValue(PortMaxValueProperty, value); }
        }

        public static readonly DependencyProperty PortMaxValueProperty =
            DependencyProperty.Register("PortMaxValue", typeof(double), typeof(BuckPortControl), new PropertyMetadata(0.0));
    }
}