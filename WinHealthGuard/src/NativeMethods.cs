using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinHealthGuard
{
    [StructLayout(LayoutKind.Sequential)]
    struct FileTime64
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out FileTime64 idle, out FileTime64 kernel, out FileTime64 user);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int SHLoadIndirectString(string source, StringBuilder output, int capacity, IntPtr reserved);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr handle);

        public static ulong FileTimeToUInt64(FileTime64 value)
        {
            return ((ulong)value.High << 32) | value.Low;
        }

        public static void EnableDarkTitle(IntPtr hwnd)
        {
            try
            {
                int use = 1;
                int hr = DwmSetWindowAttribute(hwnd, 20, ref use, 4);
                if (hr != 0) DwmSetWindowAttribute(hwnd, 19, ref use, 4);
            }
            catch
            {
            }
        }
    }
}
