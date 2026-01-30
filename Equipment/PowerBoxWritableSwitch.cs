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