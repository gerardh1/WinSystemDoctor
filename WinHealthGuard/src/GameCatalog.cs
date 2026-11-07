using System;
using System.Collections.Generic;

namespace WinHealthGuard
{
    static class StringList
    {
        public static void AddLines(ISet<string> target, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] parts = text.Split(new char[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string name = Norm(parts[i]);
                if (name.Length > 0) target.Add(name);
            }
        }

        public static string Norm(string name)
        {
            if (name == null) return "";
            name = name.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }
    }

    sealed class GameCatalog
    {
        readonly HashSet<string> _known;
        readonly HashSet<string> _ignored;
        readonly HashSet<string> _extra = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _ignoreExtra = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public GameCatalog()
        {
            _known = new HashSet<string>(KnownNamesSeed(), StringComparer.OrdinalIgnoreCase);
            _ignored = new HashSet<string>(IgnoredSeed(), StringComparer.OrdinalIgnoreCase);
        }

        public void Reload(AppSettings settings)
        {
            _extra.Clear();
            _ignoreExtra.Clear();
            if (settings == null) return;
            StringList.AddLines(_ignoreExtra, settings.IgnoreProcesses);
            StringList.AddLines(_extra, settings.ExtraGames);
            _extra.ExceptWith(_ignoreExtra);
        }

        public bool IsIgnored(string name)
        {
            name = StringList.Norm(name);
            if (name.Length == 0) return true;
            return _ignored.Contains(name) || _ignoreExtra.Contains(name);
        }

        public bool IsKnownGame(string name)
        {
            name = StringList.Norm(name);
            if (name.Length == 0 || IsIgnored(name)) return false;
            return _known.Contains(name) || _extra.Contains(name);
        }

        public string[] KnownNames()
        {
            List<string> list = new List<string>();
            foreach (string name in _known)
            {
                if (!IsIgnored(name)) list.Add(name);
            }
            foreach (string name in _extra)
            {
                if (!IsIgnored(name)) list.Add(name);
            }
            return list.ToArray();
        }

        public bool LooksLikeGamePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string lower = path.ToLowerInvariant();
            string[] bits = new string[] {
                "\\steamapps\\common\\",
                "\\epic games\\",
                "\\riot games\\",
                "\\xboxgames\\",
                "\\gog games\\",
                "\\ubisoft game\\",
                "\\ea games\\",
                "\\battlestate games\\",
                "\\blizzard entertainment\\"
            };
            for (int i = 0; i < bits.Length; i++)
            {
                if (lower.IndexOf(bits[i], StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        static string[] KnownNamesSeed()
        {
            return new string[] {
                "cs2", "csgo", "valorant", "valorant-win64-shipping",
                "league of legends", "fortniteclient-win64-shipping",
                "r5apex", "r5apex_dx12", "overwatch", "tank",
                "wow", "wow-64", "wowclassic", "wowclassic-64",
                "gta5", "gta5_enhanced", "cyberpunk2077", "eldenring",
                "dota2", "tslgame", "rocketleague", "destiny2",
                "genshinimpact", "yuanshen", "ffxiv_dx11", "ffxiv_dx12",
                "lostark", "escapefromtarkov", "rdr2", "hl2",
                "robloxplayerbeta", "robloxplayer", "rustclient",
                "dayz_x64", "dayz", "arma3_x64", "arma3",
                "squadgame", "huntgame", "deadbydaylight-win64-shipping",
                "palworld-win64-shipping", "helldivers2", "thefinals-win64-shipping",
                "bg3", "bg3_dx11", "starfield", "minecraft.windows",
                "warframe.x64", "pathofexile", "pathofexile_x64", "pathofexilesteam",
                "diablo iv", "diabloiv", "enshrouded", "oncehuman",
                "naraka", "narakabladepoint", "deltaforceclient",
                "client-win64-shipping", "soothouse", "valheim",
                "sekiro", "darksoulsiii", "witcher3", "hk",
                "easportsfc25", "easportsfc26", "fc25", "fc26",
                "nba2k25", "nba2k26", "fifa22", "fifa23",
                "cod", "modernwarfare", "cod24-cod", "sp22-cod", "blackops6",
                "warzone", "pioneer", "tungsten",
                "escapefromtarkov_be", "stellarblade", "wutheringwaves",
                "launcher-win64-shipping"
            };
        }

        static string[] IgnoredSeed()
        {
            return new string[] {
                "explorer", "searchhost", "shellexperiencehost", "startmenuexperiencehost",
                "applicationframehost", "systemsettings", "textinputhost", "lockapp",
                "sihost", "ctfmon", "dwm", "winhealthguard", "widgets", "widgetboard",
                "gamebar", "xboxgamebar", "gamebarftserver", "shellhost", "taskmgr",
                "chrome", "msedge", "firefox", "opera", "brave", "iexplore", "vivaldi",
                "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "potplayermini", "potplayermini64", "spotify",
                "zoom", "teams", "ms-teams", "powerpnt", "winword", "excel", "onenote", "outlook",
                "discord", "slack", "cursor", "code", "devenv", "idea64", "pycharm64",
                "windowsterminal", "cmd", "powershell", "pwsh", "openconsole",
                "steam", "steamwebhelper", "steamtours", "epicgameslauncher", "epicwebhelper",
                "eadesktop", "origin", "uplay", "ubisoftconnect", "battle.net", "gog galaxy",
                "riotclientservices", "riotclientux", "leagueclient", "leagueclientux",
                "bethesdanetlauncher", "xboxapp", "xboxpcapp", "applicationframehost",
                "radeonsoftware", "amdrsrcext", "presentmon-x64", "nvidia share", "nvcontainer"
            };
        }
    }
}
