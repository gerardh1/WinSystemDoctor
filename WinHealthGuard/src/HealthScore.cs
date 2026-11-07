using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinHealthGuard
{
    static class HealthScore
    {
        public static void Evaluate(HealthSnapshot snap, IList<CrashEvent> events, int idleHotStreak, bool gameActive)
        {
            Evaluate(snap, events, idleHotStreak, gameActive, null);
        }

        public static void Evaluate(HealthSnapshot snap, IList<CrashEvent> events, int idleHotStreak, bool gameActive, IList<string> addressed)
        {
            List<HealthIssue> issues = new List<HealthIssue>();
            string drive = string.IsNullOrEmpty(snap.SystemDrive) ? "C:" : snap.SystemDrive;

            if (snap.SystemFreeGb > 0 && snap.SystemFreeGb < 8)
                issues.Add(Issue(22, "Disk almost full", drive + " has " + snap.SystemFreeGb.ToString("0") + " GB free. Games crash when the drive runs out of room.", "Clean up", "shaders"));
            else if (snap.SystemFreeGb > 0 && (snap.SystemFreeGb < 16 || (snap.SystemFreePercent < 8 && snap.SystemFreeGb < 32)))
                issues.Add(Issue(12, "Low disk space", drive + " has " + snap.SystemFreeGb.ToString("0") + " GB free. Leave more room for game updates and saves.", "Clean up", "shaders"));
            else if (snap.SystemFreeGb > 0 && (snap.SystemFreeGb < 32 || snap.SystemFreePercent < 12))
                issues.Add(Issue(6, "Low disk space", drive + " has " + snap.SystemFreeGb.ToString("0") + " GB free. Leave more room for game updates and saves.", "Clean up", "shaders"));

            if (snap.CommitTotalMb > snap.CommitUsedMb)
            {
                ulong avail = snap.CommitTotalMb - snap.CommitUsedMb;
                if (avail < 800)
                    issues.Add(Issue(28, "Out of memory", "Only " + avail.ToString() + " MB of memory is left for programs. Close some apps, or let Windows manage the page file.", "Fix now", "pagefile"));
                else if (avail < 1500)
                    issues.Add(Issue(14, "Memory is running low", avail.ToString() + " MB of memory is left. A game can crash if this drops further.", "Fix now", "pagefile"));
            }

            if (idleHotStreak >= 3)
                issues.Add(Issue(10, "CPU is stuck high", "The CPU has stayed above 90% while you are not playing. A background program may be stuck.", "", ""));

            if (gameActive && !snap.OnAc)
                issues.Add(Issue(10, "Playing on battery", "Plug in the power adapter. On battery the graphics chip slows down and games stutter.", "", ""));

            if (snap.PageFileKnown && !snap.AutoPageFile && !snap.PageCanGrow)
                issues.Add(Issue(8, "Page file is fixed", "The page file has a fixed size, so Windows cannot grow it. The repair lets Windows choose the size. The page file stays on.", "Fix now", "pagefile"));

            if (snap.TruncateMemory)
                issues.Add(Issue(16, "Memory limit is on", "Windows is set to use only part of the installed RAM. The repair removes that boot limit. The graphics frame buffer in BIOS is not changed.", "Fix", "maxmem"));
            else if (snap.HardwareReservedMb >= 2048)
                issues.Add(Issue(8, "Hardware reserved memory", snap.HardwareReservedMb.ToString() + " MB is reserved for hardware. If the graphics chip is using too much, reduce the UMA frame buffer in BIOS. This app will not change BIOS.", "", ""));

            if (snap.RamSlots >= 2 && snap.RamModules == 1)
                issues.Add(Issue(6, "Single memory module", "One RAM module is installed and another slot is free. A matching second module turns on dual-channel. This app cannot install hardware.", "", ""));

            if (snap.NonPagedMb >= 900)
                issues.Add(Issue(18, "Driver memory leak", "Non-paged pool is " + snap.NonPagedMb.ToString() + " MB. A network or audio driver is holding memory. Update that driver from the chip maker or Windows Update. A graphics driver is not installed for this, and poolmon is not run.", "", ""));

            if (snap.SysMainMb >= 400)
                issues.Add(Issue(10, "SysMain is using RAM", "SysMain is holding about " + snap.SysMainMb.ToString() + " MB. The repair stops that cache service. Open programs are not closed.", "Fix", "sysmain"));

            bool browserHeavy = HasProcess(snap.TopRam, "chrome") || HasProcess(snap.TopRam, "msedge");
            if (!gameActive && snap.RamPercent >= 92 && browserHeavy)
                issues.Add(Issue(14, "Browser stays in memory", RamSentence(snap) + " The repair tells Edge and Chrome to exit when closed and turns on Memory Saver.", "Fix", "browsers"));
            else if (!gameActive && snap.RamPercent >= 92)
                issues.Add(Issue(12, "Memory is full", RamSentence(snap) + " Close a program you are not using. This app does not end tasks.", "", ""));
            else if (snap.RamPercent >= 97)
                issues.Add(Issue(14, "Memory is full", RamSentence(snap) + " A game can crash if Windows runs out of room.", "Fix now", "pagefile"));

            if (!gameActive && snap.RamPercent >= 85 && snap.DwmMb >= 700)
                issues.Add(Issue(8, "Visual effects are heavy", "Desktop Window Manager is using about " + snap.DwmMb.ToString() + " MB. The repair turns off most animations and keeps font smoothing and thumbnails.", "Fix", "visualfx"));

            if (snap.StartupUpdaters > 0)
                issues.Add(Issue(6, "Updaters start with Windows", snap.StartupUpdaters.ToString() + " updater or launcher starts with Windows. The repair turns those off. Windows and security tools stay on.", "Fix", "startuptrim"));

            if (!string.IsNullOrEmpty(snap.DiskStatus) && !snap.DiskStatus.Equals("OK", StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue(25, "Disk problem", "A drive reported a problem: " + snap.DiskStatus + ".", "", ""));

            if (snap.GpuKnown && DriverIsOld(snap.GpuDriverDate))
                issues.Add(Issue(10, "Graphics driver is old", DriverSentence(snap), "Update", "driver"));

            if (snap.PendingReboot)
                issues.Add(Issue(6, "Restart required", "Windows updates are waiting. Finish the restart before you play.", "View", "wu"));

            int game24 = 0;
            int app24 = 0;
            int crashes7 = 0;
            int tdr = 0;
            int bug = 0;
            int power = 0;
            int overlay7 = 0;
            string recentName = "";
            string restartFix = "";
            string bugFix = "";
            string gameFix = "";
            string appFix = "";
            DateTime restartWhen = DateTime.MinValue;
            DateTime bugWhen = DateTime.MinValue;
            DateTime gameWhen = DateTime.MinValue;
            DateTime appWhen = DateTime.MinValue;
            DateTime now = DateTime.UtcNow;
            if (events != null)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    CrashEvent ev = events[i];
                    double hours = (now - ev.Time.ToUniversalTime()).TotalHours;
                    if (hours < 0) hours = 0;
                    if (hours <= 24 * 7) crashes7++;
                    if (hours <= 24 && !ev.Overlay && !ev.LikelyGame && (ev.Kind == "AppCrash" || ev.Kind == "AppHang")) app24++;
                    if (hours <= 24 * 7 && ev.Overlay)
                    {
                        overlay7++;
                        if (recentName.Length == 0) recentName = ev.Process;
                    }
                    else if (hours <= 24 && ev.LikelyGame && ev.Kind != "DisplayDriver")
                    {
                        game24++;
                        if (recentName.Length == 0) recentName = ev.Process;
                    }
                    if (hours <= 72 && ev.Kind == "DisplayDriver") tdr++;
                    if (hours <= 24 * 7 && (ev.Kind == "BugCheck" || ev.Kind == "HardwareError"))
                    {
                        bug++;
                        if (ev.Time >= bugWhen)
                        {
                            bugWhen = ev.Time;
                            bugFix = ev.RepairId ?? "";
                        }
                    }
                    if (hours <= 24 * 7 && ev.Kind == "PowerLoss")
                    {
                        string finding = ev.Suggestion ?? "";
                        if (finding.IndexOf("This restart followed", StringComparison.Ordinal) != 0)
                        {
                            power++;
                            if (ev.Time >= restartWhen)
                            {
                                restartWhen = ev.Time;
                                string id = ev.RepairId ?? "";
                                if (id == "manual" || id.Length == 0) restartFix = "";
                                else restartFix = id;
                            }
                        }
                    }
                    if (hours <= 24 && ev.LikelyGame && !ev.Overlay && ev.Time >= gameWhen)
                    {
                        gameWhen = ev.Time;
                        gameFix = ev.RepairId ?? "";
                    }
                    if (hours <= 24 && !ev.Overlay && !ev.LikelyGame && (ev.Kind == "AppCrash" || ev.Kind == "AppHang") && ev.Time >= appWhen)
                    {
                        appWhen = ev.Time;
                        appFix = ev.RepairId ?? "";
                    }
                }
            }

            if (bug > 0) AddNamed(issues, 30, bugFix, "Blue screen found", "Windows crashed this week.");
            if (power > 0 && restartFix.Length > 0)
            {
                string times = power == 1 ? "once" : power.ToString() + " times";
                AddNamed(issues, 20, restartFix, "Unexpected restart", "The PC restarted " + times + ".");
            }
            else if (power > 0)
                issues.Add(Issue(20, "Power loss", "The PC lost power before Windows could write a crash dump. Check the power supply, cables, and cooling.", "", ""));
            if (tdr > 0) issues.Add(Issue(22, "Display driver reset", "The graphics driver stopped responding. The repair is a clean install from the official source.", "Update", "driver"));
            if (overlay7 > 0)
            {
                string times = overlay7 == 1 ? "once" : overlay7.ToString() + " times";
                issues.Add(Issue(18, "Overlay crashed", "A graphics overlay crashed " + times + " this week. The repair is to turn overlays off.", "Fix", "overlayoff"));
            }
            else if (game24 > 0)
                AddNamed(issues, 18, gameFix, "A game crashed", (recentName.Length > 0 ? recentName : "A game") + " crashed in the last day.");
            else if (app24 > 0)
                AddNamed(issues, 12, appFix, "A program crashed", "A program crashed in the last day.");
            else if (crashes7 >= 3)
                issues.Add(Issue(8, "Repeated crashes", crashes7.ToString() + " crashes were recorded this week.", "View", "crashes"));

            if (addressed != null && addressed.Count > 0)
            {
                for (int i = issues.Count - 1; i >= 0; i--)
                {
                    string id = issues[i].RepairId ?? "";
                    if (id.Length == 0) continue;
                    for (int j = 0; j < addressed.Count; j++)
                    {
                        if (addressed[j] == id)
                        {
                            issues.RemoveAt(i);
                            break;
                        }
                    }
                }
            }

            issues.Sort(delegate(HealthIssue a, HealthIssue b) { return b.Penalty.CompareTo(a.Penalty); });
            int penalty = 0;
            int worst = 0;
            snap.Notes = new List<HealthIssue>();
            for (int i = 0; i < issues.Count; i++)
            {
                penalty += issues[i].Penalty;
                if (issues[i].Penalty > worst) worst = issues[i].Penalty;
                snap.Notes.Add(issues[i]);
            }
            if (penalty > 100) penalty = 100;
            snap.Score = 100 - penalty;
            if (snap.Score < 50 || worst >= 22) snap.Status = "Critical";
            else if (snap.Score < 90) snap.Status = "Attention";
            else snap.Status = "Healthy";
        }

        static void AddNamed(List<HealthIssue> issues, int penalty, string repairId, string title, string detail)
        {
            string button;
            string sentence;
            if (repairId == "faststartup") { button = "Fix now"; sentence = detail + " The repair is to turn Fast Startup off."; }
            else if (repairId == "tdr") { button = "Fix now"; sentence = detail + " The repair is to give the graphics driver more time to recover."; }
            else if (repairId == "pagefile") { button = "Fix now"; sentence = detail + " The repair is to let Windows manage the page file. The page file stays on."; }
            else if (repairId == "browsers") { button = "Fix"; sentence = detail + " The repair is to stop Edge and Chrome staying open in the background."; }
            else if (repairId == "visualfx") { button = "Fix"; sentence = detail + " The repair is to turn off most animations."; }
            else if (repairId == "sysmain") { button = "Fix"; sentence = detail + " The repair is to stop the SysMain cache service."; }
            else if (repairId == "startuptrim") { button = "Fix"; sentence = detail + " The repair is to stop updater programs from starting with Windows."; }
            else if (repairId == "maxmem") { button = "Fix"; sentence = detail + " The repair is to remove the Windows boot memory limit."; }
            else if (repairId == "fault50") { button = "Fix now"; sentence = detail + " The repair is a page-file reset and a disk check."; }
            else if (repairId == "memory") { button = "Open"; sentence = detail + " The repair is a memory test on the next restart."; }
            else if (repairId == "sfc" || repairId == "dismsfc") { button = "Fix"; sentence = detail + " The repair is DISM, then a system file check."; }
            else if (repairId == "dism") { button = "Fix"; sentence = detail + " The repair is to repair the Windows image."; }
            else if (repairId == "steam") { button = "Verify"; sentence = detail + " The repair is to verify the game files."; }
            else if (repairId == "amd" || repairId == "overlayoff") { button = "Fix"; sentence = detail + " The repair is to turn overlays off."; }
            else if (repairId == "driver") { button = "Update"; sentence = detail + " The repair is an official graphics-driver install."; }
            else if (repairId == "storage") { button = "Fix"; sentence = detail + " The repair is the storage firmware and its driver."; }
            else if (repairId == "vcrepair") { button = "Fix"; sentence = detail + " The repair is the Visual C++ runtime."; }
            else if (repairId == "wureset") { button = "Fix"; sentence = detail + " The repair is to clear the Windows Update cache."; }
            else if (repairId == "dotnet") { button = "Fix"; sentence = detail + " The repair is to enable the .NET runtimes."; }
            else if (repairId == "appinit") { button = "Fix"; sentence = detail + " The repair is to clear AppInit_DLLs."; }
            else if (repairId == "chkdsk") { button = "Fix"; sentence = detail + " The repair is a disk check on the next restart."; }
            else { button = "View"; sentence = detail; repairId = "crashes"; }
            issues.Add(Issue(penalty, title, sentence, button, repairId));
        }

        public static bool DriverIsOld(DateTime date)
        {
            return date.Year >= 2015 && date < DateTime.Now.AddMonths(-6);
        }

        public static string DriverSentence(HealthSnapshot snap)
        {
            if (snap == null || !snap.GpuKnown || string.IsNullOrEmpty(snap.GpuName))
                return "The installed graphics driver has not been read yet.";
            string version = string.IsNullOrEmpty(snap.GpuDriver) ? "" : " " + snap.GpuDriver;
            string when = snap.GpuDriverDate.Year >= 2015
                ? snap.GpuDriverDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
                : "an unknown date";
            if (DriverIsOld(snap.GpuDriverDate))
                return "Installed " + snap.GpuName + " driver" + version + " is dated " + when + ". That is older than 6 months.";
            return "Installed " + snap.GpuName + " driver" + version + " is dated " + when + ". That is within 6 months, so this driver is current.";
        }

        static HealthIssue Issue(int penalty, string title, string detail, string button, string repairId)
        {
            HealthIssue issue = new HealthIssue();
            issue.Penalty = penalty;
            issue.Title = title;
            issue.Detail = detail;
            issue.Button = button;
            issue.RepairId = repairId;
            return issue;
        }

        static bool HasProcess(string top, string name)
        {
            if (string.IsNullOrEmpty(top) || string.IsNullOrEmpty(name)) return false;
            return top.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string RamSentence(HealthSnapshot snap)
        {
            string text = "Memory is at " + snap.RamPercent.ToString("0") + "%.";
            if (!string.IsNullOrEmpty(snap.TopRam)) text += " Largest programs: " + snap.TopRam + ".";
            return text;
        }
    }
}
