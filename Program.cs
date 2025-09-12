using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SCLOCUA
{
    static class Program
    {
        // ---- ЄДИНИЙ екземпляр оверлею у всьому додатку ----
        internal static ExecutiveHangarOverlay.HangarOverlayForm Overlay;
        internal static long HangarStartMs = -1;

        // EnsureOverlay: створює/реюзає оверлей, один раз резолвить старт
        internal static void EnsureOverlay()
        {
            if (HangarStartMs < 0)
                HangarStartMs = ExecutiveHangarOverlay.StartTimeProvider
                    .ResolveAsync().GetAwaiter().GetResult();

            if (Overlay == null || Overlay.IsDisposed)
                Overlay = new ExecutiveHangarOverlay.HangarOverlayForm(HangarStartMs);
        }
        // ----------------------------------------------------

        [STAThread]
        static void Main()
        {
            using (var mutex = new Mutex(true, "{EA6A248E-8F44-4C82-92F6-03F6A055E637}", out bool created))
            {
                if (!created) return;

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

                    // ---------- Глобальні гарячі клавіші -> керують одним Overlay ----------
                    ExecutiveHangarOverlay.HotkeyMessageFilter hotkey = null;
                    mainForm.HandleCreated += (s, e) =>
                    {
                        if (hotkey != null) return;

                        hotkey = new ExecutiveHangarOverlay.HotkeyMessageFilter(mainForm.Handle);

                        // F6 — show/hide
                        hotkey.OnToggleOverlay += () =>
                        {
                            Program.EnsureOverlay();
                            if (Program.Overlay.Visible) Program.Overlay.Hide();
                            else Program.Overlay.Show();
                        };

                        // Shift+F8 — click-through
                        hotkey.OnToggleClickThrough += () =>
                        {
                            Program.EnsureOverlay();
                            Program.Overlay.ToggleClickThrough();
                        };

                        // Ctrl+F8 — temporary drag
                        hotkey.OnBeginTempDrag += () =>
                        {
                            Program.EnsureOverlay();
                            Program.Overlay.BeginTemporaryDragMode();
                        };

                        // Ctrl+Shift+F7 / Shift+F7
                        hotkey.OnSetStartNow += () =>
                        {
                            Program.EnsureOverlay();
                            Program.Overlay.SetStartNow();
                        };
                        hotkey.OnPromptManualStart += () =>
                        {
                            Program.EnsureOverlay();
                            Program.Overlay.PromptManualStart();
                        };

                        // F9 / Shift+F9
                        hotkey.OnForceSync += async () =>
                        {
                            Program.EnsureOverlay();
                            await Program.Overlay.ForceSyncAsync();
                        };
                        hotkey.OnClearOverrideAndSync += async () =>
                        {
                            Program.EnsureOverlay();
                            await Program.Overlay.ClearOverrideAndSyncAsync();
                        };

                        // Scale
                        hotkey.OnScaleDown += () => { Program.EnsureOverlay(); Program.Overlay.ScaleDown(); };
                        hotkey.OnScaleUp += () => { Program.EnsureOverlay(); Program.Overlay.ScaleUp(); };
                        hotkey.OnScaleReset += () => { Program.EnsureOverlay(); Program.Overlay.ScaleReset(); };

                        // Opacity
                        hotkey.OnOpacityDown += () => { Program.EnsureOverlay(); Program.Overlay.OpacityDown(); };
                        hotkey.OnOpacityUp += () => { Program.EnsureOverlay(); Program.Overlay.OpacityUp(); };
                        hotkey.OnOpacityReset += () => { Program.EnsureOverlay(); Program.Overlay.OpacityReset(); };
                    };

                    Application.ApplicationExit += (s, e) =>
                    {
                        if (hotkey != null) hotkey.Dispose();
                        if (Overlay != null && !Overlay.IsDisposed) Overlay.Close();
                        notifyIcon.Visible = false;
                    };
                    // ----------------------------------------------------------------------

                    // Tray меню (всі дії через один Overlay)
                    ContextMenu contextMenu = new ContextMenu();

                    contextMenu.MenuItems.Add("Відкрити", (sender, e) =>
                    {
                        mainForm.Show();
                        mainForm.WindowState = FormWindowState.Normal;
                    });

                    contextMenu.MenuItems.Add("Оверлей (F6)", (sender, e) =>
                    {
                        Program.EnsureOverlay();
                        if (Program.Overlay.Visible) Program.Overlay.Hide();
                        else Program.Overlay.Show();
                    });

                    contextMenu.MenuItems.Add("Кліки крізь (Shift+F8)", (sender, e) =>
                    {
                        Program.EnsureOverlay();
                        Program.Overlay.ToggleClickThrough();
                    });

                    contextMenu.MenuItems.Add("Менший (Ctrl+–)", (s, e) => { Program.EnsureOverlay(); Program.Overlay.ScaleDown(); });
                    contextMenu.MenuItems.Add("Більший до 100% (Ctrl+=)", (s, e) => { Program.EnsureOverlay(); Program.Overlay.ScaleUp(); });
                    contextMenu.MenuItems.Add("Масштаб 100% (Ctrl+0)", (s, e) => { Program.EnsureOverlay(); Program.Overlay.ScaleReset(); });

                    contextMenu.MenuItems.Add("Прозоріше (Ctrl+Alt+–)", (s, e) => { Program.EnsureOverlay(); Program.Overlay.OpacityDown(); });
                    contextMenu.MenuItems.Add("Менш прозоре (Ctrl+Alt+=)", (s, e) => { Program.EnsureOverlay(); Program.Overlay.OpacityUp(); });
                    contextMenu.MenuItems.Add("Прозорість 0.92 (Ctrl+Alt+0)", (s, e) => { Program.EnsureOverlay(); Program.Overlay.OpacityReset(); });

                    MenuItem startupMenuItem = new MenuItem("Запускати при старті");
                    bool isStartupEnabled = IsStartupEnabled();
                    startupMenuItem.Checked = isStartupEnabled;
                    startupMenuItem.Click += (sender, e) =>
                    {
                        isStartupEnabled = !isStartupEnabled;
                        SetStartup(isStartupEnabled);
                        startupMenuItem.Checked = isStartupEnabled;
                    };
                    contextMenu.MenuItems.Add(startupMenuItem);

                    contextMenu.MenuItems.Add("Вихід", (sender, e) =>
                    {
                        notifyIcon.Visible = false;
                        Application.Exit();
                    });

                    notifyIcon.ContextMenu = contextMenu;

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
