using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using NINA.PINS.SDK;
using System.Text;

namespace NINA.PINS.Drivers {

    [Export(typeof(IEquipmentProvider))]
    [method: ImportingConstructor]
    public class LensControlProvider() : IEquipmentProvider<IFocuser> {
        public string Name => "PINS.LensControl";

        public IList<IFocuser> GetEquipment() {
            var devices = new List<IFocuser>();

            // Try to enumerate native devices via the SDK
            var ids = LensControlDriver.ScanDeviceIds();
            if (ids != null) {
                foreach (var id in ids) {
                    var serial = new StringBuilder(LensControlSDK.LC_VERSION_LEN);
                    if (LensControlSDK.LCDeviceGetSerial(id, serial) == LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                        devices.Add(new LensControlDriver(id, serial.ToString()));
                    }
                }
            }

            return devices;
        }
    }
}