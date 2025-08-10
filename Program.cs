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

                    // ---------- Overlay setup (lazy; global hotkeys) ----------
                    long startMs = ExecutiveHangarOverlay.StartTimeProvider
                        .ResolveAsync().GetAwaiter().GetResult();

                    ExecutiveHangarOverlay.HangarOverlayForm overlay = null;
                    Action ensureOverlay = () =>
                    {
                        if (overlay == null || overlay.IsDisposed)
                            overlay = new ExecutiveHangarOverlay.HangarOverlayForm(startMs);
                    };

                    ExecutiveHangarOverlay.HotkeyMessageFilter hotkey = null;
                    mainForm.HandleCreated += (s, e) =>
                    {
                        if (hotkey != null) return;

                        hotkey = new ExecutiveHangarOverlay.HotkeyMessageFilter(mainForm.Handle);

                        // F6 — show/hide
                        hotkey.OnToggleOverlay += () =>
                        {
                            ensureOverlay();
                            if (overlay.Visible) overlay.Hide();
                            else overlay.Show();
                        };

                        // F8 — toggle click-through
                        hotkey.OnToggleClickThrough += () =>
                        {
                            ensureOverlay();
                            overlay.ToggleClickThrough();
                        };

                        // Ctrl+F8 — temporary drag mode
                        hotkey.OnBeginTempDrag += () =>
                        {
                            ensureOverlay();
                            overlay.BeginTemporaryDragMode();
                        };

                        // F7 / Shift+F7
                        hotkey.OnSetStartNow += () =>
                        {
                            ensureOverlay();
                            overlay.SetStartNow();
                        };
                        hotkey.OnPromptManualStart += () =>
                        {
                            ensureOverlay();
                            overlay.PromptManualStart();
                        };

                        // F9 / Shift+F9
                        hotkey.OnForceSync += async () =>
                        {
                            ensureOverlay();
                            await overlay.ForceSyncAsync();
                        };
                        hotkey.OnClearOverrideAndSync += async () =>
                        {
                            ensureOverlay();
                            await overlay.ClearOverrideAndSyncAsync();
                        };

                        // Scale
                        hotkey.OnScaleDown += () =>
                        {
                            ensureOverlay();
                            overlay.ScaleDown();
                        };
                        hotkey.OnScaleUp += () =>
                        {
                            ensureOverlay();
                            overlay.ScaleUp();
                        };
                        hotkey.OnScaleReset += () =>
                        {
                            ensureOverlay();
                            overlay.ScaleReset();
                        };

                        // Opacity
                        hotkey.OnOpacityDown += () =>
                        {
                            ensureOverlay();
                            overlay.OpacityDown();
                        };
                        hotkey.OnOpacityUp += () =>
                        {
                            ensureOverlay();
                            overlay.OpacityUp();
                        };
                        hotkey.OnOpacityReset += () =>
                        {
                            ensureOverlay();
                            overlay.OpacityReset();
                        };
                    };

                    Application.ApplicationExit += (s, e) =>
                    {
                        if (hotkey != null) hotkey.Dispose();
                        notifyIcon.Visible = false;
                    };
                    // ---------------------------------------------------------

                    // Tray menu
                    ContextMenu contextMenu = new ContextMenu();

                    contextMenu.MenuItems.Add("Відкрити", (sender, e) =>
                    {
                        mainForm.Show();
                        mainForm.WindowState = FormWindowState.Normal;
                    });

                    contextMenu.MenuItems.Add("Оверлей (F6)", (sender, e) =>
                    {
                        ensureOverlay();
                        if (overlay.Visible) overlay.Hide();
                        else overlay.Show();
                    });

                    contextMenu.MenuItems.Add("Кліки крізь (F8)", (sender, e) =>
                    {
                        ensureOverlay();
                        overlay.ToggleClickThrough();
                    });

                    contextMenu.MenuItems.Add("Менший (Ctrl+–)", (s, e) => { ensureOverlay(); overlay.ScaleDown(); });
                    contextMenu.MenuItems.Add("Більший до 100% (Ctrl+=)", (s, e) => { ensureOverlay(); overlay.ScaleUp(); });
                    contextMenu.MenuItems.Add("Масштаб 100% (Ctrl+0)", (s, e) => { ensureOverlay(); overlay.ScaleReset(); });

                    contextMenu.MenuItems.Add("Прозоріше (Ctrl+Alt+–)", (s, e) => { ensureOverlay(); overlay.OpacityDown(); });
                    contextMenu.MenuItems.Add("Менш прозоре (Ctrl+Alt+=)", (s, e) => { ensureOverlay(); overlay.OpacityUp(); });
                    contextMenu.MenuItems.Add("Прозорість 0.92 (Ctrl+Alt+0)", (s, e) => { ensureOverlay(); overlay.OpacityReset(); });

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
