using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace WinHealthGuard
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(16, 18, 22);
        public static readonly Color Sidebar = Color.FromArgb(12, 14, 18);
        public static readonly Color Card = Color.FromArgb(28, 32, 40);
        public static readonly Color Card2 = Color.FromArgb(36, 41, 51);
        public static readonly Color Line = Color.FromArgb(48, 54, 66);
        public static readonly Color Text = Color.FromArgb(236, 238, 242);
        public static readonly Color Muted = Color.FromArgb(148, 156, 170);
        public static readonly Color Accent = Color.FromArgb(245, 196, 0);
        public static readonly Color AccentSoft = Color.FromArgb(54, 46, 16);
        public static readonly Color OnAccent = Color.FromArgb(22, 22, 22);
        public static readonly Color Good = Color.FromArgb(46, 196, 132);
        public static readonly Color Warn = Color.FromArgb(232, 176, 64);
        public static readonly Color Bad = Color.FromArgb(232, 92, 92);

        public static readonly Font FontUi = new Font("Segoe UI", 9.5f);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.5f);
        public static readonly Font FontNav = new Font("Segoe UI", 10f);
        public static readonly Font FontTitle = new Font("Segoe UI", 18f, FontStyle.Bold);
        public static readonly Font FontSection = new Font("Segoe UI", 12f, FontStyle.Bold);
        public static readonly Font FontValue = new Font("Segoe UI", 16f, FontStyle.Bold);
        public static readonly Font FontScore = new Font("Segoe UI", 28f, FontStyle.Bold);
        public static readonly Font FontMono = new Font("Consolas", 9.5f);

        public static Color ForStatus(string status)
        {
            if (status == "Critical") return Bad;
            if (status == "Attention") return Warn;
            return Good;
        }
    }

    sealed class AppSettings
    {
        public bool StartWithWindows { get; set; }
        public bool NotifyOnCrash { get; set; }
        public bool BoostPowerWhileGaming { get; set; }
        public bool PauseSampling { get; set; }
        public int IdleSampleSeconds { get; set; }
        public string ExtraGames { get; set; }
        public string IgnoreProcesses { get; set; }
        public bool PowerBoostActive { get; set; }
        public string PowerBoostRestoreGuid { get; set; }

        public static AppSettings CreateDefault()
        {
            AppSettings s = new AppSettings();
            s.StartWithWindows = false;
            s.NotifyOnCrash = true;
            s.BoostPowerWhileGaming = true;
            s.PauseSampling = false;
            s.IdleSampleSeconds = 30;
            s.ExtraGames = "";
            s.IgnoreProcesses = "";
            s.PowerBoostActive = false;
            s.PowerBoostRestoreGuid = "";
            return s;
        }

        public void Normalize()
        {
            if (ExtraGames == null) ExtraGames = "";
            if (IgnoreProcesses == null) IgnoreProcesses = "";
            if (PowerBoostRestoreGuid == null) PowerBoostRestoreGuid = "";
            if (IdleSampleSeconds != 15 && IdleSampleSeconds != 30 && IdleSampleSeconds != 60)
                IdleSampleSeconds = 30;
        }
    }

    struct CpuStamp
    {
        public ulong Idle;
        public ulong Kernel;
        public ulong User;
        public bool Has;
    }

    sealed class SlowFacts
    {
        public string GpuName = "";
        public string GpuDriver = "";
        public string GpuPnp = "";
        public DateTime GpuDriverDate = DateTime.MinValue;
        public bool GpuKnown;
        public bool ShowAmd;
        public bool ShowNvidia;
        public string DiskStatus = "";
        public bool PageFileKnown;
        public bool AutoPageFile = true;
        public bool PageCanGrow = true;
        public int NonPagedMb;
        public int HardwareReservedMb;
        public bool TruncateMemory;
        public int RamModules;
        public int RamSlots;
        public int SysMainMb;
        public int DwmMb;
        public int StartupUpdaters;
        public string TopRam = "";
        public List<string> OtherDiskNotes = new List<string>();
    }

    sealed class HealthSnapshot
    {
        public DateTime Time;
        public bool CpuValid;
        public double CpuPercent;
        public double RamPercent;
        public ulong RamUsedMb;
        public ulong RamTotalMb;
        public ulong CommitUsedMb;
        public ulong CommitTotalMb;
        public double SystemFreeGb;
        public double SystemFreePercent;
        public string SystemDrive = "";
        public string PowerGuid = "";
        public string PowerName = "";
        public bool OnAc = true;
        public int BatteryPercent = -1;
        public bool PendingReboot;
        public string ForegroundName = "";
        public bool ForegroundFullscreen;
        public bool ForegroundIsGame;
        public string GpuName = "";
        public string GpuDriver = "";
        public DateTime GpuDriverDate;
        public bool GpuKnown;
        public string DiskStatus = "";
        public bool PageFileKnown;
        public bool AutoPageFile = true;
        public bool PageCanGrow = true;
        public int NonPagedMb;
        public int HardwareReservedMb;
        public bool TruncateMemory;
        public int RamModules;
        public int RamSlots;
        public int SysMainMb;
        public int DwmMb;
        public int StartupUpdaters;
        public string TopRam = "";
        public int Score = 100;
        public string Status = "Healthy";
        public List<HealthIssue> Notes = new List<HealthIssue>();

        public void ApplySlow(SlowFacts facts)
        {
            if (facts == null) return;
            GpuName = facts.GpuName ?? "";
            GpuDriver = facts.GpuDriver ?? "";
            GpuDriverDate = facts.GpuDriverDate;
            GpuKnown = facts.GpuKnown;
            DiskStatus = facts.DiskStatus ?? "";
            PageFileKnown = facts.PageFileKnown;
            AutoPageFile = facts.AutoPageFile;
            PageCanGrow = facts.PageCanGrow;
            NonPagedMb = facts.NonPagedMb;
            HardwareReservedMb = facts.HardwareReservedMb;
            TruncateMemory = facts.TruncateMemory;
            RamModules = facts.RamModules;
            RamSlots = facts.RamSlots;
            SysMainMb = facts.SysMainMb;
            DwmMb = facts.DwmMb;
            StartupUpdaters = facts.StartupUpdaters;
            TopRam = facts.TopRam ?? "";
        }
    }

    sealed class CrashEvent
    {
        public string Id = "";
        public DateTime Time;
        public string Kind = "";
        public string Provider = "";
        public int EventId;
        public string Process = "";
        public string FaultModule = "";
        public string ExceptionCode = "";
        public string BugCheck = "";
        public string AppPath = "";
        public string Summary = "";
        public string Detail = "";
        public string Suggestion = "";
        public string RepairId = "";
        public bool LikelyGame;
        public bool Overlay;
        public string DuringGame = "";
        public bool HasContext;
        public double Cpu;
        public double Ram;
        public double DiskFreeGb;
        public bool OnAc;
        public string PowerScheme = "";

        public string ToJson()
        {
            return "{"
                + "\"id\":\"" + Json.Escape(Id) + "\","
                + "\"time\":\"" + Time.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "\","
                + "\"kind\":\"" + Json.Escape(Kind) + "\","
                + "\"provider\":\"" + Json.Escape(Provider) + "\","
                + "\"eventId\":" + EventId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"process\":\"" + Json.Escape(Process) + "\","
                + "\"faultModule\":\"" + Json.Escape(FaultModule) + "\","
                + "\"exceptionCode\":\"" + Json.Escape(ExceptionCode) + "\","
                + "\"bugCheck\":\"" + Json.Escape(BugCheck) + "\","
                + "\"appPath\":\"" + Json.Escape(AppPath) + "\","
                + "\"summary\":\"" + Json.Escape(Summary) + "\","
                + "\"detail\":\"" + Json.Escape(Detail) + "\","
                + "\"suggestion\":\"" + Json.Escape(Suggestion) + "\","
                + "\"repairId\":\"" + Json.Escape(RepairId) + "\","
                + "\"likelyGame\":" + (LikelyGame ? "true" : "false") + ","
                + "\"overlay\":" + (Overlay ? "true" : "false") + ","
                + "\"duringGame\":\"" + Json.Escape(DuringGame) + "\","
                + "\"hasContext\":" + (HasContext ? "true" : "false") + ","
                + "\"cpu\":" + Cpu.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"ram\":" + Ram.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"diskFreeGb\":" + DiskFreeGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"onAc\":" + (OnAc ? "true" : "false") + ","
                + "\"powerScheme\":\"" + Json.Escape(PowerScheme) + "\""
                + "}";
        }
    }

    sealed class RepairResult
    {
        public bool Ok;
        public bool Cancelled;
        public bool Reboot;
        public string Message = "";

        public static RepairResult Success(string message)
        {
            RepairResult r = new RepairResult();
            r.Ok = true;
            r.Message = message;
            return r;
        }

        public static RepairResult Fail(string message)
        {
            RepairResult r = new RepairResult();
            r.Ok = false;
            r.Message = message ?? "The repair failed.";
            return r;
        }
    }

    sealed class HealthIssue
    {
        public string Title = "";
        public string Detail = "";
        public string Button = "";
        public string RepairId = "";
        public int Penalty;
    }

    sealed class FixSpec
    {
        public string Id;
        public string Title;
        public string Detail;
        public string Button = "Fix";
        public bool Admin;
        public bool Reboot;
        public bool LongRunning;
        public bool Opener;
    }

    sealed class ActivityItem
    {
        public DateTime Time;
        public string Text;

        public ActivityItem(string text)
        {
            Time = DateTime.Now;
            Text = text;
        }
    }

    static class Json
    {
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            StringBuilder sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("X4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    static class TextUtil
    {
        public static string OneLine(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length > 400) return value.Substring(0, 400);
            return value;
        }

        public static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes.ToString() + " B";
            double v = bytes;
            string[] units = new string[] { "KB", "MB", "GB", "TB" };
            int i = -1;
            do
            {
                v /= 1024.0;
                i++;
            }
            while (v >= 1024.0 && i < units.Length - 1);
            return v.ToString("0.0") + " " + units[i];
        }

        public static string Clip(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? "";
            return value.Substring(0, max);
        }
    }
}
