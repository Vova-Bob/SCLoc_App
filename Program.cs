using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SCLOCUA
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            using (var mutex = new Mutex(true, "{EA6A248E-8F44-4C82-92F6-03F6A055E637}", out bool created))
            {
                if (!created)
                    return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Form1 mainForm = new Form1();
                using (NotifyIcon notifyIcon = new NotifyIcon())
                {
                    notifyIcon.Icon = mainForm.Icon;
                    notifyIcon.Text = "Українізатор Star Citizen";
                    notifyIcon.DoubleClick += (sender, e) =>
                    {
                        mainForm.Show();
                        mainForm.WindowState = FormWindowState.Normal;
                    };

                    ContextMenu contextMenu = new ContextMenu();
                    MenuItem startupMenuItem = new MenuItem("Запускати при старті");
                    bool isStartupEnabled = IsStartupEnabled();
                    startupMenuItem.Checked = isStartupEnabled;
                    startupMenuItem.Click += (sender, e) =>
                    {
                        isStartupEnabled = !isStartupEnabled;
                        SetStartup(isStartupEnabled);
                        startupMenuItem.Checked = isStartupEnabled;
                    };
                    contextMenu.MenuItems.Add("Відкрити", (sender, e) =>
                    {
                        mainForm.Show();
                        mainForm.WindowState = FormWindowState.Normal;
                    });
                    contextMenu.MenuItems.Add(startupMenuItem);
                    contextMenu.MenuItems.Add("Вихід", (sender, e) =>
                    {
                        notifyIcon.Visible = false;
                        Application.Exit();
                    });
                    notifyIcon.ContextMenu = contextMenu;

                    Application.ApplicationExit += (s, e) => notifyIcon.Visible = false;

                    mainForm.Resize += (sender, e) =>
                    {
                        if (mainForm.WindowState == FormWindowState.Minimized)
                        {
                            mainForm.Hide();
                            notifyIcon.Visible = true;
                        }
                    };

                    Application.Run(mainForm);
                }
                mutex.ReleaseMutex();
            }
        }

        static bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppConfig.RegistryKeyPath, true))
            {
                return key != null && key.GetValue(AppConfig.AppName) != null;
            }
        }

        static void SetStartup(bool enable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppConfig.RegistryKeyPath, true))
            {
                if (key == null) return;

                if (enable)
                    key.SetValue(AppConfig.AppName, Application.ExecutablePath);
                else
                    key.DeleteValue(AppConfig.AppName, false);
            }
        }
    }
}
