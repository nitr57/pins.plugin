using NINA.Core.Utility;
using NINA.PINS.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NINA.PINS.Equipment
{
    public class PowerBoxPorts<T> : BaseINPC where T : PowerBoxPort
    {
        private readonly List<T> _ports;

        public PowerBoxPorts(Func<int, T> factory, int portCount)
        {
            _ports = new List<T>(portCount);
            for (int i = 0; i < portCount; i++)
            {
                _ports.Add(factory(i));
            }
        }

        public IList<T> Ports => _ports.AsReadOnly();

        public T this[int index] => _ports[index];

        public T GetPort(uint index)
        {
            if (index >= _ports.Count)
            {
                throw new IndexOutOfRangeException($"Port index {index} is out of range.");
            }
            return _ports[(int)index];
        }

        // The over current of a port without a switch (sense only) is flagged per measurement:
        // log it at most once per interval
        private readonly Dictionary<int, DateTime> _readOnlyLogged = new Dictionary<int, DateTime>();
        private static readonly TimeSpan ReadOnlyLogInterval = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Logs a new over current (flag 0 -> 1) into the N.I.N.A. log. Called after the whole
        /// status of the port is applied (state included). The callers hold the driver's SDK
        /// lock, so the poll and the refreshes after switching do not log twice. No notification
        /// here, it could wait for the UI thread while the lock is held.
        /// </summary>
        private void LogOvercurrent(string label, PowerBoxPort port, bool was, bool wasEnabled, double lastCurrent, string hint = "")
        {
            if (!port.Overcurrent)
            {
                return;
            }

            if (was)
            {
                // Flag already logged while the port was still on: log the switch-off when it
                // comes (a retried switch-off, or a box that reports flag and state separately)
                if (wasEnabled && !port.Enabled && !port.ReadOnly)
                {
                    string n = string.IsNullOrEmpty(port.Name) ? "" : $" '{port.Name}'";
                    Logger.Warning($"PowerBox {label}{n}: over current, now switched off{hint}");
                }
                return;
            }

            string name = string.IsNullOrEmpty(port.Name) ? "" : $" '{port.Name}'";
            if (port.ReadOnly)
            {
                DateTime now = DateTime.UtcNow;
                if (_readOnlyLogged.TryGetValue(port.Index, out DateTime last) && now - last < ReadOnlyLogInterval)
                {
                    return;
                }
                _readOnlyLogged[port.Index] = now;
                Logger.Warning($"PowerBox {label}{name}: over current ({FormatCurrent(port.Current)} A), the port cannot be switched off");
                return;
            }

            if (port.Enabled)
            {
                Logger.Error($"PowerBox {label}{name}: over current, the port is still on ({FormatCurrent(port.Current)} A): switch-off failed or not reported yet");
            }
            else
            {
                Logger.Warning($"PowerBox {label}{name}: over current, switched off (last polled {FormatCurrent(lastCurrent)} A){hint}");
            }
        }

        private static string FormatCurrent(double current)
        {
            return double.IsNaN(current) ? "--" : current.ToString("0.00", CultureInfo.InvariantCulture);
        }

        internal void UpdateFromStatus(PowerBoxSDK.PB_POWER_PORT_STATUS status)
        {
            for (int i = 0; i < _ports.Count; i++)
            {
                bool was = _ports[i].Overcurrent;
                bool wasEnabled = _ports[i].Enabled;
                double last = _ports[i].Current;
                _ports[i].UpdateFromStatus(float.NaN, status.current[i], status.overcurrent[i]);
                _ports[i].UpdateReadOnly(status.readOnly[i] != 0);
                _ports[i].UpdateEnabled(status.state[i]);
                LogOvercurrent($"12V #{i + 1}", _ports[i], was, wasEnabled, last);
            }
        }

        internal void UpdateFromStatus(PowerBoxSDK.PB_USB_PORT_STATUS status)
        {
            for (int i = 0; i < _ports.Count; i++)
            {
                bool was = _ports[i].Overcurrent;
                bool wasEnabled = _ports[i].Enabled;
                double last = _ports[i].Current;
                _ports[i].UpdateFromStatus(status.voltage[i], status.current[i], status.overcurrent[i]);
                _ports[i].UpdateReadOnly(status.readOnly[i] != 0);
                _ports[i].UpdateEnabled(status.state[i]);
                LogOvercurrent($"USB #{i + 1}", _ports[i], was, wasEnabled, last);
            }
        }

        internal void UpdateFromStatus(PowerBoxSDK.PB_DEW_PORT_STATUS status)
        {
            for (int i = 0; i < _ports.Count; i++)
            {
                if (_ports[i] is PowerBoxDewPort dewPort)
                {
                    bool was = dewPort.Overcurrent;
                    bool wasEnabled = dewPort.Enabled;
                    double last = dewPort.Current;
                    dewPort.UpdateFromStatus(status.state[i], status.current[i], status.overcurrent[i], status.probe[i], status.pwm[i], status.pwmResolution);
                    LogOvercurrent($"Dew #{i + 1}", dewPort, was, wasEnabled, last,
                        dewPort.AutoMode ? "; auto mode: switch auto mode off and on to resume" : "");
                }
            }
        }

        internal void UpdateFromStatus(PowerBoxSDK.PB_BUCK_PORT_STATUS status)
        {
            if (_ports[0] is PowerBoxBuckPort buckPort)
            {
                bool was = buckPort.Overcurrent;
                bool wasEnabled = buckPort.Enabled;
                double last = buckPort.Current;
                buckPort.UpdateFromStatus(status.current, status.overcurrent, status.vset, status.vmin, status.vmax, status.voltage);
                buckPort.UpdateEnabled(status.state);
                LogOvercurrent("Buck converter", buckPort, was, wasEnabled, last);
            }
        }

        internal void UpdateFromStatus(PowerBoxSDK.PB_PWM_PORT_STATUS status)
        {
            if (_ports[0] is PowerBoxPWMPort pwmPort)
            {
                bool was = pwmPort.Overcurrent;
                bool wasEnabled = pwmPort.Enabled;
                double last = pwmPort.Current;
                pwmPort.UpdateFromStatus(status.current, status.overcurrent, status.pwm, status.pwmResolution);
                pwmPort.UpdateEnabled(status.state);
                LogOvercurrent("PWM port", pwmPort, was, wasEnabled, last);
            }
        }

        internal void UpdateFromConfig(PowerBoxSDK.PB_POWER_PORT_CONFIG config)
        {
            GetPort(config.index).UpdateFromConfig(config.enabled, config.bootState);
        }

        internal void UpdateFromConfig(PowerBoxSDK.PB_USB_PORT_CONFIG config)
        {
            GetPort(config.index).UpdateFromConfig(config.enabled, config.bootState);
        }

        internal void UpdateFromConfig(PowerBoxSDK.PB_DEW_PORT_CONFIG config)
        {
            if (GetPort(config.index) is PowerBoxDewPort dewPort)
            {
                dewPort.UpdateFromConfig(config.enabled, config.autoMode, config.autoThreshold, config.power);
            }
        }

        internal void UpdateFromConfig(PowerBoxSDK.PB_BUCK_PORT_CONFIG config)
        {
            if (GetPort(0) is PowerBoxBuckPort buckPort)
            {
                buckPort.UpdateFromConfig(config.enabled, config.bootState, config.targetVoltage);
            }
        }

        internal void UpdateFromConfig(PowerBoxSDK.PB_PWM_PORT_CONFIG config)
        {
            if (GetPort(0) is PowerBoxPWMPort pwmPort)
            {
                pwmPort.UpdatePWMFromConfig(config.enabled, config.power);
            }
        }
    }

    // Backwards compatibility aliases
    public class PowerBoxPorts : PowerBoxPorts<PowerBoxPort>
    {
        public PowerBoxPorts() : base(index => new PowerBoxPort(index), PowerBoxSDK.PB_MAX_POWER_PORTS)
        {
        }
    }

    public class PowerBoxUSBPorts : PowerBoxPorts<PowerBoxPort>
    {
        public PowerBoxUSBPorts() : base(index => new PowerBoxPort(index), PowerBoxSDK.PB_MAX_USB_PORTS)
        {
        }
    }

    public class PowerBoxDewPorts : PowerBoxPorts<PowerBoxDewPort>
    {
        public PowerBoxDewPorts() : base(index => new PowerBoxDewPort(index), PowerBoxSDK.PB_MAX_DEW_PORTS)
        {
        }
    }

    public class PowerBoxBuckPorts : PowerBoxPorts<PowerBoxBuckPort>
    {
        public PowerBoxBuckPorts() : base(index => new PowerBoxBuckPort(index), 1)
        {
        }
    }

    public class PowerBoxPWMPorts : PowerBoxPorts<PowerBoxPWMPort>
    {
        public PowerBoxPWMPorts() : base(index => new PowerBoxPWMPort(index), 1)
        {
        }
    }
}