using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using System.Collections.Generic;
using System.ComponentModel.Composition;

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

            // The flat device piggybacks on the PWM port of an already-connected PowerBox,
            // so it can only ever connect successfully once that PowerBox is connected.
            // Scanning for un-connected PowerBox device ids here would just add dead-end
            // entries that always fail to connect.
            if (PINS.ConnectedPowerBox?.Connected == true)
            {
                devices.Add(new FlatDeviceDriver(PINS.ConnectedPowerBox.DeviceId, PINS.ConnectedPowerBox.Id));
            }

            return devices;
        }
    }
}