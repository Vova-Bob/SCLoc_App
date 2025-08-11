using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SCLOCUA
{
    public partial class killFeed : Form
    {
        // ===== UI constants (no overlay scaling) =====
        private const int FEED_WIDTH = 420;   // fixed overlay width
        private const int FEED_HEIGHT = 160;   // fixed overlay height
        private const int MARGIN_L = 8, MARGIN_B = 8, INTERLINE = 2;

        // Text/bubble
        private static readonly Font FeedFont = new Font("Consolas", 12f, FontStyle.Bold);
        private const int PAD_X = 10, PAD_Y = 5, RADIUS = 6;
        private const float OUTLINE = 2.2f;
        private static readonly Color OUTLINE_COLOR = Color.Black;
        private const int GAP = 8;

        // Fade logic
        private const int MAX_LINES = 5;                  // show up to 5 lines
        private const int SHOW_MS = 7500;               // stay fully visible
        private const int FADE_MS = 1800;                // fade duration
        private const int TICK_MS = 33;                 // ~30 FPS
        private const int BASE_ALPHA = 170;               // bubble alpha at full
        private const int MIN_ALPHA = 40;                // don't go too dark while visible

        // Hotkeys (only toggle + bubble opacity)
        private const int HOTKEY_TOGGLE = 1;
        private const int HOTKEY_BUBBLE_DEC = 2;
        private const int HOTKEY_BUBBLE_INC = 3;
        private const int MOD_ALT = 0x0001;
        private const int MOD_CTRL = 0x0002;
        private const int VK_F9 = 0x78, VK_LEFT = 0x25, VK_RIGHT = 0x27;
        private const int WM_HOTKEY = 0x0312, WM_DESTROY = 0x0002;

        // WinAPI
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;
        protected override bool ShowWithoutActivation { get { return true; } }

        // State
        private readonly List<KillLine> _lines = new List<KillLine>();
        private readonly System.Windows.Forms.Timer _animTimer = new System.Windows.Forms.Timer();
        private string _logPath;
        private bool _visibleState = true;
        private bool _dragging;
        private Point _dragStart;
        private SoundPlayer _player;
        private string _wav;

        public killFeed(string folderPath)
        {
            InitializeComponent();

            // Transparent form (only bubbles are drawn by children)
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Color key = Color.Lime;
            this.BackColor = key;
            this.TransparencyKey = key;

            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(FEED_WIDTH, FEED_HEIGHT);
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(120, 120);
            this.TopMost = true; SetTopMost();
            this.ShowInTaskbar = false;

            // Drag overlay anywhere
            this.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
            this.MouseMove += (s, e) => { if (_dragging) { var scr = Cursor.Position; this.Location = new Point(scr.X - _dragStart.X, scr.Y - _dragStart.Y); } };
            this.MouseUp += (s, e) => { _dragging = false; };

            this.FormClosing += OnClosing;

            // Log + sound
            _logPath = Path.Combine(folderPath, "game.log");
            _wav = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "killSound.wav");
            if (File.Exists(_wav)) { _player = new SoundPlayer(_wav); _player.LoadAsync(); }

            // Anim timer for sequential fading
            _animTimer.Interval = TICK_MS;
            _animTimer.Tick += (s, e) => AdvanceFade();
            _animTimer.Start();

            // Hotkeys
            if (RegisterHotKey(this.Handle, HOTKEY_TOGGLE, MOD_CTRL | MOD_ALT, VK_F9) == 0 ||
                RegisterHotKey(this.Handle, HOTKEY_BUBBLE_DEC, MOD_ALT, VK_LEFT) == 0 ||
                RegisterHotKey(this.Handle, HOTKEY_BUBBLE_INC, MOD_ALT, VK_RIGHT) == 0)
                MessageBox.Show("Гарячі клавіші не зареєстровані", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);

            // Start tailing
            _ = TailAsync();
        }

        // inside class killFeed
        public void ToggleVisibility()
        {
            // Backward-compatible wrapper
            Toggle();
        }

        private void SetTopMost() => SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_TOGGLE) Toggle();
                else if (id == HOTKEY_BUBBLE_DEC) { KillLine.AdjustBaseAlpha(-20); Invalidate(); }
                else if (id == HOTKEY_BUBBLE_INC) { KillLine.AdjustBaseAlpha(+20); Invalidate(); }
            }
            else if (m.Msg == WM_DESTROY)
            {
                UnregisterHotKey(this.Handle, HOTKEY_TOGGLE);
                UnregisterHotKey(this.Handle, HOTKEY_BUBBLE_DEC);
                UnregisterHotKey(this.Handle, HOTKEY_BUBBLE_INC);
            }
            base.WndProc(ref m);
        }

        private void Toggle()
        {
            _visibleState = !_visibleState;
            if (_visibleState) { this.Show(); this.BringToFront(); SetTopMost(); }
            else this.Hide();
        }

        private async Task TailAsync()
        {
            try
            {
                using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    fs.Seek(0, SeekOrigin.End);
                    while (!IsDisposed)
                    {
                        var line = await sr.ReadLineAsync();
                        if (line == null) { await Task.Delay(100); continue; }
                        var k = Parse(line);
                        if (k != null) AddKill(k);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("Помилка читання логу: " + ex.Message); }
        }

        private void AddKill(KillData k)
        {
            if (InvokeRequired) { this.Invoke((Action)(() => AddKill(k))); return; }

            // add new line control
            var line = new KillLine(k, FeedFont);
            line.Left = MARGIN_L;
            line.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
            line.MouseMove += (s, e) => { if (_dragging) { var scr = Cursor.Position; this.Location = new Point(scr.X - _dragStart.X, scr.Y - _dragStart.Y); } };
            line.MouseUp += (s, e) => { _dragging = false; };
            Controls.Add(line);
            _lines.Add(line);

            // keep only MAX_LINES (older start fading immediately)
            while (_lines.Count > MAX_LINES) _lines[0].ForceFadeNow();

            Relayout();
            PlaySound();
        }

        private void Relayout()
        {
            // place from bottom
            int hLine = KillLine.MeasureHeight(FeedFont) + INTERLINE;
            int y = this.ClientSize.Height - MARGIN_B - hLine;
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var l = _lines[i];
                l.Top = y;
                l.ApplyMaxWidth(this.ClientSize.Width - MARGIN_L * 2);
                l.Left = MARGIN_L;
                y -= hLine;
            }
            Invalidate();
        }

        private void AdvanceFade()
        {
            // fade oldest first, next starts only after previous is fully gone
            if (_lines.Count == 0) return;
            // remove fully faded
            for (int i = _lines.Count - 1; i >= 0; i--)
                if (_lines[i].IsGone) { Controls.Remove(_lines[i]); _lines.RemoveAt(i); }

            if (_lines.Count == 0) return;

            // ensure only first "overdue" line is fading
            bool someoneFading = false;
            for (int i = 0; i < _lines.Count; i++)
            {
                var l = _lines[i];
                if (l.ShouldFade) { if (!someoneFading) { l.StartFade(); someoneFading = true; } }
                l.TickFade(); // progress if already fading
            }
            Relayout();
        }

        private void PlaySound()
        {
            try
            {
                if (!_visibleState) return;
                if (_player != null) { _player.Stop(); _player.Play(); }
                else SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        private void OnClosing(object s, FormClosingEventArgs e)
        {
            _animTimer.Stop();
            UnregisterHotKey(this.Handle, HOTKEY_TOGGLE);
            UnregisterHotKey(this.Handle, HOTKEY_BUBBLE_DEC);
            UnregisterHotKey(this.Handle, HOTKEY_BUBBLE_INC);
            if (_player != null) _player.Dispose();
        }

        // ===== parsing (no "(Bullet)" / damage) =====
        private sealed class KillData
        {
            public string T; public string Killer; public string Victim; public bool Suicide;
        }
        private KillData Parse(string line)
        {
            var tm = Regex.Match(line, @"<(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z)>");
            DateTime ts;
            if (!tm.Success || !DateTime.TryParse(tm.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out ts))
                return null;

            var m = Regex.Match(line, @"'(.*?)'.*?killed by '(.*?)'", RegexOptions.IgnoreCase);
            if (!m.Success) return null;

            string victim = m.Groups[1].Value;
            string killer = m.Groups[2].Value;
            if (killer.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return null;
            if (!IsPlayer(victim) || !IsPlayer(killer)) return null;

            return new KillData { T = ts.ToLocalTime().ToString("HH:mm"), Killer = killer, Victim = victim, Suicide = (victim == killer) };
        }
        private bool IsPlayer(string name) { return !string.IsNullOrEmpty(name) && name.IndexOf("NPC", StringComparison.OrdinalIgnoreCase) < 0; }

        // ===== One line control with its own fade =====
        private sealed class KillLine : Control
        {
            private readonly KillData _d;
            private readonly Font _font;
            private DateTime _born = DateTime.UtcNow;
            private DateTime? _fadeStartUtc = null;    // null -> not fading yet
            private int _maxWidth = 300;

            private static int _baseAlpha = BASE_ALPHA; // shared, adjustable by hotkeys

            // segment colors
            private readonly Color _time = Color.FromArgb(235, 235, 235);
            private readonly Color _killer = Color.FromArgb(95, 245, 135);
            private readonly Color _verb = Color.White;
            private readonly Color _victim = Color.FromArgb(255, 125, 125);
            private readonly Color _suicide = Color.FromArgb(255, 200, 90);

            public static void AdjustBaseAlpha(int delta)
            {
                _baseAlpha = Math.Max(40, Math.Min(240, _baseAlpha + delta));
            }

            public bool IsGone
            {
                get
                {
                    if (!_fadeStartUtc.HasValue) return false;
                    return (DateTime.UtcNow - _fadeStartUtc.Value).TotalMilliseconds >= FADE_MS;
                }
            }

            public bool ShouldFade
            {
                get { return !_fadeStartUtc.HasValue && (DateTime.UtcNow - _born).TotalMilliseconds >= SHOW_MS; }
            }

            public KillLine(KillData d, Font f)
            {
                _d = d; _font = f;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
                BackColor = Color.Transparent;
                Height = MeasureHeight(_font);
                Width = 200;
            }

            public static int MeasureHeight(Font f)
            {
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                {
                    var h = TextRenderer.MeasureText(g, "Ag", f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;
                    return (int)(h * 1.35f) + PAD_Y * 2;
                }
            }

            public void ApplyMaxWidth(int w) { _maxWidth = w; Invalidate(); }
            public void ForceFadeNow() { _fadeStartUtc = DateTime.UtcNow; }
            public void StartFade() { if (!_fadeStartUtc.HasValue) _fadeStartUtc = DateTime.UtcNow; }
            public void TickFade() { if (_fadeStartUtc.HasValue) Invalidate(); }

            protected override void OnPaintBackground(PaintEventArgs e) { /* keep transparent */ }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // compute current alpha (during fade)
                int alpha = _baseAlpha;
                if (_fadeStartUtc.HasValue)
                {
                    double t = (DateTime.UtcNow - _fadeStartUtc.Value).TotalMilliseconds / FADE_MS;
                    if (t < 0) t = 0; if (t > 1) t = 1;
                    alpha = (int)(_baseAlpha * (1.0 - t));
                }
                if (!_fadeStartUtc.HasValue) alpha = Math.Max(alpha, MIN_ALPHA);

                // build segments
                var list = new Tuple<string, Color>[]
                {
                    Tuple.Create("["+_d.T+"]", _time),
                    _d.Suicide ? Tuple.Create(_d.Victim, _victim) : Tuple.Create(_d.Killer, _killer),
                    _d.Suicide ? Tuple.Create("помер (самогубство)", _suicide) : Tuple.Create("вбив", _verb),
                    _d.Suicide ? null : Tuple.Create(_d.Victim, _victim)
                };

                // measure total width (using glyph path bounds)
                int total = 0;
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] == null) continue;
                    total += (int)Math.Ceiling(GetTextWidth(e.Graphics, _font, list[i].Item1)) + (i < list.Length - 1 ? GAP : 0);
                }
                int bubbleW = Math.Min(_maxWidth, total + PAD_X * 2);
                int bubbleH = Height - 2;
                this.Width = bubbleW; // autosize to bubble

                // bubble
                using (var path = Rounded(new Rectangle(0, 0, bubbleW, bubbleH), RADIUS))
                using (var bg = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                    e.Graphics.FillPath(bg, path);

                // draw segments
                int x = PAD_X, y = PAD_Y;
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] == null) continue;
                    using (var p = BuildTextPath(e.Graphics, _font, list[i].Item1, new Point(x, y)))
                    {
                        using (var pen = new Pen(OUTLINE_COLOR, OUTLINE) { LineJoin = LineJoin.Round }) e.Graphics.DrawPath(pen, p);
                        using (var br = new SolidBrush(list[i].Item2)) e.Graphics.FillPath(br, p);
                        var b = p.GetBounds(); x += (int)Math.Ceiling(b.Width) + (i < list.Length - 1 ? GAP : 0);
                    }
                }
            }

            private static GraphicsPath BuildTextPath(Graphics g, Font f, string s, Point origin)
            {
                var gp = new GraphicsPath();
                gp.AddString(s, f.FontFamily, (int)f.Style, g.DpiY * f.Size / 72f, origin, StringFormat.GenericTypographic);
                return gp;
            }
            private static float GetTextWidth(Graphics g, Font f, string s)
            {
                using (var p = BuildTextPath(g, f, s, new Point(0, 0))) return p.GetBounds().Width;
            }
            private static GraphicsPath Rounded(Rectangle r, int rad)
            {
                int d = rad * 2; var gp = new GraphicsPath();
                gp.AddArc(r.X, r.Y, d, d, 180, 90);
                gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                gp.CloseFigure(); return gp;
            }
        }
    }
}
