using NINA.Core.Utility;
using NINA.PINS.SDK;
using System;

namespace NINA.PINS.Equipment {

    public class PowerBoxPowerSupply : BaseINPC {
        private double _supply12V = double.NaN;
        public double Supply12V => _supply12V;

        private double _supply12A = double.NaN;
        public double Supply12A => _supply12A;

        private double _supply12W = double.NaN;
        public double Supply12W => _supply12W;

        private double _supply5V = double.NaN;
        public double Supply5V => _supply5V;

        private double _ampsPerHour = double.NaN;
        public double AmpsPerHour => _ampsPerHour;

        private double _wattsPerHour = double.NaN;
        public double WattsPerHour => _wattsPerHour;

        internal void UpdateFromStatus(PowerBoxSDK.PB_SUPPLY_STATUS status) {
            _supply12V = Math.Round(status.mainVoltage, 2);
            _supply12A = Math.Round(status.current, 2);
            _supply12W = Math.Round(_supply12V * _supply12A, 2);
            _supply5V = Math.Round(status.usbVoltage, 2);
            _ampsPerHour = Math.Round(status.ampereHours, 2);
            _wattsPerHour = Math.Round(status.wattHours, 2);

            OnPropertyChanged(nameof(Supply12V));
            OnPropertyChanged(nameof(Supply12A));
            OnPropertyChanged(nameof(Supply12W));
            OnPropertyChanged(nameof(Supply5V));
            OnPropertyChanged(nameof(AmpsPerHour));
            OnPropertyChanged(nameof(WattsPerHour));
        }
    }
}