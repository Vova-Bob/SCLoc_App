using System;
using System.Drawing;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SCLOCUA
{
    /// <summary>
    /// Borderless overlay window that mimics the executive hangar timer/indicator panel.
    /// Use <c>new HangarTimerOverlay().Show();</c> to display the overlay.
    /// </summary>
    public class HangarTimerOverlay : Form
    {
        private const int RED_PHASE = 2 * 60 * 60;    // 7200 seconds
        private const int GREEN_PHASE = 1 * 60 * 60;  // 3600 seconds
        private const int BLACK_PHASE = 5 * 60;       // 300 seconds
        private const int TOTAL_CYCLE = RED_PHASE + GREEN_PHASE + BLACK_PHASE;

        private readonly Label statusLabel = new Label();
        private readonly Label phaseTimerLabel = new Label();
        private readonly Label[] lampLabels = new Label[5];
        private readonly Label[] lampTimerLabels = new Label[5];
        private readonly Timer uiTimer = new Timer();

        private DateTime cycleStart;
        private bool initialized = false;

        private enum Phase
        {
            Close,
            Open,
            Reset
        }

        public HangarTimerOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Opacity = 0.8;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                Dock = DockStyle.Fill,
                AutoSize = true,
                BackColor = Color.Transparent
            };

            statusLabel.AutoSize = true;
            statusLabel.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            statusLabel.ForeColor = Color.White;
            statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            statusLabel.Dock = DockStyle.Top;

            phaseTimerLabel.AutoSize = true;
            phaseTimerLabel.Font = new Font("Segoe UI", 24, FontStyle.Bold);
            phaseTimerLabel.ForeColor = Color.White;
            phaseTimerLabel.TextAlign = ContentAlignment.MiddleCenter;
            phaseTimerLabel.Dock = DockStyle.Top;

            var lampLayout = new TableLayoutPanel
            {
                ColumnCount = 5,
                RowCount = 2,
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = Color.Transparent
            };

            for (int i = 0; i < 5; i++)
            {
                lampLabels[i] = new Label
                {
                    AutoSize = true,
                    Font = new Font("Segoe UI Emoji", 32),
                    Text = "⚫",
                    ForeColor = Color.White,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill
                };
                lampLayout.Controls.Add(lampLabels[i], i, 0);

                lampTimerLabels[i] = new Label
                {
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    ForeColor = Color.White,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill
                };
                lampLayout.Controls.Add(lampTimerLabels[i], i, 1);
            }

            root.Controls.Add(statusLabel);
            root.Controls.Add(phaseTimerLabel);
            root.Controls.Add(lampLayout);
            Controls.Add(root);

            uiTimer.Interval = 1000;
            uiTimer.Tick += (s, e) => UpdateDisplay();

            Load += async (s, e) => await InitializeAsync();
        }

        /// <summary>Make the overlay click-through so it does not intercept mouse input.</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TRANSPARENT = 0x20;
                const int WS_EX_TOOLWINDOW = 0x80; // hide from Alt-Tab
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                cycleStart = await FetchCycleStartAsync();
                initialized = true;
                uiTimer.Start();
                UpdateDisplay();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Sync error";
                statusLabel.ForeColor = Color.Red;
                phaseTimerLabel.Text = ex.Message;
            }
        }

        private static async Task<DateTime> FetchCycleStartAsync()
        {
            using (HttpClient client = new HttpClient())
            {
                string js = await client.GetStringAsync("https://exec.xyxyll.com/app.js");
                Match match = Regex.Match(js, @"INITIAL_OPEN_TIME\s*=\s*new Date\('([^']+)'\)");
                if (!match.Success)
                    throw new InvalidOperationException("INITIAL_OPEN_TIME not found");
                DateTimeOffset dto = DateTimeOffset.Parse(match.Groups[1].Value, null, DateTimeStyles.AssumeUniversal);
                return dto.UtcDateTime;
            }
        }

        private static string FormatTime(int seconds)
        {
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            int s = seconds % 60;
            return $"{h:00}:{m:00}:{s:00}";
        }

        private void UpdateDisplay()
        {
            if (!initialized) return;

            int elapsed = (int)(DateTime.UtcNow - cycleStart).TotalSeconds;
            int cyclePos = ((elapsed % TOTAL_CYCLE) + TOTAL_CYCLE) % TOTAL_CYCLE;

            Phase phase;
            string[] lights = new string[5];
            int remaining;

            if (cyclePos < RED_PHASE)
            {
                phase = Phase.Close;
                int timeSinceRed = cyclePos;
                int interval = RED_PHASE / 5;
                remaining = RED_PHASE - timeSinceRed;
                for (int i = 0; i < 5; i++)
                {
                    lights[i] = timeSinceRed >= (i + 1) * interval ? "green" : "red";
                }
                statusLabel.Text = "CLOSED";
                statusLabel.ForeColor = Color.Red;
            }
            else if (cyclePos < RED_PHASE + GREEN_PHASE)
            {
                phase = Phase.Open;
                int timeSinceGreen = cyclePos - RED_PHASE;
                int interval = GREEN_PHASE / 5;
                remaining = GREEN_PHASE - timeSinceGreen;
                for (int i = 0; i < 5; i++)
                {
                    lights[i] = timeSinceGreen >= (5 - i) * interval ? "black" : "green";
                }
                statusLabel.Text = "OPEN";
                statusLabel.ForeColor = Color.Lime;
            }
            else
            {
                phase = Phase.Reset;
                int timeSinceBlack = cyclePos - RED_PHASE - GREEN_PHASE;
                remaining = BLACK_PHASE - timeSinceBlack;
                for (int i = 0; i < 5; i++) lights[i] = "black";
                statusLabel.Text = "RESET";
                statusLabel.ForeColor = Color.Gray;
            }

            phaseTimerLabel.Text = FormatTime(remaining);

            for (int i = 0; i < 5; i++)
            {
                lampTimerLabels[i].Text = string.Empty;
                switch (lights[i])
                {
                    case "red": lampLabels[i].Text = "🔴"; break;
                    case "green": lampLabels[i].Text = "🟢"; break;
                    default: lampLabels[i].Text = "⚫"; break;
                }
            }

            int[] timerValues = new int[5];
            for (int i = 0; i < 5; i++) timerValues[i] = -1;

            for (int i = 0; i < 5; i++)
            {
                int? secondsLeft = null;

                if (phase == Phase.Close && lights[i] == "red")
                {
                    int target = (i + 1) * (RED_PHASE / 5);
                    int timeLeft = target - cyclePos;
                    if (timeLeft > 0) secondsLeft = timeLeft;
                }
                if (phase == Phase.Open && lights[i] == "green")
                {
                    int timeSinceGreen = cyclePos - RED_PHASE;
                    int target = (5 - i) * (GREEN_PHASE / 5);
                    int timeLeft = target - timeSinceGreen;
                    if (timeLeft > 0) secondsLeft = timeLeft;
                }

                if (secondsLeft.HasValue)
                {
                    timerValues[i] = secondsLeft.Value;
                }
            }

            int minIndex = -1;
            for (int i = 0; i < 5; i++)
            {
                if (timerValues[i] >= 0 && (minIndex == -1 || timerValues[i] < timerValues[minIndex]))
                    minIndex = i;
            }
            if (minIndex >= 0)
            {
                lampTimerLabels[minIndex].Text = FormatTime(timerValues[minIndex]).Substring(3); // MM:SS
            }
        }
    }
}
