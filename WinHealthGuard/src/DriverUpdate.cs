using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WinHealthGuard
{
    sealed class DriverOffer
    {
        public string DeviceName = "";
        public string InstalledVersion = "";
        public string InstalledDate = "";
        public string Vendor = "";
        public string HardwareId = "";
        public string OemNote = "";
        public string WindowsUpdateTitle = "";
        public string VendorUrl = "";
        public string VendorNote = "";
        public string VendorSearch = "";
        public string ThirdPartyUrl = "";
        public string ThirdPartyNote = "";
        public string CatalogNote = "";
        public string PartsNote = "";
        public bool Notebook;
        public string Error = "";

        public string Report()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrEmpty(DeviceName) ? "Graphics adapter" : DeviceName);
            if (HardwareId.Length > 0) sb.AppendLine("Hardware ID " + HardwareId + ".");
            if (InstalledVersion.Length > 0)
            {
                sb.Append("Installed driver ").Append(InstalledVersion);
                if (InstalledDate.Length > 0) sb.Append(" (").Append(InstalledDate).Append(")");
                sb.AppendLine(".");
            }
            sb.AppendLine();
            if (OemNote.Length > 0)
            {
                sb.AppendLine(OemNote);
                sb.AppendLine();
            }
            if (WindowsUpdateTitle.Length > 0)
                sb.AppendLine("Windows Update has a WHQL driver: " + WindowsUpdateTitle);
            else
                sb.AppendLine("Windows Update has no newer graphics driver.");
            if (CatalogNote.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine(CatalogNote);
            }
            if (VendorNote.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine(VendorNote);
            }
            if (ThirdPartyNote.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine(ThirdPartyNote);
            }
            if (PartsNote.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine(PartsNote);
            }
            sb.AppendLine();
            sb.Append("Order: Windows Update, then the chip maker's site, then a third-party page. The third-party page asks for confirmation and is not downloaded.");
            if (Error.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Error);
            }
            return sb.ToString().Trim();
        }
    }

    static class DriverUpdate
    {
        public static DriverOffer Search()
        {
            DriverOffer offer = new DriverOffer();
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            catch { }
            try { ReadDevice(offer); }
            catch (Exception ex) { offer.Error = ex.Message; }
            try { ReadOem(offer); }
            catch { }
            try { ReadParts(offer); }
            catch { }
            try { offer.WindowsUpdateTitle = FindWindowsUpdate(offer); }
            catch (Exception ex) { offer.Error = Append(offer.Error, "Windows Update could not be searched. " + ex.Message); }
            if (offer.WindowsUpdateTitle.Length > 0)
                offer.CatalogNote = "That package is the WHQL driver Microsoft signs and publishes. It is the same source as the Microsoft Update Catalog.";
            else
            {
                try { NoteCatalog(offer); }
                catch (Exception ex) { offer.Error = Append(offer.Error, "The Microsoft Update Catalog could not be searched. " + ex.Message); }
            }
            try { FindVendor(offer); }
            catch (Exception ex) { offer.Error = Append(offer.Error, "The official site could not be searched. " + ex.Message); }
            NoteSoftpedia(offer);
            return offer;
        }

        public static string Ask(IWin32Window owner, DriverOffer offer)
        {
            if (offer == null) return "";
            using (DriverPrompt prompt = new DriverPrompt(offer))
            {
                prompt.ShowDialog(owner);
                return prompt.Choice ?? "";
            }
        }

        public static RepairResult DownloadAndRun(DriverOffer offer)
        {
            if (offer == null || string.IsNullOrEmpty(offer.VendorUrl))
                return RepairResult.Fail("No official installer was found.");
            Uri uri;
            if (!Uri.TryCreate(offer.VendorUrl, UriKind.Absolute, out uri) || !Allowed(uri))
                return RepairResult.Fail("That download is not on the official site.");
            try
            {
                string dir = Path.Combine(AppPaths.Ensure(), "drivers");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string name = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(name) || name.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) < 0)
                    name = "driver-setup.exe";
                string dest = Path.Combine(dir, name);
                Download(uri, dest);
                RequireOfficialSignature(dest, offer.Vendor);
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = dest;
                psi.UseShellExecute = true;
                Process.Start(psi);
                AppPaths.Audit("driver download " + uri.Host + " " + name);
                return RepairResult.Success("The official installer is open. Confirm the driver it found. WinHealthGuard will not restart the PC.");
            }
            catch (Exception ex)
            {
                return RepairResult.Fail(ex.Message);
            }
        }

        public static RepairResult InstallWindowsUpdate(string title)
        {
            if (string.IsNullOrEmpty(title)) return RepairResult.Fail("Windows Update did not name a driver.");
            string dir = AppPaths.Ensure();
            string titlePath = Path.Combine(dir, "driver-wu-title.txt");
            File.WriteAllText(titlePath, title, new UTF8Encoding(false));
            string quoted = titlePath.Replace("'", "''");
            string body = "$title = [System.IO.File]::ReadAllText('" + quoted + "')\r\n"
                + "$session = New-Object -ComObject Microsoft.Update.Session\r\n"
                + "$searcher = $session.CreateUpdateSearcher()\r\n"
                + "$searcher.Online = $true\r\n"
                + "try { $searcher.ServerSelection = 2 } catch { }\r\n"
                + "$result = $searcher.Search(\"IsInstalled=0 and Type='Driver' and IsHidden=0\")\r\n"
                + "$match = $null\r\n"
                + "foreach ($u in $result.Updates) { if ($u.Title -eq $title) { $match = $u; break } }\r\n"
                + "if ($null -eq $match) { throw 'That driver is no longer offered by Windows Update.' }\r\n"
                + "$col = New-Object -ComObject Microsoft.Update.UpdateColl\r\n"
                + "[void]$col.Add($match)\r\n"
                + "$downloader = $session.CreateUpdateDownloader()\r\n"
                + "$downloader.Updates = $col\r\n"
                + "$dl = $downloader.Download()\r\n"
                + "if ($dl.ResultCode -ne 2) { throw 'Windows Update could not download the driver.' }\r\n"
                + "$installer = $session.CreateUpdateInstaller()\r\n"
                + "$installer.Updates = $col\r\n"
                + "$ins = $installer.Install()\r\n"
                + "if ($ins.ResultCode -ne 2) { throw ('Windows Update did not install the driver (code ' + $ins.ResultCode + ').') }\r\n"
                + "$msg = 'The graphics driver from Windows Update is installed.'\r\n"
                + "if ($ins.RebootRequired) { $msg += ' Restart Windows when you can.' }\r\n"
                + "[System.IO.File]::WriteAllText($resultFile, \"OK`r`n$msg\")\r\n";
            return Command.RunElevated(body, 15 * 60 * 1000, true);
        }

        static void ReadDevice(DriverOffer offer)
        {
            SlowFacts facts = new SlowFacts();
            SystemProbe.ReadGraphics(facts);
            if (!facts.GpuKnown) return;
            offer.DeviceName = facts.GpuName ?? "";
            offer.InstalledVersion = facts.GpuDriver ?? "";
            if (facts.GpuDriverDate.Year >= 2015)
                offer.InstalledDate = facts.GpuDriverDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
            offer.Vendor = VendorOf(facts.GpuPnp, facts.GpuName);
            offer.HardwareId = HardwareIdOf(facts.GpuPnp);
        }

        static void ReadParts(DriverOffer offer)
        {
            offer.Notebook = IsLaptop();
            StringBuilder sb = new StringBuilder();
            if (offer.OemNote.Length == 0)
                sb.AppendLine("No laptop brand was detected, so BIOS, touchpad, and power packages stay with Windows Update.");
            AppendAdapters(sb);
            AppendDisks(sb);
            AppendAudio(sb);
            AppendTouchpad(sb);
            offer.PartsNote = sb.ToString().Trim();
        }

        static bool IsLaptop()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                if (IsLaptopChassis(item["ChassisTypes"])) return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        static bool IsLaptopChassis(object raw)
        {
            Array values = raw as Array;
            if (values == null) return false;
            foreach (object one in values)
            {
                int n;
                if (!int.TryParse(Convert.ToString(one), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                if (n == 8 || n == 9 || n == 10 || n == 11 || n == 14 || n == 30 || n == 31 || n == 32) return true;
            }
            return false;
        }

        static bool IsMobileGpu(string name)
        {
            string n = name ?? "";
            if (n.IndexOf("laptop", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("notebook", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("mobile", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return Regex.IsMatch(n, @"\b[0-9]{3,4}M\b", RegexOptions.IgnoreCase);
        }

        static void AppendAdapters(StringBuilder sb)
        {
            int shown = 0;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, PhysicalAdapter FROM Win32_NetworkAdapter"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                if (shown >= 2) return;
                                object physical = item["PhysicalAdapter"];
                                if (physical is bool && !(bool)physical) continue;
                                string name = Convert.ToString(item["Name"]) ?? "";
                                string pnp = Convert.ToString(item["PNPDeviceID"]) ?? "";
                                if (!UsefulAdapter(name)) continue;
                                string vendor = ChipVendor(pnp, name);
                                sb.Append("Network: ").Append(name).Append(". ");
                                sb.AppendLine(NetworkSource(vendor));
                                shown++;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        static bool UsefulAdapter(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Length == 0) return false;
            if (n.IndexOf("virtual", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("miniport", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("wan ", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("debug", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("wi-fi direct", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("bluetooth", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("kernel", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        static string NetworkSource(string vendor)
        {
            if (vendor == "Intel") return "The official source is Intel's download center. This check does not install Wi-Fi or Ethernet drivers.";
            if (vendor == "Realtek") return "Onboard Realtek packages come from Windows Update or the laptop maker, not a generic Realtek download.";
            if (vendor == "MediaTek") return "MediaTek does not publish a consumer installer. Use Windows Update or the laptop maker.";
            if (vendor == "Qualcomm") return "Qualcomm wireless drivers come from Windows Update or the laptop maker.";
            if (vendor == "Broadcom") return "Broadcom drivers come from Windows Update or the laptop maker.";
            return "Use Windows Update or the laptop maker. A third-party network driver is not installed.";
        }

        static void AppendDisks(StringBuilder sb)
        {
            int shown = 0;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Model, PNPDeviceID FROM Win32_DiskDrive"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                if (shown >= 2) return;
                                string name = Convert.ToString(item["Model"]) ?? "";
                                if (name.Length == 0) continue;
                                sb.Append("Disk: ").Append(name).Append(". Firmware stays in ").Append(DiskTool(name)).AppendLine(". It is not downloaded here.");
                                shown++;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        static string DiskTool(string name)
        {
            string n = name ?? "";
            if (n.IndexOf("Samsung", StringComparison.OrdinalIgnoreCase) >= 0) return "Samsung Magician";
            if (n.IndexOf("SanDisk", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("WD", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Western Digital", StringComparison.OrdinalIgnoreCase) >= 0) return "WD Dashboard";
            if (n.IndexOf("Crucial", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Micron", StringComparison.OrdinalIgnoreCase) >= 0) return "Crucial Storage Executive";
            if (n.IndexOf("Solidigm", StringComparison.OrdinalIgnoreCase) >= 0) return "the Solidigm firmware package";
            if (n.IndexOf("Hynix", StringComparison.OrdinalIgnoreCase) >= 0) return "the SK hynix SSD tool";
            if (n.IndexOf("Kingston", StringComparison.OrdinalIgnoreCase) >= 0) return "Kingston SSD Manager";
            if (n.IndexOf("Kioxia", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Toshiba", StringComparison.OrdinalIgnoreCase) >= 0) return "Kioxia SSD Utility";
            return "that maker's own firmware tool";
        }

        static void AppendAudio(StringBuilder sb)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_SoundDevice"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                string name = Convert.ToString(item["Name"]) ?? "";
                                string pnp = Convert.ToString(item["PNPDeviceID"]) ?? "";
                                if (!UsefulAudio(name)) continue;
                                string vendor = ChipVendor(pnp, name);
                                if (vendor.Length == 0) vendor = "The laptop maker";
                                sb.Append("Audio: ").Append(name).Append(". ").Append(vendor).AppendLine(" audio is customized by the laptop maker. Use that support page or Windows Update, not a generic codec pack.");
                                return;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        static bool UsefulAudio(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Length == 0) return false;
            if (n.IndexOf("nvidia", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("display", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("hdmi", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        static void AppendTouchpad(StringBuilder sb)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PointingDevice"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                string name = Convert.ToString(item["Name"]) ?? "";
                                string pnp = Convert.ToString(item["PNPDeviceID"]) ?? "";
                                string blob = (name + " " + pnp).ToLowerInvariant();
                                if (blob.IndexOf("synaptics", StringComparison.Ordinal) < 0 && blob.IndexOf("elan", StringComparison.Ordinal) < 0
                                    && blob.IndexOf("goodix", StringComparison.Ordinal) < 0 && blob.IndexOf("alps", StringComparison.Ordinal) < 0
                                    && blob.IndexOf("touchpad", StringComparison.Ordinal) < 0 && blob.IndexOf("trackpoint", StringComparison.Ordinal) < 0)
                                    continue;
                                sb.Append("Touchpad: ").Append(name).AppendLine(". The driver comes from the laptop maker or Windows Update.");
                                return;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        static string ChipVendor(string pnp, string name)
        {
            string p = (pnp ?? "").ToUpperInvariant();
            string n = (name ?? "").ToLowerInvariant();
            if (p.IndexOf("VEN_8086", StringComparison.Ordinal) >= 0 || n.IndexOf("intel", StringComparison.Ordinal) >= 0) return "Intel";
            if (p.IndexOf("VEN_10EC", StringComparison.Ordinal) >= 0 || n.IndexOf("realtek", StringComparison.Ordinal) >= 0) return "Realtek";
            if (p.IndexOf("VEN_14C3", StringComparison.Ordinal) >= 0 || n.IndexOf("mediatek", StringComparison.Ordinal) >= 0) return "MediaTek";
            if (p.IndexOf("VEN_14E4", StringComparison.Ordinal) >= 0 || n.IndexOf("broadcom", StringComparison.Ordinal) >= 0) return "Broadcom";
            if (p.IndexOf("VEN_17CB", StringComparison.Ordinal) >= 0 || p.IndexOf("VEN_168C", StringComparison.Ordinal) >= 0 || n.IndexOf("qualcomm", StringComparison.Ordinal) >= 0 || n.IndexOf("atheros", StringComparison.Ordinal) >= 0) return "Qualcomm";
            if (p.IndexOf("VEN_1002", StringComparison.Ordinal) >= 0 || p.IndexOf("VEN_1022", StringComparison.Ordinal) >= 0 || n.IndexOf("amd", StringComparison.Ordinal) >= 0 || n.IndexOf("radeon", StringComparison.Ordinal) >= 0) return "AMD";
            if (p.IndexOf("VEN_10DE", StringComparison.Ordinal) >= 0 || n.IndexOf("nvidia", StringComparison.Ordinal) >= 0) return "NVIDIA";
            if (p.IndexOf("VEN_1013", StringComparison.Ordinal) >= 0 || n.IndexOf("cirrus", StringComparison.Ordinal) >= 0) return "Cirrus Logic";
            if (p.IndexOf("VEN_14F1", StringComparison.Ordinal) >= 0 || n.IndexOf("conexant", StringComparison.Ordinal) >= 0 || n.IndexOf("senary", StringComparison.Ordinal) >= 0) return "Conexant";
            if (p.IndexOf("VEN_06CB", StringComparison.Ordinal) >= 0 || n.IndexOf("synaptics", StringComparison.Ordinal) >= 0) return "Synaptics";
            if (p.IndexOf("VEN_04F3", StringComparison.Ordinal) >= 0 || n.IndexOf("elan", StringComparison.Ordinal) >= 0) return "ELAN";
            if (p.IndexOf("VEN_27C6", StringComparison.Ordinal) >= 0 || n.IndexOf("goodix", StringComparison.Ordinal) >= 0) return "Goodix";
            return "";
        }

        static string FindWindowsUpdate(DriverOffer offer)
        {
            string dir = AppPaths.Ensure();
            string script = Path.Combine(dir, "driver-search.ps1");
            string result = Path.Combine(dir, "driver-search.txt");
            try { if (File.Exists(result)) File.Delete(result); }
            catch { }
            string quoted = result.Replace("'", "''");
            string ps = "$ErrorActionPreference = 'Stop'\r\n"
                + "$session = New-Object -ComObject Microsoft.Update.Session\r\n"
                + "$searcher = $session.CreateUpdateSearcher()\r\n"
                + "$searcher.Online = $true\r\n"
                + "try { $searcher.ServerSelection = 2 } catch { }\r\n"
                + "$found = $searcher.Search(\"IsInstalled=0 and Type='Driver' and IsHidden=0\")\r\n"
                + "$sb = New-Object System.Text.StringBuilder\r\n"
                + "foreach ($u in $found.Updates) { [void]$sb.AppendLine($u.Title) }\r\n"
                + "[System.IO.File]::WriteAllText('" + quoted + "', $sb.ToString())\r\n";
            File.WriteAllText(script, ps, new UTF8Encoding(true));
            string output;
            string exe = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            int code = Command.Run(exe, "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"", out output, 180000);
            if (!File.Exists(result))
                throw new InvalidOperationException(output.Length == 0 ? "Windows Update did not answer." : output);
            if (code != 0 && new FileInfo(result).Length == 0)
                throw new InvalidOperationException(output.Length == 0 ? "Windows Update did not answer." : output);
            string[] lines = File.ReadAllLines(result, new UTF8Encoding(false));
            for (int i = 0; i < lines.Length; i++)
            {
                string title = (lines[i] ?? "").Trim();
                if (IsGraphicsUpdate(title, offer)) return title;
            }
            return "";
        }

        static void NoteSoftpedia(DriverOffer offer)
        {
            string query = offer.DeviceName ?? "";
            query = query.Replace("(TM)", " ").Replace("(R)", " ").Replace("™", " ").Replace("®", " ");
            query = Regex.Replace(query, "\\s+", " ").Trim();
            if (query.Length == 0) query = ((offer.Vendor ?? "") + " graphics").Trim();
            if (query.Length == 0) query = "graphics";
            offer.ThirdPartyUrl = "https://drivers.softpedia.com/dyn-search.php?search_term=" + Uri.EscapeDataString(query)
                + "&p_whatos=30&p_category=1&p_lastupdate=180&p_sortby=1";
            offer.ThirdPartyNote = "Softpedia is listed last. The page is Windows drivers, operating system Windows, graphics boards updated in the last 6 months, sorted by most downloaded. Opening it asks for confirmation. This app does not download that file.";
        }

        static void FindVendor(DriverOffer offer)
        {
            if (offer.Vendor == "AMD") FindAmd(offer);
            else if (offer.Vendor == "NVIDIA") FindNvidia(offer);
            else if (offer.Vendor == "Intel") FindIntel(offer);
            else if (offer.Vendor == "Qualcomm") offer.VendorNote = "Qualcomm graphics drivers come from Windows Update or the laptop maker. No installer is downloaded.";
            else offer.VendorNote = "The graphics maker was not recognized, so only Windows Update was searched.";
        }

        static void ReadOem(DriverOffer offer)
        {
            string maker = "";
            string model = "";
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(6);
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        using (item)
                        {
                            maker = Convert.ToString(item["Manufacturer"]) ?? "";
                            model = Convert.ToString(item["Model"]) ?? "";
                            break;
                        }
                    }
                }
            }
            if (!RealOem(maker) || !RealOem(model)) return;
            string site = OemSite(maker);
            if (site.Length == 0) return;
            offer.OemNote = "This is a " + maker.Trim() + " " + model.Trim() + ". Check that maker first for BIOS, power, touchpad, and hotkey packages: " + site + ". Those packages are not downloaded from here.";
        }

        static bool RealOem(string value)
        {
            string t = (value ?? "").Trim().ToLowerInvariant();
            if (t.Length < 2) return false;
            if (t == "default string" || t == "to be filled by o.e.m." || t == "system manufacturer" || t == "system product name") return false;
            if (t == "o.e.m." || t == "not specified" || t == "invalid" || t == "chassis manufacturer") return false;
            return true;
        }

        static string OemSite(string maker)
        {
            string t = (maker ?? "").ToLowerInvariant();
            if (t.IndexOf("dell", StringComparison.Ordinal) >= 0 || t.IndexOf("alienware", StringComparison.Ordinal) >= 0) return "https://www.dell.com/support/home";
            if (t.IndexOf("hewlett", StringComparison.Ordinal) >= 0 || t == "hp" || t.IndexOf("hp ", StringComparison.Ordinal) >= 0) return "https://support.hp.com/drivers";
            if (t.IndexOf("lenovo", StringComparison.Ordinal) >= 0) return "https://pcsupport.lenovo.com";
            if (t.IndexOf("asustek", StringComparison.Ordinal) >= 0 || t.IndexOf("asus", StringComparison.Ordinal) >= 0) return "https://www.asus.com/support/Download-Center/";
            if (t.IndexOf("acer", StringComparison.Ordinal) >= 0) return "https://www.acer.com/us-en/support/drivers-and-manuals";
            if (t.IndexOf("micro-star", StringComparison.Ordinal) >= 0 || t.IndexOf("msi", StringComparison.Ordinal) >= 0) return "https://www.msi.com/support/download";
            if (t.IndexOf("razer", StringComparison.Ordinal) >= 0) return "https://mysupport.razer.com";
            if (t.IndexOf("framework", StringComparison.Ordinal) >= 0) return "https://frame.work/support";
            if (t.IndexOf("surface", StringComparison.Ordinal) >= 0 || t.IndexOf("microsoft", StringComparison.Ordinal) >= 0) return "https://support.microsoft.com/surface";
            if (t == "lg" || t.IndexOf("lg electronics", StringComparison.Ordinal) >= 0) return "https://www.lg.com/us/support/software-firmware-drivers";
            if (t.IndexOf("samsung", StringComparison.Ordinal) >= 0) return "https://www.samsung.com/us/support/downloads/";
            if (t.IndexOf("gigabyte", StringComparison.Ordinal) >= 0) return "https://www.gigabyte.com/Support";
            if (t.IndexOf("asrock", StringComparison.Ordinal) >= 0) return "https://www.asrock.com/support";
            return "";
        }

        static void NoteCatalog(DriverOffer offer)
        {
            if (string.IsNullOrEmpty(offer.HardwareId))
            {
                offer.CatalogNote = "No hardware ID was available for a Microsoft Update Catalog search.";
                return;
            }
            string html = HttpGet("https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(offer.HardwareId), 180000);
            string flat = html.ToLowerInvariant();
            if (flat.IndexOf("we did not find any results", StringComparison.Ordinal) >= 0 || flat.IndexOf("gotodetails(", StringComparison.Ordinal) < 0)
            {
                offer.CatalogNote = "The Microsoft Update Catalog has no WHQL package for " + offer.HardwareId + ".";
                return;
            }
            offer.CatalogNote = "The Microsoft Update Catalog lists a package for " + offer.HardwareId + ". It is not installed from a downloaded .cab. Windows Update is the signed install path.";
        }

        static string HardwareIdOf(string pnp)
        {
            Match match = Regex.Match(pnp ?? "", @"PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}", RegexOptions.IgnoreCase);
            return match.Success ? match.Value.ToUpperInvariant() : "";
        }

        static void RequireOfficialSignature(string path, string vendor)
        {
            string status;
            string subject;
            ReadSignature(path, out status, out subject);
            if (!string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase) || !SignerMatches(vendor, subject))
            {
                try { File.Delete(path); }
                catch { }
                throw new InvalidOperationException("The download is not signed by " + (string.IsNullOrEmpty(vendor) ? "the chip maker" : vendor) + " or by Microsoft, so it was deleted.");
            }
        }

        static void ReadSignature(string path, out string status, out string subject)
        {
            status = "";
            subject = "";
            string dir = AppPaths.Ensure();
            string script = Path.Combine(dir, "driver-sign.ps1");
            string result = Path.Combine(dir, "driver-sign.txt");
            try { if (File.Exists(result)) File.Delete(result); }
            catch { }
            string qpath = path.Replace("'", "''");
            string qres = result.Replace("'", "''");
            string ps = "$s = Get-AuthenticodeSignature -LiteralPath '" + qpath + "'\r\n"
                + "$sub = ''\r\n"
                + "if ($null -ne $s.SignerCertificate) { $sub = $s.SignerCertificate.Subject }\r\n"
                + "[System.IO.File]::WriteAllText('" + qres + "', ($s.Status.ToString() + \"`n\" + $sub))\r\n";
            File.WriteAllText(script, ps, new UTF8Encoding(true));
            string output;
            string exe = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            Command.Run(exe, "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"", out output, 60000);
            if (!File.Exists(result)) throw new InvalidOperationException("The download's signature could not be checked.");
            string[] lines = File.ReadAllLines(result, new UTF8Encoding(true));
            if (lines.Length > 0) status = (lines[0] ?? "").Trim();
            if (lines.Length > 1) subject = (lines[1] ?? "").Trim();
        }

        static bool SignerMatches(string vendor, string subject)
        {
            string s = subject ?? "";
            if (s.IndexOf("Microsoft Windows Hardware Compatibility Publisher", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (vendor == "AMD" && s.IndexOf("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (vendor == "NVIDIA" && s.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (vendor == "Intel" && s.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static void FindAmd(DriverOffer offer)
        {
            string html = HttpGet("https://www.amd.com/en/support/download/drivers.html", 800000);
            MatchCollection matches = Regex.Matches(html, @"https://drivers\.amd\.com/drivers/installer/[A-Za-z0-9_./-]+_web\.exe");
            string url = "";
            for (int i = 0; i < matches.Count; i++)
            {
                string found = matches[i].Value;
                if (url.Length == 0) url = found;
                if (found.IndexOf("/whql/", StringComparison.OrdinalIgnoreCase) >= 0) { url = found; break; }
            }
            if (url.Length == 0)
            {
                offer.VendorNote = "AMD's driver page did not list an Auto-Detect installer.";
                return;
            }
            offer.VendorUrl = url;
            bool beta = url.IndexOf("/beta/", StringComparison.OrdinalIgnoreCase) >= 0;
            offer.VendorNote = "AMD's official Auto-Detect installer will look up the driver for this graphics chip and install it if AMD has a newer one."
                + (offer.Notebook ? " The notebook package is used, not a desktop card driver." : "")
                + (beta ? " AMD currently marks this build as beta." : "");
        }

        static void FindNvidia(DriverOffer offer)
        {
            string query = NvidiaQuery(offer.DeviceName);
            offer.VendorSearch = query;
            offer.VendorUrl = "";
            string page = NvidiaPage(offer.DeviceName);
            offer.VendorNote = "NVIDIA's official page is " + page + ". Manual Driver Search is filled with the graphics model \"" + query + "\"."
                + (offer.Notebook || IsMobileGpu(offer.DeviceName) ? " This is a notebook graphics chip, so pick the laptop product on that page." : "")
                + " This app does not download that file. Windows Update stays the install path when Microsoft has a driver.";
        }

        static bool IsGeForce(string name)
        {
            return (name ?? "").IndexOf("geforce", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string NvidiaPage(string device)
        {
            if (IsGeForce(device)) return "https://www.nvidia.com/en-us/geforce/drivers/";
            return "https://www.nvidia.com/en-us/drivers/";
        }

        static string NvidiaQuery(string device)
        {
            string query = device ?? "";
            query = query.Replace("(TM)", " ").Replace("(R)", " ").Replace("™", " ").Replace("®", " ");
            query = Regex.Replace(query, "\\s+", " ").Trim();
            if (query.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase)) query = query.Substring(7).Trim();
            if (query.EndsWith(" Graphics", StringComparison.OrdinalIgnoreCase)) query = query.Substring(0, query.Length - 9).Trim();
            if (query.Length < 3) query = "GeForce";
            return query;
        }

        public static bool OpenNvidiaPage(string product, out string message)
        {
            string query = NvidiaQuery(product);
            string url = NvidiaPage(product);
            message = "NVIDIA's driver page is open. Type \"" + query + "\" in Manual Driver Search. Nothing was downloaded.";
            try
            {
                if (FillNvidiaSearch(url, query))
                {
                    message = "NVIDIA's driver page is open. The graphics model \"" + query + "\" is in the Manual Driver Search box. Pick the matching product. Nothing was downloaded.";
                    AppPaths.Audit("nvidia search " + query);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = url;
                psi.UseShellExecute = true;
                Process.Start(psi);
                AppPaths.Audit("nvidia page " + query);
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
            return true;
        }

        static void FindIntel(DriverOffer offer)
        {
            string html = HttpGet("https://www.intel.com/content/www/us/en/support/detect.html", 800000);
            Match match = Regex.Match(html, @"https://downloadmirror\.intel\.com/[A-Za-z0-9_./%-]+\.exe", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                offer.VendorNote = "Intel's official page did not list a direct installer. Windows Update is the package that can be installed from here.";
                return;
            }
            offer.VendorUrl = match.Value;
            offer.VendorNote = "Intel's official support page lists an installer for this PC.";
        }

        static bool IsGraphicsUpdate(string title, DriverOffer offer)
        {
            if (string.IsNullOrEmpty(title)) return false;
            string t = title.ToLowerInvariant();
            if (t.IndexOf("audio", StringComparison.Ordinal) >= 0) return false;
            if (t.IndexOf("bluetooth", StringComparison.Ordinal) >= 0) return false;
            if (t.IndexOf("radeon", StringComparison.Ordinal) >= 0) return true;
            if (t.IndexOf("geforce", StringComparison.Ordinal) >= 0 || t.IndexOf("nvidia", StringComparison.Ordinal) >= 0) return true;
            if (t.IndexOf("intel", StringComparison.Ordinal) >= 0 && (t.IndexOf("graphics", StringComparison.Ordinal) >= 0 || t.IndexOf("display", StringComparison.Ordinal) >= 0 || t.IndexOf("arc", StringComparison.Ordinal) >= 0))
                return true;
            string model = ModelToken(offer == null ? "" : offer.DeviceName);
            return model.Length >= 3 && t.IndexOf(model, StringComparison.Ordinal) >= 0;
        }

        static string ModelToken(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            Match match = Regex.Match(name, @"[0-9]{3,4}[a-zA-Z]?");
            return match.Success ? match.Value.ToLowerInvariant() : "";
        }

        static string VendorOf(string pnp, string name)
        {
            string p = (pnp ?? "").ToUpperInvariant();
            string n = (name ?? "").ToLowerInvariant();
            if (p.IndexOf("VEN_1002", StringComparison.Ordinal) >= 0 || n.IndexOf("radeon", StringComparison.Ordinal) >= 0 || n.IndexOf("amd", StringComparison.Ordinal) >= 0) return "AMD";
            if (p.IndexOf("VEN_10DE", StringComparison.Ordinal) >= 0 || n.IndexOf("nvidia", StringComparison.Ordinal) >= 0 || n.IndexOf("geforce", StringComparison.Ordinal) >= 0) return "NVIDIA";
            if (p.IndexOf("VEN_8086", StringComparison.Ordinal) >= 0 || n.IndexOf("intel", StringComparison.Ordinal) >= 0) return "Intel";
            if (p.IndexOf("VEN_17CB", StringComparison.Ordinal) >= 0 || n.IndexOf("qualcomm", StringComparison.Ordinal) >= 0 || n.IndexOf("adreno", StringComparison.Ordinal) >= 0) return "Qualcomm";
            return "";
        }

        static bool IsRealGpu(string name, string pnp)
        {
            string n = (name ?? "").ToLowerInvariant();
            string p = (pnp ?? "").ToLowerInvariant();
            if (n.Length == 0) return false;
            if (n.IndexOf("remote", StringComparison.Ordinal) >= 0) return false;
            if (n.IndexOf("basic display", StringComparison.Ordinal) >= 0) return false;
            if (p.IndexOf("indirectdisplay", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        static bool TryDate(object value, out DateTime date)
        {
            date = DateTime.MinValue;
            if (value == null) return false;
            try
            {
                date = ManagementDateTimeConverter.ToDateTime(Convert.ToString(value));
                return date.Year >= 2015;
            }
            catch { return false; }
        }

        static bool Allowed(Uri uri)
        {
            if (uri == null || !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;
            string host = uri.Host.ToLowerInvariant();
            if (host == "drivers.amd.com") return true;
            if (host == "us.download.nvidia.com" || host == "international.download.nvidia.com" || host == "download.nvidia.com") return true;
            if (host == "downloadmirror.intel.com") return true;
            return false;
        }

        static bool FillNvidiaSearch(string url, string query)
        {
            string edge = EdgePath();
            if (edge.Length == 0) return false;
            int port = FreePort();
            string profile = Path.Combine(Path.GetTempPath(), "WinHealthGuard-nvidia-search");
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = edge;
            psi.Arguments = "--user-data-dir=\"" + profile + "\" --remote-debugging-port=" + port.ToString(CultureInfo.InvariantCulture)
                + " --remote-allow-origins=* --no-first-run --no-default-browser-check --disable-sync --new-window \"" + url + "\"";
            psi.UseShellExecute = false;
            Process.Start(psi);
            string ws = "";
            DateTime until = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < until && ws.Length == 0)
            {
                System.Threading.Thread.Sleep(400);
                ws = NvidiaSocket(port, url);
            }
            if (ws.Length == 0) return false;
            string expression = "(function(){var buttons=document.querySelectorAll('button');for(var i=0;i<buttons.length;i++){var t=(buttons[i].innerText||'').replace(/\\s+/g,' ').trim();if(t==='Reject Optional'){buttons[i].click();break;}}var el=document.getElementById('nvProductInput');if(!el)return 'missing';el.scrollIntoView({block:'center'});el.focus();var d=Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value');var name="
                + JsQuote(query) + ";if(d&&d.set)d.set.call(el,name);else el.value=name;el.dispatchEvent(new Event('input',{bubbles:true}));return el.value;})()";
            until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                string value = EvalPage(ws, expression);
                if (string.Equals(value, query, StringComparison.Ordinal)) return true;
                System.Threading.Thread.Sleep(700);
            }
            return false;
        }

        static string EdgePath()
        {
            string[] roots = new string[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };
            for (int i = 0; i < roots.Length; i++)
            {
                if (string.IsNullOrEmpty(roots[i])) continue;
                string path = Path.Combine(roots[i], "Microsoft", "Edge", "Application", "msedge.exe");
                if (File.Exists(path)) return path;
            }
            return "";
        }

        static int FreePort()
        {
            System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        static string NvidiaSocket(int port, string pageUrl)
        {
            try
            {
                string needle = (pageUrl ?? "").TrimEnd('/');
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/json/list");
                req.Timeout = 1500;
                req.ReadWriteTimeout = 1500;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(res.GetResponseStream()))
                {
                    string json = reader.ReadToEnd();
                    int at = 0;
                    while (at < json.Length)
                    {
                        int hit = json.IndexOf("webSocketDebuggerUrl", at, StringComparison.Ordinal);
                        if (hit < 0) break;
                        int back = hit - 900;
                        if (back < 0) back = 0;
                        string window = json.Substring(back, hit - back);
                        if (needle.Length > 0 && window.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Match match = Regex.Match(json.Substring(hit), "ws://[^\"\\s]+");
                            if (match.Success) return match.Value;
                        }
                        at = hit + 20;
                    }
                }
            }
            catch { }
            return "";
        }

        static string EvalPage(string wsUrl, string expression)
        {
            Uri uri = new Uri(wsUrl);
            using (System.Net.Sockets.TcpClient client = new System.Net.Sockets.TcpClient())
            {
                client.Connect(IPAddress.Loopback, uri.Port);
                using (NetworkStream stream = client.GetStream())
                {
                    string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                    string request = "GET " + uri.PathAndQuery + " HTTP/1.1\r\nHost: 127.0.0.1:" + uri.Port.ToString(CultureInfo.InvariantCulture)
                        + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key
                        + "\r\nSec-WebSocket-Version: 13\r\n\r\n";
                    byte[] head = Encoding.ASCII.GetBytes(request);
                    stream.Write(head, 0, head.Length);
                    string handshake = ReadHttp(stream);
                    if (handshake.IndexOf(" 101 ", StringComparison.Ordinal) < 0) return "";
                    string payload = "{\"id\":1,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":" + JsonQuote(expression) + ",\"returnByValue\":true}}";
                    SendWs(stream, payload);
                    string reply = ReadWsText(stream);
                    Match match = Regex.Match(reply, "\"value\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                    if (!match.Success) return "";
                    return Regex.Unescape(match.Groups[1].Value);
                }
            }
        }

        static string ReadHttp(NetworkStream stream)
        {
            stream.ReadTimeout = 8000;
            StringBuilder sb = new StringBuilder();
            while (sb.Length < 4096)
            {
                int b = stream.ReadByte();
                if (b < 0) break;
                sb.Append((char)b);
                if (sb.Length >= 4 && sb[sb.Length - 4] == '\r' && sb[sb.Length - 3] == '\n' && sb[sb.Length - 2] == '\r' && sb[sb.Length - 1] == '\n')
                    break;
            }
            return sb.ToString();
        }

        static void SendWs(NetworkStream stream, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            byte[] mask = new byte[4];
            new Random().NextBytes(mask);
            int header = data.Length < 126 ? 6 : 8;
            byte[] frame = new byte[header + data.Length];
            frame[0] = 0x81;
            int offset;
            if (data.Length < 126)
            {
                frame[1] = (byte)(0x80 | data.Length);
                offset = 2;
            }
            else
            {
                frame[1] = (byte)(0x80 | 126);
                frame[2] = (byte)((data.Length >> 8) & 255);
                frame[3] = (byte)(data.Length & 255);
                offset = 4;
            }
            Buffer.BlockCopy(mask, 0, frame, offset, 4);
            for (int i = 0; i < data.Length; i++) frame[offset + 4 + i] = (byte)(data[i] ^ mask[i % 4]);
            stream.Write(frame, 0, frame.Length);
        }

        static string ReadWsText(NetworkStream stream)
        {
            stream.ReadTimeout = 8000;
            for (int frames = 0; frames < 8; frames++)
            {
                int b0 = stream.ReadByte();
                int b1 = stream.ReadByte();
                if (b0 < 0 || b1 < 0) return "";
                int opcode = b0 & 15;
                int len = b1 & 127;
                if (len == 126)
                {
                    int hi = stream.ReadByte();
                    int lo = stream.ReadByte();
                    if (hi < 0 || lo < 0) return "";
                    len = (hi << 8) | lo;
                }
                else if (len == 127) return "";
                if ((b1 & 128) != 0)
                {
                    for (int i = 0; i < 4; i++) if (stream.ReadByte() < 0) return "";
                }
                byte[] payload = new byte[len];
                int got = 0;
                while (got < len)
                {
                    int n = stream.Read(payload, got, len - got);
                    if (n <= 0) return "";
                    got += n;
                }
                if (opcode == 9)
                {
                    byte[] pong = new byte[2 + len];
                    pong[0] = 0x8A;
                    pong[1] = (byte)len;
                    Buffer.BlockCopy(payload, 0, pong, 2, len);
                    stream.Write(pong, 0, pong.Length);
                    continue;
                }
                if (opcode == 1) return Encoding.UTF8.GetString(payload);
                if (opcode == 8) return "";
            }
            return "";
        }

        static string JsQuote(string value)
        {
            return JsonQuote(value ?? "");
        }

        static string JsonQuote(string value)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' || c == '"') sb.Append('\\').Append(c);
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        static string HttpGet(string url, int maxChars)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 25000;
            req.ReadWriteTimeout = 25000;
            req.UserAgent = Agent();
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (Stream stream = res.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                char[] buf = new char[maxChars];
                int got = 0;
                while (got < maxChars)
                {
                    int n = reader.Read(buf, got, maxChars - got);
                    if (n <= 0) break;
                    got += n;
                }
                return new string(buf, 0, got);
            }
        }

        static void Download(Uri uri, string dest)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(uri);
            req.Timeout = 120000;
            req.ReadWriteTimeout = 120000;
            req.UserAgent = Agent();
            req.Referer = RefererFor(uri);
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            {
                if (!Allowed(res.ResponseUri)) throw new InvalidOperationException("The download left the official site.");
                if (res.ContentLength > 900L * 1024L * 1024L) throw new InvalidOperationException("The installer is larger than expected.");
                using (Stream input = res.GetResponseStream())
                using (FileStream output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buf = new byte[65536];
                    long total = 0;
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        total += n;
                        if (total > 900L * 1024L * 1024L) throw new InvalidOperationException("The installer is larger than expected.");
                        output.Write(buf, 0, n);
                    }
                }
            }
        }

        static string RefererFor(Uri uri)
        {
            string host = uri == null ? "" : uri.Host.ToLowerInvariant();
            if (host.IndexOf("nvidia.com", StringComparison.Ordinal) >= 0) return "https://www.nvidia.com/";
            if (host.IndexOf("intel.com", StringComparison.Ordinal) >= 0) return "https://www.intel.com/";
            return "https://www.amd.com/";
        }

        static string Agent()
        {
            return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        }

        static string Append(string left, string right)
        {
            if (string.IsNullOrEmpty(left)) return right ?? "";
            if (string.IsNullOrEmpty(right)) return left;
            return left + " " + right;
        }
    }

    sealed class DriverPrompt : Form
    {
        public string Choice = "";

        public DriverPrompt(DriverOffer offer)
        {
            Text = "Graphics driver";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontUi;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(760, 560);
            TextBox body = new TextBox();
            body.Text = offer.Report();
            body.Multiline = true;
            body.ReadOnly = true;
            body.ScrollBars = ScrollBars.Vertical;
            body.WordWrap = true;
            body.BorderStyle = BorderStyle.None;
            body.ForeColor = Theme.Text;
            body.BackColor = Theme.Bg;
            Controls.Add(body);
            List<Button> actions = new List<Button>();
            if (!string.IsNullOrEmpty(offer.WindowsUpdateTitle))
            {
                Button install = Make("Windows Update", true);
                install.Click += delegate { Choice = "wu"; Close(); };
                actions.Add(install);
            }
            if (!string.IsNullOrEmpty(offer.VendorUrl))
            {
                bool beta = offer.VendorUrl.IndexOf("/beta/", StringComparison.OrdinalIgnoreCase) >= 0;
                bool primary = string.IsNullOrEmpty(offer.WindowsUpdateTitle);
                Button download = Make(beta ? "Beta installer" : "Official installer", primary);
                download.Click += delegate { Choice = "vendor"; Close(); };
                actions.Add(download);
            }
            if (offer.Vendor == "NVIDIA" && !string.IsNullOrEmpty(offer.VendorSearch))
            {
                bool primary = string.IsNullOrEmpty(offer.WindowsUpdateTitle);
                Button search = Make("NVIDIA search", primary);
                search.Click += delegate { Choice = "nvidia"; Close(); };
                actions.Add(search);
            }
            Button close = Make("Close", false);
            close.Click += delegate { Close(); };
            actions.Add(close);
            int bodyH = 320;
            body.SetBounds(18, 16, ClientSize.Width - 36, bodyH);
            int x = 18;
            int y = 16 + bodyH + 16;
            int limit = ClientSize.Width - 18;
            for (int i = 0; i < actions.Count; i++)
            {
                Button button = actions[i];
                int bw = TextRenderer.MeasureText(button.Text, button.Font).Width + 28;
                if (bw < 108) bw = 108;
                if (x > 18 && x + bw > limit)
                {
                    x = 18;
                    y += 42;
                }
                button.SetBounds(x, y, bw, 34);
                Controls.Add(button);
                x += bw + 12;
            }
            y += 46;
            if (!string.IsNullOrEmpty(offer.ThirdPartyUrl))
            {
                Button third = Make("Softpedia", false);
                int thirdW = TextRenderer.MeasureText(third.Text, third.Font).Width + 28;
                if (thirdW < 120) thirdW = 120;
                third.SetBounds(18, y, thirdW, 34);
                third.Click += delegate
                {
                    DialogResult answer = MessageBox.Show(this,
                        "Softpedia is not the chip maker and not Microsoft. A driver on that page can be old, the wrong package for this PC, or wrapped in Softpedia's own download.\n\nThe page opens on Windows drivers, with the operating system set to Windows, graphics boards updated in the last 6 months, sorted by most downloaded.\n\nWindows Update is checked first, then the maker's site. Open the Softpedia page anyway? This app will not download the file.",
                        "Third-party driver page",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning);
                    if (answer != DialogResult.OK) return;
                    Choice = "third";
                    Close();
                };
                Controls.Add(third);
                y += 46;
            }
            ClientSize = new Size(ClientSize.Width, y + 16);
        }

        static Button Make(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? Theme.Accent : Theme.Card2;
            button.ForeColor = primary ? Theme.OnAccent : Theme.Text;
            button.Font = primary ? new Font("Segoe UI", 9f, FontStyle.Bold) : Theme.FontUi;
            button.Cursor = Cursors.Hand;
            return button;
        }
    }
}
