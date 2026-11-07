using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WinHealthGuard
{
    sealed class SolutionHit
    {
        public string Source = "";
        public string Title = "";
        public string Url = "";
    }

    static class SolutionSearch
    {
        public static List<SolutionHit> Find(CrashEvent ev)
        {
            List<SolutionHit> hits = new List<SolutionHit>();
            string query = Query(ev);
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            catch { }
            Add(hits, "Microsoft", Microsoft(query), "https://learn.microsoft.com/en-us/search/?terms=" + Uri.EscapeDataString(query));
            Add(hits, "Stack Overflow", StackOverflow(query), "https://stackoverflow.com/search?q=" + Uri.EscapeDataString(query));
            Add(hits, "Reddit", Reddit(query), "https://www.reddit.com/search/?q=" + Uri.EscapeDataString(query));
            Add(hits, "GitHub", GitHub(query), "https://github.com/search?type=issues&q=" + Uri.EscapeDataString(query));
            return hits;
        }

        public static string LearnRepair(CrashEvent ev)
        {
            List<SolutionHit> hits = Find(ev);
            StringBuilder text = new StringBuilder();
            text.Append(' ').Append(Query(ev));
            for (int i = 0; i < hits.Count; i++) text.Append(' ').Append(hits[i].Title);
            return Choose(text.ToString().ToLowerInvariant());
        }

        public static string Action(string repairId)
        {
            if (repairId == "faststartup") return "Turn Fast Startup off.";
            if (repairId == "tdr") return "Give the graphics driver longer to recover.";
            if (repairId == "pagefile") return "Leave the page file on and let Windows size it.";
            if (repairId == "browsers") return "Stop Edge and Chrome from staying open after you close them.";
            if (repairId == "visualfx") return "Turn off most window animations.";
            if (repairId == "sysmain") return "Stop the SysMain cache service.";
            if (repairId == "startuptrim") return "Stop updater programs from starting with Windows.";
            if (repairId == "maxmem") return "Remove the Windows boot memory limit.";
            if (repairId == "memory") return "Test memory on the next restart.";
            if (repairId == "sfc") return "Check Windows system files.";
            if (repairId == "dism") return "Repair the Windows image.";
            if (repairId == "shaders") return "Clear shader caches and old temporary files.";
            if (repairId == "dns") return "Flush the DNS cache.";
            if (repairId == "fullscreen") return "Turn off fullscreen optimizations for the game.";
            if (repairId == "steam") return "Verify the game files in Steam.";
            if (repairId == "amd") return "Turn off the AMD overlay.";
            if (repairId == "nvidia") return "Turn off the NVIDIA overlay.";
            if (repairId == "hagsoff") return "Turn off hardware-accelerated GPU scheduling.";
            if (repairId == "driver") return "Update the graphics driver from the official source.";
            if (repairId == "netreset") return "Reset the network stack.";
            if (repairId == "dismsfc") return "Repair the Windows image, then check system files.";
            if (repairId == "fault50") return "Reset the page file and schedule a disk check.";
            if (repairId == "chkdsk") return "Schedule a disk check for the next restart.";
            if (repairId == "storage") return "Install the matching storage driver. Firmware stays with the disk maker.";
            if (repairId == "vcrepair") return "Repair the Visual C++ 2015-2022 runtime.";
            if (repairId == "wureset") return "Clear the Windows Update download cache.";
            if (repairId == "dotnet") return "Enable .NET Framework 3.5 and 4.8.";
            if (repairId == "appinit") return "Clear AppInit_DLLs.";
            if (repairId == "overlayoff") return "Turn overlays off.";
            return "";
        }

        static string Choose(string text)
        {
            if (Has(text, "critical_process_died") || Has(text, "0x000000ef")) return "dismsfc";
            if (Has(text, "dpc_watchdog") || Has(text, "0x00000133")) return "storage";
            if (Has(text, "page_fault_in_nonpaged") || Has(text, "0x00000050")) return "fault50";
            if (Has(text, "inaccessible_boot") || Has(text, "0x0000007b")) return "";
            if (Has(text, "video_tdr") || Has(text, "0x00000116") || Has(text, "nvlddmkm") || Has(text, "amdkmdag")) return "driver";
            if (Has(text, "memory_management") || Has(text, "0x0000001a") || Has(text, "irql_not_less") || Has(text, "0x0000000a")) return "memory";
            if (Has(text, "0xc0000409") || Has(text, "0xc000007b") || Has(text, "vcruntime") || Has(text, "msvcp140") || Has(text, "ucrtbase")) return "vcrepair";
            if (Has(text, "0xc0000142") || Has(text, "appinit_dlls")) return "appinit";
            if (Has(text, "0x80004005") || Has(text, "0x80070005") || Has(text, "softwaredistribution")) return "wureset";
            if (Has(text, "0x800f081f") || Has(text, "cbs_e_source_missing")) return "dism";
            if (Has(text, "clr.dll") || Has(text, "0xe0434352")) return "dotnet";
            if (Has(text, "tdrdelay") || Has(text, "display driver stopped") || Has(text, "driver timeout")) return "driver";
            if (Has(text, "page file") || Has(text, "pagefile")) return "pagefile";
            if (Has(text, "memory diagnostic") || Has(text, "memtest") || Has(text, "whea")) return "memory";
            if (Has(text, "system file checker") || Has(text, "sfc /scannow") || Has(text, "sfc.exe")) return "sfc";
            if (Has(text, "dism") || Has(text, "restorehealth")) return "dism";
            if (Has(text, "shader cache") || Has(text, "dxcache")) return "shaders";
            if (Has(text, "flush dns") || Has(text, "flushdns")) return "dns";
            if (Has(text, "fullscreen optimization")) return "fullscreen";
            if (Has(text, "in-game overlay") || Has(text, "radeon overlay") || Has(text, "discord overlay")) return "overlayoff";
            if (Has(text, "verify integrity") || Has(text, "verify game files")) return "steam";
            if (Has(text, "gpu scheduling") || Has(text, "hwschmode")) return "hagsoff";
            if (Has(text, "graphics driver update") || Has(text, "outdated driver") || Has(text, "update your graphics driver")) return "driver";
            return "";
        }

        static bool Has(string text, string needle)
        {
            return text.IndexOf(needle, StringComparison.Ordinal) >= 0;
        }

        public static string Format(CrashEvent ev, IList<SolutionHit> hits)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(ev == null ? "System error" : ev.Summary);
            if (ev != null && !string.IsNullOrEmpty(ev.Suggestion))
            {
                sb.AppendLine();
                sb.AppendLine(ev.Suggestion);
            }
            sb.AppendLine();
            sb.AppendLine("Published solutions");
            if (hits == null || hits.Count == 0)
            {
                sb.AppendLine("No matching page was found.");
                return sb.ToString().Trim();
            }
            for (int i = 0; i < hits.Count; i++)
            {
                sb.AppendLine();
                sb.AppendLine(hits[i].Source);
                sb.AppendLine(string.IsNullOrEmpty(hits[i].Title) ? hits[i].Url : hits[i].Title);
            }
            return sb.ToString().Trim();
        }

        public static void Show(IWin32Window owner, string title, string body, IList<SolutionHit> hits)
        {
            using (SolutionPrompt prompt = new SolutionPrompt(title, body, hits))
                prompt.ShowDialog(owner);
        }

        static void Add(List<SolutionHit> hits, string source, SolutionHit found, string fallbackUrl)
        {
            SolutionHit hit = found;
            if (hit == null || string.IsNullOrEmpty(hit.Url) || !Allowed(hit.Url))
            {
                hit = new SolutionHit();
                hit.Source = source;
                hit.Title = "Search " + source + " for this error";
                hit.Url = fallbackUrl;
            }
            else
            {
                hit.Source = source;
                if (string.IsNullOrEmpty(hit.Title)) hit.Title = "Open the " + source + " page";
            }
            if (Allowed(hit.Url)) hits.Add(hit);
        }

        public static string Query(CrashEvent ev)
        {
            if (ev == null) return "Windows system error";
            if (!string.IsNullOrEmpty(ev.BugCheck))
            {
                string name = CrashParser.BugCheckName(ev.BugCheck);
                return (ev.BugCheck + " " + name).Trim();
            }
            if (!string.IsNullOrEmpty(ev.ExceptionCode))
            {
                string who = !string.IsNullOrEmpty(ev.FaultModule) ? ev.FaultModule : ev.Process;
                return (ev.ExceptionCode + " " + who).Trim();
            }
            if (ev.EventId == 41) return "Kernel-Power event 41 unexpected shutdown";
            if (ev.EventId == 6008) return "Windows event 6008 unexpected shutdown";
            if (ev.Kind == "DisplayDriver") return "Display driver stopped responding and has recovered";
            if (!string.IsNullOrEmpty(ev.Process)) return ev.Process + " crash";
            if (!string.IsNullOrEmpty(ev.Summary)) return ev.Summary;
            return "Windows system error";
        }

        static SolutionHit Microsoft(string query)
        {
            string json = Get("https://learn.microsoft.com/api/search?locale=en-us&$top=3&search=" + Uri.EscapeDataString(query));
            return First(json, "results", "title", "url");
        }

        static SolutionHit StackOverflow(string query)
        {
            string json = Get("https://api.stackexchange.com/2.3/search/advanced?order=desc&sort=relevance&site=stackoverflow&pagesize=3&q=" + Uri.EscapeDataString(query));
            return First(json, "items", "title", "link");
        }

        static SolutionHit Reddit(string query)
        {
            string json = Get("https://www.reddit.com/search.json?limit=5&sort=relevance&q=" + Uri.EscapeDataString(query));
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                Dictionary<string, object> root = Parse(json);
                Dictionary<string, object> data = Dict(root, "data");
                ArrayList children = data == null ? null : data["children"] as ArrayList;
                if (children == null) return null;
                for (int i = 0; i < children.Count; i++)
                {
                    Dictionary<string, object> child = children[i] as Dictionary<string, object>;
                    Dictionary<string, object> post = child == null ? null : Dict(child, "data");
                    if (post == null) continue;
                    string title = Str(post, "title");
                    string link = Str(post, "permalink");
                    if (link.Length == 0 || title.Length == 0) continue;
                    if (link.StartsWith("/", StringComparison.Ordinal)) link = "https://www.reddit.com" + link;
                    if (!Allowed(link)) continue;
                    SolutionHit hit = new SolutionHit();
                    hit.Title = title;
                    hit.Url = link;
                    return hit;
                }
            }
            catch { }
            return null;
        }

        static SolutionHit GitHub(string query)
        {
            string json = Get("https://api.github.com/search/issues?per_page=5&q=" + Uri.EscapeDataString("\"" + query + "\""));
            SolutionHit hit = FirstMatching(json, query);
            if (hit != null) return hit;
            return First(json, "items", "title", "html_url");
        }

        static SolutionHit First(string json, string listKey, string titleKey, string urlKey)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                Dictionary<string, object> root = Parse(json);
                ArrayList list = root.ContainsKey(listKey) ? root[listKey] as ArrayList : null;
                if (list == null) return null;
                for (int i = 0; i < list.Count; i++)
                {
                    Dictionary<string, object> item = list[i] as Dictionary<string, object>;
                    if (item == null) continue;
                    string url = Str(item, urlKey);
                    string title = Str(item, titleKey);
                    if (!Allowed(url)) continue;
                    SolutionHit hit = new SolutionHit();
                    hit.Title = title;
                    hit.Url = url;
                    return hit;
                }
            }
            catch { }
            return null;
        }

        static SolutionHit FirstMatching(string json, string query)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                Dictionary<string, object> root = Parse(json);
                ArrayList list = root.ContainsKey("items") ? root["items"] as ArrayList : null;
                if (list == null) return null;
                string needle = query.ToLowerInvariant();
                for (int i = 0; i < list.Count; i++)
                {
                    Dictionary<string, object> item = list[i] as Dictionary<string, object>;
                    if (item == null) continue;
                    string title = Str(item, "title");
                    string url = Str(item, "html_url");
                    if (!Allowed(url)) continue;
                    if (title.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) < 0 && url.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) < 0)
                        continue;
                    SolutionHit hit = new SolutionHit();
                    hit.Title = title;
                    hit.Url = url;
                    return hit;
                }
            }
            catch { }
            return null;
        }

        static string Get(string url)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 20000;
                req.ReadWriteTimeout = 20000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                req.Accept = "application/json";
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (Stream stream = res.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    char[] buf = new char[400000];
                    int got = 0;
                    while (got < buf.Length)
                    {
                        int n = reader.Read(buf, got, buf.Length - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    return new string(buf, 0, got);
                }
            }
            catch
            {
                return "";
            }
        }

        static Dictionary<string, object> Parse(string json)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            return ser.Deserialize<Dictionary<string, object>>(json);
        }

        static Dictionary<string, object> Dict(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.ContainsKey(key)) return null;
            return data[key] as Dictionary<string, object>;
        }

        static string Str(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.ContainsKey(key) || data[key] == null) return "";
            return Convert.ToString(data[key]);
        }

        public static bool Allowed(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;
            string host = uri.Host.ToLowerInvariant();
            if (host == "learn.microsoft.com" || host == "support.microsoft.com" || host == "docs.microsoft.com" || host == "answers.microsoft.com") return true;
            if (host == "stackoverflow.com") return true;
            if (host == "reddit.com" || host == "www.reddit.com" || host == "old.reddit.com") return true;
            if (host == "github.com") return true;
            return false;
        }

        public static void Open(string url)
        {
            if (!Allowed(url)) return;
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = url;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
    }

    sealed class SolutionPrompt : Form
    {
        readonly ListBox _list;
        readonly List<SolutionHit> _hits = new List<SolutionHit>();

        public SolutionPrompt(string title, string body, IList<SolutionHit> hits)
        {
            Text = string.IsNullOrEmpty(title) ? "System error" : title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontUi;
            ClientSize = new System.Drawing.Size(640, 460);
            TextBox report = new TextBox();
            report.Multiline = true;
            report.ReadOnly = true;
            report.ScrollBars = ScrollBars.Vertical;
            report.BorderStyle = BorderStyle.None;
            report.BackColor = Theme.Card;
            report.ForeColor = Theme.Text;
            report.Font = Theme.FontUi;
            report.Text = body ?? "";
            report.SetBounds(16, 16, 608, 250);
            _list = new ListBox();
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Theme.Card2;
            _list.ForeColor = Theme.Text;
            _list.Font = Theme.FontUi;
            _list.IntegralHeight = false;
            _list.SetBounds(16, 278, 608, 120);
            _list.DoubleClick += delegate { OpenSelected(); };
            if (hits != null)
            {
                for (int i = 0; i < hits.Count; i++)
                {
                    if (hits[i] == null || string.IsNullOrEmpty(hits[i].Url)) continue;
                    _hits.Add(hits[i]);
                    _list.Items.Add(hits[i].Source + "  —  " + hits[i].Title);
                }
            }
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            Button open = Make("Open", true);
            open.SetBounds(16, 412, 110, 32);
            open.Click += delegate { OpenSelected(); };
            Button close = Make("Close", false);
            close.SetBounds(136, 412, 110, 32);
            close.Click += delegate { Close(); };
            Controls.Add(report);
            Controls.Add(_list);
            Controls.Add(open);
            Controls.Add(close);
        }

        void OpenSelected()
        {
            if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _hits.Count) return;
            SolutionSearch.Open(_hits[_list.SelectedIndex].Url);
        }

        static Button Make(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? Theme.Accent : Theme.Card2;
            button.ForeColor = primary ? Theme.OnAccent : Theme.Text;
            button.Font = primary ? new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold) : Theme.FontUi;
            button.Cursor = Cursors.Hand;
            return button;
        }
    }
}
