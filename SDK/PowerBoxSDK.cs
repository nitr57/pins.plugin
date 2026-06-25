using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NINA.PINS.SDK
{
    internal static class PowerBoxSDK
    {
        private const string DLL = "PowerBoxSDK.dll";

        public const int PB_MAX_NUM = 32;
        public const int PB_MAX_WIFI_NETWORKS = 32;
        public const int PB_NAME_LEN = 32;
        public const int PB_VERSION_LEN = 32;
        public const int PB_UUID_LEN = 37;
        public const int PB_IP_LEN = 16;
        public const int PB_SSID_LEN = 32;
        public const int PB_HOSTNAME_LEN = 32;
        public const int PB_PASSWORD_LEN = 64;

        public const int PB_MAX_POWER_PORTS = 8;
        public const int PB_MAX_USB_PORTS = 8;
        public const int PB_MAX_DEW_PORTS = 2;

        public enum PB_ERROR_TYPE
        {
            PB_SUCCESS = 0,
            PB_ERROR_INVALID_ID,
            PB_ERROR_INVALID_PARAMETER,
            PB_ERROR_INVALID_STATE,
            PB_ERROR_COMMUNICATION,
            PB_ERROR_NULL_POINTER,
            PB_ERROR_TIMEOUT,
            PB_ERROR_NOT_AVAILABLE,
        }

        public enum PB_WIFI_MODE : uint
        {
            PB_WIFI_MODE_AP = 0,
            PB_WIFI_MODE_CLIENT = 1,
            PB_WIFI_MODE_OFF = 2
        }

        public const uint MASK_PB_TEMPERATURE_OFFSET = 0x01;
        public const uint MASK_PB_HUMIDITY_OFFSET = 0x02;
        public const uint MASK_PB_ENV_UPDATE_RATE = 0x04;
        public const uint MASK_PB_UPDATE_RATE = 0x08;
        public const uint MASK_PB_EXT_TEMPERATURE = 0x10;
        public const uint MASK_PB_EXT_HUMIDITY = 0x20;
        public const uint MASK_PB_ALL = 0x3F;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct PB_VERSION
        {
            public uint firmware;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PB_NAME_LEN)]
            public string model;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PB_UUID_LEN)]
            public string uuid;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PB_VERSION_LEN)]
            public string serial;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_DEVICE_CONFIG
        {
            public uint mask;
            public float temperatureOffset;
            public float humidityOffset;
            public int envUpdateRate;
            public int updateRate;
            public float temperature;
            public float humidity;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_DEVICE_STATUS
        {
            public int upTime;
            public float coreTemp;
            public float temperature;
            public float humidity;
            public float dewPoint;
            public int extSensor;
            public int hasWifi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_SUPPLY_STATUS
        {
            public float mainVoltage;
            public float usbVoltage;
            public float current;
            public float averageAmps;
            public float ampereHours;
            public float wattHours;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_POWER_PORT_STATUS
        {
            public int numPorts;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_POWER_PORTS)]
            public float[] current;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_POWER_PORTS)]
            public int[] overcurrent;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_POWER_PORTS)]
            public int[] readOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_USB_PORT_STATUS
        {
            public int numPorts;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_USB_PORTS)]
            public float[] current;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_USB_PORTS)]
            public float[] voltage;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_USB_PORTS)]
            public int[] overcurrent;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_USB_PORTS)]
            public int[] readOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_DEW_PORT_STATUS
        {
            public int numPorts;
            public int pwmResolution;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_DEW_PORTS)]
            public float[] current;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_DEW_PORTS)]
            public int[] overcurrent;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_DEW_PORTS)]
            public float[] probe;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_DEW_PORTS)]
            public int[] pwm;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_DEW_PORTS)]
            public int[] state;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_BUCK_PORT_STATUS
        {
            public int numPorts;
            public float current;
            public float voltage;
            public int overcurrent;
            public float vset;
            public float vmin;
            public float vmax;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_PWM_PORT_STATUS
        {
            public int numPorts;
            public int pwmResolution;
            public float current;
            public int overcurrent;
            public int pwm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_WIFI_STATUS
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PB_IP_LEN)]
            public string IP;

            public int rssi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_WIFI_NETWORK
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PB_SSID_LEN)]
            public string ssid;

            public int rssi;
            public int channel;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_WIFI_SCAN_RESULT
        {
            public int count;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PB_MAX_WIFI_NETWORKS)]
            public PB_WIFI_NETWORK[] networks;
        }

        public const uint MASK_PORT_ENABLE = 0x01;
        public const uint MASK_PORT_BOOT_STATE = 0x02;
        public const uint MASK_PORT_AUTO_DEW_MODE = 0x04;
        public const uint MASK_PORT_AUTO_DEW_THRESHOLD = 0x08;
        public const uint MASK_PORT_POWER = 0x10;
        public const uint MASK_PORT_VOLTAGE = 0x20;
        public const uint MASK_PORT_OVERCURRENT_RESET = 0x40;
        public const uint MASK_PORT_ALL = 0x7F;

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_POWER_PORT_CONFIG
        {
            public uint mask;
            public uint index;
            public int enabled;
            public int bootState;
            public int overcurrentReset;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_USB_PORT_CONFIG
        {
            public uint mask;
            public uint index;
            public int enabled;
            public int bootState;
            public int overcurrentReset;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_DEW_PORT_CONFIG
        {
            public uint mask;
            public uint index;
            public int enabled;
            public int autoMode;
            public float autoThreshold;
            public int power;
            public int overcurrentReset;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_BUCK_PORT_CONFIG
        {
            public uint mask;
            public uint index;
            public float targetVoltage;
            public int enabled;
            public int bootState;
            public int overcurrentReset;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_PWM_PORT_CONFIG
        {
            public uint mask;
            public uint index;
            public int enabled;
            public int power;
            public int overcurrentReset;
        }

        public const uint MASK_WIFI_MODE = 0x01;
        public const uint MASK_WIFI_SSID = 0x02;
        public const uint MASK_WIFI_PASSWORD = 0x04;
        public const uint MASK_WIFI_CHANNEL = 0x08;
        public const uint MASK_WIFI_HOSTNAME = 0x10;
        public const uint MASK_WIFI_ALL = 0x1F;

        [StructLayout(LayoutKind.Sequential)]
        public struct PB_WIFI_CONFIG
        {
            public uint mask;
            public PB_WIFI_MODE mode;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PB_SSID_LEN)]
            public string ssid;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PB_PASSWORD_LEN)]
            public string pass;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PB_HOSTNAME_LEN)]
            public string hostname;

            public int channel;
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBScan(out int number, [Out] int[] ids);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBOpen(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBClose(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern PB_ERROR_TYPE PBGetSerial(int id, StringBuilder serial);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern PB_ERROR_TYPE PBScanWiFi(int id, out PB_WIFI_SCAN_RESULT result);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetConfig(int id, ref PB_DEVICE_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBSetConfig(int id, ref PB_DEVICE_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetPowerPortConfig(int id, ref PB_POWER_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBSetPowerPortConfig(int id, ref PB_POWER_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetUSBPortConfig(int id, ref PB_USB_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBSetUSBPortConfig(int id, ref PB_USB_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetDewPortConfig(int id, ref PB_DEW_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBSetDewPortConfig(int id, ref PB_DEW_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetBuckPortConfig(int id, ref PB_BUCK_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBSetBuckPortConfig(int id, ref PB_BUCK_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetPWMPortConfig(int id, ref PB_PWM_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBSetPWMPortConfig(int id, ref PB_PWM_PORT_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetWiFiConfig(int id, out PB_WIFI_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBSetWiFiConfig(int id, ref PB_WIFI_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetStatus(int id, out PB_DEVICE_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetWiFiStatus(int id, out PB_WIFI_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetSupplyStatus(int id, out PB_SUPPLY_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetPowerPortStatus(int id, out PB_POWER_PORT_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetUSBPortStatus(int id, out PB_USB_PORT_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetDewPortStatus(int id, out PB_DEW_PORT_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetBuckPortStatus(int id, out PB_BUCK_PORT_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetPWMPortStatus(int id, out PB_PWM_PORT_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBGetVersion(int id, out PB_VERSION version);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBBeep(int id, int volume, int duration_ns);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBRestart(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern PB_ERROR_TYPE PBFactoryReset(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern PB_ERROR_TYPE PBGetSDKVersion(StringBuilder version);
    }
}