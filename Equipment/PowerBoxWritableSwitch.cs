using NINA.Equipment.Interfaces;
using System;

namespace NINA.PINS.Equipment {

    public class PowerBoxWritableSwitch : PowerBoxSwitch, IWritableSwitch {
        private readonly Action<double> _valueSetter;

        public PowerBoxWritableSwitch(Func<string> nameGetter, Func<string> descGetter, Func<double> valueGetter, Action<double> valueSetter, short id, double min = 0, double max = 1, double step = 1) : base(nameGetter, descGetter, valueGetter, id) {
            Minimum = min;
            Maximum = max;
            StepSize = step;
            TargetValue = Value;

            _valueSetter = valueSetter ?? (_ => throw new InvalidOperationException("This switch is read only."));
        }

        public new bool Poll() {
            bool result = base.Poll();
            // Keep TargetValue in sync with the actual hardware value so that
            // external changes (e.g. via the pins plugin UI) are reflected in
            // the NINA switch/info endpoint and in any connected Vue front-end.
            TargetValue = Value;
            return result;
        }

        public void SetValue() {
            _valueSetter(TargetValue);
        }

        public double Maximum { get; }

        public double Minimum { get; }

        public double StepSize { get; }

        private double targetValue;

        public double TargetValue {
            get => targetValue;
            set {
                targetValue = value;
                RaisePropertyChanged();
            }
        }
    }
}