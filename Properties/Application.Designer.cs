using System.Drawing;
using System.Windows.Forms;

namespace SCLOCUA
{
    /// <summary>
    /// Specifies application-wide defaults for generated configuration.
    /// </summary>
    internal static partial class ApplicationConfiguration
    {
        // Use per-monitor DPI to avoid blurry scaling.
        public const HighDpiMode HighDpiMode = System.Windows.Forms.HighDpiMode.PerMonitorV2;
        // Render text with GDI+ for clarity.
        public const bool UseCompatibleTextRenderingDefault = false;
        // Enable themed controls.
        public const bool EnableVisualStyles = true;
        // Standardize default font across controls.
        public static readonly Font DefaultFont = new("Segoe UI", 9f);
    }
}
