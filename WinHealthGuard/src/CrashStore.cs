using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WinHealthGuard
{
    static class CrashStore
    {
        static readonly object Gate = new object();

        public static List<CrashEvent> Load()
        {
            List<CrashEvent> list = new List<CrashEvent>();
            try
            {
                string path = AppPaths.CrashesFile;
                if (!File.Exists(path)) return list;
                string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line)) continue;
                    try
                    {
                        CrashEvent ev = FromDictionary(ser.Deserialize<Dictionary<string, object>>(line));
                        if (ev != null && !string.IsNullOrEmpty(ev.Id)) list.Add(ev);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
            }
            return list;
        }

        public static void Append(CrashEvent ev)
        {
            if (ev == null) return;
            lock (Gate)
            {
                string path = Path.Combine(AppPaths.Ensure(), "crashes.jsonl");
                using (FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.WriteLine(ev.ToJson());
                }
                try
                {
                    FileInfo info = new FileInfo(path);
                    if (info.Length > 2500000) Trim(path);
                }
                catch
                {
                }
            }
        }

        static void Trim(string path)
        {
            string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
            int start = lines.Length > 1200 ? lines.Length - 1200 : 0;
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                for (int i = start; i < lines.Length; i++)
                {
                    if (!string.IsNullOrEmpty(lines[i])) writer.WriteLine(lines[i]);
                }
            }
        }

        static CrashEvent FromDictionary(Dictionary<string, object> data)
        {
            if (data == null) return null;
            CrashEvent ev = new CrashEvent();
            ev.Id = Str(data, "id");
            ev.Kind = Str(data, "kind");
            ev.Provider = Str(data, "provider");
            ev.EventId = (int)Num(data, "eventId");
            ev.Process = Str(data, "process");
            ev.FaultModule = Str(data, "faultModule");
            ev.ExceptionCode = Str(data, "exceptionCode");
            ev.BugCheck = Str(data, "bugCheck");
            ev.AppPath = Str(data, "appPath");
            ev.Summary = Str(data, "summary");
            ev.Detail = Str(data, "detail");
            ev.Suggestion = Str(data, "suggestion");
            ev.RepairId = Str(data, "repairId");
            ev.LikelyGame = Flag(data, "likelyGame");
            ev.Overlay = Flag(data, "overlay");
            ev.DuringGame = Str(data, "duringGame");
            ev.HasContext = Flag(data, "hasContext");
            ev.Cpu = Num(data, "cpu");
            ev.Ram = Num(data, "ram");
            ev.DiskFreeGb = Num(data, "diskFreeGb");
            ev.OnAc = Flag(data, "onAc");
            ev.PowerScheme = Str(data, "powerScheme");
            DateTime time;
            if (!DateTime.TryParse(Str(data, "time"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out time))
                time = DateTime.UtcNow;
            ev.Time = time;
            return ev;
        }

        static string Str(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null) return "";
            return Convert.ToString(value);
        }

        static double Num(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null) return 0;
            try { return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        static bool Flag(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null) return false;
            if (value is bool) return (bool)value;
            return string.Equals(Convert.ToString(value), "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
