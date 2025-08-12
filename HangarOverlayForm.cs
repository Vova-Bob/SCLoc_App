#if FEATURE_OVERLAY
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExecutiveHangarOverlay
{
    public class HangarOverlayForm : Form
    {
        // ---- Base canvas size (do not change) ----
        private const int BASE_W = 820;
        private const int BASE_H = 280;

        // ---- Phases (seconds) ----
        private const int RED_PHASE = 2 * 60 * 60;    // 7200
        private const int GREEN_PHASE = 1 * 60 * 60;  // 3600
        private const int BLACK_PHASE = 5 * 60;       // 300
        private const int TOTAL_CYCLE = RED_PHASE + GREEN_PHASE + BLACK_PHASE;

        // ---- State ----
        private readonly System.Windows.Forms.Timer _uiTimer;
        private long _cycleStartMs;
        private readonly string[] _lights = new string[5];
        private string _status = "default";
        private string _statusMessage = "Статус ангару невідомий";
        private string _statusLine = "";
        private int _minTimerIndex = -1;
        private readonly string[] _ledLabels = new string[5];

        // Click-through
        private bool _clickThrough = false;
        private bool _clickThroughTemp = false;

        // Drag
        private Point? _dragStart = null;

        // Scale & Opacity
        private float _scale = 1.0f;            // 0.60 .. 1.00
        private const float SCALE_MIN = 0.60f;
        private const float SCALE_MAX = 1.00f;
        private const float SCALE_STEP = 0.05f;

        private double _targetOpacity = 0.92;   // 0.50 .. 0.95
        private const double OP_MIN = 0.50;
        private const double OP_MAX = 0.95;
        private const double OP_STEP = 0.05;

        // ---- Win32 ----
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern nint GetWindowLongPtr32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        private static nint GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern nint SetWindowLongPtr32(IntPtr hWnd, int nIndex, nint dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern nint SetWindowLongPtr64(IntPtr hWnd, int nIndex, nint dwNewLong);
        private static nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);

        public HangarOverlayForm(long cycleStartMs)
        {
            SuspendLayout();
            _cycleStartMs = cycleStartMs;

            // Form setup
            Text = "Executive Hangar Overlay";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            this.DoubleBuffered = true;
            // Reduce flicker on high DPI
            this.SetStyle(System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer |
                          System.Windows.Forms.ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();
            BackColor = Color.FromArgb(18, 18, 18);
            Opacity = _targetOpacity;
            ClientSize = new Size(BASE_W, BASE_H);

            // Dragging
            MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) _dragStart = e.Location; };
            MouseMove += (_, e) =>
            {
                if (_dragStart.HasValue && e.Button == MouseButtons.Left)
                {
                    var d = new Point(e.X - _dragStart.Value.X, e.Y - _dragStart.Value.Y);
                    Location = new Point(Location.X + d.X, Location.Y + d.Y);
                }
            };
            MouseUp += (_, __) =>
            {
                _dragStart = null;
                EndTemporaryDragMode(); // restore click-through if needed
            };

            // UI timer
            _uiTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _uiTimer.Tick += (_, __) =>
            {
                UpdateModel();
                // apply opacity gradually to avoid flicker (optional)
                if (Math.Abs(Opacity - _targetOpacity) > 0.001)
                {
                    Opacity = _targetOpacity;
                }
                Invalidate();
            };
            _uiTimer.Start();

            UpdateModel();

            ResumeLayout();
        }

        // ===== Public API: called from Program.cs via global hotkeys =====

        // Click-through
        public void ToggleClickThrough()
        {
            _clickThrough = !_clickThrough;
            _clickThroughTemp = false;
            ApplyClickThrough(_clickThrough);
        }
        public void BeginTemporaryDragMode()
        {
            if (_clickThrough && !_clickThroughTemp)
            {
                _clickThroughTemp = true;
                ApplyClickThrough(false);
            }
        }
        public void EndTemporaryDragMode()
        {
            if (_clickThroughTemp)
            {
                _clickThroughTemp = false;
                ApplyClickThrough(true);
            }
        }

        // Timing
        public void SetStartNow()
        {
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            StartTimeProvider.SetLocalOverride(ms);
            _cycleStartMs = ms;
            UpdateModel();
            MessageBox.Show("Старт встановлено на ЗАРАЗ (локальний оверрайд).", "Оверлей ангару");
        }
        public void PromptManualStart()
        {
            using (var dlg = new InputMsDialog())
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    StartTimeProvider.SetLocalOverride(dlg.ValueMs);
                    _cycleStartMs = dlg.ValueMs;
                    UpdateModel();
                }
        }
        public async Task ForceSyncAsync()
        {
            long ms = await StartTimeProvider.ResolveAsync(forceRemote: true);
            _cycleStartMs = ms;
            UpdateModel();
        }
        public async Task ClearOverrideAndSyncAsync()
        {
            StartTimeProvider.ClearLocalOverride();
            long ms = await StartTimeProvider.ResolveAsync(forceRemote: true);
            _cycleStartMs = ms;
            UpdateModel();
            MessageBox.Show("Локальний оверрайд очищено.\nСинхронізовано з сервера.", "Оверлей ангару");
        }

        // Scale
        public void ScaleDown()
        {
            SetScale(Math.Max(SCALE_MIN, (float)Math.Round(_scale - SCALE_STEP, 2)));
        }
        public void ScaleUp()
        {
            SetScale(Math.Min(SCALE_MAX, (float)Math.Round(_scale + SCALE_STEP, 2)));
        }
        public void ScaleReset()
        {
            SetScale(1.0f);
        }
        private void SetScale(float s)
        {
            if (Math.Abs(s - _scale) < 0.001f) return;
            _scale = s;
            // Resize window so that scaled canvas fits exactly
            ClientSize = new Size((int)(BASE_W * _scale), (int)(BASE_H * _scale));
            Invalidate();
        }

        // Opacity
        public void OpacityDown()
        {
            _targetOpacity = Clamp(_targetOpacity - OP_STEP, OP_MIN, OP_MAX);
        }
        public void OpacityUp()
        {
            _targetOpacity = Clamp(_targetOpacity + OP_STEP, OP_MIN, OP_MAX);
        }
        public void OpacityReset()
        {
            _targetOpacity = 0.92;
        }
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        // ===== Internals =====

        private void ApplyClickThrough(bool enabled)
        {
            nint ex = GetWindowLongPtr(Handle, GWL_EXSTYLE);
            if (enabled) ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
            else ex = (ex | WS_EX_LAYERED) & ~WS_EX_TRANSPARENT;
            SetWindowLongPtr(Handle, GWL_EXSTYLE, ex);
        }

        private void UpdateModel()
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int elapsed = (int)Math.Floor((nowMs - _cycleStartMs) / 1000.0);
            if (elapsed < 0) elapsed = 0;
            int cyclePos = Mod(elapsed, TOTAL_CYCLE);

            for (int i = 0; i < 5; i++) { _lights[i] = "black"; _ledLabels[i] = null; }
            _minTimerIndex = -1;

            if (cyclePos < RED_PHASE)
            {
                int timeSinceRed = cyclePos;
                int interval = RED_PHASE / 5;

                for (int i = 0; i < 5; i++)
                    _lights[i] = (timeSinceRed >= (i + 1) * interval) ? "green" : "red";

                _status = "close";
                _statusMessage = "Ангар зачинено";
                _statusLine = "Відкриття через " + FormatHHMMSS(RED_PHASE - timeSinceRed);

                int cycleElapsed = cyclePos;
                int bestIdx = -1; int bestVal = int.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    if (_lights[i] != "red") continue;
                    int target = (i + 1) * interval;
                    int left = target - cycleElapsed;
                    if (left > 0 && left < bestVal) { bestVal = left; bestIdx = i; }
                    _ledLabels[i] = left > 0 ? FormatMMSS(left) : null;
                }
                _minTimerIndex = bestIdx;
            }
            else if (cyclePos < RED_PHASE + GREEN_PHASE)
            {
                int timeSinceGreen = cyclePos - RED_PHASE;
                int interval = GREEN_PHASE / 5;

                for (int i = 0; i < 5; i++)
                    _lights[i] = (timeSinceGreen >= (5 - i) * interval) ? "black" : "green";

                _status = "open";
                _statusMessage = "Ангар відкрито";
                _statusLine = "Перезапуск через " + FormatHHMMSS(GREEN_PHASE - timeSinceGreen);

                int bestIdx = -1; int bestVal = int.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    if (_lights[i] != "green") continue;
                    int target = (5 - i) * interval;
                    int left = target - timeSinceGreen;
                    if (left > 0 && left < bestVal) { bestVal = left; bestIdx = i; }
                    _ledLabels[i] = left > 0 ? FormatMMSS(left) : null;
                }
                _minTimerIndex = bestIdx;
            }
            else
            {
                int sinceBlack = cyclePos - RED_PHASE - GREEN_PHASE;
                for (int i = 0; i < 5; i++) _lights[i] = "black";
                _status = "reset";
                _statusMessage = "Ангар перезавантажується";
                _statusLine = "Перезапуск через " + FormatHHMMSS(BLACK_PHASE - sinceBlack);
                _minTimerIndex = -1;
            }
        }

        private static int Mod(int a, int m) { return (a % m + m) % m; }

        private static string FormatHHMMSS(int seconds)
        {
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            int s = seconds % 60;
            return $"{h:00}:{m:00}:{s:00}";
        }
        private static string FormatMMSS(int seconds)
        {
            int m = seconds / 60;
            int s = seconds % 60;
            return $"{m:00}:{s:00}";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Scale the whole canvas: draw in base coordinates
            g.ScaleTransform(_scale, _scale);

            Color bg, border, text;
            StatusPalette(_status, out bg, out border, out text);

            // Use base canvas size for layout
            var card = new Rectangle(12, 12, BASE_W - 24, BASE_H - 24);
            using (var br = new SolidBrush(bg)) g.FillRoundedRectangle(br, card, 14);
            using (var pen = new Pen(border, 1f)) g.DrawRoundedRectangle(pen, card, 14);

            using (var brush = new SolidBrush(text))
            using (var fnt = new Font("Segoe UI Semibold", 22f))
            {
                var fmt = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(_statusMessage, fnt, brush,
                    new RectangleF(0, 28, BASE_W, 36), fmt);
            }

            using (var brush = new SolidBrush(Color.FromArgb(180, 200, 200, 200)))
            using (var fnt = new Font("Consolas", 28f, FontStyle.Bold))
            {
                var fmt = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(_statusLine, fnt, brush,
                    new RectangleF(0, 82, BASE_W, 40), fmt);
            }

            int ledCount = 5;
            int spacing = 26;
            int diameter = 28;
            int rowWidth = ledCount * diameter + (ledCount - 1) * spacing;
            int startX = (BASE_W - rowWidth) / 2;
            int y = 150;

            for (int i = 0; i < ledCount; i++)
            {
                var rect = new Rectangle(startX + i * (diameter + spacing), y, diameter, diameter);
                DrawLed(g, rect, _lights[i]);

                if (_minTimerIndex == i && !string.IsNullOrEmpty(_ledLabels[i]))
                {
                    using (var brush = new SolidBrush(Color.FromArgb(200, 220, 220, 220)))
                    using (var f = new Font("Consolas", 12f, FontStyle.Regular))
                    {
                        var fmt = new StringFormat { Alignment = StringAlignment.Center };
                        g.DrawString(_ledLabels[i], f, brush,
                            new RectangleF(rect.X - 12, rect.Bottom + 6, rect.Width + 24, 20), fmt);
                    }
                }
            }

            using (var brush = new SolidBrush(Color.FromArgb(120, 200, 200, 200)))
            using (var fnt = new Font("Segoe UI", 9f))
            {
                g.DrawString(
                    "F6: вкл/викл • F7: старт=зараз • Shift+F7: ввести мс • F9: синхр. • Shift+F9: стерти+синхр. • F8: кліки крізь • Ctrl+F8: тимчас. перетягування • " +
                    "Ctrl+–/=/0: масштаб • Ctrl+Alt+–/= /0: прозорість • Esc: закрити",
                    fnt, brush, new PointF(16, BASE_H - 24));
            }
        }

        private static void DrawLed(Graphics g, Rectangle rect, string color)
        {
            Color fill = color == "red" ? Color.FromArgb(255, 80, 80)
                        : color == "green" ? Color.FromArgb(80, 200, 80)
                        : color == "black" ? Color.FromArgb(30, 30, 30)
                        : Color.Gray;

            using (var br = new SolidBrush(fill))
            using (var pen = new Pen(Color.FromArgb(90, 120, 120, 120), 1f))
            {
                g.FillEllipse(br, rect);
                g.DrawEllipse(pen, rect);
            }
        }

        private static void StatusPalette(string status, out Color bg, out Color border, out Color text)
        {
            if (status == "reset")
            {
                bg = Color.FromArgb(40, 120, 120, 20);
                border = Color.FromArgb(60, 160, 80);
                text = Color.FromArgb(220, 200, 80);
            }
            else if (status == "close")
            {
                bg = Color.FromArgb(30, 120, 35, 35);
                border = Color.FromArgb(140, 60, 60);
                text = Color.FromArgb(220, 120, 120);
            }
            else if (status == "open")
            {
                bg = Color.FromArgb(30, 35, 120, 35);
                border = Color.FromArgb(80, 160, 90);
                text = Color.FromArgb(140, 220, 140);
            }
            else
            {
                bg = Color.FromArgb(30, 80, 80, 80);
                border = Color.FromArgb(100, 100, 100);
                text = Color.FromArgb(180, 180, 180);
            }
        }
    }

    // Rounded rectangles helpers (same as before)
    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle bounds, int radius)
        {
            using (var path = RoundedRect(bounds, radius)) g.FillPath(brush, path);
        }
        public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle bounds, int radius)
        {
            using (var path = RoundedRect(bounds, radius)) g.DrawPath(pen, path);
        }
        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle b, int r)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = r * 2;
            path.AddArc(b.X, b.Y, d, d, 180, 90);
            path.AddArc(b.Right - d, b.Y, d, d, 270, 90);
            path.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
            path.AddArc(b.X, b.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Dialog for manual ms input (unchanged)
    // ---- Діалог введення часу старту (мс або HH:mm[:ss]) ----
    internal class InputMsDialog : Form
    {
        public long ValueMs { get; private set; }

        private readonly TextBox _box;
        private readonly Label _hint;

        public InputMsDialog()
        {
            SuspendLayout();
            Text = "Введіть час старту";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 170);
            MaximizeBox = MinimizeBox = false;
            // Reduce flicker on high DPI
            this.SetStyle(System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer |
                          System.Windows.Forms.ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();

            _box = new TextBox { Left = 15, Top = 15, Width = 390 };

            _hint = new Label
            {
                Left = 15,
                Top = 46,
                Width = 390,
                Height = 70,
                AutoSize = false,
                Text =
@"Приклади:
• 1753997899074       (UNIX мс)
• 15:52 або 15:52:39  (сьогодні, локальний час)",
            };

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 240, Width = 80, Top = 125 };
            var cancel = new Button { Text = "Скасувати", DialogResult = DialogResult.Cancel, Left = 325, Width = 80, Top = 125 };

            Controls.AddRange(new Control[] { _box, _hint, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;

            _box.Text = DateTime.Now.ToString("HH:mm:ss");
            ResumeLayout();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                string txt = _box.Text.Trim();
                long ms;

                // 1) Якщо число — вважаємо UNIX мс
                if (long.TryParse(txt, out ms))
                {
                    ValueMs = ms;
                }
                else
                {
                    // 2) HH:mm[:ss] — сьогоднішня дата
                    if (DateTime.TryParseExact(
                            txt,
                            new[] { "HH:mm:ss", "HH:mm" },
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime t))
                    {
                        DateTime local = DateTime.Today
                            .AddHours(t.Hour).AddMinutes(t.Minute).AddSeconds(t.Second);
                        ValueMs = new DateTimeOffset(local).ToUnixTimeMilliseconds();
                    }
                    else
                    {
                        MessageBox.Show(
                            "Невірний формат. Введіть UNIX мс або HH:mm[:ss].",
                            "Помилка вводу",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                        e.Cancel = true;
                        return;
                    }
                }
            }
            base.OnFormClosing(e);
        }
    }
}

#endif
