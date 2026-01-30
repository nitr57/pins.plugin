using System.Windows;
using System.Windows.Controls;
using NINA.PINS.Equipment;

namespace NINA.PINS.Views.PowerBox {

    public class PortTemplateSelector : DataTemplateSelector {
        public DataTemplate AlwaysOnTemplate { get; set; }

        public DataTemplate ControllableTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container) {
            if (item is PowerBoxPort port) {
                // Port 0 is always on (read-only)
                if (port.Index == 0) {
                    return AlwaysOnTemplate;
                }
                // Ports 1-5 are controllable
                return ControllableTemplate;
            }
            return base.SelectTemplate(item, container);
        }
    }
}