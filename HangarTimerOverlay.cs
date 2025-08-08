using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SCLOCUA
{
    /// <summary>
    /// Borderless always-on-top overlay window that mirrors the timer logic of
    /// https://ex-hangar.scloc.pp.ua/. The overlay uses only standard WinForms
    /// controls and Unicode emoji to replicate the lamp indicators without any
    /// custom drawing.
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
        private readonly TableLayoutPanel _lampTable;
        private readonly Label[] _lampLabels = new Label[5];
        private readonly Label[] _timerLabels = new Label[5];
        private readonly Timer _updateTimer;

        private DateTime _cycleStart;
        private bool _syncError;

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
                Font = new Font("Consolas", 28, FontStyle.Bold),
                ForeColor = Color.White
            };
            Controls.Add(_phaseTimerLabel);

            // Panel for lamps
            var lampPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            Controls.Add(lampPanel);

            // TableLayout for lamps and timers
            _lampTable = new TableLayoutPanel
            {
                ColumnCount = 5,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top
            };
            _lampTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            _lampTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            for (int i = 0; i < 5; i++)
                _lampTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

            for (int i = 0; i < 5; i++)
            {
                var lamp = new Label
                {
                    Text = "⚫",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Emoji", 32)
                };
                _lampLabels[i] = lamp;
                _lampTable.Controls.Add(lamp, i, 0);

                var timer = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.TopCenter,
                    Font = new Font("Consolas", 10f),
                    ForeColor = Color.LightGray,
                    Visible = false
                };
                _timerLabels[i] = timer;
                _lampTable.Controls.Add(timer, i, 1);
            }

            lampPanel.Controls.Add(_lampTable);
            lampPanel.Resize += (s, e) => CenterLampTable(lampPanel);
            CenterLampTable(lampPanel);

            _updateTimer = new Timer { Interval = 1000 };
            _updateTimer.Tick += (s, e) => UpdateDisplay();

            Load += async (s, e) => await InitializeAsync();
        }

        private void CenterLampTable(Panel panel)
        {
            _lampTable.Left = (panel.ClientSize.Width - _lampTable.Width) / 2;
            _lampTable.Top = (panel.ClientSize.Height - _lampTable.Height) / 2;
        }

        private async Task InitializeAsync()
        {
            if (!await FetchCycleStartAsync())
            {
                _syncError = true;
                _statusLabel.Text = "Помилка синхронізації";
                _statusLabel.ForeColor = Color.Red;
                _phaseTimerLabel.Text = "не вдалось отримати час";
            }
            else
            {
                UpdateDisplay();
                _updateTimer.Start();
            }

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
                _cycleStart = DateTime.ParseExact(match.Groups[1].Value,
                    "yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal);
                return true;
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
            int elapsed = (int)(now - _cycleStart).TotalSeconds;
            int cyclePos = ((elapsed % TOTAL_CYCLE) + TOTAL_CYCLE) % TOTAL_CYCLE;

            string status;
            string timerPrefix;
            int phaseRemaining;
            string[] lights = new string[5];

            if (cyclePos < RED_PHASE)
            {
                status = "Ангар зачинено";
                timerPrefix = "Відкриття через ";
                int timeSinceStart = cyclePos;
                int interval = RED_PHASE / 5;
                for (int i = 0; i < 5; i++)
                    lights[i] = timeSinceStart >= (i + 1) * interval ? "🟢" : "🔴";
                phaseRemaining = RED_PHASE - timeSinceStart;
                _statusLabel.ForeColor = Color.Red;
            }
            else if (cyclePos < RED_PHASE + GREEN_PHASE)
            {
                status = "Ангар відкрито";
                timerPrefix = "Перезапуск через ";
                int timeSinceStart = cyclePos - RED_PHASE;
                int interval = GREEN_PHASE / 5;
                for (int i = 0; i < 5; i++)
                    lights[i] = timeSinceStart >= (5 - i) * interval ? "⚫" : "🟢";
                phaseRemaining = GREEN_PHASE - timeSinceStart;
                _statusLabel.ForeColor = Color.Lime;
            }
            else
            {
                status = "Перезапуск";
                timerPrefix = "Відкриття через ";
                for (int i = 0; i < 5; i++)
                    lights[i] = "⚫";
                int timeSinceStart = cyclePos - RED_PHASE - GREEN_PHASE;
                phaseRemaining = BLACK_PHASE - timeSinceStart;
                _statusLabel.ForeColor = Color.Gray;
            }

            _statusLabel.Text = status;
            _phaseTimerLabel.Text = timerPrefix + FormatTime(phaseRemaining);

            int?[] timerValues = new int?[5];
            for (int i = 0; i < 5; i++)
            {
                int? secondsLeft = null;
                if (cyclePos < RED_PHASE && lights[i] == "🔴")
                {
                    int target = (i + 1) * (RED_PHASE / 5);
                    secondsLeft = target - cyclePos;
                }
                else if (cyclePos < RED_PHASE + GREEN_PHASE && lights[i] == "🟢")
                {
                    int timeSinceGreen = cyclePos - RED_PHASE;
                    int target = (5 - i) * (GREEN_PHASE / 5);
                    secondsLeft = target - timeSinceGreen;
                }
                timerValues[i] = secondsLeft > 0 ? secondsLeft : null;
            }

            int minIndex = -1;
            int? minVal = null;
            for (int i = 0; i < 5; i++)
            {
                var v = timerValues[i];
                if (v == null)
                    continue;
                if (minVal == null || v < minVal)
                {
                    minVal = v;
                    minIndex = i;
                }
            }

            for (int i = 0; i < 5; i++)
            {
                _lampLabels[i].Text = lights[i];
                if (i == minIndex && minVal.HasValue)
                {
                    _timerLabels[i].Text = FormatTime(minVal.Value).Substring(3);
                    _timerLabels[i].Visible = true;
                }
                else
                {
                    _timerLabels[i].Visible = false;
                }
            }
        }

        private static string FormatTime(int seconds)
        {
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            int s = seconds % 60;
            return $"{h:D2}:{m:D2}:{s:D2}";
        }

        // WinAPI for click-through window
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}

