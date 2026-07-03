using CommunityToolkit.Mvvm.Input;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.PINS.SDK;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NINA.PINS.Drivers {

    public class LensControlDriver : BaseINPC, IFocuser, IDisposable {

        public LensControlDriver(int uniqueId, string serial) {
            Id = serial;
            deviceId = uniqueId;
            Name = $"PINS.LensControl.{deviceId} ({serial})";
            DriverInfo = $"Serial {serial}";

            RebootCommand = new AsyncRelayCommand(Reboot, () => IsEnabledUI);
        }

        private readonly int deviceId;
        private readonly object _sdkLock = new object();
        private CancellationTokenSource pollingCts;
        private Task pollingTask;

        public string Name { get; }

        public string DisplayName => Name;

        public string Category => "PI'N'Stars device";

        public string Description => "PI'N'Stars LensControl";

        public string DriverInfo { get; }

        public string DriverVersion { get; private set; }

        public bool Connected { get; private set; }

        public IList<string> SupportedActions => new List<string>();

        public bool HasSetupDialog => false;

        public string Id { get; }

        private string _uniqueId = string.Empty;
        public string UniqueId => _uniqueId;

        private string _firmware = string.Empty;
        public string Firmware => _firmware;

        public static IList<int> ScanDeviceIds() {
            var list = new List<int>();
            int number = 0;
            int[] ids = new int[LensControlSDK.LC_MAX_NUM];
            try {
                if (LensControlSDK.LCDeviceScan(out number, ids) == LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                    for (int i = 0; i < number; ++i) {
                        list.Add(ids[i]);
                    }
                }
            } catch (Exception ex) {
                Notification.ShowError($"{ex.Message}");
            }
            return list;
        }

        public async Task<bool> Connect(CancellationToken token) {
            lock (_sdkLock) {
                if (LensControlSDK.LCDeviceOpen(deviceId) != LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                    Connected = false;
                    return Connected;
                }
            }

            // Try get SDK version
            try {
                var ver = new StringBuilder(LensControlSDK.LC_VERSION_LEN);
                lock (_sdkLock) {
                    if (LensControlSDK.LCGetSDKVersion(ver, ver.Capacity) == LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                        DriverVersion = ver.ToString();
                    } else {
                        DriverVersion = "unknown";
                    }
                }
            } catch (Exception ex) {
                Notification.ShowError($"{ex.Message}");
                DriverVersion = "error";
            }

            // Try get Device version
            try {
                LensControlSDK.LC_VERSION version = new LensControlSDK.LC_VERSION();
                lock (_sdkLock) {
                    if (LensControlSDK.LCDeviceGetVersion(deviceId, out version) == LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                        _uniqueId = version.uuid;

                        uint major = (version.firmware >> 10) & 0x3F;
                        uint minor = (version.firmware >> 5) & 0x1F;
                        uint patch = version.firmware & 0x1F;
                        _firmware = $"{major}.{minor}.{patch}";

                        OnPropertyChanged(nameof(UniqueId));
                    }
                }
            } catch (Exception ex) {
                Notification.ShowError($"{ex.Message}");
                DriverVersion = "error";
            }

            // Fetch initial status
            try {
                LensControlSDK.LC_DEVICE_STATUS status;
                lock (_sdkLock) {
                    if (LensControlSDK.LCDeviceGetStatus(deviceId, out status) == LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                        UpdateFromStatus(status);
                    }
                }
            } catch (Exception ex) {
                Notification.ShowError($"{ex.Message}");
            }

            Connected = true;

            pollingCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            pollingTask = Task.Run(async () => {
                while (!pollingCts.Token.IsCancellationRequested) {
                    try {
                        LensControlSDK.LC_DEVICE_STATUS status;
                        lock (_sdkLock) {
                            if (LensControlSDK.LCDeviceGetStatus(deviceId, out status) == LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                                UpdateFromStatus(status);
                            }
                        }
                    } catch (Exception ex) {
                        Notification.ShowError($"{ex.Message}");
                    }

                    try {
                        await Task.Delay(1000, pollingCts.Token).ConfigureAwait(false);
                    } catch (TaskCanceledException) {
                        break;
                    }
                }
            }, pollingCts.Token);

            if (Connected) {
                PINS.ConnectedLensControl = this;
            }

            return Connected;
        }

        public void Disconnect() {
            try {
                pollingCts?.Cancel();
                try {
                    // Wait for the polling task to fully stop before closing the device,
                    // otherwise in-flight status calls race the close and operate on a
                    // closed (and possibly reused) device id.
                    pollingTask?.Wait();
                } catch { }

                lock (_sdkLock) {
                    LensControlSDK.LCDeviceClose(deviceId);
                }
            } catch { } finally {
                if (PINS.ConnectedLensControl == this) {
                    PINS.ConnectedLensControl = null;
                }
                Connected = false;
            }
        }

        public void Dispose() {
            Disconnect();
            pollingCts?.Dispose();
        }

        private void UpdateFromStatus(LensControlSDK.LC_DEVICE_STATUS status) {
            _position = status.position;
            _maxStep = status.maxPosition;
            _aperture = status.apterture;
            _minAperture = status.minAperture;
            _maxAperture = status.maxAperture;
            _lensName = status.name ?? string.Empty;
            _focalLength = status.focalLength;

            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(MaxStep));
            OnPropertyChanged(nameof(MaxIncrement));
            OnPropertyChanged(nameof(Aperture));
            OnPropertyChanged(nameof(MinAperture));
            OnPropertyChanged(nameof(MaxAperture));
            OnPropertyChanged(nameof(LensName));
            OnPropertyChanged(nameof(FocalLength));
        }

        #region IFocuser

        private int _position;
        private int _maxStep;
        private bool _isMoving;
        private int _aperture;
        private int _minAperture;
        private int _maxAperture;
        private string _lensName = string.Empty;
        private int _focalLength;

        public int Position => _position;

        public int MaxStep {
            get => _maxStep;
            set { }
        }

        public bool IsMoving => _isMoving;

        public bool CanReverse => false;

        public bool Reverse {
            get => false;
            set { }
        }

        public int MaxIncrement => _maxStep;

        public bool CanSetMaxStep => false;

        public double StepSize => 1.0;

        public bool TempCompAvailable => false;

        public bool TempComp {
            get => false;
            set { }
        }

        public double Temperature => double.NaN;

        public int Aperture => _aperture;
        public int MinAperture => _minAperture;
        public int MaxAperture => _maxAperture;
        public string LensName => _lensName;
        public int FocalLength => _focalLength;

        public bool Calibrate() {
            lock (_sdkLock) {
                if (LensControlSDK.LCDeviceCalibrate(deviceId) != LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                    Notification.ShowError("Failed to calibrate LensControl.");
                    return false;
                }
            }
            return true;
        }

        public bool SetAperture(int aperture) {
            var config = new LensControlSDK.LC_DEVICE_CONFIG {
                mask = LensControlSDK.LC_CONFIG_MASK.MASK_LC_APERTURE,
                aperture = aperture
            };
            lock (_sdkLock) {
                if (LensControlSDK.LCDeviceSetConfig(deviceId, ref config) != LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                    Notification.ShowError("Failed to set aperture.");
                    return false;
                }
            }
            _aperture = aperture;
            OnPropertyChanged(nameof(Aperture));
            return true;
        }

        public async Task Move(int position, CancellationToken ct, int waitInMs = 1000) {
            _isMoving = true;
            OnPropertyChanged(nameof(IsMoving));
            try {
                var config = new LensControlSDK.LC_DEVICE_CONFIG {
                    mask = LensControlSDK.LC_CONFIG_MASK.MASK_LC_POSITION,
                    position = position
                };
                lock (_sdkLock) {
                    if (LensControlSDK.LCDeviceSetConfig(deviceId, ref config) != LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                        Notification.ShowError("Failed to move LensControl.");
                        return;
                    }
                }

                while (!ct.IsCancellationRequested) {
                    LensControlSDK.LC_DEVICE_STATUS status;
                    lock (_sdkLock) {
                        if (LensControlSDK.LCDeviceGetStatus(deviceId, out status) == LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                            UpdateFromStatus(status);
                            if (status.position == position) break;
                        }
                    }
                    await Task.Delay(waitInMs, ct).ConfigureAwait(false);
                }
            } catch (TaskCanceledException) {
            } catch (Exception ex) {
                Notification.ShowError($"Move error: {ex.Message}");
                Logger.Error($"Move error: {ex}");
            } finally {
                _isMoving = false;
                OnPropertyChanged(nameof(IsMoving));
            }
        }

        public void Halt() {
            var config = new LensControlSDK.LC_DEVICE_CONFIG {
                mask = LensControlSDK.LC_CONFIG_MASK.MASK_LC_POSITION,
                position = _position
            };
            lock (_sdkLock) {
                LensControlSDK.LCDeviceSetConfig(deviceId, ref config);
            }
            _isMoving = false;
            OnPropertyChanged(nameof(IsMoving));
        }

        #endregion IFocuser

        private bool _isEnabledUI = true;

        public bool IsEnabledUI {
            get => _isEnabledUI;
            set {
                if (_isEnabledUI != value) {
                    _isEnabledUI = value;
                    RaisePropertyChanged();
                    ((AsyncRelayCommand)RebootCommand).NotifyCanExecuteChanged();
                }
            }
        }

        private async Task Reboot() {
            IsEnabledUI = false;

            try {
                lock (_sdkLock) {
                    if (LensControlSDK.LCDeviceRestart(deviceId) != LensControlSDK.LC_ERROR_TYPE.LC_SUCCESS) {
                        Notification.ShowError("Failed to reboot LensControl device.");
                        Logger.Error("Failed to reboot device.");
                        return;
                    }
                }

                await Task.Delay(1000);

                Disconnect();
                await Connect(CancellationToken.None);
            } catch (Exception ex) {
                Notification.ShowError($"Error during reboot: {ex.Message}");
                Logger.Error($"Error during reboot: {ex}");
            } finally {
                IsEnabledUI = true;
                RaiseAllPropertiesChanged();
            }
        }

        public ICommand RebootCommand { get; private set; }

        #region Unsupported

        public void SendCommandBlind(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public bool SendCommandBool(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public string SendCommandString(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public void SetupDialog() {
            throw new NotImplementedException();
        }

        public string Action(string actionName, string actionParameters) {
            throw new NotImplementedException();
        }

        #endregion Unsupported
    }
}
