using System;
using System.Diagnostics;

namespace WinHealthGuard
{
    sealed class GameScan
    {
        readonly object _gate = new object();
        int _busy;
        DateTime _last = DateTime.MinValue;
        bool _ready;
        string _found;

        public void Read(out bool ready, out string found)
        {
            lock (_gate)
            {
                ready = _ready;
                found = _found;
            }
        }

        public void MaybeStart(GameCatalog catalog, bool inGame)
        {
            int seconds = inGame ? 15 : 60;
            if (_last != DateTime.MinValue && (DateTime.UtcNow - _last).TotalSeconds < seconds) return;
            if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            _last = DateTime.UtcNow;
            string[] names = catalog.KnownNames();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string found = Find(names);
                lock (_gate)
                {
                    _found = found;
                    _ready = true;
                }
                System.Threading.Interlocked.Exchange(ref _busy, 0);
            });
        }

        static string Find(string[] names)
        {
            Process[] processes = null;
            try { processes = Process.GetProcesses(); }
            catch { return null; }
            string found = null;
            for (int i = 0; i < processes.Length && found == null; i++)
            {
                try
                {
                    string name = processes[i].ProcessName;
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (string.Equals(name, names[j], StringComparison.OrdinalIgnoreCase))
                        {
                            found = name;
                            break;
                        }
                    }
                }
                catch
                {
                }
            }
            for (int i = 0; i < processes.Length; i++)
            {
                try { processes[i].Dispose(); }
                catch { }
            }
            return found;
        }
    }

    sealed class SessionGuard
    {
        readonly AppSettings _settings;
        bool _inGame;
        bool _batteryNoted;
        bool _boostTried;
        bool _recoverDone;
        DateTime _lastSeen = DateTime.MinValue;
        string _gameName = "";

        public SessionGuard(AppSettings settings)
        {
            _settings = settings;
        }

        public bool InGame { get { return _inGame; } }
        public string GameName { get { return _gameName ?? ""; } }

        public void Update(HealthSnapshot snap, string scannedGame, bool scanReady, Action<string> log)
        {
            bool game = snap.ForegroundIsGame;
            string name = snap.ForegroundName ?? "";
            if (!game && !string.IsNullOrEmpty(scannedGame))
            {
                game = true;
                name = scannedGame;
            }

            if (game)
            {
                _lastSeen = DateTime.UtcNow;
                _gameName = name;
                if (!_inGame)
                {
                    _inGame = true;
                    _batteryNoted = false;
                    _boostTried = false;
                    if (log != null) log("Game started: " + name);
                }
                MaybeBoost(snap, log);
            }
            else if (_inGame && (DateTime.UtcNow - _lastSeen).TotalSeconds > 25)
            {
                string ended = _gameName;
                _inGame = false;
                _gameName = "";
                if (log != null) log("Game closed: " + ended);
                Restore(log, "Power plan restored.");
            }

            if (scanReady && !_recoverDone)
            {
                _recoverDone = true;
                if (!_inGame && _settings.PowerBoostActive)
                    Restore(log, "Power plan restored.");
            }
        }

        void MaybeBoost(HealthSnapshot snap, Action<string> log)
        {
            if (!_settings.BoostPowerWhileGaming) return;
            if (!snap.OnAc)
            {
                if (!_batteryNoted && log != null)
                {
                    _batteryNoted = true;
                    log("On battery, so the power plan was left as it is.");
                }
                return;
            }
            if (_boostTried) return;
            string active = PowerPlans.ActiveGuid();
            if (PowerPlans.IsStrong(active)) return;
            _boostTried = true;
            _settings.PowerBoostRestoreGuid = active;
            _settings.PowerBoostActive = true;
            SettingsStore.Save(_settings);
            string msg;
            if (!PowerPlans.SetActive(PowerPlans.HighPerformance, out msg))
            {
                _settings.PowerBoostActive = false;
                _settings.PowerBoostRestoreGuid = "";
                SettingsStore.Save(_settings);
                if (log != null) log(msg);
                return;
            }
            if (log != null) log("Power plan set to High performance for this game.");
        }

        void Restore(Action<string> log, string success)
        {
            if (!_settings.PowerBoostActive) return;
            string guid = _settings.PowerBoostRestoreGuid;
            _settings.PowerBoostActive = false;
            _settings.PowerBoostRestoreGuid = "";
            SettingsStore.Save(_settings);
            if (string.IsNullOrEmpty(guid)) return;
            string message;
            if (PowerPlans.SetActive(guid, out message))
            {
                if (log != null) log(success);
            }
            else if (log != null)
            {
                log(message);
            }
        }
    }
}
