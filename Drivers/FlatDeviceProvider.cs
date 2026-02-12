using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.PINS.SDK;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace NINA.PINS.Drivers
{
    [Export(typeof(IEquipmentProvider))]
    [method: ImportingConstructor]
    public class FlatDeviceProvider() : IEquipmentProvider<IFlatDevice>
    {
        public string Name => "PINS.FlatDevice";

        public IList<IFlatDevice> GetEquipment()
        {
            var devices = new List<IFlatDevice>();

            // If powerbox is connected, this is our flatdevice
            if (PINS.ConnectedPowerBox?.Connected == true)
            {
                devices.Add(new FlatDeviceDriver(PINS.ConnectedPowerBox.DeviceId, PINS.ConnectedPowerBox.Id));
            }
            else
            {
                // Try to enumerate native devices via the SDK
                var ids = PowerBoxDriver.ScanDeviceIds();
                if (ids != null)
                {
                    foreach (var id in ids)
                    {
                        var serial = new StringBuilder(PowerBoxSDK.PB_VERSION_LEN);
                        if (PowerBoxSDK.PBGetSerial(id, serial) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            devices.Add(new FlatDeviceDriver(id, serial.ToString()));
                        }
                    }
                }
            }

            return devices;
        }
    }
}