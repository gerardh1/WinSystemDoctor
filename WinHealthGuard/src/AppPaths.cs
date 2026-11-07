using System;
using System.IO;
using System.Text;

namespace WinHealthGuard
{
    static class AppPaths
    {
        public static string Root
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinHealthGuard"); }
        }

        public static string Ensure()
        {
            string root = Root;
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            return root;
        }

        public static string CrashesFile
        {
            get { return Path.Combine(Root, "crashes.jsonl"); }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(Root, "settings.json"); }
        }

        public static string ActionsFile
        {
            get { return Path.Combine(Root, "actions.log"); }
        }

        public static string RepairsFile
        {
            get { return Path.Combine(Root, "repairs.jsonl"); }
        }

        public static void Audit(string line)
        {
            try
            {
                string path = Path.Combine(Ensure(), "actions.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + "\r\n", new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
