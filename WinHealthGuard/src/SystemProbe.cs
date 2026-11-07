using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinHealthGuard
{
    static class SystemProbe
    {
        public static HealthSnapshot Sample(CpuStamp previous, out CpuStamp next)
        {
            HealthSnapshot snap = new HealthSnapshot();
            snap.Time = DateTime.Now;
            next = previous;

            FileTime64 idle;
            FileTime64 kernel;
            FileTime64 user;
            if (NativeMethods.GetSystemTimes(out idle, out kernel, out user))
            {
                next.Idle = NativeMethods.FileTimeToUInt64(idle);
                next.Kernel = NativeMethods.FileTimeToUInt64(kernel);
                next.User = NativeMethods.FileTimeToUInt64(user);
                next.Has = true;
                if (previous.Has)
                {
                    ulong idleDelta = next.Idle - previous.Idle;
                    ulong total = (next.Kernel - previous.Kernel) + (next.User - previous.User);
                    if (total > 0)
                    {
                        double cpu = (1.0 - ((double)idleDelta / total)) * 100.0;
                        if (cpu < 0) cpu = 0;
                        if (cpu > 100) cpu = 100;
                        snap.CpuPercent = cpu;
                        snap.CpuValid = true;
                    }
                }
            }

            MemoryStatusEx mem = new MemoryStatusEx();
            mem.Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryStatusEx));
            if (NativeMethods.GlobalMemoryStatusEx(ref mem))
            {
                snap.RamPercent = mem.MemoryLoad;
                snap.RamTotalMb = mem.TotalPhys / (1024UL * 1024UL);
                if (mem.TotalPhys > mem.AvailPhys) snap.RamUsedMb = (mem.TotalPhys - mem.AvailPhys) / (1024UL * 1024UL);
                snap.CommitTotalMb = mem.TotalPageFile / (1024UL * 1024UL);
                if (mem.TotalPageFile > mem.AvailPageFile) snap.CommitUsedMb = (mem.TotalPageFile - mem.AvailPageFile) / (1024UL * 1024UL);
            }

            string drive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrEmpty(drive)) drive = "C:";
            snap.SystemDrive = drive;
            try
            {
                DriveInfo info = new DriveInfo(drive);
                if (info.IsReady && info.TotalSize > 0)
                {
                    snap.SystemFreeGb = info.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                    snap.SystemFreePercent = (info.AvailableFreeSpace * 100.0) / info.TotalSize;
                }
            }
            catch
            {
            }

            snap.PowerGuid = PowerPlans.ActiveGuid();
            snap.PowerName = PowerPlans.Name(snap.PowerGuid);
            snap.PendingReboot = PendingReboot();

            SystemPowerStatus power;
            if (NativeMethods.GetSystemPowerStatus(out power))
            {
                snap.OnAc = power.ACLineStatus != 0;
                if (power.BatteryLifePercent <= 100) snap.BatteryPercent = power.BatteryLifePercent;
            }

            ReadForeground(snap);
            return snap;
        }

        public static SlowFacts ReadSlow()
        {
            SlowFacts facts = new SlowFacts();
            ReadGpu(facts);
            ReadDisk(facts);
            ReadPageFile(facts);
            ReadMemory(facts);
            return facts;
        }

        static void ReadForeground(HealthSnapshot snap)
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;
                uint pid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                if (!TryProcessName(pid, out snap.ForegroundName)) return;
                NativeRect rect;
                if (!NativeMethods.GetWindowRect(hwnd, out rect)) return;
                Screen screen = Screen.FromHandle(hwnd);
                int ww = rect.Right - rect.Left;
                int hh = rect.Bottom - rect.Top;
                snap.ForegroundFullscreen = ww >= (int)(screen.Bounds.Width * 0.90) && hh >= (int)(screen.Bounds.Height * 0.88);
            }
            catch
            {
            }
        }

        static bool TryProcessName(uint pid, out string name)
        {
            name = "";
            if (pid == 0) return false;
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                {
                    name = process.ProcessName ?? "";
                    return name.Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void ReadGraphics(SlowFacts facts)
        {
            if (facts == null) return;
            ReadGpu(facts);
        }

        static void ReadGpu(SlowFacts facts)
        {
            try
            {
                List<GpuChip> chips = new List<GpuChip>();
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(8);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                string name = Convert.ToString(item["Name"]) ?? "";
                                string pnp = Convert.ToString(item["PNPDeviceID"]) ?? "";
                                if (!IsRealGpu(name, pnp)) continue;
                                GpuChip chip = new GpuChip();
                                chip.Name = name;
                                chip.Pnp = pnp;
                                chip.Version = Convert.ToString(item["DriverVersion"]) ?? "";
                                DateTime date;
                                if (TryDriverDate(item["DriverDate"], out date)) chip.Date = date;
                                chips.Add(chip);
                            }
                        }
                    }
                }
                ApplyRegistryDriver(chips);
                GpuChip best = null;
                for (int i = 0; i < chips.Count; i++)
                {
                    GpuChip chip = chips[i];
                    if (chip.Pnp.IndexOf("VEN_1002", StringComparison.OrdinalIgnoreCase) >= 0) facts.ShowAmd = true;
                    if (chip.Pnp.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0) facts.ShowNvidia = true;
                    if (best == null || NewerChip(chip, best)) best = chip;
                }
                if (best == null) return;
                facts.GpuKnown = true;
                facts.GpuName = best.Name;
                facts.GpuDriver = best.Version;
                facts.GpuPnp = best.Pnp;
                facts.GpuDriverDate = best.Date;
            }
            catch
            {
            }
        }

        static bool NewerChip(GpuChip chip, GpuChip best)
        {
            if (chip.Date.Year >= 2015 && best.Date.Year < 2015) return true;
            if (chip.Date.Year >= 2015 && best.Date.Year >= 2015 && chip.Date > best.Date) return true;
            return false;
        }

        static void ApplyRegistryDriver(List<GpuChip> chips)
        {
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (root == null) return;
                    string[] names = root.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (names[i].Length != 4) continue;
                        using (RegistryKey item = root.OpenSubKey(names[i]))
                        {
                            if (item == null) continue;
                            string desc = Convert.ToString(item.GetValue("DriverDesc")) ?? "";
                            string version = Convert.ToString(item.GetValue("DriverVersion")) ?? "";
                            string match = Convert.ToString(item.GetValue("MatchingDeviceId")) ?? "";
                            if (!IsRealGpu(desc, match)) continue;
                            DateTime date;
                            if (!TryRegistryDate(Convert.ToString(item.GetValue("DriverDate")), out date)) continue;
                            GpuChip chip = FindChip(chips, match, desc);
                            if (chip == null)
                            {
                                chip = new GpuChip();
                                chip.Name = desc;
                                chip.Pnp = match;
                                chips.Add(chip);
                            }
                            if (version.Length > 0) chip.Version = version;
                            if (chip.Date.Year < 2015 || date > chip.Date) chip.Date = date;
                            if (chip.Name.Length == 0) chip.Name = desc;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        static GpuChip FindChip(List<GpuChip> chips, string match, string desc)
        {
            string id = (match ?? "").ToUpperInvariant();
            for (int i = 0; i < chips.Count; i++)
            {
                string pnp = (chips[i].Pnp ?? "").ToUpperInvariant();
                if (id.Length >= 8 && pnp.IndexOf(id, StringComparison.Ordinal) >= 0) return chips[i];
            }
            for (int i = 0; i < chips.Count; i++)
            {
                if (desc.Length > 0 && string.Equals(chips[i].Name, desc, StringComparison.OrdinalIgnoreCase)) return chips[i];
            }
            return null;
        }

        static bool TryRegistryDate(string text, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrEmpty(text)) return false;
            if (!DateTime.TryParseExact(text.Trim(), new string[] { "M-d-yyyy", "MM-dd-yyyy", "M/d/yyyy", "MM/dd/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return false;
            return date.Year >= 2015;
        }

        static bool IsRealGpu(string name, string pnp)
        {
            string n = (name ?? "").ToLowerInvariant();
            string p = (pnp ?? "").ToLowerInvariant();
            if (n.Length == 0 && p.Length == 0) return false;
            if (n.IndexOf("remote", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("basic display", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("basic render", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("virtual", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("mirror", StringComparison.Ordinal) >= 0) return false;
            if (p.IndexOf("remotedisplay", StringComparison.Ordinal) >= 0) return false;
            if (p.IndexOf("indirectdisplay", StringComparison.Ordinal) >= 0) return false;
            if (p.IndexOf("rdpidd", StringComparison.Ordinal) >= 0) return false;
            bool pci = p.IndexOf("pci\\", StringComparison.Ordinal) >= 0 || p.IndexOf("ven_", StringComparison.Ordinal) >= 0;
            return pci;
        }

        sealed class GpuChip
        {
            public string Name = "";
            public string Version = "";
            public string Pnp = "";
            public DateTime Date = DateTime.MinValue;
        }

        static bool TryDriverDate(object value, out DateTime date)
        {
            date = DateTime.MinValue;
            if (value == null) return false;
            try
            {
                date = ManagementDateTimeConverter.ToDateTime(Convert.ToString(value));
                return date.Year >= 2015;
            }
            catch
            {
                return false;
            }
        }

        static void ReadDisk(SlowFacts facts)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Status, Model FROM Win32_DiskDrive"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(8);
                    List<string> bad = new List<string>();
                    bool any = false;
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                any = true;
                                string status = Convert.ToString(item["Status"]) ?? "";
                                if (status.Length > 0 && !status.Equals("OK", StringComparison.OrdinalIgnoreCase))
                                {
                                    string model = Convert.ToString(item["Model"]) ?? "Disk";
                                    bad.Add(model + " (" + status + ")");
                                }
                            }
                        }
                    }
                    if (!any) facts.DiskStatus = "";
                    else if (bad.Count == 0) facts.DiskStatus = "OK";
                    else facts.DiskStatus = string.Join("; ", bad.ToArray());
                }
            }
            catch
            {
            }
        }

        static void ReadPageFile(SlowFacts facts)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(8);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                object value = item["AutomaticManagedPagefile"];
                                if (value != null)
                                {
                                    facts.PageFileKnown = true;
                                    facts.AutoPageFile = Convert.ToBoolean(value);
                                }
                                ReadPageLimits(facts);
                                return;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        static void ReadPageLimits(SlowFacts facts)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT InitialSize, MaximumSize FROM Win32_PageFileSetting"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(8);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        int files = 0;
                        bool canGrow = false;
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                files++;
                                int initial = Convert.ToInt32(item["InitialSize"]);
                                int max = Convert.ToInt32(item["MaximumSize"]);
                                if (max == 0 || max > initial) canGrow = true;
                            }
                        }
                        facts.PageCanGrow = files > 0 && canGrow;
                    }
                }
            }
            catch
            {
            }
        }

        static void ReadMemory(SlowFacts facts)
        {
            try
            {
                using (PerformanceCounter counter = new PerformanceCounter("Memory", "Pool Nonpaged Bytes", true))
                {
                    float raw = counter.NextValue();
                    if (raw > 0) facts.NonPagedMb = (int)(raw / (1024f * 1024f));
                }
            }
            catch { }
            ReadReserved(facts);
            ReadModules(facts);
            ReadTopProcesses(facts);
            ReadTruncate(facts);
            facts.StartupUpdaters = CountStartupUpdaters();
        }

        static void ReadReserved(SlowFacts facts)
        {
            try
            {
                ulong physical = 0;
                ulong visible = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                physical = Convert.ToUInt64(item["TotalPhysicalMemory"]);
                                break;
                            }
                        }
                    }
                }
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                visible = Convert.ToUInt64(item["TotalVisibleMemorySize"]) * 1024UL;
                                break;
                            }
                        }
                    }
                }
                if (physical > visible && visible > 0)
                    facts.HardwareReservedMb = (int)((physical - visible) / (1024UL * 1024UL));
            }
            catch { }
        }

        static void ReadModules(SlowFacts facts)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                facts.RamSlots += Convert.ToInt32(item["MemoryDevices"]);
                            }
                        }
                    }
                }
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT BankLabel FROM Win32_PhysicalMemory"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item) facts.RamModules++;
                        }
                    }
                }
            }
            catch { }
        }

        static void ReadTopProcesses(SlowFacts facts)
        {
            int sysMainPid = 0;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId, State FROM Win32_Service WHERE Name='SysMain'"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                string state = Convert.ToString(item["State"]) ?? "";
                                if (state.Equals("Running", StringComparison.OrdinalIgnoreCase))
                                    sysMainPid = Convert.ToInt32(item["ProcessId"]);
                            }
                        }
                    }
                }
            }
            catch { }

            Process[] all = null;
            try { all = Process.GetProcesses(); }
            catch { return; }
            string firstName = "";
            string secondName = "";
            string thirdName = "";
            long first = 0;
            long second = 0;
            long third = 0;
            for (int i = 0; i < all.Length; i++)
            {
                using (all[i])
                {
                    long bytes = 0;
                    string name = "";
                    try
                    {
                        name = all[i].ProcessName ?? "";
                        bytes = all[i].WorkingSet64;
                    }
                    catch { continue; }
                    if (sysMainPid != 0 && all[i].Id == sysMainPid)
                        facts.SysMainMb = (int)(bytes / (1024L * 1024L));
                    if (name.Equals("dwm", StringComparison.OrdinalIgnoreCase))
                        facts.DwmMb = (int)(bytes / (1024L * 1024L));
                    if (!CountInTop(name)) continue;
                    if (bytes > first)
                    {
                        third = second; thirdName = secondName;
                        second = first; secondName = firstName;
                        first = bytes; firstName = name;
                    }
                    else if (bytes > second)
                    {
                        third = second; thirdName = secondName;
                        second = bytes; secondName = name;
                    }
                    else if (bytes > third)
                    {
                        third = bytes; thirdName = name;
                    }
                }
            }
            facts.TopRam = FormatTop(firstName, first, secondName, second, thirdName, third);
        }

        static bool CountInTop(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            if (n == "idle" || n == "system" || n == "registry" || n == "secure system") return false;
            if (n == "memory compression" || n == "winhealthguard") return false;
            if (n == "csrss" || n == "smss" || n == "lsass" || n == "services" || n == "svchost") return false;
            if (n == "dwm") return false;
            return true;
        }

        static string FormatTop(string a, long ab, string b, long bb, string c, long cb)
        {
            string text = "";
            text = AppendTop(text, a, ab);
            text = AppendTop(text, b, bb);
            text = AppendTop(text, c, cb);
            return text;
        }

        static string AppendTop(string text, string name, long bytes)
        {
            if (string.IsNullOrEmpty(name) || bytes <= 0) return text;
            int mb = (int)(bytes / (1024L * 1024L));
            if (mb < 80) return text;
            string part = name + " " + mb.ToString() + " MB";
            if (text.Length == 0) return part;
            return text + ", " + part;
        }

        static void ReadTruncate(SlowFacts facts)
        {
            try
            {
                string output;
                string exe = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");
                Command.Run(exe, "/enum {current}", out output, 8000);
                facts.TruncateMemory = (output ?? "").IndexOf("truncatememory", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { }
        }

        static int CountStartupUpdaters()
        {
            int count = 0;
            count += CountRun(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run");
            count += CountRun(Microsoft.Win32.Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run");
            return count;
        }

        static int CountRun(Microsoft.Win32.RegistryKey root, string path)
        {
            int count = 0;
            try
            {
                using (Microsoft.Win32.RegistryKey key = root.OpenSubKey(path))
                {
                    if (key == null) return 0;
                    string[] names = key.GetValueNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        string command = key.GetValue(names[i]) as string;
                        if (IsStartupUpdater(names[i], command)) count++;
                    }
                }
            }
            catch { }
            return count;
        }

        public static bool IsStartupUpdater(string name, string command)
        {
            string blob = ((name ?? "") + " " + (command ?? "")).ToLowerInvariant();
            if (blob.Trim().Length < 3) return false;
            if (blob.IndexOf("microsoft", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("windows", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("defender", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("security", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("onedrive", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("winhealthguard", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("nvidia", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("radeon", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("intel", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("realtek", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("\\windows\\", StringComparison.Ordinal) >= 0) return false;
            if (blob.IndexOf("update", StringComparison.Ordinal) >= 0) return true;
            if (blob.IndexOf("updater", StringComparison.Ordinal) >= 0) return true;
            if (blob.IndexOf("launcher", StringComparison.Ordinal) >= 0) return true;
            if (blob.IndexOf("helper", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static bool PendingReboot()
        {
            return KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
                || KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        }

        static bool KeyExists(string path)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path))
                    return key != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
