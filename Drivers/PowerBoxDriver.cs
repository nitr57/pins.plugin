using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.PINS.Equipment;
using NINA.PINS.SDK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NINA.PINS.Drivers
{
    public class PowerBoxDriver : BaseINPC, ISwitchHub, IDisposable
    {
        public PowerBoxDriver(int uniqueId, string serial)
        {
            _configLock = new object();

            Id = serial;
            deviceId = uniqueId;
            Name = $"PINS.PowerBox.{deviceId} ({serial})";
            DriverInfo = $"Serial {serial}";

            Switches = new AsyncObservableCollection<ISwitch>();
            _powerSupply = new PowerBoxPowerSupply();
            _powerPorts = new PowerBoxPorts();
            _usbPorts = new PowerBoxUSBPorts();
            _dewPorts = new PowerBoxDewPorts();
            _buckPorts = new PowerBoxBuckPorts();
            _pwmPorts = new PowerBoxPWMPorts();
            _wifi = new WiFi();
            WiFiNetworks = new ObservableCollection<WiFi>();

            // Read config file
            var config = Load(Id);
            config ??= GetDefaultConfig();

            // Apply config to device
            SetNames(config);

            // Write back to file
            Store(Id, config);

            SubscribeToPortChanges();

            RebootCommand = new AsyncRelayCommand(Reboot, () => IsEnabledUI);
            ResetCommand = new AsyncRelayCommand(Reset, () => IsEnabledUI);
            WiFiSurveyCommand = new AsyncRelayCommand(WiFiSurvey, () => !IsWiFiSurveyRunning);
            WiFiClientCommand = new AsyncRelayCommand(() => WiFiConnect(PowerBoxSDK.PB_WIFI_MODE.PB_WIFI_MODE_CLIENT), () => !IsWiFiConnecting && !IsWiFiSurveyRunning);
            WiFiHotspotCommand = new AsyncRelayCommand(() => WiFiConnect(PowerBoxSDK.PB_WIFI_MODE.PB_WIFI_MODE_AP), () => !IsWiFiConnecting && !IsWiFiSurveyRunning);
        }

        public ObservableCollection<WiFi> WiFiNetworks { get; set; }

        private bool _isWiFiSurveyRunning;

        public bool IsWiFiSurveyRunning
        {
            get => _isWiFiSurveyRunning;
            set
            {
                if (SetProperty(ref _isWiFiSurveyRunning, value))
                {
                    // Ensure this runs on the UI thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ((AsyncRelayCommand)WiFiSurveyCommand).NotifyCanExecuteChanged();
                    });
                }
            }
        }

        private bool _isWiFiConnecting;

        public bool IsWiFiConnecting
        {
            get => _isWiFiConnecting;
            set
            {
                if (SetProperty(ref _isWiFiConnecting, value))
                {
                    // Ensure this runs on the UI thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ((AsyncRelayCommand)WiFiClientCommand).NotifyCanExecuteChanged();
                        ((AsyncRelayCommand)WiFiHotspotCommand).NotifyCanExecuteChanged();
                    });
                }
            }
        }

        private WiFi _selectedWiFiNetwork;

        public WiFi SelectedWiFiNetwork
        {
            get => _selectedWiFiNetwork;
            set
            {
                if (_selectedWiFiNetwork != value)
                {
                    _selectedWiFiNetwork = value;
                    RaisePropertyChanged();
                }
            }
        }

        private readonly object _configLock;
        private readonly object _sdkLock = new object();
        private readonly AsyncLocal<bool> _isHardwareUpdate = new();
        private CancellationTokenSource pollingCts;
        private Task pollingTask;
        private int _consecutiveStatusFailures = 0;
        private const int MaxConsecutiveStatusFailures = 5;

        // Snapshot of the switches for the polling loop, replaced as a whole by ScanForSwitches
        // so the loop never iterates the collection while it is being rebuilt
        private volatile PowerBoxSwitch[] _pollSwitches = Array.Empty<PowerBoxSwitch>();
        private readonly List<PowerBoxSwitch> _scannedSwitches = new();
        private readonly Dictionary<string, DateTime> _lastPollErrors = new();
        private PowerBoxPowerSupply _powerSupply;
        private PowerBoxPorts _powerPorts;
        private PowerBoxUSBPorts _usbPorts;
        private PowerBoxDewPorts _dewPorts;
        private PowerBoxBuckPorts _buckPorts;
        private PowerBoxPWMPorts _pwmPorts;
        private WiFi _wifi;

        // Track actual port counts reported by hardware
        // -1: not known yet (all rows of the 12 V view are shown)
        private int _actualPowerPortCount = -1;
        public int ActualPowerPortCount => _actualPowerPortCount;

        // -1: not known yet (all rows of the USB view are shown)
        private int _actualUSBPortCount = -1;
        public int ActualUSBPortCount => _actualUSBPortCount;

        private int _actualDewPortCount = 0;
        public int ActualDewPortCount => _actualDewPortCount;

        private int _actualBuckPortCount = 0;
        public int ActualBuckPortCount => _actualBuckPortCount;

        private int _actualPWMPortCount = 0;
        public int ActualPWMPortCount => _actualPWMPortCount;

        private readonly int deviceId;
        public int DeviceId => deviceId;

        public string Name { get; }

        public string DisplayName => Name;

        public string Category => "PI'N'Stars device";

        public string Description => "PI'N'Stars PowerBox";

        public string DriverInfo { get; }

        public string DriverVersion { get; private set; }

        private bool _connected;
        public bool Connected { get => _connected; private set => SetProperty(ref _connected, value); }

        public IList<string> SupportedActions => new List<string>();

        public bool HasSetupDialog => false;

        public ICollection<ISwitch> Switches { get; }

        public string Id { get; }

        private string _uniqueId = string.Empty;
        public string UniqueId => _uniqueId;

        private string _firmware = string.Empty;
        public string Firmware => _firmware;

        private string _upTimeFormatted = string.Empty;
        public string UpTimeFormatted => _upTimeFormatted;

        private int _updateRate = -1;

        public int UpdateRate
        {
            get => _updateRate;
            set
            {
                if (_updateRate != value)
                {
                    PowerBoxSDK.PB_DEVICE_CONFIG config = new PowerBoxSDK.PB_DEVICE_CONFIG();
                    config.mask = PowerBoxSDK.MASK_PB_UPDATE_RATE;
                    config.updateRate = value;
                    lock (_sdkLock)
                    {
                        if (PowerBoxSDK.PBSetConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            _updateRate = value;
                            RaisePropertyChanged();
                        }
                        else
                        {
                            var errorMsg = $"Failed to set update rate to {value}. The device rejected this configuration value.";
                            Logger.Error(errorMsg);
                            Notification.ShowError(errorMsg);
                        }
                    }
                }
            }
        }

        #region Environment Properties

        private double _temperature = double.NaN;
        public double Temperature => _temperature;

        private double _coreTemp = double.NaN;
        public double CoreTemp => _coreTemp;

        private double _humidity = double.NaN;
        public double Humidity => _humidity;

        private double _dewPoint = double.NaN;
        public double DewPoint => _dewPoint;

        private int _envUpdateRate = -1;

        public int EnvUpdateRate
        {
            get => _envUpdateRate;
            set
            {
                if (_envUpdateRate != value)
                {
                    PowerBoxSDK.PB_DEVICE_CONFIG config = new PowerBoxSDK.PB_DEVICE_CONFIG();
                    config.mask = PowerBoxSDK.MASK_PB_ENV_UPDATE_RATE;
                    config.envUpdateRate = value;
                    lock (_sdkLock)
                    {
                        if (PowerBoxSDK.PBSetConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            _envUpdateRate = value;
                            RaisePropertyChanged();
                        }
                        else
                        {
                            var errorMsg = $"Failed to set environment update rate to {value}. The device rejected this configuration value.";
                            Logger.Error(errorMsg);
                            Notification.ShowError(errorMsg);
                        }
                    }
                }
            }
        }

        private bool _extSensor = false;
        public bool ExtSensor => _extSensor;

        private bool _hasWifi = false;
        public bool HasWifi => _hasWifi;

        private double _temperatureOffset = double.NaN;

        public double TemperatureOffset
        {
            get => _temperatureOffset;
            set
            {
                if (_temperatureOffset != value)
                {
                    PowerBoxSDK.PB_DEVICE_CONFIG config = new PowerBoxSDK.PB_DEVICE_CONFIG();
                    config.mask = PowerBoxSDK.MASK_PB_TEMPERATURE_OFFSET;
                    config.temperatureOffset = Convert.ToSingle(value);
                    lock (_sdkLock)
                    {
                        if (PowerBoxSDK.PBSetConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            _temperatureOffset = value;
                            RaisePropertyChanged();
                        }
                        else
                        {
                            var errorMsg = $"Failed to set temperature offset to {value}. The device rejected this configuration value.";
                            Logger.Error(errorMsg);
                            Notification.ShowError(errorMsg);
                        }
                    }
                }
            }
        }

        private double _humidityOffset = double.NaN;

        public double HumidityOffset
        {
            get => _humidityOffset;
            set
            {
                if (_humidityOffset != value)
                {
                    PowerBoxSDK.PB_DEVICE_CONFIG config = new PowerBoxSDK.PB_DEVICE_CONFIG();
                    config.mask = PowerBoxSDK.MASK_PB_HUMIDITY_OFFSET;
                    config.humidityOffset = Convert.ToSingle(value);
                    lock (_sdkLock)
                    {
                        if (PowerBoxSDK.PBSetConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            _humidityOffset = value;
                            RaisePropertyChanged();
                        }
                        else
                        {
                            var errorMsg = $"Failed to set humidity offset to {value}. The device rejected this configuration value.";
                            Logger.Error(errorMsg);
                            Notification.ShowError(errorMsg);
                        }
                    }
                }
            }
        }

        #endregion Environment Properties

        public PowerBoxPowerSupply PowerSupply => _powerSupply;

        public PowerBoxPorts PowerPorts => _powerPorts;

        public PowerBoxUSBPorts USBPorts => _usbPorts;

        public PowerBoxDewPorts DewPorts => _dewPorts;

        public PowerBoxBuckPorts BuckPorts => _buckPorts;

        public PowerBoxPWMPorts PWMPorts => _pwmPorts;

        public WiFi WiFi => _wifi;

        private double _supply5A = double.NaN;
        public double Supply5A => _supply5A;

        private double _supply5W = double.NaN;
        public double Supply5W => _supply5W;

        public static IList<int> ScanDeviceIds()
        {
            // SDK messages into the N.I.N.A. log, before the first SDK call (the scan logs)
            PowerBoxSdkLog.EnsureRegistered();

            var list = new List<int>();
            int number = 0;
            int[] ids = new int[PowerBoxSDK.PB_MAX_NUM];
            try
            {
                var result = PowerBoxSDK.PBScan(out number, ids);
                if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                {
                    for (int i = 0; i < number; ++i)
                    {
                        list.Add(ids[i]);
                    }
                }
                else if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_ERROR_INVALID_STATE)
                {
                    // The SDK messages ("PowerBox SDK: ...") around this entry have the details
                    const string msg = "PinsBox present but not usable (e.g. in use by another process or initialization failed, details in the PowerBox SDK messages of the log)";
                    Logger.Error(msg);
                    Notification.ShowWarning(msg);
                }
                else
                {
                    Logger.Error($"PowerBox scan failed ({result})");
                }
            }
            catch (Exception ex)
            {
                Notification.ShowError($"{ex.Message}");
            }
            return list;
        }

        public async Task<bool> Connect(CancellationToken token)
        {
            PowerBoxSdkLog.EnsureRegistered();

            PowerBoxSDK.PB_ERROR_TYPE openResult;
            lock (_sdkLock)
            {
                openResult = PowerBoxSDK.PBOpen(deviceId);
            }
            if (openResult != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
            {
                // Outside the lock; the reason is in the PowerBox SDK messages around this entry
                Logger.Error($"{Name}: could not be opened ({openResult})");
                Notification.ShowError($"{Name}: could not be opened ({openResult}), see log.");
                Connected = false;
                return Connected;
            }

            // Try get SDK version
            try
            {
                var ver = new StringBuilder(PowerBoxSDK.PB_VERSION_LEN);
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetSDKVersion(ver) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        DriverVersion = ver.ToString();
                    }
                    else
                    {
                        DriverVersion = "unknown";
                    }
                }
            }
            catch (Exception ex)
            {
                Notification.ShowError($"{ex.Message}");
                DriverVersion = "error";
            }

            // Try get Device version
            try
            {
                PowerBoxSDK.PB_VERSION version = new PowerBoxSDK.PB_VERSION();
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetVersion(deviceId, out version) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        // UUID
                        _uniqueId = version.uuid;

                        // Firmware
                        uint major = (version.firmware >> 10) & 0x3F;
                        uint minor = (version.firmware >> 5) & 0x1F;
                        uint patch = version.firmware & 0x1F;
                        _firmware = $"{major}.{minor}.{patch}";

                        OnPropertyChanged(nameof(UniqueId));
                    }
                }
            }
            catch (Exception ex)
            {
                Notification.ShowError($"{ex.Message}");
                DriverVersion = "error";
            }

            // Port counts unknown until this connection has read them (a failed status read
            // must not keep the counts of an earlier session)
            _actualPowerPortCount = -1;
            _actualUSBPortCount = -1;
            OnPropertyChanged(nameof(ActualPowerPortCount));
            OnPropertyChanged(nameof(ActualUSBPortCount));

            // Fetch initial configuration
            _isHardwareUpdate.Value = true;
            try
            {
                lock (_sdkLock)
                {
                    // Fetch initial device configuration
                    try
                    {
                        PowerBoxSDK.PB_DEVICE_CONFIG config = new PowerBoxSDK.PB_DEVICE_CONFIG();
                        if (PowerBoxSDK.PBGetConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            UpdateFromDeviceConfig(config);
                        }
                    }
                    catch (Exception ex)
                    {
                        Notification.ShowError($"{ex.Message}");
                    }

                    // Fetch initial power port configuration
                    try
                    {
                        if (PowerBoxSDK.PBGetPowerPortStatus(deviceId, out var status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            _actualPowerPortCount = Math.Max(0, Math.Min(status.numPorts, PowerBoxSDK.PB_MAX_POWER_PORTS));
                            OnPropertyChanged(nameof(ActualPowerPortCount));
                            // ReadOnly (port without a switch), state and flags are known from
                            // the status: apply it now, before the switches are set up and
                            // before the first poll
                            lock (_sdkLock)
                            {
                                _powerPorts.UpdateFromStatus(status);
                            }
                        }
                        PowerBoxSDK.PB_POWER_PORT_CONFIG config = new PowerBoxSDK.PB_POWER_PORT_CONFIG();
                        for (uint i = 0; i < Math.Max(0, _actualPowerPortCount); ++i)
                        {
                            config.index = i;
                            if (PowerBoxSDK.PBGetPowerPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                            {
                                _powerPorts.UpdateFromConfig(config);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Notification.ShowError($"{ex.Message}");
                    }

                    // Fetch initial USB port configuration
                    try
                    {
                        if (PowerBoxSDK.PBGetUSBPortStatus(deviceId, out var status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            _actualUSBPortCount = Math.Max(0, Math.Min(status.numPorts, PowerBoxSDK.PB_MAX_USB_PORTS));
                            OnPropertyChanged(nameof(ActualUSBPortCount));
                        }
                        PowerBoxSDK.PB_USB_PORT_CONFIG config = new PowerBoxSDK.PB_USB_PORT_CONFIG();
                        for (uint i = 0; i < Math.Max(0, _actualUSBPortCount); ++i)
                        {
                            config.index = i;
                            if (PowerBoxSDK.PBGetUSBPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                            {
                                _usbPorts.UpdateFromConfig(config);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Notification.ShowError($"{ex.Message}");
                    }

                    // Fetch initial Dew port configuration
                    try
                    {
                        PowerBoxSDK.PBGetDewPortStatus(deviceId, out var status);
                        _actualDewPortCount = status.numPorts;
                        PowerBoxSDK.PB_DEW_PORT_CONFIG config = new PowerBoxSDK.PB_DEW_PORT_CONFIG();
                        for (uint i = 0; i < status.numPorts; ++i)
                        {
                            config.index = i;
                            if (PowerBoxSDK.PBGetDewPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                            {
                                _dewPorts.UpdateFromConfig(config);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Notification.ShowError($"{ex.Message}");
                    }

                    // Fetch initial Buck port configuration
                    try
                    {
                        PowerBoxSDK.PBGetBuckPortStatus(deviceId, out var status);
                        _actualBuckPortCount = status.numPorts;
                        PowerBoxSDK.PB_BUCK_PORT_CONFIG config = new PowerBoxSDK.PB_BUCK_PORT_CONFIG();
                        for (uint i = 0; i < status.numPorts; ++i)
                        {
                            config.index = i;
                            if (PowerBoxSDK.PBGetBuckPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                            {
                                _buckPorts.UpdateFromConfig(config);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Notification.ShowError($"{ex.Message}");
                    }

                    // Fetch initial PWM port configuration
                    try
                    {
                        PowerBoxSDK.PBGetPWMPortStatus(deviceId, out var status);
                        _actualPWMPortCount = status.numPorts;
                        PowerBoxSDK.PB_PWM_PORT_CONFIG config = new PowerBoxSDK.PB_PWM_PORT_CONFIG();
                        for (uint i = 0; i < status.numPorts; ++i)
                        {
                            config.index = i;
                            if (PowerBoxSDK.PBGetPWMPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                            {
                                _pwmPorts.UpdateFromConfig(config);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Notification.ShowError($"{ex.Message}");
                    }

                    // Fetch initial WiFi configuration
                    try
                    {
                        PowerBoxSDK.PB_WIFI_CONFIG config = new PowerBoxSDK.PB_WIFI_CONFIG();
                        if (PowerBoxSDK.PBGetWiFiConfig(deviceId, out config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            _wifi.UpdateFromConfig(config);
                        }
                    }
                    catch (Exception ex)
                    {
                        Notification.ShowError($"{ex.Message}");
                    }
                }
            }
            finally
            {
                _isHardwareUpdate.Value = false;
            }

            // start polling after a short delay to allow device to stabilize
            pollingCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            // Capture the token, not the CTS: a token stays usable after its CTS was disposed
            var pollingToken = pollingCts.Token;
            pollingTask = Task.Run(async () =>
            {
                while (!pollingToken.IsCancellationRequested)
                {
                    _isHardwareUpdate.Value = true;
                    try
                    {
                        lock (_sdkLock)
                        {
                            // Poll device status
                            try
                            {
                                PowerBoxSDK.PB_DEVICE_STATUS status;
                                if (PowerBoxSDK.PBGetStatus(deviceId, out status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                                {
                                    _consecutiveStatusFailures = 0;
                                    UpdateFromDeviceStatus(status);

                                    if (double.IsNaN(_temperature) || status.extSensor != 0)
                                    {
                                        // Try to fetch from connected WeatherData equipment (MeteoStation)
                                        try
                                        {
                                            var mediator = PINS.WeatherDataMediator;
                                            if (mediator?.GetInfo().Connected == true)
                                            {
                                                var weatherData = mediator.GetInfo();
                                                // Only a plausible reading: NaN means no reading, and the
                                                // PowerBox rejects values outside its ranges. Checked as
                                                // sent (float); slightly above 100 % (fog) counts as 100 %
                                                float extTemperature = Convert.ToSingle(weatherData.Temperature);
                                                float extHumidity = Convert.ToSingle(weatherData.Humidity);
                                                if (extHumidity > 100f && extHumidity <= 105f)
                                                {
                                                    extHumidity = 100f;
                                                }
                                                if (IsPlausibleWeather(extTemperature, extHumidity))
                                                {
                                                    _temperature = weatherData.Temperature;
                                                    _humidity = Math.Min(weatherData.Humidity, 100.0);
                                                    _dewPoint = weatherData.DewPoint;
                                                    OnPropertyChanged(nameof(Temperature));
                                                    OnPropertyChanged(nameof(Humidity));
                                                    OnPropertyChanged(nameof(DewPoint));
                                                    Logger.Trace("Fetched environment data from WeatherData equipment.");
                                                    // If we fetched data successfully, we need to set it to the powerbox so that dew control can work
                                                    PowerBoxSDK.PB_DEVICE_CONFIG config = new PowerBoxSDK.PB_DEVICE_CONFIG();
                                                    config.mask = PowerBoxSDK.MASK_PB_EXT_TEMPERATURE | PowerBoxSDK.MASK_PB_EXT_HUMIDITY;
                                                    config.temperature = extTemperature;
                                                    config.humidity = extHumidity;
                                                    PowerBoxSDK.PBSetConfig(deviceId, ref config);
                                                }
                                            }
                                        }
                                        catch (Exception meteoEx)
                                        {
                                            Logger.Trace($"Unable to fetch WeatherData: {meteoEx.Message}");
                                        }
                                    }
                                }
                                else
                                {
                                    _consecutiveStatusFailures++;
                                }
                            }
                            catch (Exception ex)
                            {
                                _consecutiveStatusFailures++;
                                LogPollingError("device status", ex);
                            }

                            // Poll power supply status
                            try
                            {
                                PowerBoxSDK.PB_SUPPLY_STATUS status;
                                if (PowerBoxSDK.PBGetSupplyStatus(deviceId, out status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                                {
                                    _powerSupply.UpdateFromStatus(status);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogPollingError("power supply status", ex);
                            }

                            // Poll power port status
                            try
                            {
                                PowerBoxSDK.PB_POWER_PORT_STATUS status;
                                if (PowerBoxSDK.PBGetPowerPortStatus(deviceId, out status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                                {
                                    _powerPorts.UpdateFromStatus(status);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogPollingError("power port status", ex);
                            }

                            // Poll USB port status
                            try
                            {
                                PowerBoxSDK.PB_USB_PORT_STATUS status;
                                if (PowerBoxSDK.PBGetUSBPortStatus(deviceId, out status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                                {
                                    _usbPorts.UpdateFromStatus(status);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogPollingError("USB port status", ex);
                            }

                            // Poll Dew port status
                            try
                            {
                                PowerBoxSDK.PB_DEW_PORT_STATUS status;
                                if (PowerBoxSDK.PBGetDewPortStatus(deviceId, out status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                                {
                                    _dewPorts.UpdateFromStatus(status);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogPollingError("Dew port status", ex);
                            }

                            // Poll Buck port status
                            try
                            {
                                PowerBoxSDK.PB_BUCK_PORT_STATUS status;
                                if (PowerBoxSDK.PBGetBuckPortStatus(deviceId, out status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                                {
                                    _buckPorts.UpdateFromStatus(status);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogPollingError("Buck port status", ex);
                            }

                            // Poll PWM port status
                            try
                            {
                                PowerBoxSDK.PB_PWM_PORT_STATUS status;
                                if (PowerBoxSDK.PBGetPWMPortStatus(deviceId, out status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                                {
                                    _pwmPorts.UpdateFromStatus(status);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogPollingError("PWM port status", ex);
                            }

                            // Poll WiFi status
                            try
                            {
                                PowerBoxSDK.PB_WIFI_STATUS status;
                                if (PowerBoxSDK.PBGetWiFiStatus(deviceId, out status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                                {
                                    _wifi.UpdateFromStatus(status);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogPollingError("WiFi status", ex);
                            }

                            // Update 5V current: sum of the ports with a valid measurement (NaN =
                            // unknown, e.g. no sensor or an I2C error); unknown if there is none
                            double sum5A = 0.0;
                            int valid5A = 0;
                            foreach (var port in USBPorts.Ports.Take(_actualUSBPortCount))
                            {
                                if (!double.IsNaN(port.Current))
                                {
                                    sum5A += port.Current;
                                    valid5A++;
                                }
                            }
                            _supply5A = valid5A > 0 ? Math.Round(sum5A, 2) : double.NaN;
                            _supply5W = Math.Round(_supply5A * _powerSupply.Supply5V, 2);

                            OnPropertyChanged(nameof(Supply5A));
                            OnPropertyChanged(nameof(Supply5W));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Never let an exception escape the loop, otherwise the polling task
                        // ends silently and all values freeze until the device is reconnected
                        LogPollingError("polling loop", ex);
                    }
                    finally
                    {
                        _isHardwareUpdate.Value = false;
                    }

                    if (_consecutiveStatusFailures >= MaxConsecutiveStatusFailures)
                    {
                        Logger.Error($"{Name}: no response for {_consecutiveStatusFailures} consecutive polls. Treating device as disconnected.");
                        Notification.ShowError($"{Name} stopped responding and was disconnected.");
                        HandleDeviceLost();
                        break;
                    }

                    // Refresh all switch values so the NINA Switches tab reflects the latest state
                    foreach (var sw in _pollSwitches)
                    {
                        try
                        {
                            sw.Poll();
                        }
                        catch (Exception ex)
                        {
                            LogPollingError($"switch {sw.Id}", ex);
                        }
                    }

                    try
                    {
                        await Task.Delay(1000, pollingToken).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, pollingToken);

            // Should never trigger since the loop catches everything, but if the task dies
            // anyway it must not do so silently
            _ = pollingTask.ContinueWith(
                t => Logger.Error($"PowerBox polling task terminated: {t.Exception}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            // We need to wait for the status updates to arrive, so we simply loop a bit
            // Add a timeout to prevent infinite waiting (max 5 seconds)
            bool NotYetInitialized() =>
                (_actualDewPortCount > 0 && DewPorts.Ports.Count > 0 && DewPorts.Ports[0].Resolution == 0)
                // Negated, so a not yet received (NaN) value also waits
                || (_actualBuckPortCount > 0 && BuckPorts.Ports.Count > 0 && !(BuckPorts.Ports[0].MaxVoltage >= 1.0))
                || (_actualPWMPortCount > 0 && PWMPorts.Ports.Count > 0 && PWMPorts.Ports[0].Resolution == 0);

            var waitTimeout = DateTime.Now.AddSeconds(5);
            while (NotYetInitialized()
                && !token.IsCancellationRequested
                && DateTime.Now < waitTimeout)
            {
                try
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            // Check if we timed out before all values were initialized
            if (NotYetInitialized())
            {
                var errorMsg = $"PowerBox connection failed: Timeout waiting for device status initialization. Dew Resolution: {(DewPorts.Ports.Count > 0 ? DewPorts.Ports[0].Resolution : 0)}, Buck MaxVoltage: {(BuckPorts.Ports.Count > 0 ? BuckPorts.Ports[0].MaxVoltage : 0)}, PWM Resolution: {(PWMPorts.Ports.Count > 0 ? PWMPorts.Ports[0].Resolution : 0)}";
                Logger.Error(errorMsg);
                Notification.ShowError(errorMsg);

                Disconnect();

                Connected = false;
                return Connected;
            }

            // Scan for switches
            ScanForSwitches();

            // Register this PowerBox instance so other drivers (MeteoStation) can access environment data
            PINS.ConnectedPowerBox = this;

            Connected = true;

            return Connected;
        }

        public void Disconnect()
        {
            try
            {
                pollingCts?.Cancel();
                try
                {
                    // Wait for the polling task to fully stop before closing the device,
                    // otherwise in-flight PBGet*/PBSetConfig calls race the close and
                    // operate on a closed (and possibly reused) device id.
                    if (pollingTask != null && !pollingTask.Wait(TimeSpan.FromSeconds(10)))
                    {
                        Logger.Warning("PowerBox polling task did not stop within 10 s, closing anyway");
                    }
                }
                catch { }

                lock (_sdkLock)
                {
                    PowerBoxSDK.PBClose(deviceId);
                }
            }
            catch { }
            finally
            {
                // Unregister this PowerBox instance
                if (PINS.ConnectedPowerBox == this)
                {
                    PINS.ConnectedPowerBox = null;
                }
                Connected = false;
            }
        }

        // Called from within the polling loop itself when the device stops responding.
        // Unlike Disconnect(), this must not cancel/wait on pollingTask - that would
        // deadlock since we are running on that very task.
        private void HandleDeviceLost()
        {
            try
            {
                lock (_sdkLock)
                {
                    PowerBoxSDK.PBClose(deviceId);
                }
            }
            catch { }
            finally
            {
                if (PINS.ConnectedPowerBox == this)
                {
                    PINS.ConnectedPowerBox = null;
                }
                Connected = false;
            }
        }

        public void Dispose()
        {
            Disconnect();
            pollingCts?.Dispose();
        }

        private void ScanForSwitches()
        {
            Logger.Trace("Scanning for switches...");
            _pollSwitches = Array.Empty<PowerBoxSwitch>();
            _scannedSwitches.Clear();
            Switches.Clear();

            AddSwitch(new PowerBoxSwitch(() => "12V rail", () => "Supply [V]", () => _powerSupply.Supply12V, 0));
            AddSwitch(new PowerBoxSwitch(() => "Temperature", () => "Environment [°C]", () => Temperature, 1));
            AddSwitch(new PowerBoxSwitch(() => "5V rail", () => "Supply [V]", () => _powerSupply.Supply5V, 2));
            AddSwitch(new PowerBoxSwitch(() => "Humidity", () => "Environment [%]", () => Humidity, 3));
            AddSwitch(new PowerBoxSwitch(() => "Current", () => "Supply [A]", () => _powerSupply.Supply12A, 4));
            AddSwitch(new PowerBoxSwitch(() => "Dew Point", () => "Environment [°C]", () => DewPoint, 5));
            AddSwitch(new PowerBoxSwitch(() => "Power", () => "Supply [W]", () => _powerSupply.Supply12W, 6));
            AddSwitch(new PowerBoxSwitch(() => "AverageAmps", () => "Consumption average amps", () => _powerSupply.AverageAmps, 7));
            AddSwitch(new PowerBoxSwitch(() => "WattsPerHour", () => "Consumption watts per hour", () => _powerSupply.WattsPerHour, 8));
            AddSwitch(new PowerBoxSwitch(() => "AmpsPerHour", () => "Consumption amps per hour", () => _powerSupply.AmpsPerHour, 9));

            short switchId = 10;

            // Power hub
            Logger.Trace("Configuring power hub switches...");
            foreach (var port in PowerPorts.Ports.Take(_actualPowerPortCount))
            {
                if (port.ReadOnly)
                {
                    AddSwitch(new PowerBoxSwitch(() => port.Name, () => port.Overcurrent
                        ? $"12V #{port.Index + 1}: always on, over current (not switchable)"
                        : $"12V #{port.Index + 1}: always on (Current [A])", () => port.Current, switchId));
                }
                else
                {
                    AddSwitch(new PowerBoxWritableSwitch(
                        () => port.Name,
                        () => port.Overcurrent
                            ? $"12V #{port.Index + 1}: {OvercurrentText(port.Enabled)}"
                            : $"12V #{port.Index + 1}: {(port.Enabled ? port.Current : 0)}A",
                        () => port.Enabled ? 1 : 0,
                        (value) => port.Enabled = value != 0,
                        switchId));
                }
                switchId++;
            }

            // USB hub
            Logger.Trace("Configuring USB hub switches...");
            foreach (var port in USBPorts.Ports.Take(_actualUSBPortCount))
            {
                if (port.ReadOnly)
                {
                    AddSwitch(new PowerBoxSwitch(
                        () => port.Name,
                        () => $"USB #{port.Index + 1}: {UsbCurrentText(port.Current)}",
                        () => port.Current,
                        switchId));
                }
                else
                {
                AddSwitch(new PowerBoxWritableSwitch(
                    () => port.Name,
                    () => port.Overcurrent
                        ? (port.Enabled
                            ? $"USB #{port.Index + 1}: over current, switch-off failed"
                            : $"USB #{port.Index + 1}: over current, switched off")
                        : $"USB #{port.Index + 1}: {(port.Enabled ? UsbCurrentText(port.Current) : "0A")}",
                    () => port.Enabled ? 1 : 0,
                    (value) => port.Enabled = value != 0,
                    switchId));
                }
                switchId++;
            }

            // Dew hub - auto dew threshold
            Logger.Trace("Configuring Dew hub switches...");

            foreach (var port in DewPorts.Ports.Take(_actualDewPortCount))
            {
                AddSwitch(new PowerBoxWritableSwitch(
                    () => $"{port.Name} auto dew threshold",
                    () => "Set threshold [°C]",
                    () => port.AutoThreshold,
                    (value) => port.AutoThreshold = value,
                    switchId, 0.0, 25.5, 0.1));
                switchId++;
            }

            // Dew hub - auto mode
            foreach (var port in DewPorts.Ports.Take(_actualDewPortCount))
            {
                AddSwitch(new PowerBoxWritableSwitch(
                    () => $"{port.Name} auto mode",
                    () => $"Probe: {(port.Probe == -127.0f ? "--" : port.Probe)}°C",
                    () => port.AutoMode ? 1 : 0,
                    (value) => port.AutoMode = value != 0,
                    switchId));
                switchId++;
            }

            // Dew hub
            foreach (var port in DewPorts.Ports.Take(_actualDewPortCount))
            {
                AddSwitch(new PowerBoxWritableSwitch(
                    () => port.Name,
                    () => port.Overcurrent
                        ? $"Dew #{port.Index + 1}: {OvercurrentText(port.Enabled)}{(port.AutoMode && !port.Enabled ? ", switch auto mode off and on to resume" : "")}"
                        : $"Dew #{port.Index + 1}: {(port.Enabled ? Math.Round(port.Power * 100.0 / port.Resolution, 1) : 0)}% ({(port.Enabled ? Math.Round(port.Current, 2) : 0)}A)",
                    () => port.Enabled ? port.Power : 0,
                    (value) =>
                    {
                        // 0 = off, otherwise on with this power. Invalid values throw without
                        // touching the port (see the buck switch); state and power are sent in
                        // one call, so the port never runs with the previous power first.
                        string name = $"Dew #{port.Index + 1}";
                        int power = CheckPortPowerValue(name, value, port.Resolution);
                        if (port.AutoMode)
                        {
                            throw SwitchValueError($"{name}: auto mode is on, switch it off to set the power");
                        }

                        var result = ApplyDewPortConfig(port, power > 0, power);
                        if (result != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                        {
                            throw new InvalidOperationException($"{name}: setting the switch value failed ({result})");
                        }
                    },
                    switchId,
                    0,
                    port.Resolution,
                    1));
                switchId++;
            }

            // Buck port
            if (_actualBuckPortCount > 0)
            {
            var buckPort = BuckPorts.Ports[0];
            AddSwitch(new PowerBoxWritableSwitch(
                            () => buckPort.Name,
                            () => buckPort.Overcurrent
                                ? $"Buck converter: {OvercurrentText(buckPort.Enabled)}"
                                : $"Buck converter: {(buckPort.Enabled ? Math.Round(buckPort.Voltage, 2) : 0)}V ({(buckPort.Enabled ? Math.Round(buckPort.Current, 2) : 0)}A)",
                            () =>
                            {
                                return buckPort.Enabled == false ? 0 : Math.Round(buckPort.SetVoltage, 2);
                            },
                            (value) =>
                            {
                                // 0 = off, the target voltage is kept (only the state is sent;
                                // serial PowerBox: the stored target is sent along, the firmware
                                // replaces it by vmin if it is out of its current range, and before
                                // the first ADJ status line it is the SDK default 1.0 V unless a
                                // target was sent before).
                                // Otherwise on with the given voltage. State and voltage are sent
                                // in one call, so the port is never switched on with the previous
                                // voltage first (PinsBox only: the serial PowerBox firmware switches
                                // on first and only then checks and sets the voltage, so the port
                                // briefly runs at the previous DAC voltage; a rejected target
                                // switches it off again).
                                // N.I.N.A. does not range check written values (SwitchVM/API); the
                                // switch panel stepper only clamps to the Minimum/Maximum fixed at
                                // switch creation, and the sequencer validation checks them. A setter
                                // that returns normally is only verified by reading the value back
                                // (switch panel and sequencer; timeout after 4 x N.I.N.A.'s device
                                // polling interval; the Advanced API does not wait for the write at
                                // all). The read-back right after a successful write is the requested
                                // value (serial PowerBox: until the next ADJ status line), so a
                                // firmware rejection is not detected. Invalid values therefore throw
                                // right away, without touching the port.
                                double min = double.IsNaN(buckPort.MinVoltage) ? 1.0 : Math.Max(1.0, buckPort.MinVoltage);
                                double max = buckPort.MaxVoltage;
                                string range = double.IsNaN(max) ? $"0 = off, min {min} V" : $"0 = off, {min} - {max} V";

                                if (!double.IsFinite(value))
                                {
                                    throw SwitchValueError($"Buck converter: invalid value {value} V ({range})");
                                }

                                // Snap pure floating point noise to the 0.01 V grid (e.g. the N.I.N.A.
                                // stepper adds 0.1 ten times and gets 0.9999999999999999). Anything
                                // further away is rejected: rounding it would change the meaning
                                // (e.g. 0.995 -> 1.0 = on) and N.I.N.A.'s check after setting
                                // (tolerance 1e-5 against the unrounded value) would fail anyway.
                                double rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
                                if (Math.Abs(value - rounded) > 1e-6)
                                {
                                    throw SwitchValueError($"Buck converter: {value} V has more than 2 decimals ({range})");
                                }

                                if (rounded < 0 || (!double.IsNaN(max) && rounded > max))
                                {
                                    throw SwitchValueError($"Buck converter: invalid value {value} V ({range})");
                                }

                                PowerBoxSDK.PB_ERROR_TYPE result;
                                if (rounded == 0)
                                {
                                    result = ApplyBuckPortConfig(buckPort, false, double.NaN);
                                }
                                else if (rounded < min)
                                {
                                    throw SwitchValueError($"Buck converter: {value} V is below the minimum of {min} V, use 0 to switch off");
                                }
                                else
                                {
                                    result = ApplyBuckPortConfig(buckPort, true, rounded);
                                }

                                // The error was already shown (ShowBuckPortError). Throw, so N.I.N.A.
                                // sees the failure right away instead of waiting for a timeout.
                                if (result != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                                {
                                    throw new InvalidOperationException($"Buck converter: setting the switch value failed ({result})");
                                }
                            },
                            switchId,
                            // Minimum 0, so "off" (value 0, as reported while off) is a valid
                            // value for N.I.N.A. and the sequencer
                            0.0,
                            buckPort.MaxVoltage,
                            0.1));
            switchId++;
            } // end Buck port

            // PWM port
            if (_actualPWMPortCount > 0)
            {
            var pwmPort = PWMPorts.Ports[0];
            AddSwitch(new PowerBoxWritableSwitch(
                () => pwmPort.Name,
                () => pwmPort.Overcurrent
                    ? $"PWM port: {OvercurrentText(pwmPort.Enabled)}"
                    : $"PWM port: {(pwmPort.Enabled ? Math.Round(pwmPort.Power * 100.0 / pwmPort.Resolution, 1) : 0)}% ({(pwmPort.Enabled ? Math.Round(pwmPort.Current, 2) : 0)}A)",
                () =>
                {
                    if (pwmPort.Enabled == false)
                    {
                        return 0;
                    }
                    return pwmPort.Power;
                },
                (value) =>
                {
                    // 0 = off, otherwise on with this power, in one call (see the dew switch)
                    int power = CheckPortPowerValue("PWM port", value, pwmPort.Resolution);

                    var result = ApplyPWMPortConfig(pwmPort, power > 0, power);
                    if (result != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        throw new InvalidOperationException($"PWM port: setting the switch value failed ({result})");
                    }
                },
                switchId,
                0,
                pwmPort.Resolution,
                1));
            switchId++;
            } // end PWM port

            _pollSwitches = _scannedSwitches.ToArray();
            Logger.Trace($"Total switches configured: {_pollSwitches.Length}");
        }

        /// <summary>
        /// Over current text for a switch description; still on: the switch-off failed or is
        /// not reported yet.
        /// </summary>
        private static string OvercurrentText(bool stillOn)
        {
            return stillOn ? "over current, switch-off failed" : "over current, switched off";
        }

        /// <summary>
        /// USB current for a switch description; "--" if unknown (no sensor, or no valid
        /// measurement of a port that is on).
        /// </summary>
        private static string UsbCurrentText(double current)
        {
            return double.IsNaN(current) ? "--A" : $"{current}A";
        }

        private void AddSwitch(PowerBoxSwitch sw)
        {
            _scannedSwitches.Add(sw);
            Switches.Add(sw);
        }

        /// <summary>
        /// Logs a polling error, but the same error only once every 5 minutes since the loop
        /// runs every second. Returns true when the error was logged.
        /// </summary>
        private bool LogPollingError(string source, Exception ex)
        {
            var key = $"{source}|{ex.GetType().FullName}|{ex.Message}";
            var now = DateTime.UtcNow;
            lock (_lastPollErrors)
            {
                if (_lastPollErrors.TryGetValue(key, out var last) && now - last < TimeSpan.FromMinutes(5))
                {
                    return false;
                }
                _lastPollErrors[key] = now;
            }
            Logger.Error($"PowerBox polling error ({source}): {ex}");
            return true;
        }

        private void SubscribeToPortChanges()
        {
            foreach (var port in PowerPorts.Ports)
            {
                port.PropertyChanged += (s, e) =>
                {
                    Logger.Trace($"PowerPort {port.Index} property changed: {e.PropertyName}");
                    if (_isHardwareUpdate.Value)
                    {
                        Logger.Trace($"Ignoring change during hardware update");
                        return;
                    }
                    if (e.PropertyName == nameof(PowerBoxPort.Name))
                    {
                        Logger.Trace($"PowerPort {port.Index} name changed to '{port.Name}'. Persisting...");
                        PersistPortNames();
                        return;
                    }
                    uint mask = e.PropertyName switch
                    {
                        nameof(PowerBoxPort.Enabled) => PowerBoxSDK.MASK_PORT_ENABLE,
                        nameof(PowerBoxPort.BootState) => PowerBoxSDK.MASK_PORT_BOOT_STATE,
                        _ => 0
                    };
                    if (mask != 0)
                    {
                        Logger.Trace($"PowerPort {port.Index} property {e.PropertyName} changed. Persisting...");
                        if (!SetPowerPortConfig(port, mask))
                        {
                            RefreshPowerPortConfig(port.Index);
                        }
                        else if (mask == PowerBoxSDK.MASK_PORT_ENABLE)
                        {
                            // Switching clears the over current flag: show it at once, so the
                            // over current button does not flip under the mouse until the next poll
                            RefreshPowerPortStatus();
                        }
                    }
                };
            }

            foreach (var port in USBPorts.Ports)
            {
                port.PropertyChanged += (s, e) =>
                {
                    if (_isHardwareUpdate.Value) return;
                    if (e.PropertyName == nameof(PowerBoxPort.Name))
                    {
                        Logger.Trace($"USBPort {port.Index} name changed. Persisting...");
                        PersistPortNames();
                        return;
                    }
                    uint mask = e.PropertyName switch
                    {
                        nameof(PowerBoxPort.Enabled) => PowerBoxSDK.MASK_PORT_ENABLE,
                        nameof(PowerBoxPort.BootState) => PowerBoxSDK.MASK_PORT_BOOT_STATE,
                        _ => 0
                    };
                    if (mask != 0)
                    {
                        Logger.Trace($"USBPort {port.Index} property {e.PropertyName} changed. Persisting...");
                        if (!SetUSBPortConfig(port, mask))
                        {
                            RefreshUSBPortConfig(port.Index);
                        }
                        else if (mask == PowerBoxSDK.MASK_PORT_ENABLE)
                        {
                            // Switching clears the over current flag: show it at once, so the
                            // over current button does not flip under the mouse until the next poll
                            RefreshUSBPortStatus();
                        }
                    }
                };
            }

            foreach (var port in DewPorts.Ports)
            {
                port.PropertyChanged += (s, e) =>
                {
                    if (_isHardwareUpdate.Value) return;
                    if (e.PropertyName == nameof(PowerBoxPort.Name))
                    {
                        Logger.Trace($"DewPort {port.Index} name changed. Persisting...");
                        PersistPortNames();
                        return;
                    }
                    PowerBoxDewPort dewPort = (PowerBoxDewPort)port;
                    uint mask = e.PropertyName switch
                    {
                        // When enabling the dew port, always include the current power level in the
                        // same SDK call so the port turns on at the correct duty cycle. Without this,
                        // a subsequent SetPower write may be skipped (see PWMPort.SetPower below) because
                        // the hardware-reported power level still matches the requested value, leaving
                        // the dew port enabled but outputting 0% if the firmware reset the duty cycle on
                        // the previous disable.
                        nameof(PowerBoxPort.Enabled) when dewPort.Enabled => PowerBoxSDK.MASK_PORT_ENABLE | PowerBoxSDK.MASK_PORT_POWER,
                        nameof(PowerBoxPort.Enabled) => PowerBoxSDK.MASK_PORT_ENABLE,
                        nameof(PowerBoxDewPort.AutoMode) => PowerBoxSDK.MASK_PORT_AUTO_DEW_MODE,
                        nameof(PowerBoxDewPort.AutoThreshold) => PowerBoxSDK.MASK_PORT_AUTO_DEW_THRESHOLD,
                        nameof(PowerBoxDewPort.SetPower) => PowerBoxSDK.MASK_PORT_POWER,
                        _ => 0
                    };
                    if (mask != 0)
                    {
                        Logger.Trace($"DewPort {port.Index} property {e.PropertyName} changed. Persisting...");
                        if (!SetDewPortConfig(dewPort, mask))
                        {
                            RefreshDewPortConfig(port.Index);
                        }
                        else if (e.PropertyName == nameof(PowerBoxDewPort.AutoMode))
                        {
                            // Auto mode on/off is bumpless in the box: the port keeps the state and
                            // power the regulation had, read them, otherwise SetPower is stale (and a
                            // write of that value would be skipped as unchanged)
                            RefreshDewPortConfig(port.Index);
                        }
                        if ((mask & PowerBoxSDK.MASK_PORT_ENABLE) != 0 || e.PropertyName == nameof(PowerBoxDewPort.AutoMode))
                        {
                            // Switching (and auto mode on) changes the over current flag: show it at
                            // once, so the over current button does not flip under the mouse
                            RefreshDewPortStatus();
                        }
                    }
                };
            }

            var buck = BuckPorts.Ports[0];
            buck.PropertyChanged += (s, e) =>
            {
                if (_isHardwareUpdate.Value) return;
                if (e.PropertyName == nameof(PowerBoxPort.Name))
                {
                    Logger.Trace("BuckPort name changed. Persisting...");
                    PersistPortNames();
                    return;
                }
                uint mask = e.PropertyName switch
                {
                    nameof(PowerBoxPort.Enabled) => PowerBoxSDK.MASK_PORT_ENABLE,
                    nameof(PowerBoxBuckPort.SetVoltage) => PowerBoxSDK.MASK_PORT_VOLTAGE,
                    _ => 0
                };
                if (mask != 0)
                {
                    Logger.Trace($"BuckPort property {e.PropertyName} changed. Persisting...");
                    if (!SetBuckPortConfig(buck, mask))
                    {
                        RefreshBuckPortConfig();
                    }
                    if ((mask & PowerBoxSDK.MASK_PORT_ENABLE) != 0)
                    {
                        // Switching changes the over current flag: show it at once
                        RefreshBuckPortStatus();
                    }
                }
            };

            var pwm = PWMPorts.Ports[0];
            pwm.PropertyChanged += (s, e) =>
            {
                if (_isHardwareUpdate.Value) return;
                if (e.PropertyName == nameof(PowerBoxPort.Name))
                {
                    Logger.Trace("PWMPort name changed. Persisting...");
                    PersistPortNames();
                    return;
                }
                uint mask = e.PropertyName switch
                {
                    // When enabling the PWM port, always include the current power level in the
                    // same SDK call so the port turns on at the correct brightness. Without this,
                    // a subsequent SetBrightness call may be skipped by FlatDeviceVM because the
                    // hardware-reported power level still matches the requested value, leaving the
                    // PWM enabled but outputting nothing if the firmware reset the duty cycle on
                    // the previous disable.
                    nameof(PowerBoxPort.Enabled) when pwm.Enabled => PowerBoxSDK.MASK_PORT_ENABLE | PowerBoxSDK.MASK_PORT_POWER,
                    nameof(PowerBoxPort.Enabled) => PowerBoxSDK.MASK_PORT_ENABLE,
                    nameof(PowerBoxPWMPort.SetPower) => PowerBoxSDK.MASK_PORT_POWER,
                    _ => 0
                };
                if (mask != 0)
                {
                    Logger.Trace($"PWMPort property {e.PropertyName} changed. Persisting...");
                    if (!SetPWMPortConfig(pwm, mask))
                    {
                        RefreshPWMPortConfig();
                    }
                    if ((mask & PowerBoxSDK.MASK_PORT_ENABLE) != 0)
                    {
                        // Switching changes the over current flag: show it at once
                        RefreshPWMPortStatus();
                    }
                }
            };
        }

        private JObject Load(string uuid)
        {
            lock (_configLock)
            {
                string configPath = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "Config", "powerbox.cfg");

                Directory.CreateDirectory(Path.GetDirectoryName(configPath));

                if (!File.Exists(configPath))
                {
                    return null;
                }

                try
                {
                    using var fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    using var reader = new StreamReader(fs);
                    var jsonConfig = reader.ReadToEnd();

                    dynamic jsonObj = JsonConvert.DeserializeObject(jsonConfig) ?? new JObject();

                    if (jsonObj.ContainsKey(uuid))
                    {
                        return jsonObj[uuid] as JObject;
                    }
                }
                catch (Exception ex)
                {
                    Notification.ShowError($"Error reading config file: {ex.Message}");
                }

                return null;
            }
        }

        private void Store(string uuid, JObject config)
        {
            lock (_configLock)
            {
                string configPath = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "Config", "powerbox.cfg");

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));

                // Create file if it doesn't exist
                if (!File.Exists(configPath))
                {
                    File.WriteAllText(configPath, "{}");
                }

                // Read existing JSON
                dynamic jsonObj;
                try
                {
                    using (var reader = new StreamReader(configPath))
                    {
                        var jsonConfig = reader.ReadToEnd();
                        jsonObj = JsonConvert.DeserializeObject(jsonConfig) ?? new JObject();
                    }
                }
                catch (Exception ex)
                {
                    Notification.ShowError($"Error reading config file: {ex.Message}");
                    return;
                }

                // Modify the JSON object
                if (jsonObj.ContainsKey(uuid))
                {
                    jsonObj.Remove(uuid);
                }

                jsonObj[uuid] = config;

                // Serialize and write back to file
                try
                {
                    string updatedJson = JsonConvert.SerializeObject(jsonObj, Formatting.Indented);
                    File.WriteAllText(configPath, updatedJson);
                }
                catch (Exception ex)
                {
                    Notification.ShowError($"Error writing config file: {ex.Message}");
                }
            }
        }

        private void SetNames(JObject config)
        {
            try
            {
                var hubConfig = config["Hub"] as JObject;
                if (hubConfig != null)
                {
                    // 12 V and USB ports: a missing or empty name (e.g. ports 7/8 of configs
                    // written before the PinsBox had 8 ports) falls back to the default name
                    var defaultHub = GetDefaultConfig()["Hub"] as JObject;
                    string NameOrDefault(string group, string key)
                    {
                        string name = (hubConfig[group] as JObject)?[key]?.ToString();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = (defaultHub?[group] as JObject)?[key]?.ToString();
                        }
                        return name;
                    }

                    foreach (var port in PowerPorts.Ports)
                    {
                        var name = NameOrDefault("PowerPort", $"Port{port.Index + 1}");
                        if (name != null)
                        {
                            port.Name = name;
                        }
                    }
                    foreach (var port in USBPorts.Ports)
                    {
                        var name = NameOrDefault("USBPort", $"Port{port.Index + 1}");
                        if (name != null)
                        {
                            port.Name = name;
                        }
                    }
                    var dewPortConfig = hubConfig["DewPort"] as JObject;
                    if (dewPortConfig != null)
                    {
                        foreach (var port in DewPorts.Ports)
                        {
                            var nameToken = dewPortConfig[$"Port{port.Index + 1}"];
                            if (nameToken != null)
                            {
                                port.Name = nameToken.ToString();
                            }
                        }
                    }
                    var adjPortConfig = hubConfig["AdjPort"] as JObject;
                    if (adjPortConfig != null)
                    {
                        var buckNameToken = adjPortConfig["Port1"];
                        if (buckNameToken != null)
                        {
                            BuckPorts.Ports[0].Name = buckNameToken.ToString();
                        }
                        var pwmNameToken = adjPortConfig["Port2"];
                        if (pwmNameToken != null)
                        {
                            PWMPorts.Ports[0].Name = pwmNameToken.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Notification.ShowError($"Error setting port names from config: {ex.Message}");
            }
        }

        private void PersistPortNames()
        {
            try
            {
                lock (_configLock)
                {
                    string configPath = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "Config", "powerbox.cfg");

                    // Ensure directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(configPath));

                    // Create file if it doesn't exist
                    if (!File.Exists(configPath))
                    {
                        File.WriteAllText(configPath, "{}");
                    }

                    // Read existing JSON
                    dynamic jsonObj;
                    try
                    {
                        using (var reader = new StreamReader(configPath))
                        {
                            var jsonConfig = reader.ReadToEnd();
                            jsonObj = JsonConvert.DeserializeObject(jsonConfig) ?? new JObject();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error reading config file for port names: {ex.Message}");
                        return;
                    }

                    // Get or create the device config
                    JObject config = jsonObj.ContainsKey(Id) ? jsonObj[Id] as JObject : GetDefaultConfig();
                    if (config == null)
                    {
                        config = GetDefaultConfig();
                    }

                    var hubConfig = config["Hub"] as JObject;
                    if (hubConfig == null)
                    {
                        hubConfig = new JObject();
                        config["Hub"] = hubConfig;
                    }

                    // Persist Power Port names
                    var powerPortConfig = hubConfig["PowerPort"] as JObject;
                    if (powerPortConfig == null)
                    {
                        powerPortConfig = new JObject();
                        hubConfig["PowerPort"] = powerPortConfig;
                    }
                    foreach (var port in PowerPorts.Ports)
                    {
                        powerPortConfig[$"Port{port.Index + 1}"] = port.Name;
                    }

                    // Persist USB Port names
                    var usbPortConfig = hubConfig["USBPort"] as JObject;
                    if (usbPortConfig == null)
                    {
                        usbPortConfig = new JObject();
                        hubConfig["USBPort"] = usbPortConfig;
                    }
                    foreach (var port in USBPorts.Ports)
                    {
                        usbPortConfig[$"Port{port.Index + 1}"] = port.Name;
                    }

                    // Persist Dew Port names
                    var dewPortConfig = hubConfig["DewPort"] as JObject;
                    if (dewPortConfig == null)
                    {
                        dewPortConfig = new JObject();
                        hubConfig["DewPort"] = dewPortConfig;
                    }
                    foreach (var port in DewPorts.Ports)
                    {
                        dewPortConfig[$"Port{port.Index + 1}"] = port.Name;
                    }

                    // Persist Adjustable Port names
                    var adjPortConfig = hubConfig["AdjPort"] as JObject;
                    if (adjPortConfig == null)
                    {
                        adjPortConfig = new JObject();
                        hubConfig["AdjPort"] = adjPortConfig;
                    }
                    adjPortConfig["Port1"] = _actualBuckPortCount > 0 ? BuckPorts.Ports[0].Name : string.Empty;
                    adjPortConfig["Port2"] = _actualPWMPortCount > 0 ? PWMPorts.Ports[0].Name : string.Empty;

                    // Update the config in jsonObj
                    if (jsonObj.ContainsKey(Id))
                    {
                        jsonObj.Remove(Id);
                    }
                    jsonObj[Id] = config;

                    // Serialize and write back to file
                    try
                    {
                        string updatedJson = JsonConvert.SerializeObject(jsonObj, Formatting.Indented);
                        File.WriteAllText(configPath, updatedJson);
                        Logger.Trace("Port names persisted successfully");
                    }
                    catch (Exception ex)
                    {
                        Notification.ShowError($"Error writing config file: {ex.Message}");
                        Logger.Error($"Error writing port names to config: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Notification.ShowError($"Error persisting port names: {ex.Message}");
                Logger.Error($"Error persisting port names: {ex}");
            }
        }

        private static JObject GetDefaultConfig()
        {
            // Return a new entry for the uuid
            return new JObject
            {
                ["Hub"] = new JObject
                {
                    ["PowerPort"] = new JObject
                    {
                        ["Port1"] = "12V #1",
                        ["Port2"] = "12V #2",
                        ["Port3"] = "12V #3",
                        ["Port4"] = "12V #4",
                        ["Port5"] = "12V #5",
                        ["Port6"] = "12V #6",
                        ["Port7"] = "12V #7",
                        ["Port8"] = "12V #8"
                    },
                    ["AdjPort"] = new JObject
                    {
                        ["Port1"] = "Buck converter",
                        ["Port2"] = "PWM Port"
                    },
                    ["USBPort"] = new JObject
                    {
                        ["Port1"] = "USB3 #1",
                        ["Port2"] = "USB3 #2",
                        ["Port3"] = "USB3 #3",
                        ["Port4"] = "USB3 #4",
                        ["Port5"] = "USB2 #1",
                        ["Port6"] = "USB2 #2",
                        ["Port7"] = "USB2 #3",
                        ["Port8"] = "USB2 #4"
                    },
                    ["DewPort"] = new JObject
                    {
                        ["Port1"] = "Dew #1",
                        ["Port2"] = "Dew #2"
                    }
                }
            };
        }

        private bool SetPowerPortConfig(PowerBoxPort port, uint mask)
        {
            try
            {
                PowerBoxSDK.PB_POWER_PORT_CONFIG config = new PowerBoxSDK.PB_POWER_PORT_CONFIG();
                config.mask = mask;
                config.index = (uint)port.Index;
                config.enabled = port.Enabled ? 1 : 0;
                config.bootState = port.BootState ? 1 : 0;

                PowerBoxSDK.PB_ERROR_TYPE result;
                lock (_sdkLock)
                {
                    result = PowerBoxSDK.PBSetPowerPortConfig(deviceId, ref config);
                }
                if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                {
                    return true;
                }
                // Outside the lock (the notification may wait for the UI thread)
                Logger.Error($"12V #{port.Index + 1} '{port.Name}': failed to set the configuration (mask 0x{mask:X}, enabled {config.enabled}): {result}");
                Notification.ShowError($"12V #{port.Index + 1}: failed to set the power port configuration ({result}), see log.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"12V #{port.Index + 1} '{port.Name}': error setting the configuration: {ex}");
                Notification.ShowError($"Error setting port config: {ex.Message}");
                return false;
            }
        }

        private bool SetUSBPortConfig(PowerBoxPort port, uint mask)
        {
            try
            {
                PowerBoxSDK.PB_USB_PORT_CONFIG config = new PowerBoxSDK.PB_USB_PORT_CONFIG();
                config.mask = mask;
                config.index = (uint)port.Index;
                config.enabled = port.Enabled ? 1 : 0;
                config.bootState = port.BootState ? 1 : 0;

                PowerBoxSDK.PB_ERROR_TYPE result;
                lock (_sdkLock)
                {
                    result = PowerBoxSDK.PBSetUSBPortConfig(deviceId, ref config);
                }
                if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                {
                    return true;
                }
                // Outside the lock (the notification may wait for the UI thread)
                Logger.Error($"USB #{port.Index + 1} '{port.Name}': failed to set the configuration (mask 0x{mask:X}, enabled {config.enabled}): {result}");
                Notification.ShowError($"USB #{port.Index + 1}: failed to set the USB port configuration ({result}), see log.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"USB #{port.Index + 1} '{port.Name}': error setting the configuration: {ex}");
                Notification.ShowError($"Error setting port config: {ex.Message}");
                return false;
            }
        }

        private bool SetDewPortConfig(PowerBoxDewPort port, uint mask)
        {
            try
            {
                PowerBoxSDK.PB_DEW_PORT_CONFIG config = new PowerBoxSDK.PB_DEW_PORT_CONFIG();
                config.mask = mask;
                config.index = (uint)port.Index;
                config.enabled = port.Enabled ? 1 : 0;
                config.autoThreshold = Convert.ToSingle(port.AutoThreshold);
                config.autoMode = port.AutoMode ? 1 : 0;
                config.power = port.SetPower;

                PowerBoxSDK.PB_ERROR_TYPE result;
                lock (_sdkLock)
                {
                    result = PowerBoxSDK.PBSetDewPortConfig(deviceId, ref config);
                }
                if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                {
                    return true;
                }
                ShowPortError($"Dew #{port.Index + 1}", result, null);
                return false;
            }
            catch (Exception ex)
            {
                ShowPortError($"Dew #{port.Index + 1}", PowerBoxSDK.PB_ERROR_TYPE.PB_ERROR_COMMUNICATION, ex);
                return false;
            }
        }

        /// <summary>
        /// Sets state and target voltage of the buck port with a single SDK call.
        /// Two separate calls (enable, then voltage) would switch the port on with
        /// the previous voltage first. Errors are shown (and logged) here; the result
        /// is returned so the caller can report the failure to N.I.N.A.
        /// </summary>
        private PowerBoxSDK.PB_ERROR_TYPE ApplyBuckPortConfig(PowerBoxBuckPort port, bool enabled, double voltage)
        {
            // NaN: only the state is sent, the SDK keeps the stored target voltage.
            // That is only meant for switching off. Switching on without a voltage
            // would use whatever target is stored.
            if (enabled && double.IsNaN(voltage))
            {
                throw new ArgumentException("Buck converter: switching on requires a target voltage", nameof(voltage));
            }

            uint mask = PowerBoxSDK.MASK_PORT_ENABLE;
            if (!double.IsNaN(voltage))
            {
                mask |= PowerBoxSDK.MASK_PORT_VOLTAGE;
            }

            PowerBoxSDK.PB_ERROR_TYPE result;
            Exception error = null;
            try
            {
                // The config is built from the arguments, not from the model: the polling
                // task updates the model under _sdkLock and could otherwise overwrite the
                // requested values before they are sent. The model is only updated after a
                // successful call, under the same lock (Monitor is reentrant).
                lock (_sdkLock)
                {
                    result = SendBuckPortConfig(enabled, voltage, mask);
                    if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        // Update the model without triggering the per-property handler
                        bool wasHardwareUpdate = _isHardwareUpdate.Value;
                        _isHardwareUpdate.Value = true;
                        try
                        {
                            if (!double.IsNaN(voltage))
                            {
                                port.SetVoltage = voltage;
                            }
                            port.Enabled = enabled;
                        }
                        finally
                        {
                            _isHardwareUpdate.Value = wasHardwareUpdate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result = PowerBoxSDK.PB_ERROR_TYPE.PB_ERROR_COMMUNICATION;
                error = ex;
            }

            // Notifications outside the lock, they may wait for the UI thread
            if (result != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
            {
                ShowBuckPortError(result, error);
                RefreshBuckPortConfig();
            }

            // Switching changes the over current flag: show it at once (switch descriptions)
            RefreshBuckPortStatus();

            return result;
        }

        /// <summary>
        /// Reports an invalid switch value (buck, dew, PWM) and returns the exception for the caller
        /// to throw, so the switch panel and the sequencer see the failure right away
        /// instead of waiting for N.I.N.A.'s read-back timeout. Logged as error here: the
        /// switch panel (NotifyTaskCompletion) and the sequencer log the exception too
        /// (then it appears twice), but a caller that does not await the write (e.g. the
        /// Advanced API's switch/set) never observes it.
        /// Not NotSupportedException: N.I.N.A. reserves it for "identity API unavailable".
        /// SwitchVM.SetSwitchValueByIdentity converts it to InvalidOperationException, but an
        /// ISwitchMediator/ISwitchVM implementation that passes it through would make the
        /// SetSwitchValue sequence item (with a stable identity) fall back to the index write
        /// and call this setter a second time (duplicate error).
        /// </summary>
        private static Exception SwitchValueError(string message)
        {
            Logger.Error(message);
            Notification.ShowError(message);
            return new ArgumentException(message);
        }

        /// <summary>
        /// Checks a dew/PWM switch value: a whole number from 0 to the maximum of the port
        /// (2^resolution - 1). Pure floating point noise is snapped to the whole number.
        /// Throws (see SwitchValueError) otherwise, e.g. -1 would mean full power in the box.
        /// </summary>
        private static int CheckPortPowerValue(string name, double value, int maximum)
        {
            if (maximum <= 0)
            {
                throw SwitchValueError($"{name}: PWM resolution not known yet, try again");
            }

            double rounded = Math.Round(value);
            if (!double.IsFinite(value) || Math.Abs(value - rounded) > 1e-6 || rounded < 0 || rounded > maximum)
            {
                throw SwitchValueError($"{name}: invalid value {value} (0 = off, 1 - {maximum} = on with this power)");
            }

            return (int)rounded;
        }

        /// <summary>
        /// Sets state and power of a dew port with a single SDK call (like ApplyBuckPortConfig):
        /// the config is built from the arguments, the model is updated after a successful call
        /// under _sdkLock. Errors are shown here.
        /// </summary>
        private PowerBoxSDK.PB_ERROR_TYPE ApplyDewPortConfig(PowerBoxDewPort port, bool enabled, int power)
        {
            PowerBoxSDK.PB_ERROR_TYPE result;
            Exception error = null;
            try
            {
                lock (_sdkLock)
                {
                    PowerBoxSDK.PB_DEW_PORT_CONFIG config = new PowerBoxSDK.PB_DEW_PORT_CONFIG();
                    config.mask = PowerBoxSDK.MASK_PORT_ENABLE | PowerBoxSDK.MASK_PORT_POWER;
                    config.index = (uint)port.Index;
                    config.enabled = enabled ? 1 : 0;
                    config.power = power;

                    result = PowerBoxSDK.PBSetDewPortConfig(deviceId, ref config);
                    if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        bool wasHardwareUpdate = _isHardwareUpdate.Value;
                        _isHardwareUpdate.Value = true;
                        try
                        {
                            // Switching off sets the power to 0 in the box
                            port.SetPower = enabled ? power : 0;
                            port.Enabled = enabled;
                        }
                        finally
                        {
                            _isHardwareUpdate.Value = wasHardwareUpdate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result = PowerBoxSDK.PB_ERROR_TYPE.PB_ERROR_COMMUNICATION;
                error = ex;
            }

            // Notifications outside the lock, they may wait for the UI thread
            if (result != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
            {
                ShowPortError($"Dew #{port.Index + 1}", result, error);
                RefreshDewPortConfig(port.Index);
            }

            // Switching changes the over current flag: show it at once (switch descriptions)
            RefreshDewPortStatus();

            return result;
        }

        /// <summary>
        /// Sets state and power of the PWM port with a single SDK call (see ApplyDewPortConfig).
        /// </summary>
        private PowerBoxSDK.PB_ERROR_TYPE ApplyPWMPortConfig(PowerBoxPWMPort port, bool enabled, int power)
        {
            PowerBoxSDK.PB_ERROR_TYPE result;
            Exception error = null;
            try
            {
                lock (_sdkLock)
                {
                    PowerBoxSDK.PB_PWM_PORT_CONFIG config = new PowerBoxSDK.PB_PWM_PORT_CONFIG();
                    config.mask = PowerBoxSDK.MASK_PORT_ENABLE | PowerBoxSDK.MASK_PORT_POWER;
                    config.enabled = enabled ? 1 : 0;
                    config.power = power;

                    result = PowerBoxSDK.PBSetPWMPortConfig(deviceId, ref config);
                    if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        bool wasHardwareUpdate = _isHardwareUpdate.Value;
                        _isHardwareUpdate.Value = true;
                        try
                        {
                            // Switching off sets the power to 0 in the box
                            port.SetPower = enabled ? power : 0;
                            port.Enabled = enabled;
                        }
                        finally
                        {
                            _isHardwareUpdate.Value = wasHardwareUpdate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result = PowerBoxSDK.PB_ERROR_TYPE.PB_ERROR_COMMUNICATION;
                error = ex;
            }

            if (result != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
            {
                ShowPortError("PWM port", result, error);
                RefreshPWMPortConfig();
            }

            // Switching changes the over current flag: show it at once (switch descriptions)
            RefreshPWMPortStatus();

            return result;
        }

        /// <summary>
        /// Shows and logs a failed dew/PWM port configuration. Dew port names start with "Dew".
        /// </summary>
        private static void ShowPortError(string name, PowerBoxSDK.PB_ERROR_TYPE result, Exception error)
        {
            // Only dew ports have an auto mode and a threshold
            bool isDew = name.StartsWith("Dew", StringComparison.Ordinal);
            string pinsBoxHint = isDew
                ? "e.g. PWM not set up, the port is in auto mode, or an over current shutdown is pending (switch the port off)"
                : "e.g. PWM not set up, or an over current shutdown is pending (switch the port off)";
            string invalidHint = isDew ? "power or threshold out of range" : "power out of range";

            if (error != null)
            {
                Logger.Error($"{name}: error setting the configuration: {error}");
                Notification.ShowError($"{name}: error setting the configuration: {error.Message}");
            }
            else if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_ERROR_INVALID_PARAMETER)
            {
                Logger.Error($"{name}: invalid value ({result})");
                Notification.ShowError($"Failed to set {name} configuration: invalid value ({invalidHint}).");
            }
            else
            {
                Logger.Error($"{name}: failed to set the configuration ({result})");
                Notification.ShowError($"Failed to set {name} configuration ({result}), see log. PinsBox: {pinsBoxHint}; serial PowerBox: communication error.");
            }
        }

        private bool SetBuckPortConfig(PowerBoxBuckPort port, uint mask)
        {
            // Take the values right away, before waiting for the lock
            bool enabled = port.Enabled;
            double voltage = port.SetVoltage;

            PowerBoxSDK.PB_ERROR_TYPE result;
            Exception error = null;
            try
            {
                result = SendBuckPortConfig(enabled, voltage, mask);
            }
            catch (Exception ex)
            {
                result = PowerBoxSDK.PB_ERROR_TYPE.PB_ERROR_COMMUNICATION;
                error = ex;
            }

            if (result != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
            {
                ShowBuckPortError(result, error);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Sends the buck port configuration from the given values. Shows no notification,
        /// so it can be called while holding _sdkLock.
        /// </summary>
        private PowerBoxSDK.PB_ERROR_TYPE SendBuckPortConfig(bool enabled, double voltage, uint mask)
        {
            PowerBoxSDK.PB_BUCK_PORT_CONFIG config = new PowerBoxSDK.PB_BUCK_PORT_CONFIG();
            config.mask = mask;
            config.enabled = enabled ? 1 : 0;
            config.targetVoltage = Convert.ToSingle(voltage);

            lock (_sdkLock)
            {
                return PowerBoxSDK.PBSetBuckPortConfig(deviceId, ref config);
            }
        }

        private void ShowBuckPortError(PowerBoxSDK.PB_ERROR_TYPE result, Exception error)
        {
            if (error != null)
            {
                Logger.Error($"Error setting buck port config: {error}");
                Notification.ShowError($"Error setting buck port config: {error.Message}");
            }
            else if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_ERROR_INVALID_PARAMETER)
            {
                Logger.Error($"Buck port: invalid target voltage ({result})");
                Notification.ShowError("Failed to set buck port configuration: invalid target voltage.");
            }
            else
            {
                // PinsBox reports range errors as PB_ERROR_COMMUNICATION too; on the serial
                // PowerBox it means the command could not be sent
                Logger.Error($"Buck port: failed to set configuration ({result})");
                Notification.ShowError($"Buck port configuration rejected or failed ({result}), see log. PinsBox: supply limit or range, pending over-current shutdown or hardware error; serial PowerBox: communication error.");
            }
        }

        private bool SetPWMPortConfig(PowerBoxPWMPort port, uint mask)
        {
            try
            {
                PowerBoxSDK.PB_PWM_PORT_CONFIG config = new PowerBoxSDK.PB_PWM_PORT_CONFIG();
                config.mask = mask;
                config.enabled = port.Enabled ? 1 : 0;
                config.power = port.SetPower;

                PowerBoxSDK.PB_ERROR_TYPE result;
                lock (_sdkLock)
                {
                    result = PowerBoxSDK.PBSetPWMPortConfig(deviceId, ref config);
                }
                if (result == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                {
                    return true;
                }
                ShowPortError("PWM port", result, null);
                return false;
            }
            catch (Exception ex)
            {
                ShowPortError("PWM port", PowerBoxSDK.PB_ERROR_TYPE.PB_ERROR_COMMUNICATION, ex);
                return false;
            }
        }

        private void RefreshPowerPortConfig(int index)
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                PowerBoxSDK.PB_POWER_PORT_CONFIG config = new PowerBoxSDK.PB_POWER_PORT_CONFIG { index = (uint)index };
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetPowerPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _powerPorts.UpdateFromConfig(config);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshPowerPortStatus()
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetPowerPortStatus(deviceId, out var status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _powerPorts.UpdateFromStatus(status);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshUSBPortStatus()
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetUSBPortStatus(deviceId, out var status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _usbPorts.UpdateFromStatus(status);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshDewPortStatus()
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetDewPortStatus(deviceId, out var status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _dewPorts.UpdateFromStatus(status);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshBuckPortStatus()
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetBuckPortStatus(deviceId, out var status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _buckPorts.UpdateFromStatus(status);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshPWMPortStatus()
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetPWMPortStatus(deviceId, out var status) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _pwmPorts.UpdateFromStatus(status);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshUSBPortConfig(int index)
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                PowerBoxSDK.PB_USB_PORT_CONFIG config = new PowerBoxSDK.PB_USB_PORT_CONFIG { index = (uint)index };
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetUSBPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _usbPorts.UpdateFromConfig(config);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshDewPortConfig(int index)
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                PowerBoxSDK.PB_DEW_PORT_CONFIG config = new PowerBoxSDK.PB_DEW_PORT_CONFIG { index = (uint)index };
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetDewPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _dewPorts.UpdateFromConfig(config);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshBuckPortConfig()
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                PowerBoxSDK.PB_BUCK_PORT_CONFIG config = new PowerBoxSDK.PB_BUCK_PORT_CONFIG();
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetBuckPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _buckPorts.UpdateFromConfig(config);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshPWMPortConfig()
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                PowerBoxSDK.PB_PWM_PORT_CONFIG config = new PowerBoxSDK.PB_PWM_PORT_CONFIG();
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetPWMPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _pwmPorts.UpdateFromConfig(config);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void RefreshWiFiConfig()
        {
            bool wasHardwareUpdate = _isHardwareUpdate.Value;
            _isHardwareUpdate.Value = true;
            try
            {
                PowerBoxSDK.PB_WIFI_CONFIG config = new PowerBoxSDK.PB_WIFI_CONFIG();
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBGetWiFiConfig(deviceId, out config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        _wifi.UpdateFromConfig(config);
                    }
                }
            }
            catch { }
            finally { _isHardwareUpdate.Value = wasHardwareUpdate; }
        }

        private void UpdateFromDeviceStatus(PowerBoxSDK.PB_DEVICE_STATUS status)
        {
            _coreTemp = status.coreTemp == -127.0 ? double.NaN : Math.Round(status.coreTemp, 1);
            _temperature = status.temperature == -127.0 ? double.NaN : Math.Round(status.temperature, 1);
            _humidity = status.humidity == -127.0 ? double.NaN : Math.Round(status.humidity, 1);
            _dewPoint = status.dewPoint == -127.0 ? double.NaN : Math.Round(status.dewPoint, 1);
            _extSensor = status.extSensor != 0;
            _hasWifi = status.hasWifi != 0;

            // Uptime formatting
            int days = status.upTime / 86400;
            status.upTime %= 86400;

            int hours = status.upTime / 3600;
            status.upTime %= 3600;

            int minutes = status.upTime / 60;
            int seconds = status.upTime % 60;

            _upTimeFormatted = $"{days} days, {hours} hours, {minutes} min, {seconds} sec";

            OnPropertyChanged(nameof(UpTimeFormatted));
            OnPropertyChanged(nameof(CoreTemp));
            OnPropertyChanged(nameof(Temperature));
            OnPropertyChanged(nameof(Humidity));
            OnPropertyChanged(nameof(DewPoint));
            OnPropertyChanged(nameof(ExtSensor));
            OnPropertyChanged(nameof(HasWifi));
        }

        private void UpdateFromDeviceConfig(PowerBoxSDK.PB_DEVICE_CONFIG config)
        {
            _updateRate = config.updateRate;
            _envUpdateRate = config.envUpdateRate;
            _temperatureOffset = Math.Round(config.temperatureOffset, 1);
            _humidityOffset = Math.Round(config.humidityOffset, 1);

            OnPropertyChanged(nameof(UpdateRate));
            OnPropertyChanged(nameof(EnvUpdateRate));
            OnPropertyChanged(nameof(TemperatureOffset));
            OnPropertyChanged(nameof(HumidityOffset));
        }

        private bool _isEnabledUI = true;

        public bool IsEnabledUI
        {
            get => _isEnabledUI;
            set
            {
                if (_isEnabledUI != value)
                {
                    _isEnabledUI = value;
                    RaisePropertyChanged();
                    ((AsyncRelayCommand)RebootCommand).NotifyCanExecuteChanged();
                    ((AsyncRelayCommand)ResetCommand).NotifyCanExecuteChanged();
                }
            }
        }

        private async Task Reboot()
        {
            // Disable UI
            IsEnabledUI = false;

            try
            {
                // Trigger reboot
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBRestart(deviceId) != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        Notification.ShowError("Failed to reboot PowerBox device.");
                        Logger.Error("Failed to reboot device.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Notification.ShowError($"Error during reboot: {ex.Message}");
                Logger.Error($"Error during reboot: {ex}");
            }
            finally
            {
                // Enable UI
                IsEnabledUI = true;

                for (int i = 0; i < _actualPowerPortCount; i++)
                {
                    RefreshPowerPortConfig(i);
                }
                for (int i = 0; i < _actualUSBPortCount; i++)
                {
                    RefreshUSBPortConfig(i);
                }
                for (int i = 0; i < _actualDewPortCount; i++)
                {
                    RefreshDewPortConfig(i);
                }
                if (_actualBuckPortCount > 0) RefreshBuckPortConfig();
                if (_actualPWMPortCount > 0) RefreshPWMPortConfig();
                RefreshWiFiConfig();
                RaiseAllPropertiesChanged();
            }
        }

        private async Task Reset()
        {
            // Disable UI
            IsEnabledUI = false;

            try
            {
                // Trigger reboot
                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBFactoryReset(deviceId) != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        Notification.ShowError("Failed to reset PowerBox device.");
                        Logger.Error("Failed to reset device.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Notification.ShowError($"Error during reset: {ex.Message}");
                Logger.Error($"Error during reset: {ex}");
            }
            finally
            {
                // Enable UI
                IsEnabledUI = true;

                for (int i = 0; i < _actualPowerPortCount; i++)
                {
                    RefreshPowerPortConfig(i);
                }
                for (int i = 0; i < _actualUSBPortCount; i++)
                {
                    RefreshUSBPortConfig(i);
                }
                for (int i = 0; i < _actualDewPortCount; i++)
                {
                    RefreshDewPortConfig(i);
                }
                if (_actualBuckPortCount > 0) RefreshBuckPortConfig();
                if (_actualPWMPortCount > 0) RefreshPWMPortConfig();
                RefreshWiFiConfig();
                RaiseAllPropertiesChanged();
            }
        }

        // Temperature and humidity the PowerBox accepts as external values (NaN fails)
        private static bool IsPlausibleWeather(float temperature, float humidity)
        {
            return temperature > -50 && temperature < 80 && humidity > 0 && humidity <= 100;
        }

        // Starts the tone and returns at once. A failure is a warning: a missing
        // beep is no fault of the equipment (and must not trigger an alarm itself)
        public bool Beep(int volume, int durationMs)
        {
            try
            {
                PowerBoxSDK.PB_ERROR_TYPE result;
                lock (_sdkLock)
                {
                    result = PowerBoxSDK.PBBeep(deviceId, volume, durationMs);
                }
                if (result != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                {
                    Logger.Warning($"PowerBox beep failed: {result}");
                }
                return result == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS;
            }
            catch (Exception ex)
            {
                Logger.Warning($"PowerBox beep failed: {ex.Message}");
                return false;
            }
        }

        private async Task WiFiSurvey()
        {
            // Disable button
            IsWiFiSurveyRunning = true;

            // Clear current set of SSIDs
            WiFiNetworks.Clear();

            try
            {
                // Run the blocking SDK call on a background thread
                await Task.Run(() =>
                {
                    var result = new PowerBoxSDK.PB_WIFI_SCAN_RESULT();
                    bool scanOk;
                    lock (_sdkLock)
                    {
                        scanOk = PowerBoxSDK.PBScanWiFi(deviceId, out result) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS;
                    }
                    if (!scanOk)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Notification.ShowError("Failed to scan wifi networks.");
                            Logger.Error("Failed to scan wifi networks.");
                        });
                        return;
                    }

                    foreach (var item in result.networks.OrderByDescending(wifi => wifi.rssi))
                    {
                        // Skip empty or whitespace SSIDs
                        if (!string.IsNullOrWhiteSpace(item.ssid))
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                WiFiNetworks.Add(new WiFi(item.ssid, item.rssi, 0));
                            });
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Notification.ShowError($"Error during survey: {ex.Message}");
                Logger.Error($"Error during survey: {ex}");
            }
            finally
            {
                // Enable button
                IsWiFiSurveyRunning = false;
            }
        }

        private string _wifiSSID;

        public string WiFiSSID
        {
            get => _wifiSSID;
            set
            {
                if (_wifiSSID != value)
                {
                    _wifiSSID = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string _wifiPASS;

        // Getter is intentionally private: ninaAPI's switch/get-settings endpoint reflects every
        // *readable* public property into its HTTP response, which would otherwise leak the WiFi
        // password. The setter stays public so the API's set-setting endpoint (and the WiFi UI)
        // can still configure it; only in-class code (WiFiConnect) can read the value back.
        public string WiFiPASS
        {
            private get => _wifiPASS;
            set
            {
                if (_wifiPASS != value)
                {
                    _wifiPASS = value;
                    RaisePropertyChanged();
                }
            }
        }

        private async Task WiFiConnect(PowerBoxSDK.PB_WIFI_MODE mode)
        {
            // Use WiFiSSID for both client and AP modes (can be set via API or UI)
            string ssid = WiFiSSID;
            if (ssid == string.Empty)
            {
                Notification.ShowError(string.Format("SSID empty", RuntimeInformation.OSDescription));
                return;
            }
            if (WiFiPASS == string.Empty)
            {
                Notification.ShowError(string.Format("Password empty", RuntimeInformation.OSDescription));
                return;
            }

            // Disable button
            IsWiFiConnecting = true;

            try
            {
                // Run the blocking SDK call on a background thread
                await Task.Run(() =>
                {
                    PowerBoxSDK.PB_WIFI_CONFIG config = new PowerBoxSDK.PB_WIFI_CONFIG
                    {
                        mask = PowerBoxSDK.MASK_WIFI_SSID | PowerBoxSDK.MASK_WIFI_PASSWORD | PowerBoxSDK.MASK_WIFI_MODE,
                        mode = mode,
                        ssid = ssid,
                        pass = WiFiPASS
                    };
                    bool setOk;
                    lock (_sdkLock)
                    {
                        setOk = PowerBoxSDK.PBSetWiFiConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS;
                    }
                    if (!setOk)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Notification.ShowError("Failed to connect to wifi network.");
                            Logger.Error("Failed to connect to wifi network.");
                        });
                        return;
                    }

                    // Update WiFi config
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        RefreshWiFiConfig();
                    });
                });
            }
            catch (Exception ex)
            {
                Notification.ShowError($"Error during WiFi connect: {ex.Message}");
                Logger.Error($"Error during WiFi connect: {ex}");
            }
            finally
            {
                // Enable button
                IsWiFiConnecting = false;
            }
        }

        public ICommand RebootCommand { get; private set; }
        public ICommand ResetCommand { get; private set; }
        public ICommand WiFiSurveyCommand { get; private set; }
        public ICommand WiFiClientCommand { get; private set; }
        public ICommand WiFiHotspotCommand { get; private set; }

        #region Unsupported

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

        #endregion Unsupported
    }
}