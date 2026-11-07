using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WinHealthGuard
{
    static class SettingsStore
    {
        public static AppSettings Load()
        {
            try
            {
                string path = AppPaths.SettingsFile;
                if (!File.Exists(path)) return AppSettings.CreateDefault();
                string json = File.ReadAllText(path, new UTF8Encoding(false));
                AppSettings s = new JavaScriptSerializer().Deserialize<AppSettings>(json);
                if (s == null) return AppSettings.CreateDefault();
                s.Normalize();
                return s;
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
                return AppSettings.CreateDefault();
            }
        }

        public static void Save(AppSettings settings)
        {
            if (settings == null) return;
            settings.Normalize();
            string json = new JavaScriptSerializer().Serialize(settings);
            File.WriteAllText(AppPaths.SettingsFile, json, new UTF8Encoding(false));
        }
    }
}
