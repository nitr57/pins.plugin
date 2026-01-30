using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NINA.PINS.Resources.Styles {

    /// <summary>
    /// Interaktionslogik für PropertyConfig.xaml
    /// </summary>
    public partial class PropertyConfig : UserControl {

        public PropertyConfig() {
            InitializeComponent();
        }

        public string PropertyName {
            get { return (string)GetValue(PropertyNameProperty); }
            set { SetValue(PropertyNameProperty, value); }
        }

        public static readonly DependencyProperty PropertyNameProperty =
            DependencyProperty.Register("PropertyName", typeof(string), typeof(PropertyConfig), new PropertyMetadata(string.Empty));

        public string PropertyUnit {
            get { return (string)GetValue(PropertyUnitProperty); }
            set { SetValue(PropertyUnitProperty, value); }
        }

        public static readonly DependencyProperty PropertyUnitProperty =
            DependencyProperty.Register("PropertyUnit", typeof(string), typeof(PropertyConfig), new PropertyMetadata(string.Empty));

        public string PropertyToolTip {
            get { return (string)GetValue(PropertyToolTipProperty); }
            set { SetValue(PropertyToolTipProperty, value); }
        }

        public static readonly DependencyProperty PropertyToolTipProperty =
            DependencyProperty.Register("PropertyToolTip", typeof(string), typeof(PropertyConfig), new PropertyMetadata(string.Empty));

        public double PropertyMin {
            get { return (double)GetValue(PropertyMinProperty); }
            set { SetValue(PropertyMinProperty, value); }
        }

        public static readonly DependencyProperty PropertyMinProperty =
            DependencyProperty.Register("PropertyMin", typeof(double), typeof(PropertyConfig), new PropertyMetadata(0.0));

        public double PropertyMax {
            get { return (double)GetValue(PropertyMaxProperty); }
            set { SetValue(PropertyMaxProperty, value); }
        }

        public static readonly DependencyProperty PropertyMaxProperty =
            DependencyProperty.Register("PropertyMax", typeof(double), typeof(PropertyConfig), new PropertyMetadata(0.0));

        public double PropertyValue {
            get { return (double)GetValue(PropertyValueProperty); }
            set { SetValue(PropertyValueProperty, value); }
        }

        public static readonly DependencyProperty PropertyValueProperty =
            DependencyProperty.Register("PropertyValue", typeof(double), typeof(PropertyConfig), new PropertyMetadata(0.0));
    }
}