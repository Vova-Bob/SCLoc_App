using System;
using System.Drawing;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SCLOCUA
{
    /// <summary>
    /// Borderless always-on-top overlay window that mirrors the timer logic
    /// from the executive hangar web page. It synchronizes with the server
    /// using the timestamp stored in https://exec.xyxyll.com/app.js and
    /// displays the current phase, phase timer and indicator lamps with the
    /// remaining time until the next lamp switches.
    /// </summary>
    public class HangarTimerOverlay : Form
    {
        private const int RED_PHASE = 2 * 60 * 60;    // 7200 seconds
        private const int GREEN_PHASE = 1 * 60 * 60;  // 3600 seconds
        private const int BLACK_PHASE = 5 * 60;       // 300 seconds
        private const int TOTAL_CYCLE = RED_PHASE + GREEN_PHASE + BLACK_PHASE;
        private const string APP_JS_URL = "https://exec.xyxyll.com/app.js";

        private readonly Label _statusLabel;
        private readonly Label _phaseTimerLabel;
        private readonly Label[] _lampLabels = new Label[5];
        private readonly Label[] _lampTimerLabels = new Label[5];
        private readonly Timer _updateTimer;
        private readonly ToolTip _opacityTip = new ToolTip();

        private DateTime _cycleStart;
        private bool _syncError;

        public HangarTimerOverlay()
        {
            // Window configuration
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Opacity = 0.8;
            StartPosition = FormStartPosition.Manual;
            Width = 420;
            Height = 180;

            // Status label
            _statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.White
            };
            Controls.Add(_statusLabel);

            // Phase timer label
            _phaseTimerLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 50,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Consolas", 32, FontStyle.Bold),
                ForeColor = Color.White
            };
            Controls.Add(_phaseTimerLabel);

            // Lamps panel
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Clear();
            for (int i = 0; i < 5; i++)
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));

            var lampFont = new Font("Segoe UI Symbol", 32, FontStyle.Regular);
            var timerFont = new Font("Consolas", 12, FontStyle.Bold);
            for (int i = 0; i < 5; i++)
            {
                _lampLabels[i] = new Label
                {
                    Text = "●",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = lampFont,
                    ForeColor = Color.Black
                };
                panel.Controls.Add(_lampLabels[i], i, 0);

                _lampTimerLabels[i] = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.TopCenter,
                    Font = timerFont,
                    ForeColor = Color.White
                };
                panel.Controls.Add(_lampTimerLabels[i], i, 1);
            }
            Controls.Add(panel);

            // Update timer
            _updateTimer = new Timer { Interval = 1000 };
            _updateTimer.Tick += (s, e) => UpdateDisplay();

            Load += async (s, e) => await InitializeAsync();
            FormClosed += (s, e) => UnregisterHotKeys();
        }

        /// <summary>
        /// Fetches the global cycle start time from the remote JavaScript file
        /// and registers global hotkeys.
        /// </summary>
        private async Task InitializeAsync()
        {
            if (!await FetchCycleStartAsync())
            {
                _syncError = true;
                _statusLabel.Text = "SYNC ERROR";
                _statusLabel.ForeColor = Color.Red;
                _phaseTimerLabel.Text = "unable to fetch";
            }
            else
            {
                RegisterHotKeys();
                UpdateDisplay();
                _updateTimer.Start();
            }

            // Enable click-through so the overlay does not intercept mouse events
            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }

        private async Task<bool> FetchCycleStartAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string js = await client.GetStringAsync(APP_JS_URL);
                    var match = Regex.Match(js, "INITIAL_OPEN_TIME\\s*=\\s*new Date\\('([^']+)'\\)");
                    if (!match.Success)
                        return false;
                    string value = match.Groups[1].Value;
                    _cycleStart = DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private void UpdateDisplay()
        {
            if (_syncError)
                return;

            var now = DateTime.UtcNow;
            int elapsed = (int)Math.Floor((now - _cycleStart).TotalSeconds);
            int cyclePos = ((elapsed % TOTAL_CYCLE) + TOTAL_CYCLE) % TOTAL_CYCLE; // handle negative

            string status;
            int phaseRemaining;
            string[] lights = new string[5];

            if (cyclePos < RED_PHASE)
            {
                status = "closed";
                int timeSinceStart = cyclePos;
                int interval = RED_PHASE / 5;
                for (int i = 0; i < 5; i++)
                    lights[i] = timeSinceStart >= (i + 1) * interval ? "green" : "red";
                phaseRemaining = RED_PHASE - timeSinceStart;
                _statusLabel.ForeColor = Color.Red;
            }
            else if (cyclePos < RED_PHASE + GREEN_PHASE)
            {
                status = "open";
                int timeSinceStart = cyclePos - RED_PHASE;
                int interval = GREEN_PHASE / 5;
                for (int i = 0; i < 5; i++)
                    lights[i] = timeSinceStart >= (5 - i) * interval ? "black" : "green";
                phaseRemaining = GREEN_PHASE - timeSinceStart;
                _statusLabel.ForeColor = Color.Lime;
            }
            else
            {
                status = "reset";
                for (int i = 0; i < 5; i++) lights[i] = "black";
                int timeSinceStart = cyclePos - RED_PHASE - GREEN_PHASE;
                phaseRemaining = BLACK_PHASE - timeSinceStart;
                _statusLabel.ForeColor = Color.Gray;
            }

            _statusLabel.Text = status.ToUpperInvariant();
            _phaseTimerLabel.Text = FormatTime(phaseRemaining);

            // Determine timer under lamps
            string[] ledTimers = new string[5];
            int?[] timerValues = new int?[5];
            int cycleElapsed = cyclePos;
            for (int i = 0; i < 5; i++)
            {
                int? secondsLeft = null;
                if (status == "closed" && lights[i] == "red")
                {
                    int target = (i + 1) * (RED_PHASE / 5);
                    int timeLeft = target - cycleElapsed;
                    if (timeLeft > 0) secondsLeft = timeLeft;
                }
                if (status == "open" && lights[i] == "green")
                {
                    int timeSinceGreen = cycleElapsed - RED_PHASE;
                    int target = (5 - i) * (GREEN_PHASE / 5);
                    int timeLeft = target - timeSinceGreen;
                    if (timeLeft > 0) secondsLeft = timeLeft;
                }
                ledTimers[i] = secondsLeft.HasValue ? FormatTime(secondsLeft.Value).Substring(3) : string.Empty;
                timerValues[i] = secondsLeft;
            }

            int minIndex = -1;
            int? minVal = null;
            for (int i = 0; i < timerValues.Length; i++)
            {
                var v = timerValues[i];
                if (v == null) continue;
                if (minVal == null || v < minVal)
                {
                    minVal = v;
                    minIndex = i;
                }
            }

            for (int i = 0; i < 5; i++)
            {
                _lampLabels[i].ForeColor = lights[i] == "red" ? Color.Red :
                                           lights[i] == "green" ? Color.Lime : Color.Black;
                _lampTimerLabels[i].Text = i == minIndex ? ledTimers[i] : string.Empty;
            }
        }

        private static string FormatTime(int seconds)
        {
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            int s = seconds % 60;
            return $"{h:D2}:{m:D2}:{s:D2}";
        }

        #region Hotkeys
        private void RegisterHotKeys()
        {
            RegisterHotKey(Handle, 1, MOD_CONTROL | MOD_ALT, (int)Keys.Up);
            RegisterHotKey(Handle, 2, MOD_CONTROL | MOD_ALT, (int)Keys.Down);
            RegisterHotKey(Handle, 3, MOD_CONTROL | MOD_ALT, (int)Keys.Left);
            RegisterHotKey(Handle, 4, MOD_CONTROL | MOD_ALT, (int)Keys.Right);
            RegisterHotKey(Handle, 5, MOD_CONTROL | MOD_ALT, (int)Keys.Oemplus);
            RegisterHotKey(Handle, 6, MOD_CONTROL | MOD_ALT, (int)Keys.OemMinus);
            RegisterHotKey(Handle, 7, MOD_CONTROL | MOD_ALT, (int)Keys.Add);
            RegisterHotKey(Handle, 8, MOD_CONTROL | MOD_ALT, (int)Keys.Subtract);
        }

        private void UnregisterHotKeys()
        {
            for (int i = 1; i <= 8; i++)
                UnregisterHotKey(Handle, i);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                switch (id)
                {
                    case 1: Top -= 10; break;
                    case 2: Top += 10; break;
                    case 3: Left -= 10; break;
                    case 4: Left += 10; break;
                    case 5:
                    case 7:
                        Opacity = Math.Min(1.0, Opacity + 0.1);
                        _opacityTip.Show($"Opacity: {Opacity:F1}", this, 1000);
                        break;
                    case 6:
                    case 8:
                        Opacity = Math.Max(0.2, Opacity - 0.1);
                        _opacityTip.Show($"Opacity: {Opacity:F1}", this, 1000);
                        break;
                }
            }
            base.WndProc(ref m);
        }
        #endregion

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // WinAPI
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1;
        private const uint MOD_CONTROL = 0x2;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}