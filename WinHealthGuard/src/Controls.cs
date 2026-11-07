using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinHealthGuard
{
    sealed class SideButton : Button
    {
        bool _hot;
        bool _selected;

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public SideButton(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Height = 42;
            Cursor = Cursors.Hand;
            Font = Theme.FontNav;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hot = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hot = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = _selected ? Theme.AccentSoft : (_hot ? Theme.Card : Theme.Sidebar);
            using (SolidBrush brush = new SolidBrush(bg)) g.FillRectangle(brush, ClientRectangle);
            if (_selected)
            {
                using (SolidBrush brush = new SolidBrush(Theme.Accent)) g.FillRectangle(brush, 0, 8, 3, Height - 16);
            }
            Color fg = _selected ? Theme.Text : Theme.Muted;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(18, 4, Math.Max(20, Width - 28), Height - 8), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    sealed class MetricCard : Control
    {
        public string Caption = "";
        public string ValueText = "...";
        public string Hint = "";
        public Color Accent = Theme.Accent;

        public MetricCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 100;
            BackColor = Theme.Card;
        }

        public void Set(string value, string hint, Color accent)
        {
            ValueText = value ?? "";
            Hint = hint ?? "";
            Accent = accent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(Theme.Card)) g.FillRectangle(brush, ClientRectangle);
            using (Pen pen = new Pen(Theme.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            using (SolidBrush brush = new SolidBrush(Accent)) g.FillRectangle(brush, 0, 0, 3, Height);
            int inner = Math.Max(40, Width - 32);
            int cap = TextBlock.Height(Caption, Theme.FontSmall, inner, 16);
            int val = TextBlock.Height(ValueText, Theme.FontValue, inner, 26);
            int hint = TextBlock.Height(Hint, Theme.FontSmall, inner, 16);
            int y = 12;
            TextRenderer.DrawText(g, Caption, Theme.FontSmall, new Rectangle(16, y, inner, cap), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            y += cap + 4;
            TextRenderer.DrawText(g, ValueText, Theme.FontValue, new Rectangle(14, y, inner, val), Theme.Text, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            y += val + 4;
            TextRenderer.DrawText(g, Hint, Theme.FontSmall, new Rectangle(16, y, inner, hint), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        }

        public int PreferredHeight(int width)
        {
            int inner = Math.Max(40, width - 32);
            int cap = TextBlock.Height(Caption, Theme.FontSmall, inner, 16);
            int val = TextBlock.Height(ValueText, Theme.FontValue, inner, 26);
            int hint = TextBlock.Height(Hint, Theme.FontSmall, inner, 16);
            return 14 + cap + 6 + val + 6 + hint + 16;
        }
    }

    sealed class ScoreRing : Control
    {
        public int Score = 0;
        public Color Band = Theme.Good;
        public bool Ready;

        public ScoreRing()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        public void SetScore(int score, Color band, bool ready)
        {
            Score = score;
            Band = band;
            Ready = ready;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 20 || Height < 20) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            int size = Math.Min(Width, Height) - 16;
            Rectangle arc = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
            using (Pen pen = new Pen(Theme.Card2, 12)) g.DrawArc(pen, arc, 135, 270);
            if (Ready && Score > 0)
            {
                using (Pen pen = new Pen(Band, 12))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, arc, 135, 270f * Score / 100f);
                }
            }
            string text = Ready ? Score.ToString() : "...";
            TextRenderer.DrawText(g, text, Theme.FontScore, arc, Ready ? Band : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    sealed class Sparkline : Control
    {
        readonly System.Collections.Generic.List<float> _cpu = new System.Collections.Generic.List<float>();
        readonly System.Collections.Generic.List<float> _ram = new System.Collections.Generic.List<float>();

        public Sparkline()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = 180;
        }

        public void Add(double cpu, double ram)
        {
            _cpu.Add((float)cpu);
            _ram.Add((float)ram);
            if (_cpu.Count > 180)
            {
                _cpu.RemoveAt(0);
                _ram.RemoveAt(0);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            using (Pen pen = new Pen(Theme.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, "CPU", Theme.FontSmall, new Point(14, 8), Theme.Accent);
            TextRenderer.DrawText(g, "Memory", Theme.FontSmall, new Point(52, 8), Theme.Good);
            Rectangle plot = new Rectangle(12, 28, Width - 24, Height - 40);
            if (plot.Width < 10 || plot.Height < 10) return;
            using (Pen grid = new Pen(Theme.Line))
            {
                for (int i = 1; i <= 3; i++)
                {
                    int y = plot.Top + (plot.Height * i / 4);
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                }
            }
            if (_cpu.Count < 2)
            {
                TextRenderer.DrawText(g, "Collecting samples...", Theme.FontSmall, plot, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            DrawSeries(g, plot, _cpu, Theme.Accent);
            DrawSeries(g, plot, _ram, Theme.Good);
        }

        static void DrawSeries(Graphics g, Rectangle plot, System.Collections.Generic.List<float> values, Color color)
        {
            PointF[] points = new PointF[values.Count];
            float step = (float)(plot.Width - 1) / (values.Count - 1);
            for (int i = 0; i < values.Count; i++)
            {
                float v = values[i];
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                float y = plot.Bottom - (plot.Height * v / 100f);
                points[i] = new PointF(plot.Left + (step * i), y);
            }
            using (Pen pen = new Pen(color, 1.8f)) g.DrawLines(pen, points);
        }
    }

    sealed class KeepScrollPanel : Panel
    {
        protected override Point ScrollToControl(Control activeControl)
        {
            return DisplayRectangle.Location;
        }
    }

    sealed class FixRow : Panel
    {
        public readonly Button RunButton;
        readonly Label _title;
        readonly Label _detail;
        readonly Label _badge;

        public FixRow(string title, string detail, string badge, string buttonText)
        {
            Height = 92;
            BackColor = Theme.Card;
            _title = new Label();
            _title.Text = title;
            _title.Font = Theme.FontSection;
            _title.ForeColor = Theme.Text;
            _title.BackColor = Theme.Card;
            _title.AutoSize = false;
            _title.AutoEllipsis = false;
            _title.UseMnemonic = false;
            _detail = new Label();
            _detail.Text = detail;
            _detail.Font = Theme.FontSmall;
            _detail.ForeColor = Theme.Muted;
            _detail.BackColor = Theme.Card;
            _detail.AutoSize = false;
            _detail.AutoEllipsis = false;
            _detail.UseMnemonic = false;
            _badge = new Label();
            _badge.Text = badge;
            _badge.Font = Theme.FontSmall;
            _badge.ForeColor = Theme.Accent;
            _badge.BackColor = Theme.Card;
            _badge.AutoSize = false;
            _badge.AutoEllipsis = false;
            _badge.UseMnemonic = false;
            _badge.TextAlign = ContentAlignment.MiddleLeft;
            RunButton = new Button();
            RunButton.Text = string.IsNullOrEmpty(buttonText) ? "Fix" : buttonText;
            RunButton.Height = 32;
            RunButton.Width = 96;
            RunButton.FlatStyle = FlatStyle.Flat;
            RunButton.FlatAppearance.BorderSize = 0;
            RunButton.BackColor = Theme.Accent;
            RunButton.ForeColor = Theme.OnAccent;
            RunButton.Cursor = Cursors.Hand;
            RunButton.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            Controls.Add(_title);
            Controls.Add(_detail);
            Controls.Add(_badge);
            Controls.Add(RunButton);
        }

        public void LayoutRow(int width)
        {
            Width = width;
            int buttonW = Math.Max(104, TextRenderer.MeasureText(RunButton.Text, RunButton.Font).Width + 24);
            int textW = Math.Max(140, width - 16 - buttonW - 28);
            int titleH = TextBlock.Height(_title.Text, _title.Font, textW, 22);
            int badgeH = string.IsNullOrEmpty(_badge.Text) ? 0 : TextBlock.Height(_badge.Text, _badge.Font, textW, 16);
            int detailH = TextBlock.Height(_detail.Text, _detail.Font, textW, 18);
            int y = 12;
            _title.SetBounds(16, y, textW, titleH);
            y += titleH + 4;
            if (badgeH > 0)
            {
                _badge.SetBounds(16, y, textW, badgeH);
                y += badgeH + 4;
            }
            else _badge.SetBounds(16, y, textW, 0);
            _detail.SetBounds(16, y, textW, detailH);
            y += detailH + 12;
            int h = y;
            if (h < 76) h = 76;
            Height = h;
            RunButton.SetBounds(width - 16 - buttonW, 14, buttonW, 32);
        }

        public void SetDetail(string text)
        {
            _detail.Text = text ?? "";
        }
    }

    sealed class IssueRow : Panel
    {
        public readonly Button Action;
        readonly Label _title;
        readonly Label _detail;

        public IssueRow(HealthIssue issue)
        {
            Height = 64;
            BackColor = Theme.Card;
            _title = new Label();
            _title.Text = issue.Title;
            _title.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _title.ForeColor = Theme.Text;
            _title.BackColor = Theme.Card;
            _title.AutoSize = false;
            _title.AutoEllipsis = false;
            _title.UseMnemonic = false;
            _detail = new Label();
            _detail.Text = issue.Detail;
            _detail.Font = Theme.FontSmall;
            _detail.ForeColor = Theme.Muted;
            _detail.BackColor = Theme.Card;
            _detail.AutoSize = false;
            _detail.AutoEllipsis = false;
            _detail.UseMnemonic = false;
            Action = new Button();
            Action.Text = string.IsNullOrEmpty(issue.Button) ? "View" : issue.Button;
            Action.Visible = !string.IsNullOrEmpty(issue.RepairId);
            Action.Height = 32;
            Action.Width = 104;
            Action.FlatStyle = FlatStyle.Flat;
            Action.FlatAppearance.BorderSize = 0;
            Action.BackColor = Theme.Accent;
            Action.ForeColor = Theme.OnAccent;
            Action.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            Action.Cursor = Cursors.Hand;
            Controls.Add(_title);
            Controls.Add(_detail);
            Controls.Add(Action);
        }

        public int LayoutRow(int width)
        {
            Width = width;
            int buttonW = Action.Visible ? Math.Max(104, TextRenderer.MeasureText(Action.Text, Action.Font).Width + 24) : 0;
            int textW = Math.Max(120, width - 16 - (Action.Visible ? buttonW + 28 : 16));
            int titleH = TextBlock.Height(_title.Text, _title.Font, textW, 22);
            int detailH = TextBlock.Height(_detail.Text, _detail.Font, textW, 18);
            int h = 12 + titleH + 6 + detailH + 12;
            if (h < 72) h = 72;
            Height = h;
            _title.SetBounds(16, 12, textW, titleH);
            _detail.SetBounds(16, 12 + titleH + 6, textW, detailH);
            if (Action.Visible) Action.SetBounds(width - 16 - buttonW, 14, buttonW, 32);
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (SolidBrush brush = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(brush, 0, 10, 3, Height - 20);
        }
    }

    sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.AccentSoft; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.AccentSoft; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.AccentSoft; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.AccentSoft; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.AccentSoft; } }
        public override Color MenuItemBorder { get { return Theme.AccentSoft; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.Line; } }
    }

    sealed class SectionHead : Panel
    {
        readonly Label _label;

        public SectionHead(string text)
        {
            Height = 40;
            BackColor = Theme.Bg;
            _label = new Label();
            _label.Text = text;
            _label.Font = Theme.FontSection;
            _label.ForeColor = Theme.Text;
            _label.BackColor = Theme.Bg;
            _label.AutoSize = false;
            _label.AutoEllipsis = false;
            _label.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_label);
        }

        public void LayoutWidth(int width)
        {
            Width = width;
            int textW = Math.Max(40, width - 8);
            int h = TextBlock.Height(_label.Text, _label.Font, textW, 28);
            _label.SetBounds(2, 8, textW, h);
            Height = h + 14;
        }
    }

    static class TextBlock
    {
        public static int Height(string text, Font font, int width, int min)
        {
            if (font == null) return min;
            if (string.IsNullOrEmpty(text) || width < 20) return min;
            Size size = TextRenderer.MeasureText(text, font, new Size(Math.Max(20, width - 4), 4000), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            int h = size.Height + 8;
            if (h < min) h = min;
            return h;
        }
    }
}
