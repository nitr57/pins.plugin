using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NINA.PINS.SDK {

    internal static class MeteoStationSDK {
        public const int MS_MAX_NUM = 32;
        public const int MS_NAME_LEN = 32;
        public const int MS_VERSION_LEN = 32;
        public const int MS_UUID_LEN = 37;

        private const string DLL = "MeteoStationSDK.dll";

        public enum MS_ERROR_TYPE {
            MS_SUCCESS = 0,             /* Success */
            MS_ERROR_INVALID_ID,        /* Device ID is invalid */
            MS_ERROR_INVALID_PARAMETER, /* One or more parameters are invalid */
            MS_ERROR_INVALID_STATE,     /* Device is not in correct state for specific API call */
            MS_ERROR_COMMUNICATION,     /* Data communication error such as device has been removed from USB port */
            MS_ERROR_NULL_POINTER       /* Caller passes null-pointer parameter which is not expected */
        }

        [Flags]
        public enum MS_CONFIG_MASK : uint {
            MASK_MS_TEMPERATURE_OFFSET = 0x0001,
            MASK_MS_HUMIDITY_OFFSET = 0x0002,
            MASK_MS_UPDATE_RATE = 0x0004,
            MASK_MS_CLOUD_K1 = 0x0008,
            MASK_MS_CLOUD_K2 = 0x0010,
            MASK_MS_CLOUD_K3 = 0x0020,
            MASK_MS_CLOUD_K4 = 0x0040,
            MASK_MS_CLOUD_K5 = 0x0080,
            MASK_MS_CLOUD_K6 = 0x0100,
            MASK_MS_CLOUD_K7 = 0x0200,
            MASK_MS_CLOUD_TO = 0x0400,
            MASK_MS_CLOUD_TC = 0x0800,
            MASK_MS_CLOUD_FP = 0x1000,
            MASK_MS_LUX_SCALING = 0x2000,
            MASK_MS_ALL = 0x3FFF
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct MS_VERSION {
            public uint firmware;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MS_NAME_LEN)]
            public string model;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MS_UUID_LEN)]
            public string uuid;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MS_VERSION_LEN)]
            public string deviceId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct MS_DEVICE_CONFIG {
            public MS_CONFIG_MASK mask;
            public float temperatureOffset;
            public float humidityOffset;
            public int updateRate;
            public int cloudK1;
            public int cloudK2;
            public int cloudK3;
            public int cloudK4;
            public int cloudK5;
            public int cloudK6;
            public int cloudK7;
            public int cloudTemperatureOvercast;
            public int cloudTemperatureClear;
            public int cloudFlagPercent;
            public float luxScaling;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MS_DEVICE_STATUS {
            public int upTime;
            public float temperature;
            public float humidity;
            public float dewPoint;
            public float skyTemperature;
            public int cloudCover;
            public int skyState;
            public float skyBrightness;
            public float skyQuality;
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceScan(out int number, [Out] int[] ids);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceGetSerial(int id, StringBuilder serial);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceOpen(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceClose(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceGetConfig(int id, ref MS_DEVICE_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceSetConfig(int id, ref MS_DEVICE_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceGetStatus(int id, out MS_DEVICE_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceGetVersion(int id, out MS_VERSION version);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceRestart(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSDeviceFactoryReset(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern MS_ERROR_TYPE MSGetSDKVersion(StringBuilder version, int size);
    }
}