using System;
using System.Collections.Generic;

namespace WinHealthGuard
{
    static class Investigation
    {
        public static void Apply(IList<CrashEvent> events)
        {
            if (events == null) return;
            for (int i = 0; i < events.Count; i++)
            {
                CrashEvent ev = events[i];
                if (ev.Kind != "PowerLoss" && ev.Kind != "UnexpectedShutdown") continue;
                if (ev.Kind == "UnexpectedShutdown" && FindPowerLoss(events, ev) != null)
                {
                    ev.Suggestion = "This is the same restart already listed as a power loss.";
                    ev.RepairId = "";
                    continue;
                }
                CrashEvent cause = FindCause(events, ev);
                if (cause != null)
                {
                    ev.Suggestion = "This restart followed a system error: " + cause.Summary;
                    ev.RepairId = string.IsNullOrEmpty(cause.RepairId) ? "manual" : cause.RepairId;
                    continue;
                }
                ev.Suggestion = "The PC lost power before Windows could write a crash dump. Check the power supply, power cables, and cooling. No Windows setting repairs a power cut.";
                ev.RepairId = "manual";
            }
        }

        static CrashEvent FindPowerLoss(IList<CrashEvent> events, CrashEvent shutdown)
        {
            for (int i = 0; i < events.Count; i++)
            {
                CrashEvent other = events[i];
                if (other == null || other == shutdown || other.Kind != "PowerLoss") continue;
                if (Math.Abs((other.Time - shutdown.Time).TotalMinutes) <= 3) return other;
            }
            return null;
        }

        static CrashEvent FindCause(IList<CrashEvent> events, CrashEvent restart)
        {
            CrashEvent best = null;
            double bestGap = 16;
            for (int i = 0; i < events.Count; i++)
            {
                CrashEvent other = events[i];
                if (other == null || other == restart) continue;
                if (other.Kind != "BugCheck" && other.Kind != "DisplayDriver" && other.Kind != "HardwareError") continue;
                double gap = Math.Abs((other.Time - restart.Time).TotalMinutes);
                if (gap <= 15 && gap < bestGap)
                {
                    best = other;
                    bestGap = gap;
                }
            }
            return best;
        }
    }
}
