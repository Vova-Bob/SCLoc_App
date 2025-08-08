using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace SCLOCUA
{
    public class LampIndicator : Control
    {
        private Color _lampColor = Color.Black;
        private string _timerText = string.Empty;
        private bool _showTimer;

        public Color LampColor
        {
            get => _lampColor;
            set { _lampColor = value; Invalidate(); }
        }

        public string TimerText
        {
            get => _timerText;
            set { _timerText = value; Invalidate(); }
        }

        public bool ShowTimer
        {
            get => _showTimer;
            set { _showTimer = value; Invalidate(); }
        }

        public LampIndicator()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            ForeColor = Color.White;
            Size = new Size(40, 60);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int diameter = 40; // circle diameter
            int circleX = (Width - diameter) / 2;
            int circleY = 0;
            using (var brush = new SolidBrush(_lampColor))
            {
                g.FillEllipse(brush, circleX, circleY, diameter, diameter);
            }

            if (_showTimer && !string.IsNullOrEmpty(_timerText))
            {
                using (var format = new StringFormat { Alignment = StringAlignment.Center })
                using (var font = new Font("Consolas", 10, FontStyle.Bold))
                using (var brush = new SolidBrush(ForeColor))
                {
                    g.DrawString(_timerText, font, brush,
                                 new RectangleF(0, diameter, Width, Height - diameter), format);
                }
            }
        }
    }
}
