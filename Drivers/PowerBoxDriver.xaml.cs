using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.PINS.Drivers {

    [Export(typeof(ResourceDictionary))]
    public partial class PINSPowerBoxDriver : ResourceDictionary {

        public PINSPowerBoxDriver() {
            InitializeComponent();
        }
    }
}