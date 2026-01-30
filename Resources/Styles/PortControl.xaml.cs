using System.Windows;
using System.Windows.Controls;

namespace NINA.PINS.Resources.Styles {

    /// <summary>
    /// Interaktionslogik für PortControl.xaml
    /// </summary>
    public partial class PortControl : UserControl {

        public PortControl() {
            InitializeComponent();
        }

        private static void onPortTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            // When PortText changes, update the binding source if it exists
            if (d is PortControl control && e.NewValue != e.OldValue) {
                // This will trigger the binding update back to the source
            }
        }

        public string PortName {
            get { return (string)GetValue(PortNameProperty); }
            set { SetValue(PortNameProperty, value); }
        }

        public static readonly DependencyProperty PortNameProperty =
            DependencyProperty.Register("PortName", typeof(string), typeof(PortControl), new PropertyMetadata(string.Empty));

        public string PortText {
            get { return (string)GetValue(PortTextProperty); }
            set { SetValue(PortTextProperty, value); }
        }

        public static readonly DependencyProperty PortTextProperty =
            DependencyProperty.Register("PortText", typeof(string), typeof(PortControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    onPortTextChanged));

        public bool PortState {
            get { return (bool)GetValue(PortStateProperty); }
            set { SetValue(PortStateProperty, value); }
        }

        public static readonly DependencyProperty PortStateProperty =
            DependencyProperty.Register("PortState", typeof(bool), typeof(PortControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string PortCurrent {
            get { return (string)GetValue(PortCurrentProperty); }
            set { SetValue(PortCurrentProperty, value); }
        }

        public static readonly DependencyProperty PortCurrentProperty =
            DependencyProperty.Register("PortCurrent", typeof(string), typeof(PortControl), new PropertyMetadata(string.Empty));

        public bool PortOvercurrent {
            get { return (bool)GetValue(PortOvercurrentProperty); }
            set { SetValue(PortOvercurrentProperty, value); }
        }

        public static readonly DependencyProperty PortOvercurrentProperty =
            DependencyProperty.Register("PortOvercurrent", typeof(bool), typeof(PortControl), new PropertyMetadata(false));

        public bool PortBootstrap {
            get { return (bool)GetValue(PortBootstrapProperty); }
            set { SetValue(PortBootstrapProperty, value); }
        }

        public static readonly DependencyProperty PortBootstrapProperty =
            DependencyProperty.Register("PortBootstrap", typeof(bool), typeof(PortControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int PortNumber {
            get { return (int)GetValue(PortNumberProperty); }
            set { SetValue(PortNumberProperty, value); }
        }

        public static readonly DependencyProperty PortNumberProperty =
            DependencyProperty.Register("PortNumber", typeof(int), typeof(PortControl), new PropertyMetadata(-1));
    }
}