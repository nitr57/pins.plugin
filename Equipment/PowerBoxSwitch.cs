using NINA.Core.Utility;
using NINA.Equipment.Interfaces;
using System;

namespace NINA.PINS.Equipment {

    public class PowerBoxSwitch : BaseINPC, ISwitch {
        private readonly Func<string> _descGetter;
        private readonly Func<string> _nameGetter;
        private readonly Func<double> _valueGetter;

        public PowerBoxSwitch(Func<string> nameGetter, Func<string> descGetter, Func<double> valueGetter, short id) {
            Id = id;

            _nameGetter = nameGetter;
            _descGetter = descGetter;
            _valueGetter = valueGetter;

            Name = _nameGetter();
            Description = _descGetter();
            Value = _valueGetter();
        }

        public short Id { get; private set; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public double Value { get; private set; }

        public virtual bool Poll() {
            Name = _nameGetter();
            Description = _descGetter();
            Value = _valueGetter();

            RaisePropertyChanged(nameof(Description));
            RaisePropertyChanged(nameof(Name));
            RaisePropertyChanged(nameof(Value));

            return true;
        }
    }
}