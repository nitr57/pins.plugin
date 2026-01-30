using System.Windows;
using System.Windows.Controls;

namespace NINA.PINS.Resources.Styles {

    /// <summary>
    /// Interaktionslogik für AOnPortControl.xaml
    /// </summary>
    public partial class AOnPortControl : UserControl {

        public AOnPortControl() {
            InitializeComponent();
        }

        private static void onPortTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            // When PortText changes, update the binding source if it exists
            if (d is AOnPortControl control && e.NewValue != e.OldValue) {
                // This will trigger the binding update back to the source
            }
        }

        public string PortName {
            get { return (string)GetValue(PortNameProperty); }
            set { SetValue(PortNameProperty, value); }
        }

        public static readonly DependencyProperty PortNameProperty =
            DependencyProperty.Register("PortName", typeof(string), typeof(AOnPortControl), new PropertyMetadata(string.Empty));

        public string PortText {
            get { return (string)GetValue(PortTextProperty); }
            set { SetValue(PortTextProperty, value); }
        }

        public static readonly DependencyProperty PortTextProperty =
            DependencyProperty.Register("PortText", typeof(string), typeof(AOnPortControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    onPortTextChanged));

        public string PortCurrent {
            get { return (string)GetValue(PortCurrentProperty); }
            set { SetValue(PortCurrentProperty, value); }
        }

        public static readonly DependencyProperty PortCurrentProperty =
            DependencyProperty.Register("PortCurrent", typeof(string), typeof(AOnPortControl), new PropertyMetadata(string.Empty));

        public bool PortOvercurrent {
            get { return (bool)GetValue(PortOvercurrentProperty); }
            set { SetValue(PortOvercurrentProperty, value); }
        }

        public static readonly DependencyProperty PortOvercurrentProperty =
            DependencyProperty.Register("PortOvercurrent", typeof(bool), typeof(AOnPortControl), new PropertyMetadata(false));

        public int PortNumber {
            get { return (int)GetValue(PortNumberProperty); }
            set { SetValue(PortNumberProperty, value); }
        }

        public static readonly DependencyProperty PortNumberProperty =
            DependencyProperty.Register("PortNumber", typeof(int), typeof(AOnPortControl), new PropertyMetadata(-1));
    }
}