using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace SCLOCUA.UI
{
    /// <summary>
    /// Applies minor runtime tweaks to improve UI clarity on high DPI displays.
    /// </summary>
    internal static class UiFix
    {
        /// <summary>
        /// Adjusts the supplied form and its controls.
        /// </summary>
        public static void Apply(Form form)
        {
            if (form == null) return;

            // Scale with monitor DPI and avoid stretched background images.
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.BackgroundImageLayout = ImageLayout.Zoom;

            EnableDoubleBuffer(form);
            ProcessControls(form.Controls);
        }

        // Recursively process all controls.
        private static void ProcessControls(Control.ControlCollection controls)
        {
            foreach (Control ctrl in controls)
            {
                if (ctrl is Label lbl)
                {
                    // Use GDI+ rendering and transparent backgrounds for labels.
                    lbl.UseCompatibleTextRendering = false;
                    lbl.BackColor = Color.Transparent;
                }
                else if (ctrl is Button btn)
                {
                    // System style buttons have better contrast.
                    btn.FlatStyle = FlatStyle.System;
                }

                EnableDoubleBuffer(ctrl);

                if (ctrl.HasChildren)
                    ProcessControls(ctrl.Controls);
            }
        }

        // Enable DoubleBuffered via reflection for smoother redraw.
        private static void EnableDoubleBuffer(Control control)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(control, true, null);
        }
    }
}
