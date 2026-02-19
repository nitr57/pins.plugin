using NINA.Core.Utility;
using NINA.PINS.SDK;
using System;
using System.Collections.Generic;

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

        internal void UpdateFromStatus(PowerBoxSDK.PB_POWER_PORT_STATUS status)
        {
            for (int i = 0; i < _ports.Count; i++)
            {
                _ports[i].UpdateFromStatus(float.NaN, status.current[i], status.overcurrent[i]);
            }
        }

        internal void UpdateFromStatus(PowerBoxSDK.PB_USB_PORT_STATUS status)
        {
            for (int i = 0; i < _ports.Count; i++)
            {
                _ports[i].UpdateFromStatus(status.voltage[i], status.current[i], status.overcurrent[i]);
            }
        }

        internal void UpdateFromStatus(PowerBoxSDK.PB_DEW_PORT_STATUS status)
        {
            for (int i = 0; i < _ports.Count; i++)
            {
                if (_ports[i] is PowerBoxDewPort dewPort)
                {
                    dewPort.UpdateFromStatus(status.state[i], status.current[i], status.overcurrent[i], status.probe[i], status.pwm[i], status.pwmResolution);
                }
            }
        }

        internal void UpdateFromStatus(PowerBoxSDK.PB_BUCK_PORT_STATUS status)
        {
            if (_ports[0] is PowerBoxBuckPort buckPort)
            {
                buckPort.UpdateFromStatus(status.current, status.overcurrent, status.vset, status.vmin, status.vmax, status.voltage);
            }
        }

        internal void UpdateFromStatus(PowerBoxSDK.PB_PWM_PORT_STATUS status)
        {
            if (_ports[0] is PowerBoxPWMPort pwmPort)
            {
                pwmPort.UpdateFromStatus(status.current, status.overcurrent, status.pwm, status.pwmResolution);
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
                pwmPort.UpdateFromConfig(config.enabled, config.power);
            }
        }
    }

    // Backwards compatibility aliases
    public class PowerBoxPorts : PowerBoxPorts<PowerBoxPort>
    {
        public PowerBoxPorts() : base(index => new PowerBoxPort(index, index == 0), PowerBoxSDK.PB_MAX_POWER_PORTS)
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