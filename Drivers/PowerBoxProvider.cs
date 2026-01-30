using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.PINS.SDK;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace NINA.PINS.Drivers {

    [Export(typeof(IEquipmentProvider))]
    [method: ImportingConstructor]
    public class PowerBoxProvider() : IEquipmentProvider<ISwitchHub> {
        public string Name => "PINS.PowerBox";

        public IList<ISwitchHub> GetEquipment() {
            var devices = new List<ISwitchHub>();

            // Try to enumerate native devices via the SDK
            var ids = PowerBoxDriver.ScanDeviceIds();
            if (ids != null) {
                foreach (var id in ids) {
                    var serial = new StringBuilder(PowerBoxSDK.PB_VERSION_LEN);
                    if (PowerBoxSDK.PBGetSerial(id, serial) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS) {
                        devices.Add(new PowerBoxDriver(id, serial.ToString()));
                    }
                }
            }

            return devices;
        }
    }
}