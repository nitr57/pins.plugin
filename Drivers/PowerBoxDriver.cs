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
        private PowerBoxPowerSupply _powerSupply;
        private PowerBoxPorts _powerPorts;
        private PowerBoxUSBPorts _usbPorts;
        private PowerBoxDewPorts _dewPorts;
        private PowerBoxBuckPorts _buckPorts;
        private PowerBoxPWMPorts _pwmPorts;
        private WiFi _wifi;

        // Track actual port counts reported by hardware
        private int _actualPowerPortCount = 0;
        public int ActualPowerPortCount => _actualPowerPortCount;

        private int _actualUSBPortCount = 0;
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

        public bool Connected { get; private set; }

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
            var list = new List<int>();
            int number = 0;
            int[] ids = new int[PowerBoxSDK.PB_MAX_NUM];
            try
            {
                if (PowerBoxSDK.PBScan(out number, ids) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                {
                    for (int i = 0; i < number; ++i)
                    {
                        list.Add(ids[i]);
                    }
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
            lock (_sdkLock)
            {
                if (PowerBoxSDK.PBOpen(deviceId) != PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                {
                    Connected = false;
                    return Connected;
                }
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
                        PowerBoxSDK.PBGetPowerPortStatus(deviceId, out var status);
                        _actualPowerPortCount = status.numPorts;
                        PowerBoxSDK.PB_POWER_PORT_CONFIG config = new PowerBoxSDK.PB_POWER_PORT_CONFIG();
                        for (uint i = 0; i < status.numPorts; ++i)
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
                        PowerBoxSDK.PBGetUSBPortStatus(deviceId, out var status);
                        _actualUSBPortCount = status.numPorts;
                        PowerBoxSDK.PB_USB_PORT_CONFIG config = new PowerBoxSDK.PB_USB_PORT_CONFIG();
                        for (uint i = 0; i < status.numPorts; ++i)
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
            pollingTask = Task.Run(async () =>
            {
                while (!pollingCts.Token.IsCancellationRequested)
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
                                                if (!double.IsNaN(weatherData.Temperature))
                                                {
                                                    _temperature = weatherData.Temperature;
                                                    _humidity = weatherData.Humidity;
                                                    _dewPoint = weatherData.DewPoint;
                                                    OnPropertyChanged(nameof(Temperature));
                                                    OnPropertyChanged(nameof(Humidity));
                                                    OnPropertyChanged(nameof(DewPoint));
                                                    Logger.Trace("Fetched environment data from WeatherData equipment.");
                                                    // If we fetched data successfully, we need to set it to the powerbox so that dew control can work
                                                    PowerBoxSDK.PB_DEVICE_CONFIG config = new PowerBoxSDK.PB_DEVICE_CONFIG();
                                                    config.mask = PowerBoxSDK.MASK_PB_EXT_TEMPERATURE | PowerBoxSDK.MASK_PB_EXT_HUMIDITY;
                                                    config.temperature = Convert.ToSingle(Temperature);
                                                    config.humidity = Convert.ToSingle(Humidity);
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
                                Logger.Error($"Error polling device status: {ex.Message}");
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
                                Logger.Error($"Error polling status: {ex.Message}");
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
                                Logger.Error($"Error polling status: {ex.Message}");
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
                                Logger.Error($"Error polling status: {ex.Message}");
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
                                Logger.Error($"Error polling status: {ex.Message}");
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
                                Logger.Error($"Error polling status: {ex.Message}");
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
                                Logger.Error($"Error polling status: {ex.Message}");
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
                                Logger.Error($"Error polling status: {ex.Message}");
                            }

                            // Update 5V current
                            _supply5A = 0.0;
                            foreach (var port in USBPorts.Ports.Take(_actualUSBPortCount))
                            {
                                _supply5A += port.Current;
                            }
                            _supply5A = Math.Round(_supply5A, 2);
                            _supply5W = Math.Round(_supply5A * _powerSupply.Supply5V, 2);

                            OnPropertyChanged(nameof(Supply5A));
                            OnPropertyChanged(nameof(Supply5W));
                        }
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
                    foreach (var sw in Switches)
                    {
                        ((PowerBoxSwitch)sw).Poll();
                    }

                    try
                    {
                        await Task.Delay(1000, pollingCts.Token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, pollingCts.Token);

            // We need to wait for the status updates to arrive, so we simply loop a bit
            // Add a timeout to prevent infinite waiting (max 5 seconds)
            var waitTimeout = DateTime.Now.AddSeconds(5);
            while (((_actualDewPortCount > 0 && DewPorts.Ports[0].Resolution == 0)
                || (_actualBuckPortCount > 0 && BuckPorts.Ports[0].MaxVoltage < 1.0)
                || (_actualPWMPortCount > 0 && PWMPorts.Ports[0].Resolution == 0))
                && !token.IsCancellationRequested
                && DateTime.Now < waitTimeout)
            {
                Thread.Sleep(100);
            }

            // Check if we timed out before all values were initialized
            if ((_actualDewPortCount > 0 && DewPorts.Ports[0].Resolution == 0)
                || (_actualBuckPortCount > 0 && BuckPorts.Ports[0].MaxVoltage < 1.0)
                || (_actualPWMPortCount > 0 && PWMPorts.Ports[0].Resolution == 0))
            {
                var errorMsg = $"PowerBox connection failed: Timeout waiting for device status initialization. Dew Resolution: {DewPorts.Ports[0].Resolution}, Buck MaxVoltage: {BuckPorts.Ports[0].MaxVoltage}, PWM Resolution: {PWMPorts.Ports[0].Resolution}";
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
                    pollingTask?.Wait();
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
            Switches.Clear();

            Switches.Add(new PowerBoxSwitch(() => "12V rail", () => "Supply [V]", () => _powerSupply.Supply12V, 0));
            Switches.Add(new PowerBoxSwitch(() => "Temperature", () => "Environment [°C]", () => Temperature, 1));
            Switches.Add(new PowerBoxSwitch(() => "5V rail", () => "Supply [V]", () => _powerSupply.Supply5V, 2));
            Switches.Add(new PowerBoxSwitch(() => "Humidity", () => "Environment [%]", () => Humidity, 3));
            Switches.Add(new PowerBoxSwitch(() => "Current", () => "Supply [A]", () => _powerSupply.Supply12A, 4));
            Switches.Add(new PowerBoxSwitch(() => "Dew Point", () => "Environment [°C]", () => DewPoint, 5));
            Switches.Add(new PowerBoxSwitch(() => "Power", () => "Supply [W]", () => _powerSupply.Supply12W, 6));
            Switches.Add(new PowerBoxSwitch(() => "AverageAmps", () => "Consumption average amps", () => _powerSupply.AverageAmps, 7));
            Switches.Add(new PowerBoxSwitch(() => "WattsPerHour", () => "Consumption watts per hour", () => _powerSupply.WattsPerHour, 8));
            Switches.Add(new PowerBoxSwitch(() => "AmpsPerHour", () => "Consumption amps per hour", () => _powerSupply.AmpsPerHour, 9));

            short switchId = 10;

            // Power hub
            Logger.Trace("Configuring power hub switches...");
            foreach (var port in PowerPorts.Ports.Take(_actualPowerPortCount))
            {
                if (port.ReadOnly)
                {
                    Switches.Add(new PowerBoxSwitch(() => port.Name, () => $"12V #{port.Index + 1}: always on (Current [A])", () => port.Current, switchId));
                }
                else
                {
                    Switches.Add(new PowerBoxWritableSwitch(
                        () => port.Name,
                        () => $"12V #{port.Index + 1}: {(port.Enabled ? port.Current : 0)}A",
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
                    Switches.Add(new PowerBoxSwitch(
                        () => port.Name,
                        () => $"USB #{port.Index + 1}: {port.Current}A",
                        () => port.Current,
                        switchId));
                }
                else
                {
                Switches.Add(new PowerBoxWritableSwitch(
                    () => port.Name,
                    () => $"USB #{port.Index + 1}: {(port.Enabled ? port.Current : 0)}A",
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
                Switches.Add(new PowerBoxWritableSwitch(
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
                Switches.Add(new PowerBoxWritableSwitch(
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
                Switches.Add(new PowerBoxWritableSwitch(
                    () => port.Name,
                    () => $"Dew #{port.Index + 1}: {(port.Enabled ? Math.Round(port.Power * 100.0 / port.Resolution, 1) : 0)}% ({(port.Enabled ? Math.Round(port.Current, 2) : 0)}A)",
                    () => port.Enabled ? port.Power : 0,
                    (value) =>
                    {
                        // On auto mode, do nothing
                        if (port.AutoMode) return;

                        // Turn on, if duty cycle > 0 and state is off
                        if (value > 0 && port.Enabled == false)
                        {
                            port.Enabled = true;
                        }

                        // Turn off, if duty cycle is 0 and state is on
                        if (value == 0 && port.Enabled == true)
                        {
                            port.Enabled = false;
                        }

                        // Set power
                        port.SetPower = Convert.ToInt32(value);
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
            Switches.Add(new PowerBoxWritableSwitch(
                            () => buckPort.Name,
                            () => $"Buck converter: {(buckPort.Enabled ? Math.Round(buckPort.Power, 2) : 0)}V ({(buckPort.Enabled ? Math.Round(buckPort.Current, 2) : 0)}A)",
                            () =>
                            {
                                return buckPort.Enabled == false ? 0 : Math.Round(buckPort.SetVoltage, 2);
                            },
                            (value) =>
                            {
                                value = value < 1.0 ? 0.0 : value;

                                // Turn on, if voltage >= 1 and current state is off
                                if (value >= 1.0 && buckPort.Enabled == false)
                                {
                                    buckPort.Enabled = true;
                                }

                                // Turn off, if voltage is < vmin and current state is on
                                if (value < buckPort.MinVoltage && buckPort.Enabled == true)
                                {
                                    buckPort.Enabled = false;
                                }

                                // Set voltage
                                buckPort.SetVoltage = Convert.ToSingle(value);
                            },
                            switchId,
                            Math.Max(1.0, buckPort.MinVoltage),
                            buckPort.MaxVoltage,
                            0.1));
            switchId++;
            } // end Buck port

            // PWM port
            if (_actualPWMPortCount > 0)
            {
            var pwmPort = PWMPorts.Ports[0];
            Switches.Add(new PowerBoxWritableSwitch(
                () => pwmPort.Name,
                () => $"PWM port: {(pwmPort.Enabled ? Math.Round(pwmPort.Power * 100.0 / pwmPort.Resolution, 1) : 0)}% ({(pwmPort.Enabled ? Math.Round(pwmPort.Current, 2) : 0)}A)",
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
                    // Turn on, if duty cycle > 0 and current state is off
                    if (value > 0 && pwmPort.Enabled == false)
                    {
                        pwmPort.Enabled = true;
                    }

                    // Turn off, if duty cycle is 0 and current state is on
                    if (value == 0 && pwmPort.Enabled == true)
                    {
                        pwmPort.Enabled = false;
                    }

                    // Set power
                    pwmPort.SetPower = Convert.ToInt32(value);
                },
                switchId,
                0,
                pwmPort.Resolution,
                1));
            switchId++;
            } // end PWM port

            Logger.Trace($"Total switches configured: {Switches.Count}");
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
                    var powerPortConfig = hubConfig["PowerPort"] as JObject;
                    if (powerPortConfig != null)
                    {
                        foreach (var port in PowerPorts.Ports)
                        {
                            var nameToken = powerPortConfig[$"Port{port.Index + 1}"];
                            if (nameToken != null)
                            {
                                port.Name = nameToken.ToString();
                            }
                        }
                    }
                    var usbPortConfig = hubConfig["USBPort"] as JObject;
                    if (usbPortConfig != null)
                    {
                        foreach (var port in USBPorts.Ports)
                        {
                            var nameToken = usbPortConfig[$"Port{port.Index + 1}"];
                            if (nameToken != null)
                            {
                                port.Name = nameToken.ToString();
                            }
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
                        ["Port6"] = "12V #6"
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
                        ["Port6"] = "USB2 #2"
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

                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBSetPowerPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        return true;
                    }
                }
                Notification.ShowError("Failed to set power port configuration.");
                return false;
            }
            catch (Exception ex)
            {
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

                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBSetUSBPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        return true;
                    }
                }
                Notification.ShowError("Failed to set USB port configuration.");
                return false;
            }
            catch (Exception ex)
            {
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

                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBSetDewPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        return true;
                    }
                }
                Notification.ShowError("Failed to set dew port configuration.");
                return false;
            }
            catch (Exception ex)
            {
                Notification.ShowError($"Error setting port config: {ex.Message}");
                return false;
            }
        }

        private bool SetBuckPortConfig(PowerBoxBuckPort port, uint mask)
        {
            try
            {
                PowerBoxSDK.PB_BUCK_PORT_CONFIG config = new PowerBoxSDK.PB_BUCK_PORT_CONFIG();
                config.mask = mask;
                config.enabled = port.Enabled ? 1 : 0;
                config.targetVoltage = Convert.ToSingle(port.SetVoltage);

                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBSetBuckPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        return true;
                    }
                }
                Notification.ShowError("Failed to set buck port configuration.");
                return false;
            }
            catch (Exception ex)
            {
                Notification.ShowError($"Error setting port config: {ex.Message}");
                return false;
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

                lock (_sdkLock)
                {
                    if (PowerBoxSDK.PBSetPWMPortConfig(deviceId, ref config) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS)
                    {
                        return true;
                    }
                }
                Notification.ShowError("Failed to set PWM port configuration.");
                return false;
            }
            catch (Exception ex)
            {
                Notification.ShowError($"Error setting port config: {ex.Message}");
                return false;
            }
        }

        private void RefreshPowerPortConfig(int index)
        {
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
            finally { _isHardwareUpdate.Value = false; }
        }

        private void RefreshUSBPortConfig(int index)
        {
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
            finally { _isHardwareUpdate.Value = false; }
        }

        private void RefreshDewPortConfig(int index)
        {
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
            finally { _isHardwareUpdate.Value = false; }
        }

        private void RefreshBuckPortConfig()
        {
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
            finally { _isHardwareUpdate.Value = false; }
        }

        private void RefreshPWMPortConfig()
        {
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
            finally { _isHardwareUpdate.Value = false; }
        }

        private void RefreshWiFiConfig()
        {
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
            finally { _isHardwareUpdate.Value = false; }
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

        public bool Beep(int volume, int durationMs)
        {
            try
            {
                lock (_sdkLock)
                {
                    return PowerBoxSDK.PBBeep(deviceId, volume, durationMs) == PowerBoxSDK.PB_ERROR_TYPE.PB_SUCCESS;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during beep: {ex}");
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

        public string WiFiPASS
        {
            get => _wifiPASS;
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