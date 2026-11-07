using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WinHealthGuard
{
    static class Repairs
    {
        public static RepairResult Run(string id)
        {
            return Run(id, null);
        }

        public static RepairResult Run(string id, List<TuneItem> items)
        {
            if (items != null)
            {
                RepairResult custom = TuneCatalog.TryApply(id, items);
                if (custom != null) return custom;
            }
            RepairResult result;
            switch (id)
            {
                case "tweaks": result = Tweaks(); break;
                case "shaders": result = ClearCaches(); break;
                case "dns": result = FlushDns(); break;
                case "tdr": result = Tdr(); break;
                case "hagsoff": result = Hags(1, "Hardware-accelerated GPU scheduling is off. Restart Windows when you can."); break;
                case "hagson": result = Hags(2, "Hardware-accelerated GPU scheduling is on. Restart Windows when you can."); break;
                case "pagefile": result = PageFile(); break;
                case "browsers": result = Browsers(); break;
                case "visualfx": result = VisualFx(); break;
                case "sysmain": result = SysMainOff(); break;
                case "startuptrim": result = StartupTrim(); break;
                case "maxmem": result = MaxMem(); break;
                case "faststartup": result = FastStartup(); break;
                case "netreset": result = NetworkReset(); break;
                case "sfc": result = Sfc(); break;
                case "dism": result = Dism(); break;
                case "memory": result = OpenExe(Path.Combine(Environment.SystemDirectory, "mdsched.exe"), "Windows Memory Diagnostic is open. It runs on the next restart. Save your work first."); break;
                case "wu": result = OpenExe("ms-settings:windowsupdate", "Windows Update is open."); break;
                case "gaming": result = OpenExe("ms-settings:gaming-gamemode", "Game Mode settings are open."); break;
                case "amd": result = OpenExe(FixCatalog.AmdExe, "AMD Software is open. Turn off the performance overlay, metrics, and instant replay."); break;
                case "nvidia": result = OpenExe(FixCatalog.NvidiaExe, "NVIDIA software is open. Turn off the in-game overlay and instant replay."); break;
                case "overlayoff": result = OverlayOff(); break;
                case "dismsfc": result = DismThenSfc(); break;
                case "fault50": result = Fault50(); break;
                case "chkdsk": result = Chkdsk(); break;
                case "vcrepair": result = VcRepair(); break;
                case "wureset": result = WuReset(); break;
                case "dotnet": result = DotNet(); break;
                case "appinit": result = AppInit(); break;
                case "storage": result = Storage(); break;
                default: result = RepairResult.Fail("That repair is not available."); break;
            }
            AppPaths.Audit((id ?? "?") + "  " + (result.Ok ? "ok" : result.Cancelled ? "cancelled" : "fail") + "  " + TextUtil.OneLine(result.Message));
            return result;
        }

        public static RepairResult VerifySteamGame(string appPath)
        {
            string appId;
            string name;
            if (!TrySteamApp(appPath, out appId, out name))
                return RepairResult.Fail("Steam cannot verify this program. It is not an installed Steam game.");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "steam://validate/" + appId;
                psi.UseShellExecute = true;
                Process.Start(psi);
                RepairResult ok = RepairResult.Success("Steam is checking the files for " + name + ".");
                AppPaths.Audit("steam verify " + appId + " " + name);
                return ok;
            }
            catch (Exception ex)
            {
                return RepairResult.Fail(ex.Message);
            }
        }

        static bool TrySteamApp(string appPath, out string appId, out string name)
        {
            appId = "";
            name = "";
            if (!CrashParser.IsSteamPath(appPath)) return false;
            const string marker = @"\steamapps\common\";
            int common = appPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (common < 0) return false;
            string rest = appPath.Substring(common + marker.Length);
            int slash = rest.IndexOf('\\');
            if (slash <= 0) return false;
            string folder = rest.Substring(0, slash);
            string steamapps = appPath.Substring(0, common) + @"\steamapps";
            if (!Directory.Exists(steamapps)) return false;
            string[] files;
            try { files = Directory.GetFiles(steamapps, "appmanifest_*.acf"); }
            catch { return false; }
            for (int i = 0; i < files.Length; i++)
            {
                string text;
                try { text = File.ReadAllText(files[i]); }
                catch { continue; }
                string install = QuotedValue(text, "installdir");
                if (!install.Equals(folder, StringComparison.OrdinalIgnoreCase)) continue;
                appId = QuotedValue(text, "appid");
                if (appId.Length == 0)
                {
                    string file = Path.GetFileNameWithoutExtension(files[i]);
                    if (file.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase))
                        appId = file.Substring("appmanifest_".Length);
                }
                name = QuotedValue(text, "name");
                if (name.Length == 0) name = folder;
                return appId.Length > 0;
            }
            return false;
        }

        static string QuotedValue(string text, string key)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key)) return "";
            string token = "\"" + key + "\"";
            int at = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return "";
            int quote = text.IndexOf('"', at + token.Length);
            if (quote < 0) return "";
            int end = text.IndexOf('"', quote + 1);
            if (end < 0) return "";
            return text.Substring(quote + 1, end - quote - 1);
        }

        public static RepairResult DisableFullscreenOptimizations(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return RepairResult.Fail("That program file was not found.");
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"))
                {
                    string current = key.GetValue(exePath) as string;
                    const string flag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";
                    if (string.IsNullOrEmpty(current)) current = "~ " + flag;
                    else
                    {
                        if (current.IndexOf("~", StringComparison.Ordinal) < 0) current = "~ " + current;
                        if (current.IndexOf(flag, StringComparison.OrdinalIgnoreCase) < 0) current = current.Trim() + " " + flag;
                    }
                    key.SetValue(exePath, current, RegistryValueKind.String);
                }
                RepairResult ok = RepairResult.Success("Fullscreen optimizations are off for " + exePath + ". Start the game again so it applies.");
                AppPaths.Audit("fullscreen ok " + exePath);
                return ok;
            }
            catch (Exception ex)
            {
                return RepairResult.Fail(ex.Message);
            }
        }

        static RepairResult Tweaks()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool ok = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                    key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR"))
                    key.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar"))
                {
                    key.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                    key.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                    key.SetValue("ShowStartupPanel", 0, RegistryValueKind.DWord);
                    key.SetValue("UseNexusForGameBarEnabled", 0, RegistryValueKind.DWord);
                }
                sb.AppendLine("Game DVR recording is off. Game Mode is on.");
            }
            catch (Exception ex)
            {
                ok = false;
                sb.AppendLine("Could not change Game DVR: " + ex.Message);
            }

            if (!PowerPlans.Exists(PowerPlans.HighPerformance))
            {
                ok = false;
                sb.AppendLine("The High performance plan is not installed on this PC.");
            }
            else
            {
                string powerMessage;
                bool powerOk = PowerPlans.SetActive(PowerPlans.HighPerformance, out powerMessage);
                sb.AppendLine(powerMessage);
                string tuneMessage = "";
                bool tuneOk = false;
                if (powerOk) tuneOk = PowerPlans.TuneCurrentScheme(out tuneMessage);
                if (tuneOk) sb.AppendLine(tuneMessage);
                else if (powerOk && tuneMessage.Length > 0) sb.AppendLine(tuneMessage);
                if (!powerOk || !tuneOk)
                {
                    string body = powerOk ? PowerPlans.TuneScript() : PowerPlans.ActivateAndTuneScript();
                    RepairResult elevated = Command.RunElevated(body, 90000, true);
                    if (elevated.Ok)
                        sb.AppendLine(powerOk ? "USB and PCI Express power saving are off." : "High performance, USB selective suspend, and PCI Express power saving were applied.");
                    else
                    {
                        ok = false;
                        sb.AppendLine(elevated.Message);
                    }
                }
            }

            RepairResult result = new RepairResult();
            result.Ok = ok;
            result.Message = sb.ToString().Trim();
            return result;
        }

        static RepairResult ClearCaches()
        {
            int files = 0;
            long freed = 0;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] caches = new string[] {
                Path.Combine(local, "D3DSCache"),
                Path.Combine(local, "NVIDIA", "DXCache"),
                Path.Combine(local, "NVIDIA", "GLCache"),
                Path.Combine(local, "NVIDIA Corporation", "NV_Cache"),
                Path.Combine(local, "AMD", "DxCache"),
                Path.Combine(local, "AMD", "DxcCache"),
                Path.Combine(local, "AMD", "GLCache"),
                Path.Combine(local, "AMD", "Dx9Cache")
            };
            for (int i = 0; i < caches.Length; i++) freed += DeleteTree(caches[i], false, ref files);
            freed += DeleteTree(Path.GetTempPath(), true, ref files);
            return RepairResult.Success("Removed " + files.ToString() + " files (" + TextUtil.SizeText(freed) + "). Shader caches were cleared. Temp files older than 48 hours were removed. Steam game files were left alone.");
        }

        static long DeleteTree(string root, bool oldOnly, ref int files)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return 0;
            long freed = 0;
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] subs = null;
                string[] found = null;
                try { subs = Directory.GetDirectories(dir); } catch { }
                try { found = Directory.GetFiles(dir); } catch { }
                if (subs != null)
                {
                    for (int i = 0; i < subs.Length; i++) stack.Push(subs[i]);
                }
                if (found == null) continue;
                for (int i = 0; i < found.Length; i++)
                {
                    try
                    {
                        if (found[i].IndexOf(@"\WinHealthGuard\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        FileInfo info = new FileInfo(found[i]);
                        if (oldOnly && info.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-48)) continue;
                        long len = info.Length;
                        if (info.IsReadOnly) info.IsReadOnly = false;
                        info.Delete();
                        freed += len;
                        files++;
                    }
                    catch
                    {
                    }
                }
            }
            return freed;
        }

        static RepairResult FlushDns()
        {
            string output;
            int code = Command.Run(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns", out output, 15000);
            if (code == 0) return RepairResult.Success("DNS cache flushed.");
            return RepairResult.Fail(TextUtil.OneLine(output).Length == 0 ? "ipconfig /flushdns failed." : TextUtil.OneLine(output));
        }

        static RepairResult Tdr()
        {
            string body = "$path = 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers'\r\n"
                + "if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }\r\n"
                + "New-ItemProperty -Path $path -Name TdrDelay -PropertyType DWord -Value 8 -Force | Out-Null\r\n"
                + "New-ItemProperty -Path $path -Name TdrDdiDelay -PropertyType DWord -Value 10 -Force | Out-Null\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nGPU timeout is now 8 seconds (Windows default is 2). Restart when you can. This gives the driver longer to recover instead of resetting the display.\")\r\n";
            RepairResult result = Command.RunElevated(body, 90000, true);
            result.Reboot = result.Ok;
            return result;
        }

        static RepairResult Hags(int mode, string message)
        {
            string body = "$path = 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers'\r\n"
                + "if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }\r\n"
                + "New-ItemProperty -Path $path -Name HwSchMode -PropertyType DWord -Value " + mode.ToString() + " -Force | Out-Null\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`n" + message + "\")\r\n";
            RepairResult result = Command.RunElevated(body, 90000, true);
            result.Reboot = result.Ok;
            return result;
        }

        static RepairResult PageFile()
        {
            string body = "Get-CimInstance -ClassName Win32_PageFileSetting -ErrorAction SilentlyContinue | Remove-CimInstance -ErrorAction SilentlyContinue\r\n"
                + "$cs = Get-CimInstance -ClassName Win32_ComputerSystem\r\n"
                + "$cs.AutomaticManagedPagefile = $true\r\n"
                + "Set-CimInstance -InputObject $cs\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nThe page file stays on and Windows chooses its size. It is not turned off and not locked to a fixed size. Restart when you can.\")\r\n";
            RepairResult result = Command.RunElevated(body, 120000, true);
            result.Reboot = result.Ok;
            return result;
        }

        static RepairResult Browsers()
        {
            string body = "function Set-Pol([string]$path, [string]$name, [int]$value) {\r\n"
                + "  if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }\r\n"
                + "  New-ItemProperty -Path $path -Name $name -PropertyType DWord -Value $value -Force | Out-Null\r\n"
                + "}\r\n"
                + "Set-Pol 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Edge' 'StartupBoostEnabled' 0\r\n"
                + "Set-Pol 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Edge' 'BackgroundModeEnabled' 0\r\n"
                + "Set-Pol 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Edge' 'SleepingTabsEnabled' 1\r\n"
                + "Set-Pol 'HKLM:\\SOFTWARE\\Policies\\Google\\Chrome' 'BackgroundModeEnabled' 0\r\n"
                + "Set-Pol 'HKLM:\\SOFTWARE\\Policies\\Google\\Chrome' 'HighEfficiencyModeEnabled' 1\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nEdge and Chrome will exit when you close them, and Memory Saver is on. Close the browser once so it reads the change. Open programs were not ended.\")\r\n";
            return Command.RunElevated(body, 90000, true);
        }

        static RepairResult VisualFx()
        {
            try
            {
                NativeMethods.SystemParametersInfo(0x103F, 0, IntPtr.Zero, 3);
                NativeMethods.SystemParametersInfo(0x004B, 1, IntPtr.Zero, 3);
                using (RegistryKey desktop = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
                {
                    if (desktop != null)
                    {
                        desktop.SetValue("FontSmoothing", "2", RegistryValueKind.String);
                        desktop.SetValue("DragFullWindows", "0", RegistryValueKind.String);
                    }
                }
                using (RegistryKey advanced = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    if (advanced != null) advanced.SetValue("IconsOnly", 0, RegistryValueKind.DWord);
                }
                using (RegistryKey effects = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"))
                {
                    if (effects != null) effects.SetValue("VisualFXSetting", 3, RegistryValueKind.DWord);
                }
                return RepairResult.Success("Most window animations are off. Font smoothing and file thumbnails stay on. Open programs were not ended.");
            }
            catch (Exception ex)
            {
                return RepairResult.Fail(ex.Message);
            }
        }

        static RepairResult SysMainOff()
        {
            string body = "Stop-Service -Name SysMain -Force -ErrorAction SilentlyContinue\r\n"
                + "Set-Service -Name SysMain -StartupType Disabled\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nSysMain is stopped and will not start again. Windows will not preload apps into cached memory. Open programs were not ended. Turn the service back on in services.msc if you want that cache.\")\r\n";
            return Command.RunElevated(body, 90000, true);
        }

        static RepairResult StartupTrim()
        {
            List<string> disabled = new List<string>();
            TrimRun(Registry.CurrentUser, disabled);
            if (disabled.Count == 0)
                return RepairResult.Success("No third-party updater or launcher was set to start with this account. Windows and security tools were left on. Open programs were not ended.");
            return RepairResult.Success("These will not start at the next sign-in: " + string.Join(", ", disabled.ToArray()) + ". Open programs were not ended.");
        }

        static void TrimRun(RegistryKey root, List<string> disabled)
        {
            try
            {
                using (RegistryKey run = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                using (RegistryKey approved = root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
                {
                    if (run == null || approved == null) return;
                    string[] names = run.GetValueNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        string command = run.GetValue(names[i]) as string;
                        if (!SystemProbe.IsStartupUpdater(names[i], command)) continue;
                        byte[] data = approved.GetValue(names[i]) as byte[];
                        if (data == null || data.Length == 0) data = new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                        else data[0] = 3;
                        approved.SetValue(names[i], data, RegistryValueKind.Binary);
                        disabled.Add(names[i]);
                    }
                }
            }
            catch { }
        }

        static RepairResult MaxMem()
        {
            string body = "$out = & \"$env:SystemRoot\\System32\\bcdedit.exe\" /enum '{current}'\r\n"
                + "if ($out -notmatch 'truncatememory') { [System.IO.File]::WriteAllText($resultFile, \"OK`r`nWindows is not limiting how much RAM it uses. BIOS video memory was not changed.\"); return }\r\n"
                + "& \"$env:SystemRoot\\System32\\bcdedit.exe\" /deletevalue '{current}' truncatememory\r\n"
                + "if ($LASTEXITCODE -ne 0) { throw \"The boot memory limit could not be removed ($LASTEXITCODE).\" }\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nThe Windows boot memory limit is removed. BIOS video memory was not changed. Restart Windows when you can.\")\r\n";
            RepairResult result = Command.RunElevated(body, 90000, true);
            result.Reboot = result.Ok && (result.Message ?? "").IndexOf("not limiting", StringComparison.Ordinal) < 0;
            return result;
        }

        static RepairResult FastStartup()
        {
            string body = "$path = 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power'\r\n"
                + "if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }\r\n"
                + "$current = 1\r\n"
                + "try { $current = [int](Get-ItemProperty -Path $path -Name HiberbootEnabled -ErrorAction Stop).HiberbootEnabled } catch { $current = 1 }\r\n"
                + "if ($current -eq 0) { [System.IO.File]::WriteAllText($resultFile, \"OK`r`nFast Startup is already off.\"); return }\r\n"
                + "New-ItemProperty -Path $path -Name HiberbootEnabled -PropertyType DWord -Value 0 -Force | Out-Null\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nFast Startup is off. Restart Windows when you can.\")\r\n";
            RepairResult result = Command.RunElevated(body, 90000, true);
            result.Reboot = result.Ok && (result.Message ?? "").IndexOf("already off", StringComparison.OrdinalIgnoreCase) < 0;
            return result;
        }

        static RepairResult NetworkReset()
        {
            string body = "& \"$env:SystemRoot\\System32\\netsh.exe\" winsock reset\r\n"
                + "if ($LASTEXITCODE -ne 0) { throw \"Winsock reset failed ($LASTEXITCODE).\" }\r\n"
                + "& \"$env:SystemRoot\\System32\\netsh.exe\" int ip reset\r\n"
                + "& \"$env:SystemRoot\\System32\\ipconfig.exe\" /flushdns\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nWinsock and the IP stack were reset. Restart Windows. VPN clients and custom DNS need to be set up again.\")\r\n";
            RepairResult result = Command.RunElevated(body, 120000, true);
            result.Reboot = result.Ok;
            return result;
        }

        static RepairResult OverlayOff()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                    key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR"))
                    key.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                return RepairResult.Success("Game DVR recording is off. Turn off Discord, Steam, and the graphics overlay before you start the program again.");
            }
            catch (Exception ex)
            {
                return RepairResult.Fail(ex.Message);
            }
        }

        static RepairResult DismThenSfc()
        {
            string body = ToolReadHelpers()
                + DismCapture()
                + "if ($dismCode -ne 0 -and $dismCode -ne 3010) { throw $dismMsg }\r\n"
                + SfcCapture()
                + "[System.IO.File]::WriteAllText($resultFile, (\"OK`r`n\" + $dismMsg + \"`r`n`r`n\" + $sfcMsg), [System.Text.Encoding]::UTF8)\r\n";
            return Command.RunElevated(body, 90 * 60 * 1000, false);
        }

        static RepairResult Fault50()
        {
            string body = "Get-CimInstance -ClassName Win32_PageFileSetting -ErrorAction SilentlyContinue | Remove-CimInstance -ErrorAction SilentlyContinue\r\n"
                + "$cs = Get-CimInstance -ClassName Win32_ComputerSystem\r\n"
                + "$cs.AutomaticManagedPagefile = $true\r\n"
                + "Set-CimInstance -InputObject $cs\r\n"
                + "echo Y | & \"$env:SystemRoot\\System32\\chkdsk.exe\" C: /F /R\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nWindows will manage the page file. The disk check runs on the next restart and can take a long time.\")\r\n";
            RepairResult result = Command.RunElevated(body, 10 * 60 * 1000, true);
            result.Reboot = result.Ok;
            return result;
        }

        static RepairResult Chkdsk()
        {
            string body = "echo Y | & \"$env:SystemRoot\\System32\\chkdsk.exe\" C: /F /R\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nThe disk check runs on the next restart and can take a long time.\")\r\n";
            RepairResult result = Command.RunElevated(body, 10 * 60 * 1000, true);
            result.Reboot = result.Ok;
            return result;
        }

        static RepairResult VcRepair()
        {
            List<string> ids = new List<string>();
            CollectVc(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"), ids);
            CollectVc(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"), ids);
            if (ids.Count == 0)
                return RepairResult.Fail("The Visual C++ 2015-2022 redistributable is not installed. Install both the 32-bit and 64-bit packages from Microsoft. Do not download a single DLL.");
            System.Text.StringBuilder body = new System.Text.StringBuilder();
            for (int i = 0; i < ids.Count; i++)
                body.Append("Start-Process -FilePath \"$env:SystemRoot\\System32\\msiexec.exe\" -ArgumentList '/fa ").Append(ids[i]).Append(" /qn /norestart' -Wait\r\n");
            body.Append("[System.IO.File]::WriteAllText($resultFile, \"OK`r`nThe installed Visual C++ runtimes were repaired.\")\r\n");
            return Command.RunElevated(body.ToString(), 20 * 60 * 1000, true);
        }

        static void CollectVc(RegistryKey key, List<string> ids)
        {
            if (key == null) return;
            using (key)
            {
                string[] names = key.GetSubKeyNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].Length < 2 || names[i][0] != '{') continue;
                    using (RegistryKey sub = key.OpenSubKey(names[i]))
                    {
                        if (sub == null) continue;
                        string display = sub.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(display)) continue;
                        if (display.IndexOf("Visual C++", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (display.IndexOf("2015", StringComparison.Ordinal) < 0 && display.IndexOf("2017", StringComparison.Ordinal) < 0
                            && display.IndexOf("2019", StringComparison.Ordinal) < 0 && display.IndexOf("2022", StringComparison.Ordinal) < 0)
                            continue;
                        if (!ids.Contains(names[i])) ids.Add(names[i]);
                    }
                }
            }
        }

        static RepairResult WuReset()
        {
            string body = "Stop-Service -Name wuauserv,bits -Force -ErrorAction SilentlyContinue\r\n"
                + "$dir = Join-Path $env:SystemRoot 'SoftwareDistribution\\Download'\r\n"
                + "if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath (Join-Path $dir '*') -Recurse -Force -ErrorAction SilentlyContinue }\r\n"
                + "Start-Service -Name bits,wuauserv\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nThe Windows Update download cache was cleared and the services were started.\")\r\n";
            return Command.RunElevated(body, 120000, true);
        }

        static RepairResult DotNet()
        {
            string body = "$log = Join-Path $env:TEMP 'whg-dotnet.txt'\r\n"
                + "function Enable-Fx([string]$name) {\r\n"
                + "  & \"$env:SystemRoot\\System32\\Dism.exe\" /Online /Enable-Feature /FeatureName:$name /All /NoRestart *> $log\r\n"
                + "  return $LASTEXITCODE\r\n"
                + "}\r\n"
                + "$a = Enable-Fx 'NetFx3'\r\n"
                + "$b = Enable-Fx 'NetFx4-AdvSrvs'\r\n"
                + "if ($b -ne 0 -and $b -ne 3010) { $b = Enable-Fx 'NetFx4' }\r\n"
                + "if (($a -ne 0 -and $a -ne 3010) -or ($b -ne 0 -and $b -ne 3010)) { throw \"DISM could not enable .NET (exit $a / $b). If it reported a missing source, mount a matching Windows ISO.\" }\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`n.NET Framework 3.5 and 4.x were enabled.\")\r\n";
            return Command.RunElevated(body, 40 * 60 * 1000, false);
        }

        static RepairResult AppInit()
        {
            string native = ReadAppInit(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows");
            string wow = ReadAppInit(@"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows");
            if (native.Length == 0 && wow.Length == 0)
                return RepairResult.Success("AppInit_DLLs is already clear. No extra library is being forced into programs.");
            string body = "foreach ($path in @('HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Windows','HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows NT\\CurrentVersion\\Windows')) {\r\n"
                + "  if (-not (Test-Path -LiteralPath $path)) { continue }\r\n"
                + "  New-ItemProperty -Path $path -Name AppInit_DLLs -PropertyType String -Value '' -Force | Out-Null\r\n"
                + "  New-ItemProperty -Path $path -Name LoadAppInit_DLLs -PropertyType DWord -Value 0 -Force | Out-Null\r\n"
                + "}\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nAppInit_DLLs is clear. Extra libraries are no longer forced into every program.\")\r\n";
            return Command.RunElevated(body, 90000, true);
        }

        static string ReadAppInit(string path)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
                {
                    if (key == null) return "";
                    string value = key.GetValue("AppInit_DLLs") as string;
                    return string.IsNullOrEmpty(value) ? "" : value.Trim();
                }
            }
            catch
            {
                return "";
            }
        }

        static RepairResult Storage()
        {
            string body = "$models = @(Get-CimInstance -ClassName Win32_DiskDrive | ForEach-Object { $_.Model })\r\n"
                + "$names = ($models -join ', ')\r\n"
                + "if ([string]::IsNullOrWhiteSpace($names)) { $names = 'the installed disk' }\r\n"
                + "$tool = \"that maker's own firmware tool\"\r\n"
                + "if ($names -match 'Samsung') { $tool = 'Samsung Magician from Samsung' }\r\n"
                + "elseif ($names -match 'WD |Western Digital|SanDisk') { $tool = 'WD Dashboard from Western Digital' }\r\n"
                + "elseif ($names -match 'Crucial|Micron') { $tool = 'Crucial Storage Executive from Crucial' }\r\n"
                + "elseif ($names -match 'Solidigm') { $tool = 'the Solidigm firmware package' }\r\n"
                + "elseif ($names -match 'SK hynix|Hynix') { $tool = 'the SK hynix SSD tool' }\r\n"
                + "elseif ($names -match 'Kingston') { $tool = 'Kingston SSD Manager' }\r\n"
                + "elseif ($names -match 'Kioxia|Toshiba') { $tool = 'Kioxia SSD Utility' }\r\n"
                + "$session = New-Object -ComObject Microsoft.Update.Session\r\n"
                + "$searcher = $session.CreateUpdateSearcher()\r\n"
                + "$searcher.Online = $true\r\n"
                + "try { $searcher.ServerSelection = 2 } catch { }\r\n"
                + "$found = $searcher.Search(\"IsInstalled=0 and Type='Driver' and IsHidden=0\")\r\n"
                + "$match = $null\r\n"
                + "foreach ($u in $found.Updates) {\r\n"
                + "  $t = [string]$u.Title\r\n"
                + "  if ($t -match 'Display|Radeon|GeForce|Graphics') { continue }\r\n"
                + "  if ($t -match 'NVMe|SATA|storahci|Storage|Chipset|RST') { $match = $u; break }\r\n"
                + "}\r\n"
                + "if ($null -eq $match) { [System.IO.File]::WriteAllText($resultFile, \"OK`r`nNo storage driver was waiting in Windows Update. Firmware for $names stays in $tool. That tool is not downloaded here.\"); return }\r\n"
                + "$col = New-Object -ComObject Microsoft.Update.UpdateColl\r\n"
                + "[void]$col.Add($match)\r\n"
                + "$downloader = $session.CreateUpdateDownloader(); $downloader.Updates = $col; $dl = $downloader.Download()\r\n"
                + "if ($dl.ResultCode -ne 2) { throw 'Windows Update could not download the storage driver.' }\r\n"
                + "$installer = $session.CreateUpdateInstaller(); $installer.Updates = $col; $ins = $installer.Install()\r\n"
                + "if ($ins.ResultCode -ne 2) { throw 'Windows Update did not install the storage driver.' }\r\n"
                + "$title = $match.Title\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`nInstalled $title from Windows Update. Disks: $names. Restart when you can. Firmware stays in $tool and is not downloaded here.\")\r\n";
            RepairResult result = Command.RunElevated(body, 20 * 60 * 1000, true);
            result.Reboot = result.Ok && (result.Message ?? "").IndexOf("Installed ", StringComparison.Ordinal) >= 0;
            return result;
        }

        static RepairResult Sfc()
        {
            string body = ToolReadHelpers() + SfcCapture()
                + "[System.IO.File]::WriteAllText($resultFile, (\"OK`r`n\" + $sfcMsg), [System.Text.Encoding]::UTF8)\r\n";
            return Command.RunElevated(body, 45 * 60 * 1000, false);
        }

        static RepairResult Dism()
        {
            string body = ToolReadHelpers() + DismCapture()
                + "if ($dismCode -ne 0 -and $dismCode -ne 3010) { throw $dismMsg }\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, (\"OK`r`n\" + $dismMsg), [System.Text.Encoding]::UTF8)\r\n";
            return Command.RunElevated(body, 60 * 60 * 1000, false);
        }

        static string CapturePath(string name)
        {
            return Path.Combine(AppPaths.Ensure(), name).Replace("'", "''");
        }

        static string ToolReadHelpers()
        {
            return "function Read-ToolText([string]$path) {\r\n"
                + "  if (-not (Test-Path -LiteralPath $path)) { return '' }\r\n"
                + "  $bytes = [System.IO.File]::ReadAllBytes($path)\r\n"
                + "  if ($bytes.Length -eq 0) { return '' }\r\n"
                + "  $unicode = $false\r\n"
                + "  if ($bytes.Length -ge 2 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254) { $unicode = $true }\r\n"
                + "  else {\r\n"
                + "    $zeros = 0\r\n"
                + "    $limit = [Math]::Min(200, $bytes.Length)\r\n"
                + "    for ($i = 1; $i -lt $limit; $i += 2) { if ($bytes[$i] -eq 0) { $zeros++ } }\r\n"
                + "    if ($zeros -ge 8) { $unicode = $true }\r\n"
                + "  }\r\n"
                + "  if ($unicode) { $text = [System.Text.Encoding]::Unicode.GetString($bytes) } else { $text = [System.Text.Encoding]::UTF8.GetString($bytes) }\r\n"
                + "  return (($text -replace [char]0, '') ).Trim()\r\n"
                + "}\r\n"
                + "function Join-Names($list) {\r\n"
                + "  $shown = New-Object System.Collections.Generic.List[string]\r\n"
                + "  foreach ($one in $list) { if ($shown.Count -ge 8) { break }; [void]$shown.Add($one) }\r\n"
                + "  $text = [string]::Join(', ', $shown.ToArray())\r\n"
                + "  $extra = $list.Count - $shown.Count\r\n"
                + "  if ($extra -gt 0) { $text = $text + ', and ' + $extra + ' more' }\r\n"
                + "  return $text\r\n"
                + "}\r\n"
                + "function Read-NewLines([string]$path, [long]$mark) {\r\n"
                + "  $lines = New-Object System.Collections.Generic.List[string]\r\n"
                + "  if (-not (Test-Path -LiteralPath $path)) { return $lines }\r\n"
                + "  $fs = $null\r\n"
                + "  try {\r\n"
                + "    $fs = New-Object System.IO.FileStream($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)\r\n"
                + "    if ($mark -gt 0 -and $mark -lt $fs.Length) { [void]$fs.Seek($mark, [System.IO.SeekOrigin]::Begin) }\r\n"
                + "    $reader = New-Object System.IO.StreamReader($fs)\r\n"
                + "    while (($line = $reader.ReadLine()) -ne $null) { [void]$lines.Add($line) }\r\n"
                + "    $reader.Dispose()\r\n"
                + "  } catch { }\r\n"
                + "  finally { if ($null -ne $fs) { $fs.Dispose() } }\r\n"
                + "  return $lines\r\n"
                + "}\r\n";
        }

        static string SfcCapture()
        {
            string capture = CapturePath("sfc-capture.txt");
            return "$cbs = Join-Path $env:SystemRoot 'Logs\\CBS\\CBS.log'\r\n"
                + "$mark = 0L\r\n"
                + "if (Test-Path -LiteralPath $cbs) { $mark = (Get-Item -LiteralPath $cbs).Length }\r\n"
                + "$capture = '" + capture + "'\r\n"
                + "& cmd.exe /d /c ('sfc.exe /scannow > \"' + $capture + '\" 2>&1')\r\n"
                + "$sfcCode = $LASTEXITCODE\r\n"
                + "$console = Read-ToolText $capture\r\n"
                + "$flat = ([regex]::Replace($console, '\\s+', ' ')).ToLowerInvariant()\r\n"
                + "$repaired = New-Object System.Collections.Generic.List[string]\r\n"
                + "$failed = New-Object System.Collections.Generic.List[string]\r\n"
                + "foreach ($line in (Read-NewLines $cbs $mark)) {\r\n"
                + "  if ($line.IndexOf('[SR]') -lt 0) { continue }\r\n"
                + "  $at = $line.IndexOf('Repairing file ')\r\n"
                + "  if ($at -ge 0) {\r\n"
                + "    $rest = $line.Substring($at + 15)\r\n"
                + "    $cut = $rest.LastIndexOf(' from store')\r\n"
                + "    if ($cut -gt 0) { $rest = $rest.Substring(0, $cut) }\r\n"
                + "    $colon = $rest.LastIndexOf(':')\r\n"
                + "    if ($colon -gt 0) { $rest = $rest.Substring($colon - 1).Trim() }\r\n"
                + "    if ($rest.Length -gt 3 -and -not $repaired.Contains($rest)) { [void]$repaired.Add($rest) }\r\n"
                + "  }\r\n"
                + "  $bad = $line.IndexOf('Cannot repair member file')\r\n"
                + "  $q1 = $line.IndexOf(']\"')\r\n"
                + "  if ($bad -ge 0 -and $q1 -ge 0) {\r\n"
                + "    $q2 = $line.IndexOf('\"', $q1 + 2)\r\n"
                + "    if ($q2 -gt $q1) {\r\n"
                + "      $name = $line.Substring($q1 + 2, $q2 - $q1 - 2)\r\n"
                + "      if ($name.Length -gt 0 -and -not $failed.Contains($name)) { [void]$failed.Add($name) }\r\n"
                + "    }\r\n"
                + "  }\r\n"
                + "}\r\n"
                + "$sfcMsg = ''\r\n"
                + "if ($flat.Contains('did not find any integrity violations') -and $repaired.Count -eq 0 -and $failed.Count -eq 0) {\r\n"
                + "  $sfcMsg = 'System files are healthy. Windows Resource Protection did not find any integrity violations.'\r\n"
                + "} elseif ($failed.Count -gt 0 -or $flat.Contains('unable to fix some')) {\r\n"
                + "  $sfcMsg = 'The system file check found damage.'\r\n"
                + "  if ($repaired.Count -gt 0) { $sfcMsg = $sfcMsg + ' Repaired: ' + (Join-Names $repaired) + '.' }\r\n"
                + "  if ($failed.Count -gt 0) { $sfcMsg = $sfcMsg + ' Could not repair: ' + (Join-Names $failed) + '.' }\r\n"
                + "  elseif ($flat.Contains('unable to fix some')) { $sfcMsg = $sfcMsg + ' Windows Resource Protection could not repair some files. The names are in CBS.log.' }\r\n"
                + "} elseif ($repaired.Count -gt 0 -or $flat.Contains('successfully repaired')) {\r\n"
                + "  if ($repaired.Count -gt 0) { $sfcMsg = 'System files were repaired: ' + (Join-Names $repaired) + '.' }\r\n"
                + "  else { $sfcMsg = 'System files were repaired. Windows Resource Protection found corrupt files and successfully repaired them.' }\r\n"
                + "} elseif ($flat.Contains('could not perform') -or ($sfcCode -is [int] -and $sfcCode -ne 0)) {\r\n"
                + "  $sfcMsg = 'The system file check could not finish. Restart Windows, then run it again.'\r\n"
                + "} else {\r\n"
                + "  $sfcMsg = 'System files are healthy. The check finished and did not list any damaged files.'\r\n"
                + "}\r\n";
        }

        static string DismCapture()
        {
            string capture = CapturePath("dism-capture.txt");
            return "$dismLog = Join-Path $env:SystemRoot 'Logs\\DISM\\dism.log'\r\n"
                + "$dismMark = 0L\r\n"
                + "if (Test-Path -LiteralPath $dismLog) { $dismMark = (Get-Item -LiteralPath $dismLog).Length }\r\n"
                + "$dismCapture = '" + capture + "'\r\n"
                + "& cmd.exe /d /c ('dism.exe /Online /Cleanup-Image /RestoreHealth > \"' + $dismCapture + '\" 2>&1')\r\n"
                + "$dismCode = $LASTEXITCODE\r\n"
                + "$dismText = Read-ToolText $dismCapture\r\n"
                + "$dismFlat = ([regex]::Replace($dismText, '\\s+', ' ')).ToLowerInvariant()\r\n"
                + "$dismRepaired = $false\r\n"
                + "foreach ($line in (Read-NewLines $dismLog $dismMark)) {\r\n"
                + "  $low = $line.ToLowerInvariant()\r\n"
                + "  if ($low.Contains('corruption was repaired') -or $low.Contains('repairing corruption') -or $low.Contains('repaired the component store')) { $dismRepaired = $true; break }\r\n"
                + "}\r\n"
                + "if ($dismFlat.Contains('no component store corruption')) {\r\n"
                + "  $dismMsg = 'The Windows image is healthy. No component store corruption was detected.'\r\n"
                + "} elseif ($dismCode -ne 0 -and $dismCode -ne 3010) {\r\n"
                + "  $dismMsg = 'The Windows image repair did not finish (exit ' + $dismCode + ').'\r\n"
                + "} elseif ($dismRepaired) {\r\n"
                + "  $dismMsg = 'The Windows image was repaired. The restore operation completed successfully.'\r\n"
                + "} elseif ($dismFlat.Contains('completed successfully')) {\r\n"
                + "  $dismMsg = 'The Windows image is healthy. The restore operation completed successfully and did not report corruption.'\r\n"
                + "} else {\r\n"
                + "  $dismMsg = 'The Windows image repair finished without a listed error.'\r\n"
                + "}\r\n";
        }

        static RepairResult OpenExe(string target, string success)
        {
            if (string.IsNullOrEmpty(target) || (target.IndexOf("\\", StringComparison.Ordinal) >= 0 && !File.Exists(target) && target.IndexOf("ms-settings:", StringComparison.Ordinal) < 0))
                return RepairResult.Fail("That program is not installed.");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = target;
                psi.UseShellExecute = true;
                Process.Start(psi);
                return RepairResult.Success(success);
            }
            catch (Exception ex)
            {
                return RepairResult.Fail(ex.Message);
            }
        }
    }

    static class FixCatalog
    {
        public static readonly string AmdExe = @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe";
        public static string NvidiaExe;

        public static List<FixSpec> Build()
        {
            List<FixSpec> list = new List<FixSpec>();
            list.Add(Make("driver", "Graphics driver",
                "Checks Windows Update first, then the graphics maker's own site. A downloaded installer must be signed by that maker or by Microsoft. Third-party driver updaters are not used.",
                "Update", false, false, true, false));
            list.Add(Make("faststartup", "Fast Startup",
                "Turns Fast Startup off so Windows does a full shutdown. This is a performance choice, not the repair for a power loss.",
                "Fix now", true, true, false, false));
            list.Add(Make("tweaks", "Game Optimizer",
                "Turns on High performance and Game Mode, and stops background recording while you play.",
                "Fix", true, false, false, false));
            list.Add(Make("shaders", "File Cleanup",
                "Removes old temporary files and shader caches so the system drive has more room.",
                "Clean up", false, false, false, false));
            list.Add(Make("dns", "Flush DNS",
                "Clears the address cache when an online game cannot connect.",
                "Fix", false, false, false, false));
            list.Add(Make("fullscreen", "Fullscreen optimizations",
                "Stops Windows from changing how a chosen game draws its fullscreen window.",
                "Fix", false, false, false, false));
            list.Add(Make("tdr", "GPU timeout",
                "Gives the graphics driver longer to recover so a hitch does not crash the game.",
                "Fix now", true, true, false, false));
            list.Add(Make("hagsoff", "GPU scheduling",
                "Turns off a scheduler that makes some games stutter or crash.",
                "Fix now", true, true, false, false));
            list.Add(Make("hagson", "GPU scheduling",
                "Turns hardware-accelerated GPU scheduling back on.",
                "Fix", true, true, false, false));
            list.Add(Make("pagefile", "Page file",
                "Leaves the page file on and lets Windows choose its size. The page file is not turned off and not locked to a fixed size.",
                "Fix now", true, true, false, false));
            list.Add(Make("netreset", "Network reset",
                "Rebuilds the network stack when online games keep dropping.",
                "Fix now", true, true, false, false));
            list.Add(Make("sfc", "System files",
                "Checks Windows files and repairs ones that are damaged.",
                "Fix", true, false, true, false));
            list.Add(Make("dism", "Windows image",
                "Repairs the Windows image when the system file check cannot finish.",
                "Fix", true, false, true, false));
            list.Add(Make("dismsfc", "System files",
                "Repairs the Windows image with DISM, then checks system files. This can take more than an hour.",
                "Fix", true, false, true, false));
            list.Add(Make("fault50", "Page file and disk",
                "Lets Windows manage the page file and schedules a full disk check for the next restart. That check can take hours. The PC is not restarted for you.",
                "Fix now", true, true, false, false));
            list.Add(Make("chkdsk", "Disk check",
                "Schedules a full disk check for the next restart. It can take hours. The PC is not restarted for you.",
                "Fix", true, true, false, false));
            list.Add(Make("storage", "Storage driver",
                "Installs a storage or chipset driver from Windows Update when one is waiting. Firmware still comes from the disk maker's own tool.",
                "Fix", true, false, true, false));
            list.Add(Make("vcrepair", "Visual C++ runtime",
                "Repairs the installed Visual C++ 2015-2022 packages with the official installer. A single DLL is not downloaded.",
                "Fix", true, false, true, false));
            list.Add(Make("wureset", "Windows Update cache",
                "Stops Windows Update, deletes only the download cache, and starts the services again.",
                "Fix", true, false, false, false));
            list.Add(Make("dotnet", ".NET Framework",
                "Enables .NET Framework 3.5 and 4.8. If Windows is missing the source files, mount a matching ISO. This can take a long time.",
                "Fix", true, false, true, false));
            list.Add(Make("appinit", "AppInit libraries",
                "Clears AppInit_DLLs so extra libraries are not loaded into every program. Nothing changes when the value is already empty.",
                "Fix", true, false, false, false));
            list.Add(Make("overlayoff", "Overlays",
                "Turns off Windows Game DVR recording. Also turn off Discord, Steam, and the graphics overlay in those apps.",
                "Fix", false, false, false, false));
            list.Add(Make("browsers", "Browser background",
                "Tells Edge and Chrome to stop running after you close them, and turns on Memory Saver. Open programs are not closed.",
                "Fix", true, false, false, false));
            list.Add(Make("visualfx", "Visual effects",
                "Turns off most window animations and keeps font smoothing and file thumbnails. Open programs are not closed.",
                "Fix", false, false, false, false));
            list.Add(Make("sysmain", "SysMain cache",
                "Stops SysMain and keeps it from starting. Windows will stop preloading apps into cached memory. Open programs are not closed.",
                "Fix", true, false, false, false));
            list.Add(Make("startuptrim", "Startup updaters",
                "Turns off third-party updaters and launchers that start with Windows. Windows, security, and the chip-maker tools stay on. Nothing is closed.",
                "Fix", false, false, false, false));
            list.Add(Make("maxmem", "Boot memory limit",
                "Removes a Windows boot limit that hides part of the installed RAM. BIOS video memory is not changed.",
                "Fix", true, true, false, false));
            list.Add(Make("memory", "Memory diagnostic",
                "Tests your memory the next time Windows restarts.",
                "Open", false, true, false, true));
            list.Add(Make("wu", "Windows Update",
                "Opens Windows Update so pending fixes can be installed.",
                "Open", false, false, false, true));
            list.Add(Make("gaming", "Game Mode",
                "Opens Game Mode so you can confirm it is on.",
                "Open", false, false, false, true));
            if (File.Exists(AmdExe))
            {
                list.Add(Make("amd", "AMD Software",
                    "Opens AMD Software so you can turn off the in-game overlay.",
                    "Open", false, false, false, true));
            }
            NvidiaExe = FindNvidia();
            if (!string.IsNullOrEmpty(NvidiaExe))
            {
                list.Add(Make("nvidia", "NVIDIA",
                    "Opens NVIDIA software so you can turn off the in-game overlay.",
                    "Open", false, false, false, true));
            }
            return list;
        }

        public static FixSpec Find(List<FixSpec> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == id) return list[i];
            }
            return null;
        }

        static string FindNvidia()
        {
            string[] paths = new string[] {
                @"C:\Program Files\NVIDIA Corporation\NVIDIA app\CEF\NVIDIA App.exe",
                @"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe",
                @"C:\Program Files\NVIDIA Corporation\Control Panel Client\nvcplui.exe",
                @"C:\Program Files\NVIDIA Corporation\NVIDIA GeForce Experience\NVIDIA GeForce Experience.exe"
            };
            for (int i = 0; i < paths.Length; i++)
            {
                if (File.Exists(paths[i])) return paths[i];
            }
            return null;
        }

        static FixSpec Make(string id, string title, string detail, string button, bool admin, bool reboot, bool longRunning, bool opener)
        {
            FixSpec spec = new FixSpec();
            spec.Id = id;
            spec.Title = title;
            spec.Detail = detail;
            spec.Button = button;
            spec.Admin = admin;
            spec.Reboot = reboot;
            spec.LongRunning = longRunning;
            spec.Opener = opener;
            return spec;
        }
    }
}
