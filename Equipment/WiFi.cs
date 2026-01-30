using NINA.Core.Utility;
using NINA.PINS.SDK;

namespace NINA.PINS.Equipment {

    public class WiFi : BaseINPC {

        public WiFi() {
        }

        public WiFi(string ssid, int rssi, int channel) {
            _ssid = ssid;
            _rssi = rssi;
            _channel = channel;
        }

        private string _ssid = string.Empty;

        public string SSID {
            get => _ssid;
            set {
                if (_ssid != value) {
                    _ssid = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string _ipAddress = string.Empty;
        public string IPAddress => _ipAddress;

        private int _rssi = int.MinValue;
        public int RSSI => _rssi;

        private int _channel;
        public int Channel => _channel;

        private string _mode;

        public string Mode {
            get => _mode;
            set {
                if (_mode != value) {
                    _mode = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string _hostName = string.Empty;
        public string HostName => _hostName;

        internal void UpdateFromStatus(PowerBoxSDK.PB_WIFI_STATUS status) {
            _rssi = status.rssi;
            _ipAddress = status.IP;

            OnPropertyChanged(nameof(RSSI));
            OnPropertyChanged(nameof(IPAddress));
        }

        internal void UpdateFromConfig(PowerBoxSDK.PB_WIFI_CONFIG config) {
            _channel = config.channel;
            _mode = config.mode == PowerBoxSDK.PB_WIFI_MODE.PB_WIFI_MODE_CLIENT ? "stationary" : "hotspot";
            _ssid = config.ssid;
            _hostName = config.hostname;

            OnPropertyChanged(nameof(Channel));
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(SSID));
            OnPropertyChanged(nameof(HostName));
        }
    }
}