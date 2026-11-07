using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WinHealthGuard
{
    sealed class FixMeter
    {
        public bool Ready;
        public int Score;
        public double DiskFreeGb;
        public int SysMainMb;
        public int StartupUpdaters;
        public int DwmMb;
        public bool Truncate;
        public bool PageAuto;
        public bool PageGrow;
        public string GpuDriver = "";
        public string GpuDate = "";

        public static FixMeter From(HealthSnapshot snap)
        {
            FixMeter meter = new FixMeter();
            if (snap == null) return meter;
            meter.Ready = snap.CpuValid;
            meter.Score = snap.Score;
            meter.DiskFreeGb = snap.SystemFreeGb;
            meter.SysMainMb = snap.SysMainMb;
            meter.StartupUpdaters = snap.StartupUpdaters;
            meter.DwmMb = snap.DwmMb;
            meter.Truncate = snap.TruncateMemory;
            meter.PageAuto = snap.AutoPageFile;
            meter.PageGrow = snap.PageCanGrow;
            meter.GpuDriver = snap.GpuDriver ?? "";
            if (snap.GpuDriverDate.Year >= 2015) meter.GpuDate = snap.GpuDriverDate.ToString("yyyy-MM-dd");
            return meter;
        }
    }

    sealed class FixProof
    {
        public string Id = "";
        public DateTime When = DateTime.MinValue;
        public int ScoreBefore;
        public int ScoreAfter;
        public string Result = "";
    }

    static class FixCheck
    {
        public static List<FixProof> Load()
        {
            List<FixProof> list = new List<FixProof>();
            try
            {
                string path = AppPaths.RepairsFile;
                if (!File.Exists(path)) return list;
                string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
                for (int i = 0; i < lines.Length; i++)
                {
                    FixProof proof = Parse(lines[i]);
                    if (proof != null) list.Add(proof);
                }
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
            }
            return list;
        }

        public static void Save(List<FixProof> list)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                int start = 0;
                if (list != null && list.Count > 40) start = list.Count - 40;
                if (list != null)
                {
                    for (int i = start; i < list.Count; i++)
                    {
                        FixProof proof = list[i];
                        if (proof == null || string.IsNullOrEmpty(proof.Result)) continue;
                        string result = proof.Result.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
                        sb.Append(proof.When.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                        sb.Append('\t');
                        sb.Append(proof.Id ?? "");
                        sb.Append('\t');
                        sb.Append(proof.ScoreBefore.ToString(CultureInfo.InvariantCulture));
                        sb.Append('\t');
                        sb.Append(proof.ScoreAfter.ToString(CultureInfo.InvariantCulture));
                        sb.Append('\t');
                        sb.AppendLine(result);
                    }
                }
                File.WriteAllText(AppPaths.RepairsFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
            }
        }

        public static string Describe(string id, FixMeter before, FixMeter after)
        {
            if (after == null) after = new FixMeter();
            if (before == null) before = new FixMeter();
            StringBuilder sb = new StringBuilder();
            if (!before.Ready)
                sb.Append("Health score is now " + after.Score.ToString() + ". ");
            else if (after.Score > before.Score)
                sb.Append("Health score went from " + before.Score.ToString() + " to " + after.Score.ToString() + ". ");
            else if (after.Score < before.Score)
                sb.Append("Health score went from " + before.Score.ToString() + " to " + after.Score.ToString() + ". ");
            else
                sb.Append("Health score stayed at " + after.Score.ToString() + ". ");

            bool opener = id == "memory" || id == "wu" || id == "gaming" || id == "amd" || id == "nvidia";
            bool improved = false;
            bool failed = false;
            sb.Append(Specific(id, before, after, ref improved, ref failed));
            bool better = !opener && !failed && (improved || (before.Ready && after.Score > before.Score));
            if (opener)
                sb.Append("This only opened a settings screen, so there is no measured change yet.");
            else if (better)
                sb.Append("This PC is in better shape than before the repair.");
            else
                sb.Append("Nothing measured got better yet. Crash History shows whether the same error comes back.");
            return sb.ToString().Trim();
        }

        static string Specific(string id, FixMeter before, FixMeter after, ref bool improved, ref bool failed)
        {
            if (id == "maxmem")
            {
                if (before.Truncate && !after.Truncate)
                {
                    improved = true;
                    return "Checked: the boot memory limit is off. ";
                }
                if (after.Truncate)
                {
                    failed = true;
                    return "Checked: the boot memory limit is still on. ";
                }
                return "Checked: no boot memory limit is set. ";
            }
            if (id == "pagefile")
            {
                bool wasStuck = before.Ready && !before.PageAuto && !before.PageGrow;
                bool canGrow = after.PageAuto || after.PageGrow;
                if (wasStuck && canGrow)
                {
                    improved = true;
                    return "Checked: Windows can grow the page file. ";
                }
                if (!canGrow)
                {
                    failed = true;
                    return "Checked: the page file is still a fixed size. ";
                }
                return "Checked: the page file can grow. ";
            }
            if (id == "sysmain")
            {
                if (before.SysMainMb >= 80 && after.SysMainMb + 50 < before.SysMainMb)
                {
                    improved = true;
                    return "SysMain dropped from " + before.SysMainMb.ToString() + " MB to " + after.SysMainMb.ToString() + " MB. ";
                }
                if (after.SysMainMb >= 80) failed = true;
                return "SysMain is using " + after.SysMainMb.ToString() + " MB. ";
            }
            if (id == "startuptrim")
            {
                if (after.StartupUpdaters < before.StartupUpdaters)
                {
                    improved = true;
                    return "Startup updaters went from " + before.StartupUpdaters.ToString() + " to " + after.StartupUpdaters.ToString() + ". ";
                }
                if (before.StartupUpdaters > 0) failed = true;
                return "Startup updaters are still " + after.StartupUpdaters.ToString() + ". ";
            }
            if (id == "shaders")
            {
                if (after.DiskFreeGb >= before.DiskFreeGb + 0.1)
                {
                    improved = true;
                    return "Free space went from " + Gb(before.DiskFreeGb) + " GB to " + Gb(after.DiskFreeGb) + " GB. ";
                }
                return "Free space is " + Gb(after.DiskFreeGb) + " GB. ";
            }
            if (id == "visualfx")
            {
                if (before.DwmMb >= 100 && after.DwmMb + 80 < before.DwmMb)
                {
                    improved = true;
                    return "Desktop Window Manager dropped from " + before.DwmMb.ToString() + " MB to " + after.DwmMb.ToString() + " MB. ";
                }
                return "Most animations are off. Memory use can fall after you sign in again. ";
            }
            if (id == "browsers")
                return "Edge and Chrome release memory the next time they close. ";
            if (id == "overlayoff")
                return "Overlays are off. A new overlay crash would show in Crash History. ";
            if (id == "faststartup")
                return "Fast Startup is off. The next shutdown is a full shutdown. ";
            if (id == "dns")
                return "The DNS cache was cleared. ";
            if (id == "tdr")
                return "The graphics driver was given more time to recover. That applies to the next game. ";
            if (id == "hagsoff" || id == "hagson")
                return "Hardware-accelerated GPU scheduling was changed. It applies after you restart. ";
            if (id == "driver")
            {
                bool changed = before.Ready && (before.GpuDriver != after.GpuDriver || before.GpuDate != after.GpuDate);
                if (changed)
                {
                    improved = true;
                    return "Graphics driver changed from " + DriverText(before) + " to " + DriverText(after) + ". ";
                }
                failed = true;
                return "The running driver is still " + DriverText(after) + ". It updates after Windows finishes the install. ";
            }
            if (id == "sfc" || id == "dism" || id == "dismsfc")
                return "";
            if (id == "chkdsk" || id == "fault50")
                return "The disk check runs on the next restart. ";
            if (id == "steam")
                return "Steam checked the game files. ";
            if (id == "fullscreen")
                return "Fullscreen optimizations are off for that program. It applies the next time the program starts. ";
            if (id == "memory" || id == "wu" || id == "gaming" || id == "amd" || id == "nvidia")
                return "";
            return "";
        }

        static string DriverText(FixMeter meter)
        {
            if (meter == null || string.IsNullOrEmpty(meter.GpuDriver)) return "unknown";
            if (string.IsNullOrEmpty(meter.GpuDate)) return meter.GpuDriver;
            return meter.GpuDriver + " (" + meter.GpuDate + ")";
        }

        static string Gb(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        static FixProof Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] parts = line.Split('\t');
            if (parts.Length < 5) return null;
            DateTime when;
            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when)) return null;
            int before;
            int after;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out before)) return null;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out after)) return null;
            FixProof proof = new FixProof();
            proof.When = when.ToUniversalTime();
            proof.Id = parts[1] ?? "";
            proof.ScoreBefore = before;
            proof.ScoreAfter = after;
            proof.Result = parts[4] ?? "";
            return proof;
        }
    }
}
