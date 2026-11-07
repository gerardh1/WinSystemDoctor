using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace WinHealthGuard
{
    static class SelfTest
    {
        public static int Run()
        {
            StringBuilder sb = new StringBuilder();
            int code = 0;
            try
            {
                CpuStamp stamp = new CpuStamp();
                CpuStamp next;
                HealthSnapshot first = SystemProbe.Sample(stamp, out next);
                Thread.Sleep(400);
                HealthSnapshot snap = SystemProbe.Sample(next, out stamp);
                sb.AppendLine("cpuValid=" + snap.CpuValid.ToString());
                sb.AppendLine("cpu=" + snap.CpuPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("ramMb=" + snap.RamUsedMb.ToString() + "/" + snap.RamTotalMb.ToString());
                sb.AppendLine("ramPercent=" + snap.RamPercent.ToString("0"));
                sb.AppendLine("commitMb=" + snap.CommitUsedMb.ToString() + "/" + snap.CommitTotalMb.ToString());
                sb.AppendLine("diskFreeGb=" + snap.SystemFreeGb.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("powerGuid=" + snap.PowerGuid);
                sb.AppendLine("powerName=" + snap.PowerName);
                sb.AppendLine("onAc=" + snap.OnAc.ToString());
                sb.AppendLine("foreground=" + snap.ForegroundName);

                string same;
                bool setOk = PowerPlans.SetActive(snap.PowerGuid, out same);
                sb.AppendLine("setActiveSame=" + setOk.ToString() + " " + TextUtil.OneLine(same));

                SlowFacts slow = SystemProbe.ReadSlow();
                snap.ApplySlow(slow);
                sb.AppendLine("gpu=" + snap.GpuName);
                sb.AppendLine("driver=" + snap.GpuDriver);
                sb.AppendLine("driverDate=" + (snap.GpuDriverDate == DateTime.MinValue ? "" : snap.GpuDriverDate.ToString("yyyy-MM-dd")));
                sb.AppendLine("diskStatus=" + snap.DiskStatus);
                sb.AppendLine("pageKnown=" + snap.PageFileKnown.ToString() + " auto=" + snap.AutoPageFile.ToString());
                sb.AppendLine("showAmd=" + slow.ShowAmd.ToString());

                List<CrashEvent> events = CrashMonitor.ReadRecent(TimeSpan.FromDays(14), 20);
                for (int i = 0; i < events.Count; i++) CrashParser.Diagnose(events[i]);
                Investigation.Apply(events);
                sb.AppendLine("recentEvents=" + events.Count.ToString());
                int show = events.Count < 5 ? events.Count : 5;
                for (int i = 0; i < show; i++)
                {
                    sb.AppendLine("event " + events[i].Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                        + " " + events[i].Kind + " " + events[i].Process + " " + events[i].Summary);
                }
                HealthScore.Evaluate(snap, events, 0, false);
                sb.AppendLine("score=" + snap.Score.ToString() + " status=" + snap.Status);
                sb.AppendLine("nonPaged=" + snap.NonPagedMb.ToString() + " reserved=" + snap.HardwareReservedMb.ToString() + " modules=" + snap.RamModules.ToString() + "/" + snap.RamSlots.ToString() + " sysmain=" + snap.SysMainMb.ToString() + " dwm=" + snap.DwmMb.ToString() + " startup=" + snap.StartupUpdaters.ToString() + " truncate=" + snap.TruncateMemory.ToString() + " pageGrow=" + snap.PageCanGrow.ToString());
                for (int n = 0; n < snap.Notes.Count; n++)
                    sb.AppendLine("note=" + snap.Notes[n].Penalty.ToString() + " " + snap.Notes[n].Title + " | " + snap.Notes[n].Detail);
                if (!snap.CpuValid || snap.RamTotalMb == 0) code = 1;
                sb.AppendLine(code == 0 ? "OK" : "FAIL");
            }
            catch (Exception ex)
            {
                code = 1;
                sb.AppendLine(ex.ToString());
                sb.AppendLine("FAIL");
            }

            string text = sb.ToString();
            try
            {
                File.WriteAllText(Path.Combine(AppPaths.Ensure(), "self-test.txt"), text, new UTF8Encoding(false));
            }
            catch { }
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt"), text, new UTF8Encoding(false));
            }
            catch { }
            return code;
        }
    }
}
