using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SCLOCUA
{
    /// <summary>
    /// Custom panel that draws five lamps indicating the executive hangar
    /// status. The color logic and timing mirror the JavaScript tracker.
    /// </summary>
    public class HangarLampPanel : Control
    {
        private const int LampCount = 5;

        // Durations taken directly from the JavaScript implementation.
        private static readonly TimeSpan OPEN_DURATION = TimeSpan.FromMilliseconds(3900246);
        private static readonly TimeSpan CLOSE_DURATION = TimeSpan.FromMilliseconds(7200453);
        private static readonly TimeSpan CYCLE_DURATION = OPEN_DURATION + CLOSE_DURATION;
        private static readonly DateTimeOffset INITIAL_OPEN_TIME =
            new DateTimeOffset(2025, 7, 17, 19, 32, 24, 883, TimeSpan.FromHours(-4));

        private readonly Timer _timer;
        private readonly LampColor[] _currentColors = new LampColor[LampCount];

        public HangarStatus CurrentStatus { get; private set; }
        public TimeSpan TimeToNextChange { get; private set; }

        private static readonly Threshold[] thresholds = new[]
        {
            new Threshold(TimeSpan.Zero, TimeSpan.FromMinutes(12),
                new[]{LampColor.Green,LampColor.Green,LampColor.Green,LampColor.Green,LampColor.Green}),
            new Threshold(TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(24),
                new[]{LampColor.Green,LampColor.Green,LampColor.Green,LampColor.Green,LampColor.Empty}),
            new Threshold(TimeSpan.FromMinutes(24), TimeSpan.FromMinutes(36),
                new[]{LampColor.Green,LampColor.Green,LampColor.Green,LampColor.Empty,LampColor.Empty}),
            new Threshold(TimeSpan.FromMinutes(36), TimeSpan.FromMinutes(48),
                new[]{LampColor.Green,LampColor.Green,LampColor.Empty,LampColor.Empty,LampColor.Empty}),
            new Threshold(TimeSpan.FromMinutes(48), TimeSpan.FromMinutes(60),
                new[]{LampColor.Green,LampColor.Empty,LampColor.Empty,LampColor.Empty,LampColor.Empty}),
            new Threshold(TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(65),
                new[]{LampColor.Empty,LampColor.Empty,LampColor.Empty,LampColor.Empty,LampColor.Empty}),
            new Threshold(TimeSpan.FromMinutes(65), TimeSpan.FromMinutes(89),
                new[]{LampColor.Red,LampColor.Red,LampColor.Red,LampColor.Red,LampColor.Red}),
            new Threshold(TimeSpan.FromMinutes(89), TimeSpan.FromMinutes(113),
                new[]{LampColor.Green,LampColor.Red,LampColor.Red,LampColor.Red,LampColor.Red}),
            new Threshold(TimeSpan.FromMinutes(113), TimeSpan.FromMinutes(137),
                new[]{LampColor.Green,LampColor.Green,LampColor.Red,LampColor.Red,LampColor.Red}),
            new Threshold(TimeSpan.FromMinutes(137), TimeSpan.FromMinutes(161),
                new[]{LampColor.Green,LampColor.Green,LampColor.Green,LampColor.Red,LampColor.Red}),
            new Threshold(TimeSpan.FromMinutes(161), TimeSpan.FromMinutes(185),
                new[]{LampColor.Green,LampColor.Green,LampColor.Green,LampColor.Green,LampColor.Red})
        };

        public HangarLampPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => UpdateState();
            _timer.Start();

            UpdateState();
        }

        private void UpdateState()
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = now - INITIAL_OPEN_TIME;
            var cycleMs = CYCLE_DURATION.TotalMilliseconds;
            var timeInCycleMs = ((elapsed.TotalMilliseconds % cycleMs) + cycleMs) % cycleMs;
            var timeInCycle = TimeSpan.FromMilliseconds(timeInCycleMs);

            if (timeInCycle < OPEN_DURATION)
            {
                CurrentStatus = HangarStatus.Online;
                TimeToNextChange = OPEN_DURATION - timeInCycle;
            }
            else
            {
                CurrentStatus = HangarStatus.Offline;
                TimeToNextChange = CYCLE_DURATION - timeInCycle;
            }

            var threshold = thresholds.FirstOrDefault(t => timeInCycle >= t.Min && timeInCycle < t.Max);
            if (threshold != null)
                Array.Copy(threshold.Colors, _currentColors, LampCount);
            else
                Array.Fill(_currentColors, LampColor.Empty);

            Invalidate();
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler StatusChanged;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int spacing = 4;
            int diameter = Math.Min((ClientSize.Width - spacing * (LampCount + 1)) / LampCount,
                                    ClientSize.Height - spacing * 2);
            int y = (ClientSize.Height - diameter) / 2;

            for (int i = 0; i < LampCount; i++)
            {
                int x = spacing + i * (diameter + spacing);
                using (var brush = new SolidBrush(GetColor(_currentColors[i])))
                    e.Graphics.FillEllipse(brush, x, y, diameter, diameter);
            }
        }

        private Color GetColor(LampColor lamp)
        {
            switch (lamp)
            {
                case LampColor.Green:
                    return ColorTranslator.FromHtml("#4CAF50");
                case LampColor.Red:
                    return ColorTranslator.FromHtml("#f44336");
                default:
                    return Color.Black; // empty
            }
        }

        public enum LampColor { Green, Red, Empty }
        public enum HangarStatus { Online, Offline }

        private class Threshold
        {
            public TimeSpan Min { get; }
            public TimeSpan Max { get; }
            public LampColor[] Colors { get; }
            public Threshold(TimeSpan min, TimeSpan max, LampColor[] colors)
            {
                Min = min; Max = max; Colors = colors;
            }
        }

        #region Lamp timer placeholder
        // Place here if a countdown under the active lamp is needed.
        #endregion
    }
}
