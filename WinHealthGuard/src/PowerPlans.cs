using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinHealthGuard
{
    static class PowerPlans
    {
        public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string PowerCfg()
        {
            return Path.Combine(Environment.SystemDirectory, "powercfg.exe");
        }

        public static string ActiveGuid()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes"))
                {
                    if (key == null) return "";
                    object value = key.GetValue("ActivePowerScheme");
                    return value == null ? "" : Convert.ToString(value);
                }
            }
            catch
            {
                return "";
            }
        }

        public static bool IsStrong(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            return guid.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase)
                || guid.Equals(Ultimate, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Exists(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\" + guid))
                    return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static string Name(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return "Unknown";
            lock (Names)
            {
                string cached;
                if (Names.TryGetValue(guid, out cached)) return cached;
            }
            string resolved = Resolve(guid);
            lock (Names) { Names[guid] = resolved; }
            return resolved;
        }

        public static bool SetActive(string guid, out string message)
        {
            if (!Exists(guid))
            {
                message = "That power plan is not installed.";
                return false;
            }
            string output;
            int code = Command.Run(PowerCfg(), "/setactive " + guid, out output, 15000);
            if (code == 0)
            {
                message = "Power plan is now " + Name(guid) + ".";
                return true;
            }
            message = "Could not change the power plan. " + TextUtil.OneLine(output);
            return false;
        }

        public static bool TuneCurrentScheme(out string message)
        {
            string exe = PowerCfg();
            string[] args = new string[] {
                "/SETACVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0",
                "/SETDCVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0",
                "/SETACVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 0",
                "/SETDCVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 0",
                "/SETACTIVE SCHEME_CURRENT"
            };
            for (int i = 0; i < args.Length; i++)
            {
                string output;
                int code = Command.Run(exe, args[i], out output, 15000);
                if (code != 0)
                {
                    message = TextUtil.OneLine(output);
                    if (message.Length == 0) message = "powercfg failed.";
                    return false;
                }
            }
            message = "USB selective suspend is off, and PCI Express link-state power saving is off.";
            return true;
        }

        public static string TuneScript()
        {
            return "& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETACVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0\r\n"
                + "if ($LASTEXITCODE -ne 0) { throw 'Could not change USB selective suspend.' }\r\n"
                + "& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETDCVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0\r\n"
                + "& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETACVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 0\r\n"
                + "& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETDCVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 0\r\n"
                + "& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETACTIVE SCHEME_CURRENT\r\n";
        }

        public static string ActivateAndTuneScript()
        {
            return "& \"$env:SystemRoot\\System32\\powercfg.exe\" /setactive " + HighPerformance + "\r\n"
                + "if ($LASTEXITCODE -ne 0) { throw 'Could not activate the High performance plan.' }\r\n"
                + TuneScript();
        }

        static string Resolve(string guid)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\" + guid))
                {
                    if (key == null) return guid;
                    string friendly = key.GetValue("FriendlyName") as string;
                    return Localize(friendly, guid);
                }
            }
            catch
            {
                return guid;
            }
        }

        static string Localize(string friendly, string fallbackGuid)
        {
            if (string.IsNullOrEmpty(friendly)) return fallbackGuid;
            string source = friendly;
            string fallback = friendly;
            if (friendly[0] == '@')
            {
                int first = friendly.IndexOf(',');
                if (first > 0)
                {
                    int second = friendly.IndexOf(',', first + 1);
                    if (second > 0)
                    {
                        source = friendly.Substring(0, second);
                        fallback = friendly.Substring(second + 1);
                    }
                }
            }
            StringBuilder buffer = new StringBuilder(512);
            int hr = NativeMethods.SHLoadIndirectString(source, buffer, buffer.Capacity, IntPtr.Zero);
            if (hr == 0 && buffer.Length > 0) return buffer.ToString();
            return fallback;
        }
    }
}
