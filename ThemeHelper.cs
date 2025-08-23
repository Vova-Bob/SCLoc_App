using System.Drawing;
using System.Windows.Forms;

namespace SCLOCUA
{
    /// <summary>
    /// Applies unified font and rendering settings.
    /// </summary>
    internal static class ThemeHelper
    {
        public static void ApplyTheme(Form root)
        {
            if (root == null) return;

            // Base font and double buffering
            root.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // Enable double buffering via reflection
            var prop = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(root, true, null);

            ApplyToChildren(root.Controls, root.Font);
        }

        private static void ApplyToChildren(Control.ControlCollection controls, Font baseFont)
        {
            foreach (Control control in controls)
            {
                control.Font = baseFont;
                if (control is Label lbl) lbl.UseCompatibleTextRendering = false;
                if (control is Button btn) btn.UseCompatibleTextRendering = false;

                // Transparent controls inherit parent's color to avoid artifacts
                if (control.BackColor == Color.Transparent)
                    control.BackColor = control.Parent?.BackColor ?? SystemColors.Control;

                if (control.HasChildren)
                    ApplyToChildren(control.Controls, baseFont);
            }
        }
    }
}
