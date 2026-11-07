using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinHealthGuard
{
    sealed class CrashMonitor : IDisposable
    {
        readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        readonly List<EventLogWatcher> _watchers = new List<EventLogWatcher>();
        public event Action<CrashEvent> Detected;

        public void Remember(IList<CrashEvent> events)
        {
            if (events == null) return;
            lock (_seen)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    if (!string.IsNullOrEmpty(events[i].Id)) _seen.Add(events[i].Id);
                }
            }
        }

        public List<CrashEvent> ImportRecent()
        {
            List<CrashEvent> all = ReadRecent(TimeSpan.FromDays(7), 300);
            List<CrashEvent> fresh = new List<CrashEvent>();
            for (int i = 0; i < all.Count; i++)
            {
                if (TryAdd(all[i].Id)) fresh.Add(all[i]);
            }
            return fresh;
        }

        public void Start()
        {
            Watch("Application", "*[System[(EventID=1000 or EventID=1002)]]");
            Watch("System", "*[System[(EventID=41 or EventID=1001 or EventID=6008 or EventID=4101 or EventID=18)]]");
        }

        public void Dispose()
        {
            for (int i = 0; i < _watchers.Count; i++)
            {
                try
                {
                    _watchers[i].Enabled = false;
                    _watchers[i].Dispose();
                }
                catch
                {
                }
            }
            _watchers.Clear();
        }

        public static List<CrashEvent> ReadRecent(TimeSpan window, int cap)
        {
            long ms = (long)window.TotalMilliseconds;
            if (ms < 60000) ms = 60000;
            List<CrashEvent> list = new List<CrashEvent>();
            ReadLog(list, "Application", "*[System[(EventID=1000 or EventID=1002) and TimeCreated[timediff(@SystemTime) <= " + ms.ToString(CultureInfo.InvariantCulture) + "]]]", cap);
            ReadLog(list, "System", "*[System[(EventID=41 or EventID=1001 or EventID=6008 or EventID=4101 or EventID=18) and TimeCreated[timediff(@SystemTime) <= " + ms.ToString(CultureInfo.InvariantCulture) + "]]]", cap);
            return list;
        }

        static void ReadLog(List<CrashEvent> list, string logName, string xpath, int cap)
        {
            EventLogQuery query = new EventLogQuery(logName, PathType.LogName, xpath);
            query.ReverseDirection = true;
            query.TolerateQueryErrors = true;
            using (EventLogReader reader = new EventLogReader(query))
            {
                while (list.Count < cap)
                {
                    EventRecord record = reader.ReadEvent();
                    if (record == null) break;
                    using (record)
                    {
                        CrashEvent ev = CrashParser.Parse(record);
                        if (ev != null) list.Add(ev);
                    }
                }
            }
        }

        void Watch(string logName, string xpath)
        {
            try
            {
                EventLogQuery query = new EventLogQuery(logName, PathType.LogName, xpath);
                query.TolerateQueryErrors = true;
                EventLogWatcher watcher = new EventLogWatcher(query);
                watcher.EventRecordWritten += OnWritten;
                watcher.Enabled = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
            }
        }

        void OnWritten(object sender, EventRecordWrittenEventArgs e)
        {
            if (e == null || e.EventException != null || e.EventRecord == null) return;
            try
            {
                CrashEvent ev = CrashParser.Parse(e.EventRecord);
                if (ev == null) return;
                if (!TryAdd(ev.Id)) return;
                Action<CrashEvent> handler = Detected;
                if (handler != null) handler(ev);
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
            }
        }

        bool TryAdd(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_seen) return _seen.Add(id);
        }
    }

    static class CrashParser
    {
        static readonly Regex HexInText = new Regex(@"0x[0-9a-fA-F]{8}", RegexOptions.Compiled);

        public static CrashEvent Parse(EventRecord record)
        {
            if (record == null) return null;
            string provider = record.ProviderName ?? "";
            string logName = record.LogName ?? "";
            int id = record.Id;
            string kind = KindOf(logName, provider, id);
            if (kind.Length == 0) return null;

            CrashEvent ev = new CrashEvent();
            ev.Provider = provider;
            ev.EventId = id;
            ev.Kind = kind;
            ev.Time = record.TimeCreated.HasValue ? record.TimeCreated.Value.ToUniversalTime() : DateTime.UtcNow;
            ev.Detail = Clip(SafeDescription(record), 5000);

            if (kind == "AppCrash" || kind == "AppHang")
            {
                ev.Process = Prop(record, 0);
                ev.FaultModule = Prop(record, 3);
                ev.ExceptionCode = HexCode(Prop(record, 6));
                ev.AppPath = Prop(record, 10);
                if (kind == "AppHang") ev.ExceptionCode = "";
                ev.Overlay = IsOverlay(ev.Process, ev.AppPath);
                ev.Summary = BuildAppSummary(ev);
            }
            else if (kind == "DisplayDriver")
            {
                ev.Summary = "Display driver stopped responding and was reset.";
                ev.Process = "Display driver";
            }
            else if (provider.IndexOf("Kernel-Power", StringComparison.OrdinalIgnoreCase) >= 0 && id == 41)
            {
                int code;
                if (int.TryParse(Prop(record, 0), NumberStyles.Integer, CultureInfo.InvariantCulture, out code) && code != 0)
                {
                    ev.Kind = "BugCheck";
                    ev.BugCheck = "0x" + code.ToString("x8");
                    ev.Summary = "Blue screen " + ev.BugCheck + " " + BugCheckName(ev.BugCheck) + ".";
                }
                else
                {
                    ev.Kind = "PowerLoss";
                    ev.Summary = "The PC lost power or froze and then restarted.";
                }
            }
            else if (id == 6008)
            {
                ev.Summary = "The previous shutdown was unexpected.";
            }
            else if (provider.IndexOf("WHEA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ev.Summary = "Windows logged a fatal hardware error.";
                string found = FirstHex(ev.Detail);
                if (found.Length > 0) ev.BugCheck = found;
            }
            else
            {
                string found = FirstHex(ev.Detail);
                if (found.Length == 0) found = FirstHex(PropsText(record));
                ev.BugCheck = found;
                if (ev.BugCheck.Length > 0)
                    ev.Summary = "Blue screen " + ev.BugCheck + " " + BugCheckName(ev.BugCheck) + ".";
                else
                    ev.Summary = "Windows recorded a system error (" + provider + " " + id.ToString(CultureInfo.InvariantCulture) + ").";
            }

            if (record.RecordId.HasValue)
                ev.Id = logName + ":" + record.RecordId.Value.ToString(CultureInfo.InvariantCulture);
            else
                ev.Id = logName + ":" + ev.Time.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + id.ToString(CultureInfo.InvariantCulture) + ":" + ev.Process;

            return ev;
        }

        public static void Diagnose(CrashEvent ev)
        {
            if (ev == null) return;
            string code = (ev.ExceptionCode ?? "").ToLowerInvariant();
            string bug = (ev.BugCheck ?? "").ToLowerInvariant();
            string module = (ev.FaultModule ?? "").ToLowerInvariant();
            bool gpuModule = module.IndexOf("nvlddmkm", StringComparison.Ordinal) >= 0
                || module.IndexOf("amdkmdag", StringComparison.Ordinal) >= 0
                || module.IndexOf("amdkmdap", StringComparison.Ordinal) >= 0
                || module.IndexOf("igdkmd", StringComparison.Ordinal) >= 0
                || module.IndexOf("dxgkrnl", StringComparison.Ordinal) >= 0
                || module.IndexOf("nvwgf", StringComparison.Ordinal) >= 0
                || module.IndexOf("aticfx", StringComparison.Ordinal) >= 0
                || module.IndexOf("amdxc", StringComparison.Ordinal) >= 0
                || module.IndexOf("d3d11", StringComparison.Ordinal) >= 0
                || module.IndexOf("d3d12", StringComparison.Ordinal) >= 0
                || module.IndexOf("dxgi", StringComparison.Ordinal) >= 0;

            if (ev.Kind == "DisplayDriver" || bug == "0x00000116" || bug == "0x00000117" || bug == "0x000000ea"
                || code == "0x887a0005" || code == "0x887a0006" || code == "0x887a0007" || gpuModule)
            {
                ev.Suggestion = "The graphics driver stopped responding and Windows could not reset it. The repair is a clean install from Windows Update or the graphics maker. A third-party driver updater is not used.";
                ev.RepairId = "driver";
                return;
            }
            if (ev.Overlay)
            {
                bool amd = (ev.AppPath ?? "").IndexOf(@"\AMD\CNext\", StringComparison.OrdinalIgnoreCase) >= 0
                    || (ev.Process ?? "").IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0
                    || (ev.Process ?? "").IndexOf("PresentMon", StringComparison.OrdinalIgnoreCase) >= 0;
                ev.Suggestion = amd
                    ? "An access violation in the AMD overlay. The repair is to turn that overlay off."
                    : "An overlay hooked the program and crashed. The repair is to turn overlays off.";
                ev.RepairId = amd ? "amd" : "overlayoff";
                return;
            }
            if (bug == "0x000000ef")
            {
                ev.Suggestion = "A required Windows process stopped. The repair is DISM image repair, then a system file check.";
                ev.RepairId = "dismsfc";
                return;
            }
            if (bug == "0x0000000a" || bug == "0x000000d1")
            {
                ev.Suggestion = "A driver touched memory it was not allowed to touch. The repair is a memory test. If a dump names a graphics driver, update that driver instead.";
                ev.RepairId = gpuModule ? "driver" : "memory";
                return;
            }
            if (bug == "0x0000001a" || bug == "0x00000154")
            {
                ev.Suggestion = "Windows memory management failed. The usual cause is bad RAM, an unstable memory profile, or a damaged page file. The repair is a memory test.";
                ev.RepairId = "memory";
                return;
            }
            if (bug == "0x00000133")
            {
                ev.Suggestion = "A storage or device driver stalled. The repair is the disk maker's firmware and the matching storage driver, not a graphics driver.";
                ev.RepairId = "storage";
                return;
            }
            if (bug == "0x00000050")
            {
                ev.Suggestion = "Windows could not read memory that must stay in RAM. The repair is a page-file reset and a disk check for bad sectors.";
                ev.RepairId = "fault50";
                return;
            }
            if (bug == "0x0000007b")
            {
                ev.Suggestion = "Windows lost the boot disk. Restore the original AHCI or RAID setting in BIOS. This app will not change that setting while Windows is running.";
                ev.RepairId = "manual";
                return;
            }
            if (ev.Kind == "HardwareError" || bug == "0x00000124")
            {
                ev.Suggestion = "The processor reported a hardware error. The repair is a memory test. Turn memory overclocking off in BIOS if the test fails.";
                ev.RepairId = "memory";
                return;
            }
            if (IsLauncherBrowser(ev))
            {
                ev.Suggestion = "The game launcher's browser stopped. Windows did not crash. The repair is to verify the game files.";
                ev.RepairId = IsSteamPath(ev.AppPath) ? "steam" : "manual";
                return;
            }
            if (code == "0xc0000005")
            {
                ev.Suggestion = "The program read or wrote memory it does not own. The repair is to verify its files and turn overlays off.";
                ev.RepairId = IsSteamPath(ev.AppPath) ? "steam" : "overlayoff";
                return;
            }
            if (code == "0xc0000409")
            {
                ev.Suggestion = "A stack guard was overwritten and Windows stopped the program. The repair is the Visual C++ runtime.";
                ev.RepairId = "vcrepair";
                return;
            }
            if (code == "0xc0000374" || bug == "0x0000013a")
            {
                ev.Suggestion = "The program corrupted its own heap, usually from an add-in or a driver. Start that program in its safe mode and update the printer driver. This app will not remove add-ins for you.";
                ev.RepairId = "manual";
                return;
            }
            if (code == "0xc000007b")
            {
                ev.Suggestion = "A 32-bit and 64-bit library were mixed, or the C++ runtime is damaged. The repair is both Visual C++ packages.";
                ev.RepairId = "vcrepair";
                return;
            }
            if (code == "0xc0000142")
            {
                ev.Suggestion = "A DLL failed to start with the program. The repair is to clear AppInit_DLLs so extra libraries are not forced into every program.";
                ev.RepairId = "appinit";
                return;
            }
            if (code == "0x80070005")
            {
                ev.Suggestion = "Windows was denied access. The repair is to reset the Windows Update download cache and run the repair with administrator approval.";
                ev.RepairId = "wureset";
                return;
            }
            if (code == "0x80004005")
            {
                ev.Suggestion = "Windows Update failed on a damaged download cache. The repair is to clear that cache and start the update services again.";
                ev.RepairId = "wureset";
                return;
            }
            if (code == "0x800f081f")
            {
                ev.Suggestion = "Windows is missing the source files for a repair. The repair is DISM image repair. If the source is still missing, mount a matching Windows ISO.";
                ev.RepairId = "dism";
                return;
            }
            if (code == "0xe0434352" || module.IndexOf("clr.dll", StringComparison.Ordinal) >= 0)
            {
                ev.Suggestion = "A .NET program crashed because the runtime is missing or damaged. The repair is to enable .NET Framework 3.5 and 4.8.";
                ev.RepairId = "dotnet";
                return;
            }
            if (code == "0x80000003")
            {
                ev.Suggestion = "The program stopped on its own breakpoint. That is not a Windows fault, and there is no system setting to change.";
                ev.RepairId = "manual";
                return;
            }
            if (ev.Kind == "PowerLoss" || ev.Kind == "UnexpectedShutdown")
            {
                ev.Suggestion = "The PC lost power before Windows could write a crash dump. Check the power supply, power cables, and cooling. No Windows setting repairs a power cut.";
                ev.RepairId = "manual";
                return;
            }
            if (ev.Kind == "AppHang")
            {
                ev.Suggestion = "The program stopped answering Windows. It was waiting on a file, a network share, or another process. There is no setting that unsticks a hang after it has ended.";
                ev.RepairId = "manual";
                return;
            }
            if (ev.LikelyGame || ev.Kind == "AppCrash")
            {
                if (IsWindowsProgram(ev))
                {
                    ev.Suggestion = "A Windows program stopped. The repair is DISM image repair, then a system file check.";
                    ev.RepairId = "dismsfc";
                    return;
                }
                if (IsSteamPath(ev.AppPath))
                {
                    ev.Suggestion = "The game crashed. The repair is to verify its files.";
                    ev.RepairId = "steam";
                    return;
                }
                string mod = module;
                if (mod.IndexOf("vcruntime", StringComparison.Ordinal) >= 0 || mod.IndexOf("msvcp140", StringComparison.Ordinal) >= 0 || mod.IndexOf("ucrtbase", StringComparison.Ordinal) >= 0)
                {
                    ev.Suggestion = "A Visual C++ runtime library is missing or damaged. The repair is the official 2015-2022 redistributable, not a single DLL from the web.";
                    ev.RepairId = "vcrepair";
                    return;
                }
                ev.Suggestion = "This program crashed. No Windows setting matches this error yet.";
                ev.RepairId = "";
                return;
            }
            ev.Suggestion = "No stop code matched a known Windows fault. The repair is a system file check.";
            ev.RepairId = "sfc";
        }

        public static string FriendlyKind(string kind)
        {
            if (kind == "AppCrash") return "Crash";
            if (kind == "AppHang") return "Hung";
            if (kind == "BugCheck") return "Blue screen";
            if (kind == "UnexpectedShutdown") return "Shutdown";
            if (kind == "PowerLoss") return "Power loss";
            if (kind == "DisplayDriver") return "GPU driver";
            if (kind == "HardwareError") return "Hardware";
            return kind ?? "";
        }

        public static string ExceptionName(string code)
        {
            code = (code ?? "").ToLowerInvariant();
            if (code == "0xc0000005") return "access violation";
            if (code == "0xc000007b") return "invalid image format";
            if (code == "0xc0000142") return "DLL failed to initialize";
            if (code == "0x80000003") return "breakpoint";
            if (code == "0xc0000409") return "stack buffer overrun";
            if (code == "0xc00000fd") return "stack overflow";
            if (code == "0xc0000374") return "heap corruption";
            if (code == "0xc000001d") return "illegal instruction";
            if (code == "0xc0000096") return "privileged instruction";
            if (code == "0x887a0005") return "DXGI device removed";
            if (code == "0x887a0006") return "DXGI device hung";
            if (code == "0x887a0007") return "DXGI device reset";
            if (code == "0xe06d7363") return "C++ exception";
            return "";
        }

        public static string BugCheckName(string code)
        {
            code = (code ?? "").ToLowerInvariant();
            if (code == "0x0000000a") return "IRQL_NOT_LESS_OR_EQUAL";
            if (code == "0x0000001a") return "MEMORY_MANAGEMENT";
            if (code == "0x0000001e") return "KMODE_EXCEPTION_NOT_HANDLED";
            if (code == "0x0000003b") return "SYSTEM_SERVICE_EXCEPTION";
            if (code == "0x00000050") return "PAGE_FAULT_IN_NONPAGED_AREA";
            if (code == "0x0000007b") return "INACCESSIBLE_BOOT_DEVICE";
            if (code == "0x0000007e") return "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED";
            if (code == "0x0000007f") return "UNEXPECTED_KERNEL_MODE_TRAP";
            if (code == "0x0000009f") return "DRIVER_POWER_STATE_FAILURE";
            if (code == "0x000000d1") return "DRIVER_IRQL_NOT_LESS_OR_EQUAL";
            if (code == "0x000000ea") return "THREAD_STUCK_IN_DEVICE_DRIVER";
            if (code == "0x000000ef") return "CRITICAL_PROCESS_DIED";
            if (code == "0x000000f4") return "CRITICAL_OBJECT_TERMINATION";
            if (code == "0x00000116") return "VIDEO_TDR_FAILURE";
            if (code == "0x00000117") return "VIDEO_TDR_TIMEOUT_DETECTED";
            if (code == "0x00000124") return "WHEA_UNCORRECTABLE_ERROR";
            if (code == "0x00000133") return "DPC_WATCHDOG_VIOLATION";
            if (code == "0x00000139") return "KERNEL_SECURITY_CHECK_FAILURE";
            if (code == "0x0000013a") return "KERNEL_MODE_HEAP_CORRUPTION";
            if (code == "0x00000154") return "UNEXPECTED_STORE_EXCEPTION";
            return "";
        }

        static bool IsLauncherBrowser(CrashEvent ev)
        {
            string module = (ev.FaultModule ?? "").ToLowerInvariant();
            string process = (ev.Process ?? "").ToLowerInvariant();
            if (process.IndexOf("tbs_browser", StringComparison.Ordinal) >= 0) return true;
            if (module.IndexOf("qbcore", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        public static bool IsSteamPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.IndexOf(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsWindowsProgram(CrashEvent ev)
        {
            string process = (ev.Process ?? "").ToLowerInvariant();
            if (process == "taskbarhero" || process == "taskbarhero.exe") return true;
            string path = ev.AppPath ?? "";
            if (path.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows) && path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string KindOf(string logName, string provider, int id)
        {
            if (string.Equals(logName, "Application", StringComparison.OrdinalIgnoreCase))
            {
                if (id == 1000 && provider == "Application Error") return "AppCrash";
                if (id == 1002 && provider == "Application Hang") return "AppHang";
                return "";
            }
            if (provider == "Microsoft-Windows-Kernel-Power" && id == 41) return "PowerLoss";
            if (provider == "EventLog" && id == 6008) return "UnexpectedShutdown";
            if (id == 4101) return "DisplayDriver";
            if (provider.IndexOf("WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase) >= 0 && id == 1001) return "BugCheck";
            if (provider.IndexOf("WHEA-Logger", StringComparison.OrdinalIgnoreCase) >= 0 && id == 18) return "HardwareError";
            return "";
        }

        static string BuildAppSummary(CrashEvent ev)
        {
            string name = string.IsNullOrEmpty(ev.Process) ? "A program" : ev.Process;
            if (ev.Kind == "AppHang") return name + " stopped responding.";
            string meaning = ExceptionName(ev.ExceptionCode);
            if (ev.ExceptionCode.Length > 0 && meaning.Length > 0)
            {
                string module = string.IsNullOrEmpty(ev.FaultModule) ? "" : " in " + ev.FaultModule;
                return name + " crashed: " + meaning + " (" + ev.ExceptionCode + ")" + module + ".";
            }
            if (ev.ExceptionCode.Length > 0) return name + " crashed (" + ev.ExceptionCode + ").";
            return name + " crashed.";
        }

        static bool IsOverlay(string process, string path)
        {
            string p = process ?? "";
            string a = path ?? "";
            if (a.IndexOf(@"\AMD\CNext\", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("nvidia overlay", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("gameoverlay", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string[] names = new string[] {
                "PresentMon-x64.exe", "RadeonSoftware.exe", "AMDRSSrcExt.exe", "cncmd.exe",
                "NVIDIA Share.exe", "GameOverlayUI.exe", "GameOverlayUI64.exe"
            };
            for (int i = 0; i < names.Length; i++)
            {
                if (p.Equals(names[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static string Prop(EventRecord record, int index)
        {
            try
            {
                if (record.Properties == null || record.Properties.Count <= index || record.Properties[index] == null) return "";
                object value = record.Properties[index].Value;
                return value == null ? "" : Convert.ToString(value);
            }
            catch
            {
                return "";
            }
        }

        static string PropsText(EventRecord record)
        {
            try
            {
                if (record.Properties == null) return "";
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < record.Properties.Count && i < 8; i++)
                {
                    sb.Append(' ');
                    sb.Append(Prop(record, i));
                }
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        static string SafeDescription(EventRecord record)
        {
            try { return record.FormatDescription() ?? ""; }
            catch { return ""; }
        }

        static string HexCode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            raw = raw.Trim();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(2);
            ulong value;
            if (!ulong.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return "";
            if (value <= 0xFFFFFFFFUL) return "0x" + value.ToString("x8");
            return "0x" + value.ToString("x");
        }

        static string FirstHex(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            Match match = HexInText.Match(text);
            if (!match.Success) return "";
            return match.Value.ToLowerInvariant();
        }

        static string Clip(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? "";
            return value.Substring(0, max);
        }
    }
}
