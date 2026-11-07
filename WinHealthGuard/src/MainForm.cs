using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinHealthGuard
{
    sealed class MainForm : Form
    {
        readonly bool _startTray;
        readonly AppSettings _settings;
        readonly GameCatalog _catalog = new GameCatalog();
        readonly GameScan _gameScan = new GameScan();
        readonly SessionGuard _session;
        readonly CrashMonitor _monitor = new CrashMonitor();
        readonly List<CrashEvent> _events = new List<CrashEvent>();
        readonly List<ActivityItem> _activity = new List<ActivityItem>();
        readonly List<FixSpec> _fixes;
        readonly List<FixRow> _fixRows = new List<FixRow>();
        readonly List<Control> _fixLayout = new List<Control>();
        readonly System.Windows.Forms.Timer _timer;

        bool _traySuppressed;
        bool _exit;
        bool _hiding;
        bool _loadingSettings;
        bool _repairing;
        bool _layingDash;
        bool _layingFixes;
        bool _pendingCounts;
        string _pendingRepair = "";
        readonly Dictionary<string, DateTime> _addressed = new Dictionary<string, DateTime>();
        readonly List<FixProof> _proofs = new List<FixProof>();
        FixMeter _before;
        bool _haveBefore;
        string _breakdown = "";
        bool _balloonOnce;
        int _hot;
        string _lastStatus;
        string _selectedId;
        DateTime _lastBalloon = DateTime.MinValue;
        CpuStamp _stamp;
        HealthSnapshot _last;
        SlowFacts _slow = new SlowFacts();
        readonly object _slowLock = new object();
        int _slowBusy;
        int _gpuBusy;
        string _driverRowText = "";
        DateTime _nextSlow = DateTime.MinValue;

        EventWaitHandle _showEvent;
        System.Threading.Thread _waiter;
        NotifyIcon _tray;
        ToolStripMenuItem _pauseItem;
        Icon _iconGood;
        Icon _iconWarn;
        Icon _iconBad;
        IntPtr _hGood;
        IntPtr _hWarn;
        IntPtr _hBad;

        Panel _side;
        Panel _host;
        Panel _progressHost;
        Label _progressLabel;
        ProgressBar _progress;
        Label _brand;
        Label _tag;
        Label _sideHint;
        SideButton _navDash;
        SideButton _navCrash;
        SideButton _navFix;
        SideButton _navSet;
        Panel _pageDash;
        Panel _pageCrash;
        Panel _pageFix;
        Panel _pageSet;

        ScoreRing _ring;
        Label _statusWord;
        Label _headline;
        Label _meta;
        Panel _issueHost;
        string _issueKey = "";
        MetricCard _cpuCard;
        MetricCard _ramCard;
        MetricCard _diskCard;
        MetricCard _powerCard;
        Sparkline _chart;
        Label _activityTitle;
        ListBox _activityList;

        ComboBox _filter;
        Label _countLabel;
        DataGridView _grid;
        TextBox _detail;
        Panel _crashBar;
        Button _exportBtn;
        Button _copyBtn;
        Button _logBtn;
        Button _applyBtn;
        Button _investBtn;

        Panel _fixScroll;
        TextBox _fixLog;
        Label _fixResult;
        Label _fixIntro;

        CheckBox _startCheck;
        CheckBox _notifyCheck;
        CheckBox _boostCheck;
        CheckBox _pauseCheck;
        ComboBox _intervalCombo;
        TextBox _extraBox;
        TextBox _ignoreBox;
        readonly List<Control> _setRows = new List<Control>();
        string _dashTextKey = "";

        public MainForm(bool startTray)
        {
            _startTray = startTray;
            _settings = SettingsStore.Load();
            _catalog.Reload(_settings);
            _session = new SessionGuard(_settings);
            _fixes = FixCatalog.Build();
            _proofs.AddRange(FixCheck.Load());

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.FontUi;
            Text = "WinHealthGuard";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1180, 980);
            MinimumSize = new Size(1040, 760);
            DoubleBuffered = true;

            BuildIcons();
            BuildShell();
            BuildDashboard();
            BuildCrashes();
            BuildFixes();
            BuildSettings();
            BuildTray();
            ShowPage("dash");
            LoadSettingsToUi();
            StartupRegistration.Apply(_settings.StartWithWindows);

            _events.AddRange(CrashStore.Load());
            SortEvents();
            RefreshFindings();
            _monitor.Remember(_events);
            Rebind();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 500;
            _timer.Tick += OnTick;
            _timer.Start();

            _gameScan.MaybeStart(_catalog, false);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_startTray && !_traySuppressed)
            {
                _traySuppressed = true;
                value = false;
                if (!IsHandleCreated) CreateHandle();
                ShowInTaskbar = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.EnableDarkTitle(Handle);
            try
            {
                _showEvent = EventWaitHandle.OpenExisting(@"Local\WinHealthGuard.Show");
            }
            catch
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WinHealthGuard.Show");
            }
            if (_waiter == null)
            {
                _waiter = new Thread(WaitLoop);
                _waiter.IsBackground = true;
                _waiter.Start();
            }
            if (!_monitorsStarted)
            {
                _monitorsStarted = true;
                _monitor.Detected += OnCrashBackground;
                _monitor.Start();
                BeginImport();
            }
        }

        bool _monitorsStarted;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int w = 1180;
            int h = 980;
            if (w > area.Width - 24) w = Math.Max(960, area.Width - 24);
            if (h > area.Height - 24) h = Math.Max(640, area.Height - 24);
            MinimumSize = new Size(Math.Min(1040, w), Math.Min(700, h));
            if (Width < w - 8 || Height < h - 8) Size = new Size(w, h);
            LayoutSide();
            LayoutDashboard();
            LayoutFixes();
            LayoutCrashBar();
            LayoutSettings();
            FitCrashDetail();
            FitFixResult();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && !_hiding && !_exit) HideToTray();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _exit = true;
                try { if (_showEvent != null) _showEvent.Set(); } catch { }
                if (_timer != null) _timer.Dispose();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
                _monitor.Dispose();
            }
            if (_hGood != IntPtr.Zero) { NativeMethods.DestroyIcon(_hGood); _hGood = IntPtr.Zero; }
            if (_hWarn != IntPtr.Zero) { NativeMethods.DestroyIcon(_hWarn); _hWarn = IntPtr.Zero; }
            if (_hBad != IntPtr.Zero) { NativeMethods.DestroyIcon(_hBad); _hBad = IntPtr.Zero; }
            base.Dispose(disposing);
        }

        void WaitLoop()
        {
            while (!_exit)
            {
                try
                {
                    if (_showEvent == null) return;
                    _showEvent.WaitOne();
                }
                catch { return; }
                if (_exit) break;
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke(new Action(ShowFromTray));
                }
                catch { }
            }
        }

        void BuildIcons()
        {
            _hGood = MakeIcon(Theme.Good);
            _hWarn = MakeIcon(Theme.Warn);
            _hBad = MakeIcon(Theme.Bad);
            _iconGood = Icon.FromHandle(_hGood);
            _iconWarn = Icon.FromHandle(_hWarn);
            _iconBad = Icon.FromHandle(_hBad);
            Icon = _iconGood;
        }

        static IntPtr MakeIcon(Color color)
        {
            using (Bitmap bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush brush = new SolidBrush(color)) g.FillEllipse(brush, 1, 1, 30, 30);
                using (Pen pen = new Pen(Color.White, 2.2f))
                {
                    g.DrawLines(pen, new Point[] {
                        new Point(7, 17), new Point(12, 17), new Point(15, 10),
                        new Point(19, 23), new Point(22, 17), new Point(26, 17)
                    });
                }
                return bmp.GetHicon();
            }
        }

        void BuildShell()
        {
            _side = new Panel();
            _side.Dock = DockStyle.Left;
            _side.Width = 220;
            _side.BackColor = Theme.Sidebar;
            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = Theme.Bg;
            _brand = LabelOf("WinHealthGuard", Theme.FontSection, Theme.Text);
            _tag = LabelOf("PC health", Theme.FontSmall, Theme.Muted);
            _sideHint = LabelOf("Runs in the background.", Theme.FontSmall, Theme.Muted);
            _navDash = new SideButton("Home");
            _navCrash = new SideButton("Crash History");
            _navFix = new SideButton("Performance");
            _navSet = new SideButton("Settings");
            _navDash.Click += delegate { ShowPage("dash"); };
            _navCrash.Click += delegate { ShowPage("crashes"); };
            _navFix.Click += delegate { ShowPage("fixes"); };
            _navSet.Click += delegate { ShowPage("settings"); };
            _side.Controls.Add(_brand);
            _side.Controls.Add(_tag);
            _side.Controls.Add(_sideHint);
            _side.Controls.Add(_navDash);
            _side.Controls.Add(_navCrash);
            _side.Controls.Add(_navFix);
            _side.Controls.Add(_navSet);
            _side.Resize += delegate { LayoutSide(); };
            _pageDash = Page();
            _pageCrash = Page();
            _pageFix = Page();
            _pageSet = Page();
            _host.Controls.Add(_pageDash);
            _host.Controls.Add(_pageCrash);
            _host.Controls.Add(_pageFix);
            _host.Controls.Add(_pageSet);
            _progressHost = new Panel();
            _progressHost.Dock = DockStyle.Bottom;
            _progressHost.Height = 78;
            _progressHost.BackColor = Theme.Card;
            _progressHost.Padding = new Padding(16, 8, 16, 10);
            _progressHost.Visible = false;
            _progressLabel = LabelOf("Working...", Theme.FontUi, Theme.Text);
            _progressLabel.Dock = DockStyle.Top;
            _progressLabel.Height = 40;
            _progressLabel.AutoEllipsis = false;
            _progressLabel.TextAlign = ContentAlignment.MiddleLeft;
            _progress = new ProgressBar();
            _progress.Dock = DockStyle.Bottom;
            _progress.Height = 16;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 28;
            _progressHost.Controls.Add(_progress);
            _progressHost.Controls.Add(_progressLabel);
            Controls.Add(_host);
            Controls.Add(_side);
            Controls.Add(_progressHost);
            LayoutSide();
        }

        void BuildDashboard()
        {
            _ring = new ScoreRing();
            _statusWord = LabelOf("Checking your PC", Theme.FontTitle, Theme.Text);
            _headline = LabelOf("Looking for problems that crash games.", Theme.FontUi, Theme.Muted);
            _meta = LabelOf("", Theme.FontSmall, Theme.Muted);
            _meta.Visible = false;
            _issueHost = new Panel();
            _issueHost.BackColor = Theme.Bg;
            _pageDash.AutoScroll = true;
            _cpuCard = new MetricCard();
            _cpuCard.Caption = "CPU";
            _ramCard = new MetricCard();
            _ramCard.Caption = "Memory";
            _diskCard = new MetricCard();
            _diskCard.Caption = "System drive";
            _powerCard = new MetricCard();
            _powerCard.Caption = "Power";
            _chart = new Sparkline();
            _activityTitle = LabelOf("Activity", Theme.FontSection, Theme.Text);
            _activityList = new ListBox();
            _activityList.IntegralHeight = false;
            _activityList.BorderStyle = BorderStyle.None;
            _activityList.BackColor = Theme.Card;
            _activityList.ForeColor = Theme.Text;
            _activityList.Font = Theme.FontSmall;
            _activityList.DrawMode = DrawMode.OwnerDrawFixed;
            _activityList.ItemHeight = 24;
            _activityList.DrawItem += DrawActivity;
            _chart.Visible = false;
            _activityTitle.Visible = false;
            _activityList.Visible = false;
            _pageDash.Controls.Add(_ring);
            _pageDash.Controls.Add(_statusWord);
            _pageDash.Controls.Add(_headline);
            _pageDash.Controls.Add(_issueHost);
            _pageDash.Controls.Add(_cpuCard);
            _pageDash.Controls.Add(_ramCard);
            _pageDash.Controls.Add(_diskCard);
            _pageDash.Controls.Add(_powerCard);
            _pageDash.Controls.Add(_chart);
            _pageDash.Controls.Add(_activityTitle);
            _pageDash.Controls.Add(_activityList);
            _pageDash.Resize += delegate { LayoutDashboard(); };
        }

        void BuildCrashes()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 52;
            bar.BackColor = Theme.Bg;
            _crashBar = bar;
            _filter = new ComboBox();
            _filter.DropDownStyle = ComboBoxStyle.DropDownList;
            _filter.FlatStyle = FlatStyle.Flat;
            _filter.BackColor = Theme.Card;
            _filter.ForeColor = Theme.Text;
            _filter.Font = Theme.FontUi;
            _filter.Items.AddRange(new object[] { "All", "Games", "Blue screens", "Display driver", "Applications" });
            _filter.SelectedIndex = 0;
            _filter.DropDownWidth = 180;
            _filter.SelectedIndexChanged += delegate { Rebind(); };
            _countLabel = LabelOf("", Theme.FontSmall, Theme.Muted);
            _exportBtn = MakeButton("Export", false);
            _exportBtn.Click += delegate { ExportCsv(); };
            _copyBtn = MakeButton("Copy", false);
            _copyBtn.Click += delegate { try { if (_detail.Text.Length > 0) Clipboard.SetText(_detail.Text); } catch { } };
            _logBtn = MakeButton("Log folder", false);
            _logBtn.Click += delegate { OpenLogs(); };
            _investBtn = MakeButton("Check", false);
            _investBtn.Click += delegate { BeginErrorCheck(); };
            _applyBtn = MakeButton("Fix", true);
            _applyBtn.Enabled = false;
            _applyBtn.Click += delegate { ApplySelected(); };
            bar.Controls.Add(_filter);
            bar.Controls.Add(_countLabel);
            bar.Controls.Add(_exportBtn);
            bar.Controls.Add(_copyBtn);
            bar.Controls.Add(_logBtn);
            bar.Controls.Add(_investBtn);
            bar.Controls.Add(_applyBtn);
            bar.Resize += delegate { LayoutCrashBar(); };

            _detail = new TextBox();
            _detail.Multiline = true;
            _detail.ReadOnly = true;
            _detail.ScrollBars = ScrollBars.Vertical;
            _detail.BorderStyle = BorderStyle.None;
            _detail.BackColor = Theme.Card;
            _detail.ForeColor = Theme.Text;
            _detail.Font = Theme.FontUi;
            _detail.Dock = DockStyle.Bottom;
            _detail.Height = 210;
            _detail.Text = "Crashes from the last 7 days are imported at startup, and new ones are logged while this program is running.";

            _grid = new DataGridView();
            StyleGrid(_grid);
            _grid.Dock = DockStyle.Fill;
            _grid.SelectionChanged += GridSelectionChanged;
            _grid.HandleCreated += delegate { try { NativeMethods.SetWindowTheme(_grid.Handle, "DarkMode_Explorer", null); } catch { } };

            _pageCrash.Padding = new Padding(16, 8, 16, 12);
            _pageCrash.Controls.Add(_grid);
            _pageCrash.Controls.Add(_detail);
            _pageCrash.Resize += delegate { FitCrashDetail(); };
            _pageCrash.Controls.Add(bar);
        }

        void BuildFixes()
        {
            _fixIntro = LabelOf("Each repair runs only when you press its button.", Theme.FontSmall, Theme.Muted);
            _fixIntro.AutoSize = false;
            _fixIntro.TextAlign = ContentAlignment.MiddleLeft;
            _pageFix.Resize += delegate { LayoutFixChrome(); };
            _fixResult = LabelOf(LatestResultText(), Theme.FontUi, Theme.Text);
            _fixResult.AutoSize = false;
            _fixResult.TextAlign = ContentAlignment.MiddleLeft;
            _fixLog = new TextBox();
            _fixLog.Multiline = true;
            _fixLog.ReadOnly = true;
            _fixLog.ScrollBars = ScrollBars.Vertical;
            _fixLog.BorderStyle = BorderStyle.None;
            _fixLog.BackColor = Theme.Card;
            _fixLog.ForeColor = Theme.Text;
            _fixLog.Font = Theme.FontSmall;
            _fixLog.Height = 72;
            _fixLog.Text = "Repair results appear here. Nothing runs until you press Run.";
            _fixScroll = new KeepScrollPanel();
            _fixScroll.AutoScroll = true;
            _fixScroll.BackColor = Theme.Bg;
            string lastGroup = "";
            List<FixSpec> ordered = OrderFixes(_fixes);
            for (int i = 0; i < ordered.Count; i++)
            {
                FixSpec spec = ordered[i];
                string group = FixGroup(spec.Id);
                if (group != lastGroup)
                {
                    SectionHead head = new SectionHead(group);
                    _fixLayout.Add(head);
                    _fixScroll.Controls.Add(head);
                    lastGroup = group;
                }
                FixRow row = new FixRow(spec.Title, FirstSentence(spec.Detail), Badge(spec), spec.Button);
                row.RunButton.Tag = spec;
                row.RunButton.Click += OnRunClick;
                _fixRows.Add(row);
                _fixLayout.Add(row);
                _fixScroll.Controls.Add(row);
            }
            _fixScroll.Resize += delegate { LayoutFixes(); };
            _pageFix.Controls.Add(_fixScroll);
            _pageFix.Controls.Add(_fixLog);
            _pageFix.Controls.Add(_fixResult);
            _pageFix.Controls.Add(_fixIntro);
            LayoutFixChrome();
            RefreshTuneSummaries();
        }

        void BuildSettings()
        {
            _startCheck = Check("Start with Windows, minimized to the tray.");
            _notifyCheck = Check("Show a tray notice when a crash is logged.");
            _boostCheck = Check("While a game runs on AC power, switch to High performance, then restore the previous plan when the game closes.");
            _pauseCheck = Check("Pause the live meters. Crash watch stays on.");
            _startCheck.CheckedChanged += delegate { OnSettingChanged(); };
            _notifyCheck.CheckedChanged += delegate { OnSettingChanged(); };
            _boostCheck.CheckedChanged += delegate { OnSettingChanged(); };
            _pauseCheck.CheckedChanged += delegate { OnSettingChanged(); };
            Label intervalLabel = LabelOf("Sample interval while the window is hidden", Theme.FontSmall, Theme.Muted);
            _intervalCombo = new ComboBox();
            _intervalCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _intervalCombo.FlatStyle = FlatStyle.Flat;
            _intervalCombo.BackColor = Theme.Card;
            _intervalCombo.ForeColor = Theme.Text;
            _intervalCombo.Items.AddRange(new object[] { "15 seconds", "30 seconds", "60 seconds" });
            _intervalCombo.SelectedIndexChanged += delegate { OnSettingChanged(); };
            Label extraLabel = LabelOf("Extra game process names (one per line, without .exe)", Theme.FontSmall, Theme.Muted);
            _extraBox = Box();
            Label ignoreLabel = LabelOf("Never treat these process names as games", Theme.FontSmall, Theme.Muted);
            _ignoreBox = Box();
            Button save = MakeButton("Save lists", true);
            save.Click += delegate { OnSettingChanged(); AddActivity("Settings saved."); };
            Button logs = MakeButton("Open log folder", false);
            logs.Click += delegate { OpenLogs(); };
            Button quit = MakeButton("Quit WinHealthGuard", false);
            quit.Click += delegate { ExitApp(); };
            Label about = LabelOf("WinHealthGuard watches this PC in the background and keeps a log of crashes.", Theme.FontSmall, Theme.Muted);

            _pageSet.Padding = new Padding(20);
            _pageSet.AutoScroll = true;
            _setRows.Add(_startCheck);
            _setRows.Add(_notifyCheck);
            _setRows.Add(_boostCheck);
            _setRows.Add(_pauseCheck);
            _setRows.Add(intervalLabel);
            _setRows.Add(_intervalCombo);
            _setRows.Add(extraLabel);
            _setRows.Add(_extraBox);
            _setRows.Add(ignoreLabel);
            _setRows.Add(_ignoreBox);
            _setRows.Add(save);
            _setRows.Add(logs);
            _setRows.Add(quit);
            _setRows.Add(about);
            for (int i = 0; i < _setRows.Count; i++) _pageSet.Controls.Add(_setRows[i]);
            _pageSet.Resize += delegate { LayoutSettings(); };
            LayoutSettings();
        }

        void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            menu.BackColor = Theme.Card;
            menu.ForeColor = Theme.Text;
            ToolStripMenuItem open = new ToolStripMenuItem("Open WinHealthGuard");
            open.ForeColor = Theme.Text;
            open.Click += delegate { ShowFromTray(); };
            _pauseItem = new ToolStripMenuItem("Pause sampling");
            _pauseItem.ForeColor = Theme.Text;
            _pauseItem.Click += delegate { TogglePause(); };
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.ForeColor = Theme.Text;
            exit.Click += delegate { ExitApp(); };
            menu.Items.Add(open);
            menu.Items.Add(_pauseItem);
            menu.Items.Add(exit);
            _tray = new NotifyIcon();
            _tray.Icon = _iconGood;
            _tray.Text = "WinHealthGuard";
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;
            _tray.MouseDoubleClick += delegate { ShowFromTray(); };
            _tray.BalloonTipClicked += delegate { ShowFromTray(); ShowPage("crashes"); };
            UpdatePauseMenu();
        }

        void OnTick(object sender, EventArgs e)
        {
            try
            {
                CpuStamp next;
                HealthSnapshot snap = SystemProbe.Sample(_stamp, out next);
                _stamp = next;
                lock (_slowLock) snap.ApplySlow(_slow);
                bool scanReady;
                string found;
                _gameScan.Read(out scanReady, out found);
                snap.ForegroundIsGame = !_catalog.IsIgnored(snap.ForegroundName)
                    && (_catalog.IsKnownGame(snap.ForegroundName)
                        || (snap.ForegroundFullscreen && !string.IsNullOrEmpty(snap.ForegroundName)));
                bool gameNow = snap.ForegroundIsGame || !string.IsNullOrEmpty(found);
                if (snap.CpuValid && !gameNow && snap.CpuPercent >= 90) _hot++;
                else _hot = 0;
                _session.Update(snap, found, scanReady, AddActivity);
                gameNow = _session.InGame;
                HealthScore.Evaluate(snap, _events, _hot, gameNow, AddressedIds());
                _last = snap;
                ShowSnap(snap);
                _gameScan.MaybeStart(_catalog, _session.InGame);
                MaybeStartSlow();
                UpdateInterval();
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
            }
        }

        void ShowSnap(HealthSnapshot snap)
        {
            bool ready = snap.CpuValid;
            _ring.SetScore(snap.Score, Theme.ForStatus(snap.Status), ready);
            int issues = snap.Notes == null ? 0 : snap.Notes.Count;
            if (!ready)
            {
                _statusWord.Text = "Checking your PC";
                _statusWord.ForeColor = Theme.Muted;
                _headline.Text = "Looking for problems that crash games.";
            }
            else if (snap.Status == "Healthy" || issues == 0)
            {
                _statusWord.Text = "Your PC is healthy";
                _statusWord.ForeColor = Theme.Good;
                _headline.Text = "No issues found.";
            }
            else if (snap.Status == "Critical")
            {
                _statusWord.Text = "Action required";
                _statusWord.ForeColor = Theme.Bad;
                _headline.Text = issues == 1 ? "1 issue can make games crash or stutter." : issues.ToString() + " issues can make games crash or stutter.";
            }
            else
            {
                _statusWord.Text = "Attention needed";
                _statusWord.ForeColor = Theme.Warn;
                _headline.Text = issues == 1 ? "1 issue can make games crash or stutter." : issues.ToString() + " issues can make games crash or stutter.";
            }
            if (_settings.PauseSampling && ready)
                _headline.Text = "Live meters are paused. Crash watch stays on.";
            RenderIssues(snap);

            if (!snap.CpuValid) _cpuCard.Set("...", "Checking", Theme.Muted);
            else
            {
                Color c = snap.CpuPercent >= 90 ? Theme.Bad : snap.CpuPercent >= 70 ? Theme.Warn : Theme.Good;
                string hint = snap.CpuPercent < 25 ? "Quiet" : snap.CpuPercent < 70 ? "Busy" : "High";
                _cpuCard.Set(snap.CpuPercent.ToString("0") + "%", hint, c);
            }
            double usedGb = snap.RamUsedMb / 1024.0;
            double totalGb = snap.RamTotalMb / 1024.0;
            _ramCard.Set(usedGb.ToString("0.0") + " GB", "of " + totalGb.ToString("0.0") + " GB", Theme.Good);
            _diskCard.Set(snap.SystemFreeGb.ToString("0.0") + " GB free", snap.SystemDrive, snap.SystemFreeGb < 20 ? Theme.Warn : Theme.Good);
            string powerHint = snap.OnAc ? "Plugged in" : "On battery";
            if (_settings.PowerBoostActive) powerHint = "Game boost";
            _powerCard.Set(string.IsNullOrEmpty(snap.PowerName) ? "..." : snap.PowerName, powerHint, Theme.Good);

            if (snap.CpuValid) _chart.Add(snap.CpuPercent, snap.RamPercent);
            if (_lastStatus != snap.Status)
            {
                if (_lastStatus != null) AddActivity("Health is " + snap.Status + " (" + snap.Score.ToString() + ").");
                _lastStatus = snap.Status;
                UpdateIcon(snap.Status);
            }
            string tip = string.Format("WinHealthGuard {0} {1}  CPU {2:0}%  RAM {3:0}%", snap.Status, snap.Score, snap.CpuPercent, snap.RamPercent);
            try { _tray.Text = tip.Length > 120 ? tip.Substring(0, 120) : tip; }
            catch { _tray.Text = "WinHealthGuard"; }
            string dashKey = _statusWord.Text + "|" + _headline.Text + "|"
                + _cpuCard.ValueText + "|" + _cpuCard.Hint + "|"
                + _ramCard.ValueText + "|" + _ramCard.Hint + "|"
                + _diskCard.ValueText + "|" + _diskCard.Hint + "|"
                + _powerCard.ValueText + "|" + _powerCard.Hint;
            if (dashKey != _dashTextKey)
            {
                _dashTextKey = dashKey;
                LayoutDashboard();
            }
            UpdateDriverRow();
        }

        void RenderIssues(HealthSnapshot snap)
        {
            StringBuilder key = new StringBuilder();
            int count = snap.Notes == null ? 0 : snap.Notes.Count;
            for (int i = 0; i < count; i++)
            {
                HealthIssue issue = snap.Notes[i];
                key.Append(issue.Title).Append('|').Append(issue.RepairId).Append('|').Append(issue.Detail).Append('\n');
            }
            string next = key.ToString();
            if (next == _issueKey && _issueHost.Controls.Count == count) return;
            _issueKey = next;
            _issueHost.Controls.Clear();
            for (int i = 0; i < count; i++)
            {
                HealthIssue issue = snap.Notes[i];
                IssueRow row = new IssueRow(issue);
                string id = issue.RepairId;
                row.Action.Click += delegate { OnIssue(id); };
                _issueHost.Controls.Add(row);
            }
            LayoutDashboard();
        }

        void OnIssue(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (id == "crashes")
            {
                ShowPage("crashes");
                return;
            }
            FixSpec spec = FixCatalog.Find(_fixes, id);
            if (spec == null)
            {
                ShowPage("fixes");
                return;
            }
            RunFix(spec);
        }

        void MaybeStartSlow()
        {
            if (DateTime.UtcNow < _nextSlow) return;
            if (System.Threading.Interlocked.CompareExchange(ref _slowBusy, 1, 0) != 0) return;
            _nextSlow = DateTime.UtcNow.AddMinutes(10);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    SlowFacts facts = SystemProbe.ReadSlow();
                    lock (_slowLock) _slow = facts;
                }
                catch (Exception ex) { Program.LogException(ex); }
                finally { System.Threading.Interlocked.Exchange(ref _slowBusy, 0); }
            });
        }

        void UpdateInterval()
        {
            if (_settings.PauseSampling && _last != null && _last.CpuValid)
            {
                _timer.Stop();
                return;
            }
            if (!_timer.Enabled) _timer.Start();
            bool visible = Visible && WindowState != FormWindowState.Minimized;
            int ms = visible ? 2000 : (_session.InGame ? 10000 : _settings.IdleSampleSeconds * 1000);
            if (_timer.Interval != ms) _timer.Interval = ms;
        }

        void OnCrashBackground(CrashEvent ev)
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action(delegate { AcceptLive(ev); })); }
            catch (InvalidOperationException) { }
        }

        void AcceptLive(CrashEvent ev)
        {
            Prepare(ev, true);
            CrashStore.Append(ev);
            _events.Insert(0, ev);
            if (_events.Count > 1000) _events.RemoveAt(_events.Count - 1);
            ForgetAddressed(ev);
            RefreshFindings();
            AddActivity("Crash logged: " + ev.Summary);
            NotifyCrash(ev);
            Rebind();
            if (_last != null)
            {
                HealthScore.Evaluate(_last, _events, _hot, _session.InGame, AddressedIds());
                ShowSnap(_last);
            }
        }

        void BeginImport()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<CrashEvent> fresh = null;
                string error = null;
                try { fresh = _monitor.ImportRecent(); }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Program.LogException(ex);
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (error != null)
                        {
                            AddActivity("Could not read the Windows event log: " + error);
                            return;
                        }
                        int n = fresh == null ? 0 : fresh.Count;
                        for (int i = 0; i < n; i++)
                        {
                            Prepare(fresh[i], false);
                            CrashStore.Append(fresh[i]);
                            _events.Add(fresh[i]);
                        }
                        if (n > 0) SortEvents();
                        RefreshFindings();
                        Rebind();
                        if (n == 0) AddActivity("No new crashes in the last 7 days.");
                        else AddActivity("Imported " + n.ToString() + " crash events from the last 7 days.");
                    }));
                }
                catch { }
            });
        }

        void Prepare(CrashEvent ev, bool live)
        {
            ev.LikelyGame = _catalog.IsKnownGame(ev.Process) || _catalog.LooksLikeGamePath(ev.AppPath);
            if (live && _session.InGame)
            {
                ev.DuringGame = _session.GameName;
                ev.LikelyGame = true;
            }
            if (live && _last != null && _last.CpuValid)
            {
                ev.HasContext = true;
                ev.Cpu = _last.CpuPercent;
                ev.Ram = _last.RamPercent;
                ev.DiskFreeGb = _last.SystemFreeGb;
                ev.OnAc = _last.OnAc;
                ev.PowerScheme = _last.PowerName ?? "";
            }
            CrashParser.Diagnose(ev);
        }

        void RefreshFindings()
        {
            for (int i = 0; i < _events.Count; i++)
            {
                CrashEvent ev = _events[i];
                ev.LikelyGame = _catalog.IsKnownGame(ev.Process) || _catalog.LooksLikeGamePath(ev.AppPath) || !string.IsNullOrEmpty(ev.DuringGame);
                CrashParser.Diagnose(ev);
            }
            Investigation.Apply(_events);
        }

        void Rebind()
        {
            if (_grid.Columns.Count == 0)
            {
                _grid.Columns.Add("time", "When");
                _grid.Columns.Add("type", "Kind");
                _grid.Columns.Add("process", "Program");
                _grid.Columns.Add("code", "Code");
                _grid.Columns.Add("summary", "What happened");
                _grid.Columns[0].FillWeight = 90;
                _grid.Columns[1].FillWeight = 78;
                _grid.Columns[2].FillWeight = 90;
                _grid.Columns[3].FillWeight = 70;
                _grid.Columns[4].FillWeight = 220;
                _grid.Columns[0].MinimumWidth = 128;
                _grid.Columns[1].MinimumWidth = 88;
                _grid.Columns[2].MinimumWidth = 110;
                _grid.Columns[3].MinimumWidth = 72;
                _grid.Columns[4].MinimumWidth = 220;
            }
            string keep = _selectedId;
            _grid.SelectionChanged -= GridSelectionChanged;
            _grid.Rows.Clear();
            int shown = 0;
            int select = -1;
            for (int i = 0; i < _events.Count; i++)
            {
                CrashEvent ev = _events[i];
                if (!Pass(ev)) continue;
                string code = ev.ExceptionCode.Length > 0 ? ev.ExceptionCode : ev.BugCheck;
                int row = _grid.Rows.Add(
                    ev.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    CrashParser.FriendlyKind(ev.Kind),
                    ev.Process,
                    code,
                    ev.Summary);
                _grid.Rows[row].Tag = ev;
                if (ev.Id == keep) select = row;
                shown++;
            }
            _countLabel.Text = shown.ToString() + " shown";
            _grid.SelectionChanged += GridSelectionChanged;
            if (select >= 0 && select < _grid.Rows.Count)
                _grid.CurrentCell = _grid.Rows[select].Cells[0];
            else
                OnCrashSelected();
        }

        void GridSelectionChanged(object sender, EventArgs e)
        {
            OnCrashSelected();
        }

        void OnCrashSelected()
        {
            CrashEvent ev = SelectedEvent();
            _selectedId = ev == null ? null : ev.Id;
            _detail.Text = ev == null
                ? "Select a crash to see what happened and which repair applies."
                : DetailText(ev);
            _applyBtn.Text = "Fix";
            _applyBtn.Enabled = ev != null;
        }

        CrashEvent SelectedEvent()
        {
            if (_grid.CurrentRow == null) return null;
            return _grid.CurrentRow.Tag as CrashEvent;
        }

        bool Pass(CrashEvent ev)
        {
            string mode = _filter.SelectedItem as string;
            if (string.IsNullOrEmpty(mode) || mode == "All") return true;
            if (mode == "Games") return ev.LikelyGame || ev.Overlay;
            if (mode == "Blue screens")
                return ev.Kind == "BugCheck" || ev.Kind == "HardwareError" || ev.Kind == "PowerLoss" || ev.Kind == "UnexpectedShutdown";
            if (mode == "Display driver") return ev.Kind == "DisplayDriver";
            return ev.Kind == "AppCrash" || ev.Kind == "AppHang";
        }

        string DetailText(CrashEvent ev)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(ev.Summary);
            if (!string.IsNullOrEmpty(ev.Suggestion))
            {
                sb.AppendLine();
                sb.AppendLine("Repair");
                sb.AppendLine(ev.Suggestion);
            }
            sb.AppendLine();
            sb.AppendLine("Details");
            Field(sb, "When", ev.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            Field(sb, "Kind", CrashParser.FriendlyKind(ev.Kind));
            Field(sb, "Source", (ev.Provider ?? "") + "  " + ev.EventId.ToString());
            Field(sb, "Program", ev.Process);
            Field(sb, "Path", ev.AppPath);
            Field(sb, "Module", ev.FaultModule);
            if (!string.IsNullOrEmpty(ev.ExceptionCode))
            {
                string name = CrashParser.ExceptionName(ev.ExceptionCode);
                Field(sb, "Exception", ev.ExceptionCode + (name.Length > 0 ? "  " + name : ""));
            }
            if (!string.IsNullOrEmpty(ev.BugCheck))
            {
                string name = CrashParser.BugCheckName(ev.BugCheck);
                Field(sb, "Stop code", ev.BugCheck + (name.Length > 0 ? "  " + name : ""));
            }
            Field(sb, "Session", ev.DuringGame);
            if (ev.HasContext)
            {
                Field(sb, "System", string.Format("CPU {0:0}%   RAM {1:0}%   {2:0.0} GB free   {3}   {4}",
                    ev.Cpu, ev.Ram, ev.DiskFreeGb, ev.PowerScheme, ev.OnAc ? "Plugged in" : "On battery"));
            }
            string values = TuneCatalog.Preview(ev.RepairId);
            if (values.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Current values");
                sb.AppendLine(values);
            }
            string check = CrashCheck(ev);
            if (check.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("After the repair");
                sb.AppendLine(check);
            }
            string report = TidyReport(ev.Detail);
            if (report.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Windows report");
                sb.Append(report);
            }
            return sb.ToString().Trim();
        }

        string CrashCheck(CrashEvent ev)
        {
            if (ev == null) return "";
            string id = ev.RepairId ?? "";
            FixProof proof = LatestProof(id);
            if (proof == null)
            {
                if (ExplainOnly(ev))
                    return "This error has no setting to change. If it happens again, a newer row appears in this list.";
                if (id.Length == 0)
                    return "No repair has been run for this error yet.";
                return "After you run the repair, this page checks whether the same error comes back.";
            }
            string stamp = proof.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            if (ev.Time.ToUniversalTime() > proof.When.AddSeconds(2))
                return "This error happened again after the repair on " + stamp + ". " + proof.Result;
            if (CameBack(ev, proof.When))
                return "The same kind of error happened again after the repair on " + stamp + ".";
            return "No new copy of this error since the repair on " + stamp + ". " + proof.Result;
        }

        FixProof LatestProof(string repairId)
        {
            for (int i = _proofs.Count - 1; i >= 0; i--)
            {
                if (SameRepair(_proofs[i].Id, repairId)) return _proofs[i];
            }
            return null;
        }

        bool CameBack(CrashEvent ev, DateTime after)
        {
            string id = ev.RepairId ?? "";
            for (int i = 0; i < _events.Count; i++)
            {
                CrashEvent other = _events[i];
                if (other == null || other.Id == ev.Id) continue;
                if (other.Time.ToUniversalTime() <= after.AddSeconds(2)) continue;
                if (SameRepair(other.RepairId, id)) return true;
            }
            return false;
        }

        static bool SameRepair(string left, string right)
        {
            left = left ?? "";
            right = right ?? "";
            if (left.Length == 0 || right.Length == 0) return false;
            if (left == right) return true;
            if ((left == "amd" || left == "overlayoff") && (right == "amd" || right == "overlayoff")) return true;
            if ((left == "sfc" || left == "dismsfc") && (right == "sfc" || right == "dismsfc")) return true;
            return false;
        }

        static void Field(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            sb.Append(label);
            sb.Append(": ");
            sb.AppendLine(value.Trim());
        }

        static string TidyReport(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder sb = new StringBuilder();
            int kept = 0;
            bool blank = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = CollapseSpaces(lines[i]);
                if (line.Length == 0)
                {
                    if (kept > 0 && !blank)
                    {
                        sb.AppendLine();
                        blank = true;
                    }
                    continue;
                }
                blank = false;
                sb.AppendLine(line);
                kept++;
                if (kept >= 24)
                {
                    sb.AppendLine("...");
                    break;
                }
            }
            return sb.ToString().Trim();
        }

        static string CollapseSpaces(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            StringBuilder sb = new StringBuilder();
            bool space = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == ' ' || c == '\t')
                {
                    if (sb.Length > 0) space = true;
                    continue;
                }
                if (space)
                {
                    sb.Append(' ');
                    space = false;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static string FirstSentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int dot = text.IndexOf(". ", StringComparison.Ordinal);
            if (dot < 28 || dot > 160) return text.Trim();
            return text.Substring(0, dot + 1);
        }

        static List<FixSpec> OrderFixes(List<FixSpec> specs)
        {
            List<FixSpec> ordered = new List<FixSpec>();
            if (specs == null) return ordered;
            string[] groups = new string[] { "Memory", "Graphics", "Windows", "Network and disks" };
            for (int g = 0; g < groups.Length; g++)
            {
                for (int i = 0; i < specs.Count; i++)
                {
                    if (FixGroup(specs[i].Id) == groups[g]) ordered.Add(specs[i]);
                }
            }
            return ordered;
        }

        static string FixGroup(string id)
        {
            if (id == "pagefile" || id == "fault50" || id == "memory" || id == "browsers" || id == "visualfx" || id == "sysmain" || id == "startuptrim" || id == "maxmem")
                return "Memory";
            if (id == "driver" || id == "tdr" || id == "hagsoff" || id == "hagson" || id == "overlayoff" || id == "amd" || id == "nvidia" || id == "fullscreen")
                return "Graphics";
            if (id == "dns" || id == "netreset" || id == "storage" || id == "chkdsk")
                return "Network and disks";
            return "Windows";
        }

        void BeginSteamVerify(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                for (int i = 0; i < _events.Count; i++)
                {
                    if (_events[i].RepairId == "steam" && !string.IsNullOrEmpty(_events[i].AppPath))
                    {
                        path = _events[i].AppPath;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this, "Select the game crash in Crash History, then press Verify.", "Verify game files", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowPage("crashes");
                return;
            }
            if (MessageBox.Show(this, "Steam will check this game's installed files and replace anything that does not match.\n\n" + path, "Verify game files", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;
            if (_repairing) return;
            RememberBefore();
            _pendingRepair = "steam";
            _pendingCounts = true;
            _repairing = true;
            ShowProgress("Verifying game files...");
            string gamePath = path;
            BeginInvoke(new Action(delegate
            {
                RepairResult done = Repairs.VerifySteamGame(gamePath);
                FinishTask("Verify game files", done.Message, done.Ok);
            }));
        }

        void BeginDriverCheck()
        {
            if (_repairing) return;
            _repairing = true;
            ShowProgress("Checking Windows Update, then the maker, then a third-party page...");
            Thread thread = new Thread(new ThreadStart(delegate
            {
                DriverOffer offer = DriverUpdate.Search();
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _repairing = false;
                        _progress.Style = ProgressBarStyle.Continuous;
                        _progress.Value = 100;
                        _progressLabel.Text = "Driver check finished.";
                        LogFix(offer.Report());
                        string choice = DriverUpdate.Ask(this, offer);
                        HideProgress();
                        if (choice == "wu") StartDriverInstall(offer.WindowsUpdateTitle);
                        else if (choice == "vendor") StartDriverDownload(offer);
                        else if (choice == "nvidia") OpenNvidiaSearch(offer.VendorSearch);
                        else if (choice == "third") OpenSoftpedia(offer.ThirdPartyUrl);
                    }));
                }
                catch { }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        void StartDriverInstall(string title)
        {
            if (_repairing) return;
            RememberBefore();
            _pendingRepair = "driver";
            _pendingCounts = true;
            _repairing = true;
            ShowProgress("Installing the graphics driver from Windows Update...");
            Thread thread = new Thread(new ThreadStart(delegate
            {
                RepairResult result;
                try { result = DriverUpdate.InstallWindowsUpdate(title); }
                catch (Exception ex) { result = RepairResult.Fail(ex.Message); }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        FinishTask("Graphics driver", result.Message, result.Ok);
                    }));
                }
                catch { }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        void OpenNvidiaSearch(string product)
        {
            if (_repairing) return;
            _repairing = true;
            ShowProgress("Opening NVIDIA's driver page...");
            Thread thread = new Thread(new ThreadStart(delegate
            {
                string message;
                bool ok;
                try { ok = DriverUpdate.OpenNvidiaPage(product, out message); }
                catch (Exception ex)
                {
                    ok = false;
                    message = ex.Message;
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _repairing = false;
                        _progress.Style = ProgressBarStyle.Continuous;
                        _progress.Value = 100;
                        _progressLabel.Text = ok ? "Finished." : "Failed.";
                        LogFix(message);
                        AddActivity(message);
                        MessageBox.Show(this, message, "NVIDIA driver search", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                        HideProgress();
                    }));
                }
                catch { }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        void OpenSoftpedia(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return;
            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return;
            string host = uri.Host.ToLowerInvariant();
            if (host != "www.softpedia.com" && host != "drivers.softpedia.com") return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = uri.AbsoluteUri;
                psi.UseShellExecute = true;
                Process.Start(psi);
                LogFix("Opened the Softpedia page after the warning. Nothing was downloaded.");
                AddActivity("Opened the Softpedia driver page. Nothing was downloaded.");
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
            }
        }

        void StartDriverDownload(DriverOffer offer)
        {
            if (_repairing) return;
            RememberBefore();
            _pendingRepair = "driver";
            _pendingCounts = true;
            _repairing = true;
            ShowProgress("Downloading the official graphics installer...");
            Thread thread = new Thread(new ThreadStart(delegate
            {
                RepairResult result;
                try { result = DriverUpdate.DownloadAndRun(offer); }
                catch (Exception ex) { result = RepairResult.Fail(ex.Message); }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        FinishTask("Graphics driver", result.Message, result.Ok);
                    }));
                }
                catch { }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        string RepairButton(CrashEvent ev)
        {
            if (ev == null || string.IsNullOrEmpty(ev.RepairId)) return "";
            if (ev.RepairId == "steam" || ev.RepairId == "fullscreen" || ev.RepairId == "driver") return ev.RepairId;
            FixSpec spec = FixCatalog.Find(_fixes, ev.RepairId);
            return spec == null ? "" : ev.RepairId;
        }

        void ApplySelected()
        {
            if (_repairing) return;
            CrashEvent ev = SelectedEvent();
            if (ev == null)
            {
                MessageBox.Show(this, "Select a crash first.", "Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RefreshFindings();
            ev = FindEvent(ev.Id) ?? ev;
            if (ExplainOnly(ev))
            {
                FinishTask("Fix", Report(ev), true);
                return;
            }
            string repair = RepairButton(ev);
            if (repair.Length > 0)
            {
                RunKnownRepair(ev, repair);
                return;
            }
            BeginLearnAndFix(ev);
        }

        void RunKnownRepair(CrashEvent ev, string repair)
        {
            if (repair == "steam")
            {
                BeginSteamVerify(ev == null ? "" : ev.AppPath);
                return;
            }
            if (repair == "fullscreen")
            {
                string path = ev == null ? "" : ev.AppPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = PickExe();
                if (string.IsNullOrEmpty(path)) return;
                RunFullscreen(path);
                return;
            }
            if (repair == "driver")
            {
                BeginDriverCheck();
                return;
            }
            FixSpec spec = FixCatalog.Find(_fixes, repair);
            if (spec != null) RunFix(spec);
        }

        void OnRunClick(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;
            FixSpec spec = button.Tag as FixSpec;
            if (spec != null) RunFix(spec);
        }

        void RunFix(FixSpec spec)
        {
            if (_repairing) return;
            _pendingRepair = "";
            _pendingCounts = false;
            if (spec.Id == "driver")
            {
                BeginDriverCheck();
                return;
            }
            if (spec.Id == "steam")
            {
                BeginSteamVerify("");
                return;
            }
            if (spec.Id == "fullscreen")
            {
                string path = PickExe();
                if (string.IsNullOrEmpty(path)) return;
                _pendingRepair = "fullscreen";
                _pendingCounts = true;
                RunFullscreen(path);
                return;
            }
            string warn = spec.Detail;
            if (spec.Id == "shaders" && _session.InGame)
                warn += "\n\nA game looks like it is running. Close it first so the cache files are not locked.";
            if (spec.Admin) warn += "\n\nWindows will ask you to approve administrator access.";
            if (spec.Reboot) warn += "\n\nRestart Windows after this. WinHealthGuard will not restart the PC for you.";
            if (spec.LongRunning) warn += "\n\nThis can take a long time. You can hide this window to the tray while it runs.";
            List<TuneItem> chosen = TuneCatalog.For(spec.Id);
            if (chosen.Count > 0)
            {
                string tuneNote = warn + "\n\nTick a line to change it. The value on the right is what will be written.";
                if (spec.Id == "pagefile" || spec.Id == "fault50" || spec.Id == "maxmem")
                    tuneNote += " The page file stays on, and a memory limit cannot be typed in.";
                if (!TuneDialog.Ask(this, spec.Title, tuneNote, chosen))
                    return;
            }
            else if (MessageBox.Show(this, warn, spec.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;
            RememberBefore();
            _pendingRepair = spec.Id;
            _pendingCounts = !spec.Opener && spec.Id != "shaders";
            if (spec.Opener)
            {
                _repairing = true;
                ShowProgress("Opening " + spec.Title + "...");
                string openId = spec.Id;
                string openTitle = spec.Title;
                BeginInvoke(new Action(delegate
                {
                    RepairResult opened = Repairs.Run(openId);
                    FinishTask(openTitle, opened.Message, opened.Ok);
                }));
                return;
            }
            _repairing = true;
            ShowProgress("Fixing: " + spec.Title);
            string id = spec.Id;
            string title = spec.Title;
            bool reboot = spec.Reboot;
            Thread thread = new Thread(new ThreadStart(delegate
            {
                RepairResult result;
                try { result = chosen.Count > 0 ? Repairs.Run(id, chosen) : Repairs.Run(id); }
                catch (Exception ex) { result = RepairResult.Fail(ex.Message); }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        string message = result.Message ?? "";
                        if (result.Ok && chosen.Count > 0)
                        {
                            string breakdown = TuneCatalog.Diff(chosen, TuneCatalog.For(id));
                            if (breakdown.Length > 0) _breakdown = breakdown;
                        }
                        if (result.Ok && (reboot || result.Reboot))
                            message = message.Trim() + "\n\nRestart Windows when you can so this change can take effect.";
                        FinishTask(title, message, result.Ok);
                    }));
                }
                catch { }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        void BeginErrorCheck()
        {
            if (_repairing) return;
            CrashEvent ev = SelectedEvent();
            if (ev == null)
            {
                MessageBox.Show(this, "Select a crash first.", "Check", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _repairing = true;
            ShowProgress("Checking this system error...");
            string id = ev.Id;
            RefreshFindings();
            Rebind();
            CrashEvent current = FindEvent(id);
            if (current == null) current = ev;
            if (ExplainOnly(current) || RepairButton(current).Length > 0)
            {
                string report = Report(current);
                BeginInvoke(new Action(delegate { FinishTask("Check", report, true); }));
                return;
            }
            Thread thread = new Thread(new ThreadStart(delegate
            {
                string learned = SolutionSearch.LearnRepair(current);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (!string.IsNullOrEmpty(learned))
                        {
                            current.RepairId = learned;
                            string action = SolutionSearch.Action(learned);
                            if (action.Length > 0) current.Suggestion = "The repair for this error is: " + action;
                        }
                        _detail.Text = Report(current);
                        FinishTask("Check", Report(current), true);
                    }));
                }
                catch { }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        void BeginLearnAndFix(CrashEvent ev)
        {
            _repairing = true;
            ShowProgress("Choosing a repair for this error...");
            string id = ev.Id;
            Thread thread = new Thread(new ThreadStart(delegate
            {
                string learned = SolutionSearch.LearnRepair(ev);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _repairing = false;
                        HideProgress();
                        CrashEvent current = FindEvent(id);
                        if (current == null) current = ev;
                        if (!string.IsNullOrEmpty(learned))
                        {
                            current.RepairId = learned;
                            string action = SolutionSearch.Action(learned);
                            if (action.Length > 0) current.Suggestion = "The repair for this error is: " + action;
                            _detail.Text = Report(current);
                            RunKnownRepair(current, learned);
                            return;
                        }
                        FinishTask("Fix", current.Summary + "\n\nNo automatic repair matches this error.", false);
                    }));
                }
                catch { }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        static string Report(CrashEvent ev)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(ev.Summary);
            if (!string.IsNullOrEmpty(ev.Suggestion))
            {
                sb.AppendLine();
                sb.AppendLine(ev.Suggestion);
            }
            string action = SolutionSearch.Action(ev.RepairId);
            if (action.Length > 0)
            {
                sb.AppendLine();
                sb.Append("Repair: ").Append(action);
            }
            else if (string.IsNullOrEmpty(ev.Suggestion) && ev.RepairId != "manual")
            {
                sb.AppendLine();
                sb.Append("No automatic repair matches this error.");
            }
            return sb.ToString().Trim();
        }

        static bool ExplainOnly(CrashEvent ev)
        {
            if (ev == null) return false;
            if (ev.RepairId == "manual") return true;
            if (!string.IsNullOrEmpty(ev.RepairId)) return false;
            string suggestion = ev.Suggestion ?? "";
            return suggestion.IndexOf("same restart", StringComparison.OrdinalIgnoreCase) >= 0
                || suggestion.IndexOf("lost power", StringComparison.OrdinalIgnoreCase) >= 0
                || suggestion.IndexOf("No Windows setting", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        CrashEvent FindEvent(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i].Id == id) return _events[i];
            }
            return null;
        }

        void RunFullscreen(string path)
        {
            if (string.IsNullOrEmpty(path) || _repairing) return;
            RememberBefore();
            _pendingRepair = "fullscreen";
            _pendingCounts = true;
            _repairing = true;
            ShowProgress("Turning off fullscreen optimizations...");
            string chosen = path;
            BeginInvoke(new Action(delegate
            {
                RepairResult done = Repairs.DisableFullscreenOptimizations(chosen);
                FinishTask("Fullscreen optimizations", done.Message, done.Ok);
            }));
        }

        void ShowProgress(string text)
        {
            FitProgress(text);
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 28;
            _progressHost.Visible = true;
            SetFixButtons(false);
            _investBtn.Enabled = false;
            _applyBtn.Enabled = false;
        }

        void FitProgress(string text)
        {
            _progressLabel.Text = text ?? "";
            int inner = _progressHost.ClientSize.Width - _progressHost.Padding.Horizontal;
            if (inner < 240) inner = 640;
            int h = TextBlock.Height(_progressLabel.Text, _progressLabel.Font, inner, 22);
            _progressLabel.Height = h;
            int host = h + 42;
            if (host < 72) host = 72;
            _progressHost.Height = host;
        }

        void HideProgress()
        {
            _progressHost.Visible = false;
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
            SetFixButtons(true);
            _investBtn.Enabled = true;
            OnCrashSelected();
        }

        void FinishTask(string title, string message, bool ok)
        {
            _repairing = false;
            string repairId = _pendingRepair ?? "";
            bool counts = _pendingCounts && ok;
            _pendingRepair = "";
            _pendingCounts = false;
            if (counts) Address(repairId);
            if (string.IsNullOrEmpty(message)) message = ok ? "Finished." : "The task failed.";
            string breakdown = _breakdown ?? "";
            _breakdown = "";
            int score = -1;
            if (repairId.Length > 0)
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 100;
                _progressLabel.Text = "Updating the health score...";
                score = RefreshHomeNow(true);
                string check = "";
                if (ok && _haveBefore)
                {
                    FixMeter after = FixMeter.From(_last);
                    check = FixCheck.Describe(repairId, _before, after);
                    if (breakdown.Length > 0) check = breakdown + "\n" + check;
                    if (!OpenerRepair(repairId))
                    {
                        FixProof proof = new FixProof();
                        proof.Id = repairId;
                        proof.When = DateTime.UtcNow;
                        proof.ScoreBefore = _before == null ? 0 : _before.Score;
                        proof.ScoreAfter = after.Score;
                        proof.Result = check;
                        _proofs.Add(proof);
                        FixCheck.Save(_proofs);
                    }
                    ShowFixResult(check);
                }
                if (check.Length > 0) message = message.Trim() + "\n\n" + check;
                else message = message.Trim() + "\n\nHealth score is now " + score.ToString() + ".";
            }
            _haveBefore = false;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;
            _progressLabel.Text = ok ? "Finished." : "Failed.";
            LogFix(message);
            AddActivity(message);
            RefreshTuneSummaries();
            if (Visible)
                MessageBox.Show(this, message, title, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            else
                MessageBox.Show(message, title, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            HideProgress();
            if (repairId.Length == 0) RefreshHomeNow(false);
        }

        int RefreshHomeNow(bool includeSlow)
        {
            try
            {
                CpuStamp next;
                HealthSnapshot snap = SystemProbe.Sample(_stamp, out next);
                _stamp = next;
                if (includeSlow)
                {
                    SlowFacts facts = SystemProbe.ReadSlow();
                    lock (_slowLock) _slow = facts;
                    _nextSlow = DateTime.UtcNow.AddMinutes(10);
                }
                lock (_slowLock) snap.ApplySlow(_slow);
                HealthScore.Evaluate(snap, _events, _hot, _session.InGame, AddressedIds());
                _last = snap;
                ShowSnap(snap);
                return snap.Score;
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
                if (_last == null) return 0;
                HealthScore.Evaluate(_last, _events, _hot, _session.InGame, AddressedIds());
                ShowSnap(_last);
                return _last.Score;
            }
        }

        List<string> AddressedIds()
        {
            List<string> list = new List<string>();
            foreach (KeyValuePair<string, DateTime> pair in _addressed) list.Add(pair.Key);
            return list;
        }

        void Address(string repairId)
        {
            if (string.IsNullOrEmpty(repairId) || repairId == "crashes" || repairId == "shaders") return;
            DateTime now = DateTime.UtcNow;
            _addressed[repairId] = now;
            if (repairId == "amd" || repairId == "overlayoff")
            {
                _addressed["amd"] = now;
                _addressed["overlayoff"] = now;
            }
            if (repairId == "sfc" || repairId == "dismsfc")
            {
                _addressed["sfc"] = now;
                _addressed["dismsfc"] = now;
            }
        }

        void ForgetAddressed(CrashEvent ev)
        {
            if (ev == null) return;
            string id = ev.RepairId ?? "";
            DateTime when;
            if (id.Length == 0 || !_addressed.TryGetValue(id, out when)) return;
            if (ev.Time.ToUniversalTime() >= when.AddSeconds(-2)) _addressed.Remove(id);
        }

        string PickExe()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Programs (*.exe)|*.exe";
                dialog.Title = "Choose the game executable";
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                return dialog.FileName;
            }
        }

        void SetFixButtons(bool enabled)
        {
            for (int i = 0; i < _fixRows.Count; i++) _fixRows[i].RunButton.Enabled = enabled;
        }

        void LogFix(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _fixLog.AppendText("\r\n" + DateTime.Now.ToString("HH:mm:ss") + "  " + message.Trim() + "\r\n");
        }

        void RefreshTuneSummaries()
        {
            for (int i = 0; i < _fixRows.Count; i++)
            {
                FixSpec spec = _fixRows[i].RunButton.Tag as FixSpec;
                if (spec == null) continue;
                string text = spec.Id == "driver" ? HealthScore.DriverSentence(_last) : FirstSentence(spec.Detail);
                string now = TuneCatalog.Summary(spec.Id);
                if (spec.Id != "driver" && now.Length > 0) text = text + " Now: " + now + ".";
                if (spec.Id == "driver") _driverRowText = text;
                _fixRows[i].SetDetail(text);
            }
            LayoutFixes();
        }

        void RememberBefore()
        {
            _before = FixMeter.From(_last);
            _haveBefore = true;
        }

        static bool OpenerRepair(string id)
        {
            return id == "memory" || id == "wu" || id == "gaming" || id == "amd" || id == "nvidia";
        }

        string LatestResultText()
        {
            for (int i = _proofs.Count - 1; i >= 0; i--)
            {
                if (_proofs[i] != null && !string.IsNullOrEmpty(_proofs[i].Result)) return _proofs[i].Result;
            }
            return "After a repair, this line compares the health score with the reading taken just before it. It says when the score or the repaired setting improved.";
        }

        void ShowFixResult(string text)
        {
            if (_fixResult == null || string.IsNullOrEmpty(text)) return;
            _fixResult.Text = text;
            FitFixResult();
        }

        void FitFixResult()
        {
            LayoutFixChrome();
        }

        void LayoutFixChrome()
        {
            if (_pageFix == null || _fixScroll == null || _fixIntro == null || _fixResult == null || _fixLog == null) return;
            int w = _pageFix.ClientSize.Width;
            int h = _pageFix.ClientSize.Height;
            if (w < 80 || h < 80) return;
            int inner = w - 32;
            int introH = TextBlock.Height(_fixIntro.Text, _fixIntro.Font, inner, 20) + 8;
            int resultH = TextBlock.Height(_fixResult.Text, _fixResult.Font, inner, 36) + 8;
            int logH = 72;
            _fixIntro.SetBounds(16, 8, inner, introH);
            _fixResult.SetBounds(16, 8 + introH, inner, resultH);
            int scrollTop = 16 + introH + resultH;
            int scrollH = h - scrollTop - logH - 24;
            if (scrollH < 80) scrollH = 80;
            _fixScroll.SetBounds(16, scrollTop, inner, scrollH);
            _fixLog.SetBounds(16, scrollTop + scrollH + 8, inner, logH);
        }

        void ExportCsv()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV (*.csv)|*.csv";
                dialog.FileName = "WinHealthGuard-crashes.csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Time,Type,Process,Code,Summary,Path,Suggestion");
                for (int i = 0; i < _events.Count; i++)
                {
                    CrashEvent ev = _events[i];
                    string code = ev.ExceptionCode.Length > 0 ? ev.ExceptionCode : ev.BugCheck;
                    sb.Append(Csv(ev.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))).Append(',');
                    sb.Append(Csv(CrashParser.FriendlyKind(ev.Kind))).Append(',');
                    sb.Append(Csv(ev.Process)).Append(',');
                    sb.Append(Csv(code)).Append(',');
                    sb.Append(Csv(ev.Summary)).Append(',');
                    sb.Append(Csv(ev.AppPath)).Append(',');
                    sb.Append(Csv(ev.Suggestion)).AppendLine();
                }
                File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
                AddActivity("Exported crashes to " + dialog.FileName);
            }
        }

        static string Csv(string value)
        {
            if (value == null) value = "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        void LoadSettingsToUi()
        {
            _loadingSettings = true;
            _startCheck.Checked = _settings.StartWithWindows;
            _notifyCheck.Checked = _settings.NotifyOnCrash;
            _boostCheck.Checked = _settings.BoostPowerWhileGaming;
            _pauseCheck.Checked = _settings.PauseSampling;
            _extraBox.Text = _settings.ExtraGames ?? "";
            _ignoreBox.Text = _settings.IgnoreProcesses ?? "";
            if (_settings.IdleSampleSeconds == 15) _intervalCombo.SelectedIndex = 0;
            else if (_settings.IdleSampleSeconds == 60) _intervalCombo.SelectedIndex = 2;
            else _intervalCombo.SelectedIndex = 1;
            _loadingSettings = false;
        }

        void OnSettingChanged()
        {
            if (_loadingSettings) return;
            _settings.StartWithWindows = _startCheck.Checked;
            _settings.NotifyOnCrash = _notifyCheck.Checked;
            _settings.BoostPowerWhileGaming = _boostCheck.Checked;
            _settings.PauseSampling = _pauseCheck.Checked;
            _settings.ExtraGames = _extraBox.Text ?? "";
            _settings.IgnoreProcesses = _ignoreBox.Text ?? "";
            int seconds = 30;
            if (_intervalCombo.SelectedIndex == 0) seconds = 15;
            else if (_intervalCombo.SelectedIndex == 2) seconds = 60;
            _settings.IdleSampleSeconds = seconds;
            SettingsStore.Save(_settings);
            StartupRegistration.Apply(_settings.StartWithWindows);
            _catalog.Reload(_settings);
            UpdatePauseMenu();
            UpdateInterval();
        }

        void TogglePause()
        {
            _settings.PauseSampling = !_settings.PauseSampling;
            _loadingSettings = true;
            _pauseCheck.Checked = _settings.PauseSampling;
            _loadingSettings = false;
            SettingsStore.Save(_settings);
            UpdatePauseMenu();
            UpdateInterval();
            if (_last != null) ShowSnap(_last);
            AddActivity(_settings.PauseSampling ? "Performance sampling paused." : "Performance sampling resumed.");
        }

        void UpdatePauseMenu()
        {
            if (_pauseItem != null) _pauseItem.Text = _settings.PauseSampling ? "Resume sampling" : "Pause sampling";
        }

        void ShowPage(string key)
        {
            _pageDash.Visible = key == "dash";
            _pageCrash.Visible = key == "crashes";
            _pageFix.Visible = key == "fixes";
            _pageSet.Visible = key == "settings";
            _navDash.Selected = key == "dash";
            _navCrash.Selected = key == "crashes";
            _navFix.Selected = key == "fixes";
            _navSet.Selected = key == "settings";
            if (key == "dash" || key == "fixes") RefreshGraphics();
            if (key == "dash") LayoutDashboard();
            if (key == "fixes") LayoutFixes();
            if (key == "crashes")
            {
                LayoutCrashBar();
                FitCrashDetail();
            }
            if (key == "settings") LayoutSettings();
        }

        void RefreshGraphics()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _gpuBusy, 1, 0) != 0) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                SlowFacts gpu = new SlowFacts();
                try { SystemProbe.ReadGraphics(gpu); }
                catch (Exception ex) { Program.LogException(ex); }
                finally { System.Threading.Interlocked.Exchange(ref _gpuBusy, 0); }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (!gpu.GpuKnown) return;
                        lock (_slowLock)
                        {
                            _slow.GpuKnown = true;
                            _slow.GpuName = gpu.GpuName;
                            _slow.GpuDriver = gpu.GpuDriver;
                            _slow.GpuPnp = gpu.GpuPnp;
                            _slow.GpuDriverDate = gpu.GpuDriverDate;
                            _slow.ShowAmd = gpu.ShowAmd;
                            _slow.ShowNvidia = gpu.ShowNvidia;
                        }
                        if (_last != null)
                        {
                            lock (_slowLock) _last.ApplySlow(_slow);
                            HealthScore.Evaluate(_last, _events, _hot, _session.InGame, AddressedIds());
                            ShowSnap(_last);
                        }
                        UpdateDriverRow();
                    }));
                }
                catch { }
            });
        }

        void UpdateDriverRow()
        {
            string text = HealthScore.DriverSentence(_last);
            if (text == _driverRowText) return;
            _driverRowText = text;
            for (int i = 0; i < _fixRows.Count; i++)
            {
                FixSpec spec = _fixRows[i].RunButton.Tag as FixSpec;
                if (spec == null || spec.Id != "driver") continue;
                _fixRows[i].SetDetail(text);
            }
            LayoutFixes();
        }

        void LayoutSide()
        {
            int w = _side.ClientSize.Width;
            int textW = Math.Max(40, w - 36);
            int brandH = TextBlock.Height(_brand.Text, _brand.Font, textW, 24);
            int tagH = TextBlock.Height(_tag.Text, _tag.Font, textW, 18);
            _brand.SetBounds(18, 18, textW, brandH);
            _tag.SetBounds(18, 18 + brandH + 4, textW, tagH);
            int y = 18 + brandH + 4 + tagH + 16;
            SideButton[] navs = new SideButton[] { _navDash, _navCrash, _navFix, _navSet };
            for (int i = 0; i < navs.Length; i++)
            {
                int navH = TextBlock.Height(navs[i].Text, navs[i].Font, Math.Max(40, w - 56), 22) + 16;
                if (navH < 42) navH = 42;
                navs[i].SetBounds(10, y, w - 20, navH);
                y += navH + 6;
            }
            int hintH = TextBlock.Height(_sideHint.Text, _sideHint.Font, textW, 18);
            _sideHint.SetBounds(18, Math.Max(y + 8, _side.Height - hintH - 16), textW, hintH);
        }

        void LayoutCrashBar()
        {
            if (_crashBar == null) return;
            int w = _crashBar.ClientSize.Width;
            if (w < 200) return;
            int applyW = BarTextWidth(_applyBtn);
            int investW = BarTextWidth(_investBtn);
            int logW = BarTextWidth(_logBtn);
            int copyW = BarTextWidth(_copyBtn);
            int exportW = BarTextWidth(_exportBtn);
            int buttons = applyW + investW + logW + copyW + exportW + 8 * 4;
            bool wrap = w < buttons + 220;
            _crashBar.Height = wrap ? 96 : 52;
            int y = wrap ? 52 : 10;
            int x = w;
            PlaceBar(ref x, y, _applyBtn, applyW);
            PlaceBar(ref x, y, _investBtn, investW);
            PlaceBar(ref x, y, _logBtn, logW);
            PlaceBar(ref x, y, _copyBtn, copyW);
            PlaceBar(ref x, y, _exportBtn, exportW);
            int filterRight = wrap ? w : _exportBtn.Left - 12;
            int filterW = Math.Min(190, Math.Max(168, filterRight - 96));
            if (filterW > filterRight - 8 && filterRight > 180) filterW = filterRight - 96;
            if (filterW < 140) filterW = 168;
            _filter.SetBounds(0, 10, filterW, 28);
            int countW = Math.Max(48, filterRight - filterW - 12);
            int countH = TextBlock.Height(_countLabel.Text, _countLabel.Font, countW, 20);
            _countLabel.SetBounds(filterW + 8, 10, countW, countH);
        }

        static int BarTextWidth(Button button)
        {
            int width = TextRenderer.MeasureText(button.Text, button.Font).Width + 24;
            if (width < 72) width = 72;
            return width;
        }

        static void PlaceBar(ref int x, int y, Button button, int width)
        {
            x -= width;
            button.SetBounds(x, y, width, 32);
            x -= 8;
        }

        void LayoutDashboard()
        {
            if (_layingDash || _pageDash == null) return;
            _layingDash = true;
            try
            {
                int pad = 20;
                int viewW = _pageDash.ClientSize.Width;
                if (_host != null && _host.ClientSize.Width > 80 && viewW > _host.ClientSize.Width)
                    viewW = _host.ClientSize.Width;
                int w = viewW - pad * 2 - SystemInformation.VerticalScrollBarWidth;
                if (w < 200) return;
                int origin = _pageDash.AutoScrollPosition.Y;
                int y = origin + pad;
                _ring.SetBounds(pad, y, 148, 148);
                int tx = pad + 168;
                int tw = Math.Max(100, w - 168);
                int statusH = TextBlock.Height(_statusWord.Text, _statusWord.Font, tw, 36);
                int headH = TextBlock.Height(_headline.Text, _headline.Font, tw, 22);
                _statusWord.SetBounds(tx, y + 16, tw, statusH);
                _headline.SetBounds(tx, y + 16 + statusH + 8, tw, headH);
                int headerBlock = Math.Max(156, 16 + statusH + 8 + headH + 16);
                int rowY = 0;
                int rows = _issueHost.Controls.Count;
                for (int i = 0; i < rows; i++)
                {
                    IssueRow row = _issueHost.Controls[i] as IssueRow;
                    if (row == null) continue;
                    int rh = row.LayoutRow(w);
                    row.SetBounds(0, rowY, w, rh);
                    rowY += rh + 8;
                }
                int issueH = rows == 0 ? 0 : rowY;
                int issuesTop = origin + pad + headerBlock;
                _issueHost.SetBounds(pad, issuesTop, w, issueH);
                int cards = issuesTop + issueH + (rows == 0 ? 0 : 8);
                int gap = 12;
                int columns = 4;
                int cw = (w - gap * 3) / 4;
                if (cw < 220)
                {
                    columns = 2;
                    cw = (w - gap) / 2;
                }
                if (cw < 80) return;
                MetricCard[] meters = new MetricCard[] { _cpuCard, _ramCard, _diskCard, _powerCard };
                int cardH = 96;
                for (int i = 0; i < meters.Length; i++)
                {
                    int wantH = meters[i].PreferredHeight(cw);
                    if (wantH > cardH) cardH = wantH;
                }
                int cardRows = (meters.Length + columns - 1) / columns;
                for (int i = 0; i < meters.Length; i++)
                {
                    int col = i % columns;
                    int row = i / columns;
                    meters[i].SetBounds(pad + col * (cw + gap), cards + row * (cardH + gap), cw, cardH);
                }
                int content = (cards - origin) + cardRows * cardH + (cardRows - 1) * gap + pad;
                Size need = new Size(0, content);
                if (_pageDash.AutoScrollMinSize != need) _pageDash.AutoScrollMinSize = need;
            }
            finally
            {
                _layingDash = false;
            }
        }

        void FitCrashDetail()
        {
            if (_detail == null || _pageCrash == null) return;
            int h = _pageCrash.ClientSize.Height;
            if (h < 200) return;
            int detail = h / 2;
            if (detail < 220) detail = 220;
            if (detail > 460) detail = 460;
            if (_detail.Height != detail) _detail.Height = detail;
        }

        void LayoutFixes()
        {
            if (_layingFixes || _fixScroll == null) return;
            _layingFixes = true;
            try
            {
                Point hold = _fixScroll.AutoScrollPosition;
                for (int pass = 0; pass < 2; pass++)
                {
                    int clientW = _fixScroll.ClientSize.Width;
                    int clientH = _fixScroll.ClientSize.Height;
                    if (clientW < 80) return;
                    int bar = SystemInformation.VerticalScrollBarWidth;
                    int w = clientW;
                    int content = StackFixes(w, false);
                    if (content > clientH && clientW - bar >= 200)
                    {
                        w = clientW - bar;
                        content = StackFixes(w, false);
                    }
                    Size need = content > clientH ? new Size(0, content) : Size.Empty;
                    if (_fixScroll.AutoScrollMinSize != need) _fixScroll.AutoScrollMinSize = need;
                    if (need.IsEmpty) _fixScroll.AutoScrollPosition = new Point(0, 0);
                    else if (_fixScroll.AutoScrollPosition != hold)
                        _fixScroll.AutoScrollPosition = new Point(-hold.X, -hold.Y);
                    StackFixes(w, true);
                }
            }
            finally
            {
                _layingFixes = false;
            }
        }

        int StackFixes(int w, bool place)
        {
            int ox = place ? _fixScroll.AutoScrollPosition.X : 0;
            int oy = place ? _fixScroll.AutoScrollPosition.Y : 0;
            int y = 0;
            int last = _fixLayout.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                Control item = _fixLayout[i];
                FixRow row = item as FixRow;
                SectionHead head = item as SectionHead;
                if (row != null) row.LayoutRow(w);
                else if (head != null) head.LayoutWidth(w);
                else item.Width = w;
                if (place) item.SetBounds(ox, oy + y, w, item.Height);
                y += item.Height;
                if (i < last) y += row == null ? 2 : 8;
            }
            return y;
        }

        void AddActivity(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(delegate { AddActivity(text); })); }
                catch { }
                return;
            }
            _activity.Insert(0, new ActivityItem(text));
            if (_activity.Count > 150) _activity.RemoveAt(_activity.Count - 1);
            _activityList.Items.Insert(0, _activity[0]);
            if (_activityList.Items.Count > 150) _activityList.Items.RemoveAt(_activityList.Items.Count - 1);
        }

        void DrawActivity(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _activityList.Items.Count) return;
            ActivityItem item = _activityList.Items[e.Index] as ActivityItem;
            if (item == null) return;
            Color bg = (e.State & DrawItemState.Selected) == DrawItemState.Selected ? Theme.AccentSoft : Theme.Card;
            using (SolidBrush brush = new SolidBrush(bg)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, item.Time.ToString("HH:mm:ss"), Theme.FontSmall, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, 70, e.Bounds.Height), Theme.Muted, TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, item.Text, Theme.FontSmall, new Rectangle(e.Bounds.X + 82, e.Bounds.Y, e.Bounds.Width - 90, e.Bounds.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void NotifyCrash(CrashEvent ev)
        {
            if (!_settings.NotifyOnCrash || _tray == null) return;
            if ((DateTime.UtcNow - _lastBalloon).TotalSeconds < 20) return;
            _lastBalloon = DateTime.UtcNow;
            _tray.BalloonTipTitle = "Crash logged";
            string text = ev.Summary ?? "A crash was logged.";
            if (text.Length > 220) text = text.Substring(0, 220);
            _tray.BalloonTipText = text;
            _tray.ShowBalloonTip(4000);
        }

        void HideToTray()
        {
            if (_hiding || _exit) return;
            _hiding = true;
            ShowInTaskbar = false;
            Hide();
            _hiding = false;
            UpdateInterval();
            if (!_balloonOnce)
            {
                _balloonOnce = true;
                _tray.BalloonTipTitle = "WinHealthGuard is still watching";
                _tray.BalloonTipText = "It stays in the tray and uses very little CPU. Double-click the icon to open it.";
                _tray.ShowBalloonTip(3000);
            }
        }

        void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            UpdateInterval();
            LayoutDashboard();
        }

        void ExitApp()
        {
            _exit = true;
            try { if (_showEvent != null) _showEvent.Set(); } catch { }
            Close();
        }

        void OpenLogs()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.Arguments = "\"" + AppPaths.Ensure() + "\"";
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Log folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void UpdateIcon(string status)
        {
            Icon icon = _iconGood;
            if (status == "Attention") icon = _iconWarn;
            else if (status == "Critical") icon = _iconBad;
            if (_tray != null) _tray.Icon = icon;
            Icon = icon;
        }

        void SortEvents()
        {
            _events.Sort(delegate(CrashEvent a, CrashEvent b) { return b.Time.CompareTo(a.Time); });
            if (_events.Count > 1000) _events.RemoveRange(1000, _events.Count - 1000);
        }

        static string Badge(FixSpec spec)
        {
            if (spec.LongRunning) return "Takes a while";
            if (spec.Reboot && spec.Admin) return "Admin and a restart";
            if (spec.Admin) return "Asks for admin";
            if (spec.Reboot) return "Needs a restart";
            if (spec.Opener) return "Opens a window";
            return "Ready";
        }

        static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Theme.Card;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Theme.Line;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowTemplate.Height = 30;
            grid.ColumnHeadersHeight = 32;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Card2;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.FontSmall;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Card2;
            grid.DefaultCellStyle.BackColor = Theme.Card;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.DefaultCellStyle.Font = Theme.FontUi;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.Padding = new Padding(4, 6, 4, 6);
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.AlternatingRowsDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(32, 36, 46);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Theme.Text;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Theme.Text;
        }

        static Panel Page()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Bg;
            return panel;
        }

        static Label LabelOf(string text, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoSize = false;
            label.AutoEllipsis = false;
            label.UseMnemonic = false;
            return label;
        }

        static Button MakeButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.Height = 32;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? Theme.Accent : Theme.Card2;
            button.ForeColor = primary ? Theme.OnAccent : Theme.Text;
            button.Font = primary ? new Font("Segoe UI", 9f, FontStyle.Bold) : Theme.FontUi;
            button.Cursor = Cursors.Hand;
            return button;
        }

        static CheckBox Check(string text)
        {
            CheckBox box = new CheckBox();
            box.Text = text;
            box.ForeColor = Theme.Text;
            box.BackColor = Theme.Bg;
            box.Font = Theme.FontUi;
            box.AutoSize = false;
            return box;
        }

        static TextBox Box()
        {
            TextBox box = new TextBox();
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Theme.Card;
            box.ForeColor = Theme.Text;
            box.Font = Theme.FontUi;
            return box;
        }

        void LayoutSettings()
        {
            if (_pageSet == null || _setRows.Count == 0) return;
            int inner = _pageSet.ClientSize.Width - _pageSet.Padding.Horizontal;
            if (inner < 240) inner = 240;
            int y = 8;
            int i = 0;
            while (i < _setRows.Count)
            {
                Control control = _setRows[i];
                CheckBox check = control as CheckBox;
                Button button = control as Button;
                if (check != null)
                {
                    check.AutoSize = true;
                    check.MaximumSize = new Size(inner, 0);
                    Size pref = check.GetPreferredSize(new Size(inner, 0));
                    int h = Math.Max(28, pref.Height);
                    check.SetBounds(0, y, inner, h);
                    y += h + 12;
                    i++;
                    continue;
                }
                if (button != null)
                {
                    int x = 0;
                    while (i < _setRows.Count)
                    {
                        Button next = _setRows[i] as Button;
                        if (next == null) break;
                        int bw = TextRenderer.MeasureText(next.Text, next.Font).Width + 28;
                        if (bw < 120) bw = 120;
                        if (x > 0 && x + bw > inner)
                        {
                            x = 0;
                            y += 42;
                        }
                        next.SetBounds(x, y, bw, 34);
                        x += bw + 10;
                        i++;
                    }
                    y += 48;
                    continue;
                }
                TextBox box = control as TextBox;
                if (box != null && box.Multiline)
                {
                    box.SetBounds(0, y, Math.Min(680, inner), 88);
                    y += 100;
                    i++;
                    continue;
                }
                ComboBox combo = control as ComboBox;
                if (combo != null)
                {
                    combo.SetBounds(0, y, Math.Min(220, inner), 28);
                    y += 40;
                    i++;
                    continue;
                }
                int textH = TextBlock.Height(control.Text, control.Font, inner, 18);
                control.SetBounds(0, y, inner, textH);
                y += textH + 8;
                i++;
            }
            int need = y + 16;
            if (_pageSet.AutoScrollMinSize.Height != need) _pageSet.AutoScrollMinSize = new Size(0, need);
        }

        void Place(Control control, int x, int y, int w, int h)
        {
            control.SetBounds(x, y, w, h);
            _pageSet.Controls.Add(control);
        }
    }

    static class StartupRegistration
    {
        public static void Apply(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (enable)
                        key.SetValue("WinHealthGuard", "\"" + Application.ExecutablePath + "\" --tray");
                    else
                    {
                        try { key.DeleteValue("WinHealthGuard", false); }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
            }
        }
    }
}
