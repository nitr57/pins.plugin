using Accord;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.PINS.Equipment;
using NINA.PINS.SDK;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.PINS.Drivers
{
    public class FlatDeviceDriver : BaseINPC, IFlatDevice, IDisposable
    {
        public FlatDeviceDriver(int uniqueId, string serial)
        {
            Id = serial;
            deviceId = uniqueId;
            Name = $"PINS.FlatDevice.{deviceId} ({serial})";
            DriverInfo = $"Serial {serial}";
        }

        private readonly int deviceId;

        public string Name { get; }

        public string DisplayName => Name;

        public string Category => "PI'N'Stars device";

        public string Description => "PI'N'Stars FlatDevice";

        public string DriverInfo { get; }

        public string DriverVersion { get; private set; }

        private bool _connected;
        public bool Connected { get => _connected; private set => SetProperty(ref _connected, value); }

        public IList<string> SupportedActions => new List<string>();

        public bool HasSetupDialog => false;

        public string Id { get; }

        private string _uniqueId = string.Empty;
        public string UniqueId => _uniqueId;

        public CoverState CoverState => CoverState.NotPresent;

        // The flat device piggybacks on the PowerBox's PWM port. That PowerBox can disconnect
        // (or be replaced by a different instance) at any time while this driver stays
        // Connected, so every access must re-check availability rather than dereference directly.
        private bool IsPowerBoxAvailable => PINS.ConnectedPowerBox?.Connected == true;

        public int MaxBrightness => IsPowerBoxAvailable ? PINS.ConnectedPowerBox.PWMPorts[0].Resolution : 0;

        public int MinBrightness => 0;

        public bool LightOn
        {
            get => IsPowerBoxAvailable && PINS.ConnectedPowerBox.PWMPorts[0].Enabled;
            set
            {
                if (IsPowerBoxAvailable)
                {
                    PINS.ConnectedPowerBox.PWMPorts[0].Enabled = value;
                }
                else
                {
                    Logger.Error("Cannot set LightOn: PINS PowerBox is not connected.");
                }
            }
        }

        public int Brightness
        {
            get => IsPowerBoxAvailable ? PINS.ConnectedPowerBox.PWMPorts[0].Power : 0;
            set
            {
                if (IsPowerBoxAvailable)
                {
                    PINS.ConnectedPowerBox.PWMPorts[0].SetPower = value;
                }
                else
                {
                    Logger.Error("Cannot set Brightness: PINS PowerBox is not connected.");
                }
            }
        }

        public bool SupportsOpenClose => false;

        public bool SupportsOnOff => true;

        public async Task<bool> Connect(CancellationToken token)
        {
            if (IsPowerBoxAvailable)
            {
                Connected = true;
                return true;
            }

            Logger.Error($"Cannot connect PINS flat device, connect PINS switch first");
            Notification.ShowError($"Cannot connect PINS flat device, connect PINS switch first");
            Connected = false;
            return false;
        }

        public void Disconnect()
        {
            Connected = false;
        }

        public void Dispose()
        {
            Disconnect();
        }

        #region Unsupported

        public Task<bool> Open(CancellationToken ct, int delay = 300)
        {
            throw new NotImplementedException();
        }

        public Task<bool> Close(CancellationToken ct, int delay = 300)
        {
            throw new NotImplementedException();
        }

        public void SendCommandBlind(string command, bool raw = true)
        {
            throw new NotImplementedException();
        }

        public bool SendCommandBool(string command, bool raw = true)
        {
            throw new NotImplementedException();
        }

        public string SendCommandString(string command, bool raw = true)
        {
            throw new NotImplementedException();
        }

        public void SetupDialog()
        {
            throw new NotImplementedException();
        }

        public string Action(string actionName, string actionParameters)
        {
            throw new NotImplementedException();
        }

        public string PortName { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        #endregion Unsupported
    }
}