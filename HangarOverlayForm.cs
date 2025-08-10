using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExecutiveHangarOverlay
{
    public class HangarOverlayForm : Form
    {
        // ----- Phases (seconds) -----
        // Keep identical to the TS code to avoid drift
        private const int RED_PHASE = 2 * 60 * 60;    // 7200
        private const int GREEN_PHASE = 1 * 60 * 60;  // 3600
        private const int BLACK_PHASE = 5 * 60;       // 300
        private const int TOTAL_CYCLE = RED_PHASE + GREEN_PHASE + BLACK_PHASE;

        // ----- Fields -----
        private readonly Timer _uiTimer;           // UI refresh timer
        private long _cycleStartMs;                // Unix ms of cycle start
        private string[] _lights = new string[5];  // "red" | "green" | "black"
        private string _status = "default";        // close | open | reset | default
        private string _statusMessage = "Статус ангару невідомий";
        private string _statusLine = "";
        private int _minTimerIndex = -1;
        private string[] _ledLabels = new string[5]; // per-light mm:ss label or null
        private bool _clickThrough = false;       // allow mouse through overlay

        // ----- Win32 (click-through toggle) -----
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public HangarOverlayForm(long cycleStartMs)
        {
            _cycleStartMs = cycleStartMs;

            // --- Form basics (overlay) ---
            Text = "Executive Hangar Overlay";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            DoubleBuffered = true; // reduce flicker
            BackColor = Color.FromArgb(18, 18, 18);
            Opacity = 0.92; // subtle transparency
            ClientSize = new Size(820, 280);

            // Enable dragging by mouse down anywhere
            MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) _dragStart = e.Location; };
            MouseMove += (_, e) =>
            {
                if (_dragStart.HasValue && e.Button == MouseButtons.Left)
                {
                    var delta = new Point(e.X - _dragStart.Value.X, e.Y - _dragStart.Value.Y);
                    Location = new Point(Location.X + delta.X, Location.Y + delta.Y);
                }
            };
            MouseUp += (_, __) => _dragStart = null;

            // Hotkeys: F8 toggles click-through, F7/F9 manage start time, Esc closes
            KeyPreview = true;
            KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.F8)
                {
                    _clickThrough = !_clickThrough;
                    ApplyClickThrough(_clickThrough);
                }
                else if (e.KeyCode == Keys.F7 && !e.Shift)
                {
                    long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    StartTimeProvider.SetLocalOverride(ms);
                    _cycleStartMs = ms;
                    UpdateModel();
                    MessageBox.Show("Start set to NOW (local override).");
                }
                else if (e.KeyCode == Keys.F7 && e.Shift)
                {
                    using (var dlg = new InputMsDialog())
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            StartTimeProvider.SetLocalOverride(dlg.ValueMs);
                            _cycleStartMs = dlg.ValueMs;
                            UpdateModel();
                        }
                }
                else if (e.KeyCode == Keys.F9)
                {
                    long ms = await StartTimeProvider.ForceResyncAsync();
                    _cycleStartMs = ms;
                    UpdateModel();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };

            // Initialize timer
            _uiTimer = new Timer { Interval = 200 }; // ~5fps is enough
            _uiTimer.Tick += (_, __) => { UpdateModel(); Invalidate(); };
            _uiTimer.Start();

            // First values
            UpdateModel();
        }

        // Remember initial mouse pos for dragging
        private Point? _dragStart = null;

        // Apply WS_EX_TRANSPARENT so mouse clicks pass through
        private void ApplyClickThrough(bool enabled)
        {
            // NOTE: keep WS_EX_LAYERED so opacity still works
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            if (enabled) ex |= (WS_EX_TRANSPARENT | WS_EX_LAYERED);
            else ex = (ex | WS_EX_LAYERED) & ~WS_EX_TRANSPARENT;
            SetWindowLong(Handle, GWL_EXSTYLE, ex);
        }

        // Compute overlay model once per tick
        private void UpdateModel()
        {
            // --- Time math (identical to TS) ---
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int elapsed = (int)Math.Floor((nowMs - _cycleStartMs) / 1000.0);
            if (elapsed < 0) elapsed = 0;
            int cyclePos = Mod(elapsed, TOTAL_CYCLE);

            // Reset defaults
            for (int i = 0; i < 5; i++) { _lights[i] = "black"; _ledLabels[i] = null; }
            _minTimerIndex = -1;

            if (cyclePos < RED_PHASE)
            {
                // ----- RED phase -----
                int timeSinceRed = cyclePos;
                int interval = RED_PHASE / 5;

                // Left -> right: turns green once threshold passed
                for (int i = 0; i < 5; i++)
                    _lights[i] = (timeSinceRed >= (i + 1) * interval) ? "green" : "red";

                _status = "close";
                _statusMessage = "Ангар зачинено";
                _statusLine = "Відкриття через " + FormatHHMMSS(RED_PHASE - timeSinceRed);

                // Per-lamp timers for remaining sub-intervals
                int cycleElapsed = cyclePos;
                int bestIdx = -1; int bestVal = int.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    if (_lights[i] != "red") continue; // only red lamps tick in this phase
                    int target = (i + 1) * interval;
                    int left = target - cycleElapsed;
                    if (left > 0 && left < bestVal) { bestVal = left; bestIdx = i; }
                    _ledLabels[i] = left > 0 ? FormatMMSS(left) : null;
                }
                _minTimerIndex = bestIdx;
            }
            else if (cyclePos < RED_PHASE + GREEN_PHASE)
            {
                // ----- GREEN phase -----
                int timeSinceGreen = cyclePos - RED_PHASE;
                int interval = GREEN_PHASE / 5;

                // Right -> left: turns black once threshold passed
                for (int i = 0; i < 5; i++)
                    _lights[i] = (timeSinceGreen >= (5 - i) * interval) ? "black" : "green";

                _status = "open";
                _statusMessage = "Ангар відкрито";
                _statusLine = "Перезапуск через " + FormatHHMMSS(GREEN_PHASE - timeSinceGreen);

                // Per-lamp timers for green lamps
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
                // ----- BLACK (reset) phase -----
                int sinceBlack = cyclePos - RED_PHASE - GREEN_PHASE;
                for (int i = 0; i < 5; i++) _lights[i] = "black";
                _status = "reset";
                _statusMessage = "Ангар перезавантажується";
                _statusLine = "Перезапуск через " + FormatHHMMSS(BLACK_PHASE - sinceBlack);
                _minTimerIndex = -1;
            }
        }

        // Simple positive modulo
        private static int Mod(int a, int m) => (a % m + m) % m;

        // Format helpers (DRY)
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

        // Paint everything in one pass (KISS)
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Colors by status
            var (bg, border, text) = StatusPalette(_status);

            // Outer card
            var card = new Rectangle(12, 12, ClientSize.Width - 24, ClientSize.Height - 24);
            using (var br = new SolidBrush(bg)) g.FillRoundedRectangle(br, card, 14);
            using (var pen = new Pen(border, 1f)) g.DrawRoundedRectangle(pen, card, 14);

            // Title
            using (var brush = new SolidBrush(text))
            using (var fnt = new Font("Segoe UI Semibold", 22f))
            {
                var fmt = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(_statusMessage, fnt, brush,
                    new RectangleF(0, 28, ClientSize.Width, 36), fmt);
            }

            // Big line (timer string)
            using (var brush = new SolidBrush(Color.FromArgb(180, 200, 200, 200)))
            using (var fnt = new Font("Consolas", 28f, FontStyle.Bold))
            {
                var fmt = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(_statusLine, fnt, brush,
                    new RectangleF(0, 82, ClientSize.Width, 40), fmt);
            }

            // LEDs row
            int ledCount = 5;
            int spacing = 26;
            int diameter = 28;
            int rowWidth = ledCount * diameter + (ledCount - 1) * spacing;
            int startX = (ClientSize.Width - rowWidth) / 2;
            int y = 150;

            for (int i = 0; i < ledCount; i++)
            {
                var rect = new Rectangle(startX + i * (diameter + spacing), y, diameter, diameter);
                DrawLed(g, rect, _lights[i]);

                // Show label only for the closest-to-change lamp
                if (_minTimerIndex == i && !string.IsNullOrEmpty(_ledLabels[i]))
                {
                    using (var brush = new SolidBrush(Color.FromArgb(180, 200, 200, 200)))
                    using (var f = new Font("Consolas", 14f))
                    {
                        var fmt = new StringFormat { Alignment = StringAlignment.Center };
                        g.DrawString(_ledLabels[i], f, brush,
                            new RectangleF(rect.X - 10, rect.Bottom + 8, rect.Width + 20, 24), fmt);
                    }
                }
            }

            // Hint footer
            using (var brush = new SolidBrush(Color.FromArgb(120, 200, 200, 200)))
            using (var fnt = new Font("Segoe UI", 9f))
            {
                g.DrawString("F7: set start • Shift+F7: input ms • F9: resync • F8: click-through • Esc: close • Drag to move",
                    fnt, brush, new PointF(16, ClientSize.Height - 24));
            }
        }

        // Draw a single LED with subtle border and glow
        private static void DrawLed(Graphics g, Rectangle rect, string color)
        {
            Color fill = color switch
            {
                "red" => Color.FromArgb(255, 80, 80),
                "green" => Color.FromArgb(80, 200, 80),
                "black" => Color.FromArgb(30, 30, 30),
                _ => Color.Gray
            };
            using (var br = new SolidBrush(fill))
            using (var pen = new Pen(Color.FromArgb(90, 120, 120, 120), 1f))
            {
                g.FillEllipse(br, rect);
                g.DrawEllipse(pen, rect);
            }
        }

        // Map status to palette
        private static (Color bg, Color border, Color text) StatusPalette(string status)
        {
            return status switch
            {
                "reset" => (Color.FromArgb(40, 120, 120, 20), Color.FromArgb(60, 160, 80), Color.FromArgb(220, 200, 80)),
                "close" => (Color.FromArgb(30, 120, 35, 35), Color.FromArgb(140, 60, 60), Color.FromArgb(220, 120, 120)),
                "open"  => (Color.FromArgb(30, 35, 120, 35), Color.FromArgb(80, 160, 90), Color.FromArgb(140, 220, 140)),
                _       => (Color.FromArgb(30, 80, 80, 80), Color.FromArgb(100, 100, 100), Color.FromArgb(180, 180, 180)),
            };
        }
    }

    // ---- Rounded rectangles helpers (GDI+) ----
    // Keep them here (KISS) to avoid extra classes.
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

    // Simple dialog to input a start timestamp in milliseconds
    internal class InputMsDialog : Form
    {
        public long ValueMs { get; private set; }
        private readonly TextBox _box;

        public InputMsDialog()
        {
            Text = "Enter start (ms)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(300, 110);

            _box = new TextBox { Left = 15, Top = 15, Width = 260 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 120, Width = 80, Top = 60 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 205, Width = 80, Top = 60 };

            Controls.AddRange(new Control[] { _box, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                if (!long.TryParse(_box.Text, out long ms))
                {
                    MessageBox.Show("Invalid number");
                    e.Cancel = true;
                    return;
                }
                ValueMs = ms;
            }
            base.OnFormClosing(e);
        }
    }
}

