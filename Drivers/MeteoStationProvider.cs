using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using NINA.PINS.SDK;
using System.Text;

namespace NINA.PINS.Drivers {

    [Export(typeof(IEquipmentProvider))]
    [method: ImportingConstructor]
    public class MeteoStationProvider() : IEquipmentProvider<IWeatherData> {
        public string Name => "PINS.MeteoStation";

        public IList<IWeatherData> GetEquipment() {
            var devices = new List<IWeatherData>();

            // Try to enumerate native devices via the SDK
            var ids = MeteoStationDriver.ScanDeviceIds();
            if (ids != null) {
                foreach (var id in ids) {
                    var serial = new StringBuilder(MeteoStationSDK.MS_VERSION_LEN);
                    if (MeteoStationSDK.MSDeviceGetSerial(id, serial) == MeteoStationSDK.MS_ERROR_TYPE.MS_SUCCESS) {
                        devices.Add(new MeteoStationDriver(id, serial.ToString()));
                    }
                }
            }

            return devices;
        }
    }
}