using NINA.Core.Utility;
using System;

namespace NINA.PINS.Equipment {

    public class PowerBoxPort : BaseINPC {
        private readonly bool _readOnly;
        public bool ReadOnly => _readOnly;

        private readonly int _index;
        public int Index => _index;

        private string _name = string.Empty;

        public string Name {
            get => _name;
            set {
                if (_name != value) {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        private double _voltage = double.NaN;
        public double Voltage => _voltage;

        private double _current = double.NaN;
        public double Current => _current;

        private bool _enabled = false;

        public bool Enabled {
            get => _enabled;
            set {
                if (_enabled != value) {
                    _enabled = value;
                    OnPropertyChanged(nameof(Enabled));
                }
            }
        }

        private bool _bootState = false;

        public bool BootState {
            get => _bootState;
            set {
                if (_bootState != value) {
                    _bootState = value;
                    OnPropertyChanged(nameof(BootState));
                }
            }
        }

        private bool _overcurrent = false;
        public bool Overcurrent => _overcurrent;

        internal PowerBoxPort(int index)
        {
            _index = index;
            _readOnly = false;
        }
        
        internal PowerBoxPort(int index, bool readOnly)
        {
            _index = index;
            _readOnly = readOnly;
        }

        internal virtual void UpdateFromStatus(float voltage, float current, int overcurrent) {
            _voltage = Math.Round(voltage, 2);
            _current = Math.Round(current, 2);
            _overcurrent = overcurrent != 0;

            OnPropertyChanged(nameof(Voltage));
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(Overcurrent));
        }

        internal virtual void UpdateFromConfig(int enabled, int bootstate) {
            _enabled = enabled != 0;
            _bootState = bootstate != 0;

            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(BootState));
        }
    }

    public class PowerBoxDewPort : PowerBoxPort {

        internal PowerBoxDewPort(int index) : base(index)
        {
        }

        private bool _autoMode = true;

        public bool AutoMode {
            get => _autoMode;
            set {
                if (_autoMode != value) {
                    _autoMode = value;
                    OnPropertyChanged(nameof(AutoMode));
                }
            }
        }

        private double _autoThreshold = float.NaN;

        public double AutoThreshold {
            get => _autoThreshold;
            set {
                if (_autoThreshold != value) {
                    _autoThreshold = value;
                    OnPropertyChanged(nameof(AutoThreshold));
                }
            }
        }

        private int _power = 0;
        public int Power => _power;

        private int _setPower = 0;

        public int SetPower {
            get => _setPower;
            set {
                if (_setPower != value) {
                    _setPower = value;
                    OnPropertyChanged(nameof(SetPower));
                }
            }
        }

        private double _probe = double.NaN;
        public double Probe => _probe;

        private int _resolution = 0;
        public int Resolution => (1 << _resolution) - 1;

        internal void UpdateFromStatus(int enabled, float current, int overcurrent, float probe, int power, int resolution) {
            base.UpdateFromStatus(float.NaN, current, overcurrent);
            base.UpdateFromConfig(enabled, 0);

            _probe = Math.Round(probe, 1);
            _power = power;
            _resolution = resolution;

            OnPropertyChanged(nameof(Power));
            OnPropertyChanged(nameof(Probe));
            OnPropertyChanged(nameof(Resolution));
        }

        internal void UpdateFromConfig(int enabled, int automode, float autothreshold, int power) {
            base.UpdateFromConfig(enabled, 0);

            _autoMode = automode != 0;
            _autoThreshold = autothreshold;
            _setPower = power;

            OnPropertyChanged(nameof(AutoMode));
            OnPropertyChanged(nameof(AutoThreshold));
            OnPropertyChanged(nameof(SetPower));
        }
    }

    public class PowerBoxBuckPort : PowerBoxPort {

        internal PowerBoxBuckPort(int index) : base(index)
        {
        }

        private double _power = double.NaN;

        public double Power {
            get => _power;
            set {
                if (_power != value) {
                    _power = value;
                    OnPropertyChanged(nameof(Power));
                }
            }
        }

        private double _setVoltage = double.NaN;

        public double SetVoltage {
            get => _setVoltage;
            set {
                if (_setVoltage != value) {
                    _setVoltage = value;
                    OnPropertyChanged(nameof(SetVoltage));
                }
            }
        }

        private double _minVoltage = double.NaN;
        public double MinVoltage => _minVoltage;

        private double _maxVoltage = double.NaN;
        public double MaxVoltage => _maxVoltage;

        internal void UpdateFromStatus(float current, int overcurrent, float vset, float vmin, float vmax, float voltage) {
            base.UpdateFromStatus(voltage, current, overcurrent);

            _setVoltage = Math.Round(vset, 2);
            _minVoltage = Math.Round(vmin, 2);
            _maxVoltage = Math.Round(vmax, 2);

            OnPropertyChanged(nameof(SetVoltage));
            OnPropertyChanged(nameof(MinVoltage));
            OnPropertyChanged(nameof(MaxVoltage));
        }

        internal void UpdateFromConfig(int enabled, int bootstate, float voltage) {
            base.UpdateFromConfig(enabled, bootstate);

            _setVoltage = Math.Round(voltage, 2);

            OnPropertyChanged(nameof(SetVoltage));
        }
    }

    public class PowerBoxPWMPort : PowerBoxPort {

        internal PowerBoxPWMPort(int index) : base(index)
        {
        }

        private int _power = 0;

        public int Power => _power;

        private int _setPower = 0;

        public int SetPower {
            get => _setPower;
            set {
                if (_setPower != value) {
                    _setPower = value;
                    OnPropertyChanged(nameof(SetPower));
                }
            }
        }

        private int _resolution = 0;
        public int Resolution => (1 << _resolution) - 1;

        internal void UpdateFromStatus(float current, int overcurrent, int power, int resolution) {
            base.UpdateFromStatus(float.NaN, current, overcurrent);

            _power = power;
            _resolution = resolution;

            OnPropertyChanged(nameof(Power));
            OnPropertyChanged(nameof(Resolution));
        }

        internal override void UpdateFromConfig(int enabled, int power) {
            base.UpdateFromConfig(enabled, 0);

            _setPower = power;

            OnPropertyChanged(nameof(SetPower));
        }
    }
}