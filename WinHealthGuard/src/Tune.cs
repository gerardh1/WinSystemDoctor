using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinHealthGuard
{
    sealed class TuneItem
    {
        public string Key = "";
        public string Name = "";
        public string Current = "";
        public string Selected = "";
        public bool Apply = true;
        public bool Editable = true;
        public string[] Choices = new string[0];
    }

    static class TuneCatalog
    {
        public static List<TuneItem> For(string id)
        {
            List<TuneItem> list = new List<TuneItem>();
            if (id == "tweaks") Tweaks(list);
            else if (id == "tdr") Tdr(list);
            else if (id == "hagsoff" || id == "hagson") Hags(list, id == "hagson");
            else if (id == "faststartup") Fast(list);
            else if (id == "overlayoff") Overlay(list);
            else if (id == "browsers") Browsers(list);
            else if (id == "visualfx") Visual(list);
            else if (id == "sysmain") SysMain(list);
            else if (id == "startuptrim") Startup(list);
            else if (id == "pagefile" || id == "fault50") Page(list, id == "fault50");
            else if (id == "maxmem") MaxMem(list);
            else if (id == "shaders") Disk(list);
            else if (id == "dns") Info(list, "dns", "Address cache", "Cleared when you apply");
            else if (id == "netreset") Info(list, "net", "Winsock and IP stack", "Reset when you apply");
            else if (id == "sfc" || id == "dismsfc") Info(list, "sfc", "System files", "Checked when you apply");
            else if (id == "dism") Info(list, "dism", "Windows image", "Repaired when you apply");
            else if (id == "chkdsk") Info(list, "disk", "Disk check", "Scheduled for the next restart");
            else if (id == "wureset") Info(list, "wu", "Update download cache", "Cleared when you apply");
            else if (id == "appinit") Info(list, "appinit", "AppInit_DLLs", AppInitNow());
            else if (id == "vcrepair") Info(list, "vc", "Visual C++ 2015-2022", "Repaired with the official installer");
            else if (id == "dotnet") Info(list, "dotnet", ".NET Framework 3.5 and 4.8", "Enabled when you apply");
            else if (id == "storage") Info(list, "storage", "Storage driver", "Windows Update only");
            return list;
        }

        public static string Summary(string id)
        {
            if (id == "tdr") return "timeout " + TdrNow("TdrDelay", 2) + "s, DDI " + TdrNow("TdrDdiDelay", 5) + "s";
            if (id == "hagsoff" || id == "hagson") return "scheduling " + HagsNow();
            if (id == "faststartup") return "Fast Startup " + (Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1) == 0 ? "off" : "on");
            if (id == "overlayoff") return "Game DVR " + OnOff(Dword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1));
            if (id == "sysmain") return "SysMain " + SysMainNow();
            if (id == "shaders") return FreeNow();
            if (id == "tweaks") return "Game DVR " + OnOff(Dword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1)) + ", plan " + PlanNow();
            if (id == "startuptrim") return StartupCount() + " updater(s) at sign-in";
            if (id == "browsers") return "Edge background " + OnOff(Dword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 1));
            if (id == "visualfx") return "animations " + (Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 0) == 3 ? "custom" : "Windows default");
            return "";
        }

        public static string Preview(string id)
        {
            List<TuneItem> items = For(id);
            if (items.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(items[i].Name);
                sb.Append(": ");
                sb.AppendLine(items[i].Current);
            }
            return sb.ToString().Trim();
        }

        public static string Diff(List<TuneItem> before, List<TuneItem> after)
        {
            if (before == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < before.Count; i++)
            {
                TuneItem item = before[i];
                if (item == null || !item.Apply) continue;
                string now = item.Current ?? "";
                if (after != null)
                {
                    for (int j = 0; j < after.Count; j++)
                    {
                        if (after[j] != null && after[j].Key == item.Key)
                        {
                            now = after[j].Current ?? "";
                            break;
                        }
                    }
                }
                sb.Append(item.Name);
                sb.Append(": ");
                if (now != item.Current) sb.Append(item.Current).Append(" → ").Append(now);
                else if (item.Editable && item.Selected != null && item.Selected != item.Current) sb.Append(item.Current).Append(" → ").Append(item.Selected);
                else sb.Append(item.Current);
                sb.AppendLine();
            }
            return sb.ToString().Trim();
        }

        public static RepairResult TryApply(string id, List<TuneItem> items)
        {
            if (items == null) return null;
            if (!Any(items)) return RepairResult.Success("Nothing was changed.");
            try
            {
                if (id == "tweaks") return ApplyTweaks(items);
                if (id == "tdr") return ApplyTdr(items);
                if (id == "hagsoff" || id == "hagson") return ApplyHags(items);
                if (id == "faststartup") return ApplyFast(items);
                if (id == "overlayoff") return ApplyOverlay(items);
                if (id == "browsers") return ApplyBrowsers(items);
                if (id == "visualfx") return ApplyVisual(items);
                if (id == "sysmain") return ApplySysMain(items);
                if (id == "startuptrim") return ApplyStartup(items);
                if (id == "pagefile") return null;
                if (id == "fault50") return ApplyFault(items);
                if (id == "maxmem") return null;
            }
            catch (Exception ex)
            {
                return RepairResult.Fail(ex.Message);
            }
            return null;
        }

        static void Tweaks(List<TuneItem> list)
        {
            Add(list, "dvr", "Game DVR recording", OnOff(Dword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1)), "Off", OffOn());
            Add(list, "capture", "App capture", OnOff(Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1)), "Off", OffOn());
            Add(list, "gamemode", "Game Mode", OnOff(Dword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1)), "On", OnOffChoices());
            Add(list, "panel", "Game Bar startup panel", OnOff(Dword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "ShowStartupPanel", 1)), "Off", OffOn());
            Add(list, "plan", "Power plan", PlanNow(), "High performance", new string[] { "High performance", "Keep current" });
            Add(list, "usb", "USB selective suspend", "On the active plan", "Off", new string[] { "Off", "On" });
            Add(list, "pcie", "PCI Express power saving", "On the active plan", "Off", new string[] { "Off", "On" });
        }

        static void Tdr(List<TuneItem> list)
        {
            Add(list, "delay", "GPU timeout", TdrNow("TdrDelay", 2) + " seconds", "8 seconds", new string[] { "2 seconds", "4 seconds", "8 seconds", "10 seconds" });
            Add(list, "ddi", "GPU driver timeout", TdrNow("TdrDdiDelay", 5) + " seconds", "10 seconds", new string[] { "5 seconds", "8 seconds", "10 seconds", "12 seconds" });
        }

        static void Hags(List<TuneItem> list, bool on)
        {
            Add(list, "hags", "GPU scheduling", HagsNow(), on ? "On" : "Off", new string[] { "Off", "On" });
        }

        static void Fast(List<TuneItem> list)
        {
            string now = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1) == 0 ? "Off" : "On";
            Add(list, "fast", "Fast Startup", now, "Off", new string[] { "Off", "On" });
        }

        static void Overlay(List<TuneItem> list)
        {
            Add(list, "dvr", "Game DVR recording", OnOff(Dword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1)), "Off", OffOn());
            Add(list, "capture", "App capture", OnOff(Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1)), "Off", OffOn());
        }

        static void Browsers(List<TuneItem> list)
        {
            Add(list, "edgeboost", "Edge startup boost", OnOff(Dword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 1)), "Off", OffOn());
            Add(list, "edgeback", "Edge background", OnOff(Dword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 1)), "Off", OffOn());
            Add(list, "edgesleep", "Edge sleeping tabs", OnOff(Dword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "SleepingTabsEnabled", 0)), "On", OnOffChoices());
            Add(list, "chromeback", "Chrome background", OnOff(Dword(Registry.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", 1)), "Off", OffOn());
            Add(list, "chromesaver", "Chrome memory saver", OnOff(Dword(Registry.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "HighEfficiencyModeEnabled", 0)), "On", OnOffChoices());
        }

        static void Visual(List<TuneItem> list)
        {
            int fx = Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 0);
            Add(list, "anim", "Window animations", fx == 3 ? "Custom" : "Windows default", "Off", new string[] { "Off", "On" });
            Add(list, "font", "Font smoothing", DwordText(Registry.CurrentUser, @"Control Panel\Desktop", "FontSmoothing") == "2" ? "On" : "Off", "On", OnOffChoices());
            Add(list, "drag", "Show window contents while dragging", DwordText(Registry.CurrentUser, @"Control Panel\Desktop", "DragFullWindows") == "0" ? "Off" : "On", "Off", OffOn());
            Add(list, "thumbs", "File thumbnails", Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 0) == 0 ? "On" : "Off", "On", OnOffChoices());
        }

        static void SysMain(List<TuneItem> list)
        {
            Add(list, "sysmain", "SysMain service", SysMainNow(), "Disabled", new string[] { "Disabled", "Manual", "Automatic" });
        }

        static void Startup(List<TuneItem> list)
        {
            List<string> names = StartupNames();
            if (names.Count == 0)
            {
                Info(list, "startup", "Sign-in programs", "No third-party updater is set to start");
                return;
            }
            for (int i = 0; i < names.Count; i++)
                Add(list, "run:" + names[i], names[i], "Starts at sign-in", "Off at sign-in", new string[] { "Off at sign-in", "Leave on" });
        }

        static void Page(List<TuneItem> list, bool withDisk)
        {
            Info(list, "page", "Page file", "Stays on. Windows chooses the size. A fixed size is not offered.");
            if (withDisk) Info(list, "disk", "Disk check", "Scheduled for the next restart when ticked");
        }

        static void MaxMem(List<TuneItem> list)
        {
            Info(list, "maxmem", "Boot memory limit", "Removed when it is set. A new limit is not offered.");
        }

        static void Disk(List<TuneItem> list)
        {
            Info(list, "disk", "Free space", FreeNow());
        }

        static void Info(List<TuneItem> list, string key, string name, string current)
        {
            TuneItem item = new TuneItem();
            item.Key = key;
            item.Name = name;
            item.Current = current;
            item.Selected = current;
            item.Apply = true;
            item.Editable = false;
            item.Choices = new string[0];
            list.Add(item);
        }

        static void Add(List<TuneItem> list, string key, string name, string current, string selected, string[] choices)
        {
            TuneItem item = new TuneItem();
            item.Key = key;
            item.Name = name;
            item.Current = current ?? "";
            item.Selected = selected ?? "";
            item.Apply = true;
            item.Editable = true;
            item.Choices = choices ?? new string[0];
            list.Add(item);
        }

        static RepairResult ApplyTweaks(List<TuneItem> items)
        {
            StringBuilder sb = new StringBuilder();
            if (Want(items, "dvr")) DwordSet(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", Bit(Pick(items, "dvr"), true));
            if (Want(items, "capture")) DwordSet(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", Bit(Pick(items, "capture"), true));
            if (Want(items, "gamemode"))
            {
                int on = Bit(Pick(items, "gamemode"), false);
                DwordSet(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", on);
                DwordSet(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", on);
            }
            if (Want(items, "panel"))
            {
                int off = Bit(Pick(items, "panel"), true);
                DwordSet(Registry.CurrentUser, @"Software\Microsoft\GameBar", "ShowStartupPanel", off);
                DwordSet(Registry.CurrentUser, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", off);
            }
            sb.Append("Game settings were written. ");
            if (Want(items, "plan") && Pick(items, "plan") == "High performance")
            {
                string message;
                if (!PowerPlans.SetActive(PowerPlans.HighPerformance, out message))
                {
                    string body = "& \"$env:SystemRoot\\System32\\powercfg.exe\" /setactive " + PowerPlans.HighPerformance + "\r\n"
                        + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nHigh performance is on.\")\r\n";
                    RepairResult elevated = Command.RunElevated(body, 90000, true);
                    if (!elevated.Ok) return elevated;
                    sb.Append("High performance is on. ");
                }
                else sb.Append(message).Append(" ");
            }
            if (Want(items, "usb") || Want(items, "pcie"))
            {
                int usb = Want(items, "usb") && Pick(items, "usb") == "On" ? 1 : 0;
                int pcie = Want(items, "pcie") && Pick(items, "pcie") == "On" ? 1 : 0;
                if (!Want(items, "usb")) usb = -1;
                if (!Want(items, "pcie")) pcie = -1;
                RepairResult power = PowerValues(usb, pcie);
                if (!power.Ok) return power;
                sb.Append("Power saving values were written. ");
            }
            return RepairResult.Success(sb.ToString().Trim());
        }

        static RepairResult PowerValues(int usb, int pcie)
        {
            StringBuilder body = new StringBuilder();
            if (usb == 0 || usb == 1)
            {
                body.Append("& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETACVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 ").Append(usb).Append("\r\n");
                body.Append("& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETDCVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 ").Append(usb).Append("\r\n");
            }
            if (pcie == 0 || pcie == 1)
            {
                body.Append("& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETACVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 ").Append(pcie).Append("\r\n");
                body.Append("& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETDCVALUEINDEX SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 ").Append(pcie).Append("\r\n");
            }
            body.Append("& \"$env:SystemRoot\\System32\\powercfg.exe\" /SETACTIVE SCHEME_CURRENT\r\n");
            body.Append("[System.IO.File]::WriteAllText($resultFile, \"OK`r`nPower values were written.\")\r\n");
            return Command.RunElevated(body.ToString(), 90000, true);
        }

        static RepairResult ApplyTdr(List<TuneItem> items)
        {
            int delay = Allow(Seconds(Pick(items, "delay"), 8), new int[] { 2, 4, 8, 10 }, 8);
            int ddi = Allow(Seconds(Pick(items, "ddi"), 10), new int[] { 5, 8, 10, 12 }, 10);
            StringBuilder body = new StringBuilder();
            body.Append("$path = 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers'\r\n");
            body.Append("if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }\r\n");
            if (Want(items, "delay")) body.Append("New-ItemProperty -Path $path -Name TdrDelay -PropertyType DWord -Value ").Append(delay).Append(" -Force | Out-Null\r\n");
            if (Want(items, "ddi")) body.Append("New-ItemProperty -Path $path -Name TdrDdiDelay -PropertyType DWord -Value ").Append(ddi).Append(" -Force | Out-Null\r\n");
            body.Append("[System.IO.File]::WriteAllText($resultFile, \"OK`r`nGPU timeout values were written. Restart when you can.\")\r\n");
            RepairResult result = Command.RunElevated(body.ToString(), 90000, true);
            result.Reboot = result.Ok;
            return result;
        }

        static RepairResult ApplyHags(List<TuneItem> items)
        {
            int mode = Pick(items, "hags") == "On" ? 2 : 1;
            string body = "$path = 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers'\r\n"
                + "if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }\r\n"
                + "New-ItemProperty -Path $path -Name HwSchMode -PropertyType DWord -Value " + mode.ToString() + " -Force | Out-Null\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nGPU scheduling was written. Restart when you can.\")\r\n";
            RepairResult result = Command.RunElevated(body, 90000, true);
            result.Reboot = result.Ok;
            return result;
        }

        static RepairResult ApplyFast(List<TuneItem> items)
        {
            int value = Pick(items, "fast") == "On" ? 1 : 0;
            string body = "$path = 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power'\r\n"
                + "if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }\r\n"
                + "New-ItemProperty -Path $path -Name HiberbootEnabled -PropertyType DWord -Value " + value.ToString() + " -Force | Out-Null\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nFast Startup was written. Restart when you can.\")\r\n";
            RepairResult result = Command.RunElevated(body, 90000, true);
            result.Reboot = result.Ok;
            return result;
        }

        static RepairResult ApplyOverlay(List<TuneItem> items)
        {
            if (Want(items, "dvr")) DwordSet(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", Bit(Pick(items, "dvr"), true));
            if (Want(items, "capture")) DwordSet(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", Bit(Pick(items, "capture"), true));
            return RepairResult.Success("Overlay settings were written.");
        }

        static RepairResult ApplyBrowsers(List<TuneItem> items)
        {
            StringBuilder body = new StringBuilder();
            body.Append("function Set-Pol([string]$path, [string]$name, [int]$value) {\r\n");
            body.Append("  if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }\r\n");
            body.Append("  New-ItemProperty -Path $path -Name $name -PropertyType DWord -Value $value -Force | Out-Null\r\n}\r\n");
            Pol(body, items, "edgeboost", @"HKLM:\SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", true);
            Pol(body, items, "edgeback", @"HKLM:\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", true);
            Pol(body, items, "edgesleep", @"HKLM:\SOFTWARE\Policies\Microsoft\Edge", "SleepingTabsEnabled", false);
            Pol(body, items, "chromeback", @"HKLM:\SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", true);
            Pol(body, items, "chromesaver", @"HKLM:\SOFTWARE\Policies\Google\Chrome", "HighEfficiencyModeEnabled", false);
            body.Append("[System.IO.File]::WriteAllText($resultFile, \"OK`r`nBrowser settings were written. Close Edge and Chrome once so they read the change.\")\r\n");
            return Command.RunElevated(body.ToString(), 90000, true);
        }

        static void Pol(StringBuilder body, List<TuneItem> items, string key, string path, string name, bool offIsZero)
        {
            if (!Want(items, key)) return;
            int value = Bit(Pick(items, key), offIsZero);
            body.Append("Set-Pol '").Append(path).Append("' '").Append(name).Append("' ").Append(value).Append("\r\n");
        }

        static RepairResult ApplyVisual(List<TuneItem> items)
        {
            if (Want(items, "anim"))
            {
                uint on = Pick(items, "anim") == "On" ? 1u : 0u;
                NativeMethods.SystemParametersInfo(0x103F, on, IntPtr.Zero, 3);
            }
            if (Want(items, "font"))
            {
                uint on = Pick(items, "font") == "Off" ? 0u : 1u;
                NativeMethods.SystemParametersInfo(0x004B, on, IntPtr.Zero, 3);
                TextSet(Registry.CurrentUser, @"Control Panel\Desktop", "FontSmoothing", on == 1 ? "2" : "0");
            }
            if (Want(items, "drag")) TextSet(Registry.CurrentUser, @"Control Panel\Desktop", "DragFullWindows", Pick(items, "drag") == "Off" ? "0" : "1");
            if (Want(items, "thumbs")) DwordSet(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", Pick(items, "thumbs") == "Off" ? 1 : 0);
            DwordSet(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3);
            return RepairResult.Success("Visual settings were written.");
        }

        static RepairResult ApplySysMain(List<TuneItem> items)
        {
            string mode = Pick(items, "sysmain");
            if (mode != "Disabled" && mode != "Manual" && mode != "Automatic") mode = "Disabled";
            string body = "Set-Service -Name SysMain -StartupType " + mode + "\r\n";
            if (mode == "Disabled") body += "Stop-Service -Name SysMain -Force -ErrorAction SilentlyContinue\r\n";
            body += "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nSysMain is " + mode + ". Open programs were not ended.\")\r\n";
            return Command.RunElevated(body, 90000, true);
        }

        static RepairResult ApplyStartup(List<TuneItem> items)
        {
            List<string> allowed = StartupNames();
            List<string> done = new List<string>();
            using (RegistryKey approved = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
            {
                if (approved == null) return RepairResult.Fail("The startup list could not be opened.");
                for (int i = 0; i < items.Count; i++)
                {
                    TuneItem item = items[i];
                    if (item == null || !item.Apply || item.Key.IndexOf("run:", StringComparison.Ordinal) != 0) continue;
                    if (item.Selected != "Off at sign-in") continue;
                    string name = item.Key.Substring(4);
                    if (!Listed(allowed, name)) continue;
                    byte[] data = approved.GetValue(name) as byte[];
                    if (data == null || data.Length == 0) data = new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                    else data[0] = 3;
                    approved.SetValue(name, data, RegistryValueKind.Binary);
                    done.Add(name);
                }
            }
            if (done.Count == 0) return RepairResult.Success("No sign-in program was changed.");
            return RepairResult.Success("Off at the next sign-in: " + string.Join(", ", done.ToArray()) + ".");
        }

        static RepairResult ApplyFault(List<TuneItem> items)
        {
            bool page = Want(items, "page");
            bool disk = Want(items, "disk");
            if (page && disk) return null;
            if (page) return Repairs.Run("pagefile");
            if (disk) return Repairs.Run("chkdsk");
            return RepairResult.Success("Nothing was changed.");
        }

        static bool Listed(List<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool Any(List<TuneItem> items)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].Apply) return true;
            return false;
        }

        static bool Want(List<TuneItem> items, string key)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].Key == key && items[i].Apply) return true;
            return false;
        }

        static string Pick(List<TuneItem> items, string key)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].Key == key) return items[i].Selected ?? "";
            return "";
        }

        static int Bit(string label, bool offIsZero)
        {
            bool off = label == "Off";
            if (offIsZero) return off ? 0 : 1;
            return off ? 0 : 1;
        }

        static int Seconds(string text, int fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            int i = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
            int n;
            if (i == 0 || !int.TryParse(text.Substring(0, i), out n)) return fallback;
            return n;
        }

        static int Allow(int value, int[] allowed, int fallback)
        {
            for (int i = 0; i < allowed.Length; i++)
                if (allowed[i] == value) return value;
            return fallback;
        }

        static string[] OffOn()
        {
            return new string[] { "Off", "On" };
        }

        static string[] OnOffChoices()
        {
            return new string[] { "On", "Off" };
        }

        static string OnOff(int value)
        {
            return value == 0 ? "Off" : "On";
        }

        static int Dword(RegistryKey root, string path, string name, int missing)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(path))
                {
                    if (key == null) return missing;
                    object value = key.GetValue(name);
                    if (value == null) return missing;
                    return Convert.ToInt32(value);
                }
            }
            catch { return missing; }
        }

        static string DwordText(RegistryKey root, string path, string name)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(path))
                {
                    if (key == null) return "";
                    object value = key.GetValue(name);
                    return value == null ? "" : Convert.ToString(value);
                }
            }
            catch { return ""; }
        }

        static void DwordSet(RegistryKey root, string path, string name, int value)
        {
            using (RegistryKey key = root.CreateSubKey(path))
            {
                if (key != null) key.SetValue(name, value, RegistryValueKind.DWord);
            }
        }

        static void TextSet(RegistryKey root, string path, string name, string value)
        {
            using (RegistryKey key = root.CreateSubKey(path))
            {
                if (key != null) key.SetValue(name, value, RegistryValueKind.String);
            }
        }

        static int TdrNow(string name, int missing)
        {
            return Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", name, missing);
        }

        static string HagsNow()
        {
            int mode = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1);
            if (mode == 2) return "On";
            if (mode == 1) return "Off";
            return "Windows default";
        }

        static string SysMainNow()
        {
            int start = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 2);
            if (start == 4) return "Disabled";
            if (start == 3) return "Manual";
            return "Automatic";
        }

        static string PlanNow()
        {
            return PowerPlans.Name(PowerPlans.ActiveGuid());
        }

        static string FreeNow()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                DriveInfo drive = new DriveInfo(root);
                double gb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                return gb.ToString("0.0") + " GB free";
            }
            catch { return "Free space"; }
        }

        static string AppInitNow()
        {
            string value = DwordText(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", "AppInit_DLLs");
            if (string.IsNullOrEmpty(value)) value = DwordText(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows", "AppInit_DLLs");
            return string.IsNullOrEmpty(value) ? "Empty" : value;
        }

        static int StartupCount()
        {
            return StartupNames().Count;
        }

        static List<string> StartupNames()
        {
            List<string> names = new List<string>();
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (run == null) return names;
                    string[] found = run.GetValueNames();
                    for (int i = 0; i < found.Length; i++)
                    {
                        string command = run.GetValue(found[i]) as string;
                        if (SystemProbe.IsStartupUpdater(found[i], command)) names.Add(found[i]);
                    }
                }
            }
            catch { }
            return names;
        }
    }

    sealed class TuneDialog : Form
    {
        readonly List<TuneItem> _items;
        readonly List<CheckBox> _checks = new List<CheckBox>();
        readonly List<Label> _nows = new List<Label>();
        readonly List<ComboBox> _values = new List<ComboBox>();
        Panel _list;

        TuneDialog(string title, string detail, List<TuneItem> items)
        {
            _items = items;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontUi;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(720, 560);

            Label intro = new Label();
            intro.Text = detail;
            intro.Dock = DockStyle.Top;
            intro.AutoSize = false;
            intro.AutoEllipsis = false;
            intro.Padding = new Padding(16, 12, 16, 8);
            intro.ForeColor = Theme.Muted;
            intro.BackColor = Theme.Bg;
            intro.Height = TextBlock.Height(detail, Theme.FontUi, 680, 36) + 28;

            Panel buttons = new Panel();
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = 56;
            buttons.BackColor = Theme.Card;
            Button cancel = Make("Cancel", false);
            Button apply = Make("Apply", true);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            apply.Click += delegate { OnApply(); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(apply);
            buttons.Resize += delegate
            {
                apply.SetBounds(buttons.Width - 120, 12, 104, 32);
                cancel.SetBounds(buttons.Width - 232, 12, 104, 32);
            };

            _list = new Panel();
            _list.Dock = DockStyle.Fill;
            _list.AutoScroll = true;
            _list.BackColor = Theme.Bg;
            for (int i = 0; i < items.Count; i++)
            {
                TuneItem item = items[i];
                CheckBox box = new CheckBox();
                box.Text = item.Name;
                box.Checked = item.Apply;
                box.ForeColor = Theme.Text;
                box.BackColor = Theme.Card;
                box.AutoSize = true;
                box.UseMnemonic = false;
                Label now = new Label();
                now.Text = "Now: " + item.Current;
                now.ForeColor = Theme.Muted;
                now.BackColor = Theme.Bg;
                now.AutoSize = false;
                now.AutoEllipsis = false;
                now.UseMnemonic = false;
                now.Font = Theme.FontSmall;
                ComboBox combo = new ComboBox();
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                combo.FlatStyle = FlatStyle.Flat;
                combo.Enabled = item.Editable;
                int widest = 160;
                if (item.Choices != null)
                {
                    for (int c = 0; c < item.Choices.Length; c++)
                    {
                        combo.Items.Add(item.Choices[c]);
                        int choiceW = TextRenderer.MeasureText(item.Choices[c], Theme.FontUi).Width + 32;
                        if (choiceW > widest) widest = choiceW;
                    }
                }
                int pick = combo.Items.IndexOf(item.Selected);
                if (pick >= 0) combo.SelectedIndex = pick;
                else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                combo.Tag = widest;
                if (!item.Editable) combo.Visible = false;
                _list.Controls.Add(box);
                _list.Controls.Add(now);
                _list.Controls.Add(combo);
                _checks.Add(box);
                _nows.Add(now);
                _values.Add(combo);
            }
            _list.Resize += delegate { LayoutTune(); };

            Controls.Add(_list);
            Controls.Add(buttons);
            Controls.Add(intro);
            CancelButton = cancel;
            AcceptButton = apply;
            Load += delegate { LayoutTune(); };
        }

        void LayoutTune()
        {
            if (_list == null) return;
            int inner = _list.ClientSize.Width - 36;
            if (inner < 220) inner = 220;
            int y = 8;
            for (int i = 0; i < _checks.Count; i++)
            {
                CheckBox box = _checks[i];
                box.MaximumSize = new Size(inner, 0);
                Size pref = box.GetPreferredSize(new Size(inner, 0));
                int boxH = pref.Height;
                if (boxH < 24) boxH = 24;
                box.SetBounds(12, y, inner, boxH);
                y += boxH + 4;
                Label now = _nows[i];
                int nowH = TextBlock.Height(now.Text, now.Font, inner - 16, 18);
                now.SetBounds(28, y, inner - 16, nowH);
                y += nowH + 6;
                ComboBox combo = _values[i];
                if (combo.Visible)
                {
                    int want = 220;
                    if (combo.Tag is int) want = (int)combo.Tag;
                    int cw = want;
                    if (cw > inner - 16) cw = inner - 16;
                    if (cw < 160) cw = Math.Min(160, inner - 16);
                    combo.SetBounds(28, y, cw, 28);
                    int drop = want;
                    if (drop < cw) drop = cw;
                    combo.DropDownWidth = drop;
                    y += 36;
                }
                y += 12;
            }
            _list.AutoScrollMinSize = new Size(0, y + 8);
        }

        void OnApply()
        {
            bool any = false;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].Apply = _checks[i].Checked;
                if (_values[i].SelectedItem != null) _items[i].Selected = Convert.ToString(_values[i].SelectedItem);
                if (_items[i].Apply) any = true;
            }
            if (!any)
            {
                MessageBox.Show(this, "Tick at least one line to change.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        static Button Make(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? Theme.Accent : Theme.Card2;
            button.ForeColor = primary ? Theme.OnAccent : Theme.Text;
            button.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            return button;
        }

        public static bool Ask(IWin32Window owner, string title, string detail, List<TuneItem> items)
        {
            using (TuneDialog dialog = new TuneDialog(title, detail, items))
                return dialog.ShowDialog(owner) == DialogResult.OK;
        }
    }
}
