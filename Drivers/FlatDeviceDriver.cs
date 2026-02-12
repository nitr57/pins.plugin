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

        public bool Connected { get; private set; }

        public IList<string> SupportedActions => new List<string>();

        public bool HasSetupDialog => false;

        public string Id { get; }

        private string _uniqueId = string.Empty;
        public string UniqueId => _uniqueId;

        public CoverState CoverState => CoverState.NotPresent;

        public int MaxBrightness => PINS.ConnectedPowerBox.PWMPorts[0].Resolution;

        public int MinBrightness => 0;

        public bool LightOn
        {
            get => Connected && PINS.ConnectedPowerBox.PWMPorts[0].Enabled;
            set
            {
                PINS.ConnectedPowerBox.PWMPorts[0].Enabled = value;
            }
        }

        public int Brightness
        {
            get => Connected ? PINS.ConnectedPowerBox.PWMPorts[0].Power : 0;
            set
            {
                PINS.ConnectedPowerBox.PWMPorts[0].SetPower = value;
            }
        }

        public bool SupportsOpenClose => false;

        public bool SupportsOnOff => true;

        public async Task<bool> Connect(CancellationToken token)
        {
            if (PINS.ConnectedPowerBox?.Connected == true)
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