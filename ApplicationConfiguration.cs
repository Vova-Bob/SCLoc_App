using System.Windows.Forms;

namespace SCLOCUA.Manual
{
    internal static class ApplicationConfiguration
    {
        public static void Initialize()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
    }
}
