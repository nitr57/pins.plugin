using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NINA.PINS.SDK {

    internal static class LensControlSDK {
        public const int LC_MAX_NUM = 32;
        public const int LC_NAME_LEN = 64;
        public const int LC_VERSION_LEN = 32;
        public const int LC_UUID_LEN = 37;

        private const string DLL = "LensControlSDK.dll";

        public enum LC_ERROR_TYPE {
            LC_SUCCESS = 0,             /* Success */
            LC_ERROR_INVALID_ID,        /* Device ID is invalid */
            LC_ERROR_INVALID_PARAMETER, /* One or more parameters are invalid */
            LC_ERROR_INVALID_STATE,     /* Device is not in correct state for specific API call */
            LC_ERROR_COMMUNICATION,     /* Data communication error such as device has been removed from USB port */
            LC_ERROR_NULL_POINTER,      /* Caller passes null-pointer parameter which is not expected */
        }

        [Flags]
        public enum LC_CONFIG_MASK : uint {
            MASK_LC_APERTURE = 0x0001,
            MASK_LC_POSITION = 0x0002,
            MASK_LC_ALL = 0x0003
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct LC_VERSION {
            public uint firmware;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = LC_UUID_LEN)]
            public string uuid;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = LC_VERSION_LEN)]
            public string serial;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct LC_DEVICE_CONFIG {
            public LC_CONFIG_MASK mask;
            public int aperture;
            public int position;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LC_DEVICE_STATUS {
            public int connected;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = LC_NAME_LEN)]
            public string name;
            public int focalLength;
            public int maxAperture;
            public int minAperture;
            public int aperture;
            public int maxPosition;
            public int position;
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceScan(out int number, [Out] int[] ids);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceOpen(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceClose(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceGetSerial(int id, StringBuilder serial);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceGetConfig(int id, ref LC_DEVICE_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceSetConfig(int id, ref LC_DEVICE_CONFIG config);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceGetStatus(int id, out LC_DEVICE_STATUS status);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceGetVersion(int id, out LC_VERSION version);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceRestart(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCDeviceCalibrate(int id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern LC_ERROR_TYPE LCGetSDKVersion(StringBuilder version, int size);
    }
}