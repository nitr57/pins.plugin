using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.PINS.Drivers {

    [Export(typeof(ResourceDictionary))]
    public partial class PINSMeteoStationDriver : ResourceDictionary {

        public PINSMeteoStationDriver() {
            InitializeComponent();
        }
    }
}