using System;
using System.Drawing;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Globalization;

namespace SCLOCUA
{
    /// <summary>
    /// Borderless always-on-top overlay window that mirrors the executive
    /// hangar timer. It uses only standard WinForms controls to display the
    /// current status, phase timer and a row of five emoji lamps with a
    /// countdown under the next lamp to change.
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
        private readonly Panel _lampsPanel;
        private readonly TableLayoutPanel _lampTable;
        private readonly Timer _updateTimer = new Timer { Interval = 1000 };
        private readonly ToolTip _opacityTip = new ToolTip();

        private DateTime _cycleStart;
        private bool _syncError;
        private bool _visibleState = true;

        public HangarTimerOverlay()
        {
            // Window configuration
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Opacity = 0.85;
            StartPosition = FormStartPosition.Manual;
            Width = 420;
            Height = 200;

            // Status label (Ukrainian text)
            _statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.Red,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_statusLabel);

            // Phase timer label
            _phaseTimerLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 60,
                Font = new Font("Consolas", 36, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_phaseTimerLabel);

            // Panel that holds lamp table for horizontal centering
            _lampsPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = Color.Transparent
            };
            Controls.Add(_lampsPanel);

            _lampTable = new TableLayoutPanel
            {
                RowCount = 2,
                ColumnCount = 5,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            for (int i = 0; i < 5; i++)
                _lampTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _lampTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _lampTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            for (int i = 0; i < 5; i++)
            {
                var lamp = new Label
                {
                    Text = "⚫",
                    AutoSize = true,
                    Font = new Font("Segoe UI Emoji", 36, FontStyle.Regular),
                    Anchor = AnchorStyles.None,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                var timer = new Label
                {
                    AutoSize = true,
                    Font = new Font("Consolas", 12, FontStyle.Bold),
                    ForeColor = Color.LightGray,
                    Anchor = AnchorStyles.None,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Visible = false
                };
                _lampLabels[i] = lamp;
                _lampTimerLabels[i] = timer;
                _lampTable.Controls.Add(lamp, i, 0);
                _lampTable.Controls.Add(timer, i, 1);
            }

            _lampsPanel.Controls.Add(_lampTable);
            _lampsPanel.Resize += (s, e) =>
            {
                _lampTable.Location = new Point((_lampsPanel.Width - _lampTable.Width) / 2, 0);
            };

            _updateTimer.Tick += UpdateTimerTick;
            Load += HangarTimerOverlay_Load;
        }

        private async void HangarTimerOverlay_Load(object sender, EventArgs e)
        {
            await InitializeAsync();
        }

        private void UpdateTimerTick(object sender, EventArgs e) => UpdateDisplay();

        /// <summary>
        /// Fetches cycle start time from remote JavaScript and enables hotkeys.
        /// </summary>
        private async Task InitializeAsync()
        {
            if (!await FetchCycleStartAsync())
            {
                _syncError = true;
                _statusLabel.Text = "ПОМИЛКА";
                _statusLabel.ForeColor = Color.Red;
                if (string.IsNullOrEmpty(_phaseTimerLabel.Text))
                    _phaseTimerLabel.Text = "unable to fetch";
            }
            else
            {
                RegisterHotKeys();
                UpdateDisplay();
                _updateTimer.Start();
            }

            // Enable click-through
            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }

        private async Task<bool> FetchCycleStartAsync()
        {
            try
            {
                var client = HttpClientService.Client;
                string js = await client.GetStringAsync(APP_JS_URL);
                var match = Regex.Match(js, "INITIAL_OPEN_TIME\\s*=\\s*new Date\\('([^']+)'\\)");
                if (!match.Success)
                    return false;
                string value = match.Groups[1].Value;
                _cycleStart = DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
                return true;
            }
            catch
            {
                _phaseTimerLabel.Text = "fetch error";
                return false;
            }
        }

        private void UpdateDisplay()
        {
            if (_syncError) return;

            var now = DateTime.UtcNow;
            int elapsed = (int)Math.Floor((now - _cycleStart).TotalSeconds);
            int cyclePos = ((elapsed % TOTAL_CYCLE) + TOTAL_CYCLE) % TOTAL_CYCLE;

            string status;
            int phaseRemaining;
            string[] lights = new string[5];

            if (cyclePos < RED_PHASE)
            {
                status = "ЗАКРИТО";
                int timeSinceStart = cyclePos;
                int interval = RED_PHASE / 5;
                for (int i = 0; i < 5; i++)
                    lights[i] = timeSinceStart >= (i + 1) * interval ? "green" : "red";
                phaseRemaining = RED_PHASE - timeSinceStart;
                _statusLabel.ForeColor = Color.Red;
            }
            else if (cyclePos < RED_PHASE + GREEN_PHASE)
            {
                status = "ВІДКРИТО";
                int timeSinceStart = cyclePos - RED_PHASE;
                int interval = GREEN_PHASE / 5;
                for (int i = 0; i < 5; i++)
                    lights[i] = timeSinceStart >= (5 - i) * interval ? "black" : "green";
                phaseRemaining = GREEN_PHASE - timeSinceStart;
                _statusLabel.ForeColor = Color.Lime;
            }
            else
            {
                status = "СКИДАННЯ";
                for (int i = 0; i < 5; i++)
                    lights[i] = "black";
                int timeSinceStart = cyclePos - RED_PHASE - GREEN_PHASE;
                phaseRemaining = BLACK_PHASE - timeSinceStart;
                _statusLabel.ForeColor = Color.Gray;
            }

            _statusLabel.Text = status;
            _phaseTimerLabel.Text = FormatTime(phaseRemaining);

            string[] ledTimers = new string[5];
            int?[] timerValues = new int?[5];
            int cycleElapsed = cyclePos;
            for (int i = 0; i < 5; i++)
            {
                int? secondsLeft = null;
                if (status == "ЗАКРИТО" && lights[i] == "red")
                {
                    int target = (i + 1) * (RED_PHASE / 5);
                    int timeLeft = target - cycleElapsed;
                    if (timeLeft > 0) secondsLeft = timeLeft;
                }
                if (status == "ВІДКРИТО" && lights[i] == "green")
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
                string emoji = lights[i] == "red" ? "🔴" : lights[i] == "green" ? "🟢" : "⚫";
                _lampLabels[i].Text = emoji;
                _lampTimerLabels[i].Text = ledTimers[i];
                _lampTimerLabels[i].Visible = i == minIndex && !string.IsNullOrEmpty(ledTimers[i]);
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

        public void ToggleVisibility()
        {
            if (InvokeRequired)
            {
                Invoke((Action)ToggleVisibility);
                return;
            }

            _visibleState = !_visibleState;
            if (_visibleState)
            {
                Show();
                BringToFront();
                Enabled = true;
            }
            else
            {
                Hide();
                Enabled = false;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _updateTimer.Stop();
            _updateTimer.Tick -= UpdateTimerTick;
            _updateTimer.Dispose();
            Load -= HangarTimerOverlay_Load;
            _opacityTip.Dispose();
            UnregisterHotKeys();
            base.OnFormClosed(e);
        }

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

