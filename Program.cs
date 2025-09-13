using System;
using System.Threading;
using System.Threading.Tasks; // async/await support
using System.Windows.Forms;
using Microsoft.Win32;

namespace SCLOCUA
{
    static class Program
    {
        // ---- ЄДИНИЙ екземпляр оверлею у всьому додатку ----
        internal static ExecutiveHangarOverlay.HangarOverlayForm Overlay;
        internal static long HangarStartMs = -1;
        private static readonly SemaphoreSlim _overlayGate = new SemaphoreSlim(1, 1); // serialize overlay creation

        // EnsureOverlayAsync: create/reuse overlay and resolve start time once without blocking UI
        internal static async Task EnsureOverlayAsync()
        {
            if (Overlay != null && !Overlay.IsDisposed && HangarStartMs >= 0) return;

            await _overlayGate.WaitAsync();
            try
            {
                if (HangarStartMs < 0)
                    HangarStartMs = await ExecutiveHangarOverlay.StartTimeProvider
                        .ResolveAsync().ConfigureAwait(true);

                if (Overlay == null || Overlay.IsDisposed)
                    Overlay = new ExecutiveHangarOverlay.HangarOverlayForm(HangarStartMs);
            }
            finally
            {
                _overlayGate.Release();
            }
        }

        // Helper to safely operate on overlay from any thread
        internal static async Task TryWithOverlayAsync(Func<ExecutiveHangarOverlay.HangarOverlayForm, Task> action)
        {
            await EnsureOverlayAsync();
            var ov = Overlay;
            if (ov == null || ov.IsDisposed) return;

            if (ov.InvokeRequired)
            {
                var tcs = new TaskCompletionSource<bool>();
                ov.BeginInvoke(new Action(async () =>
                {
                    try { await action(ov); tcs.SetResult(true); }
                    catch (Exception ex) { tcs.SetException(ex); }
                }));
                await tcs.Task.ConfigureAwait(true);
            }
            else
            {
                await action(ov);
            }
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

                // global exception handlers to improve stability
                Application.ThreadException += (s, e) =>
                    MessageBox.Show(e.Exception.Message, "Unhandled UI exception",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    MessageBox.Show((e.ExceptionObject as Exception)?.Message ?? e.ExceptionObject.ToString(),
                        "Unhandled exception", MessageBoxButtons.OK, MessageBoxIcon.Error);

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
                        hotkey.OnToggleOverlay += async () =>
                        {
                            await Program.TryWithOverlayAsync(o =>
                            {
                                if (o.Visible) o.Hide(); else o.Show();
                                return Task.CompletedTask;
                            });
                        };

                        // Shift+F8 — click-through
                        hotkey.OnToggleClickThrough += async () =>
                        {
                            await Program.TryWithOverlayAsync(o =>
                            {
                                o.ToggleClickThrough();
                                return Task.CompletedTask;
                            });
                        };

                        // Ctrl+F8 — temporary drag
                        hotkey.OnBeginTempDrag += async () =>
                        {
                            await Program.TryWithOverlayAsync(o =>
                            {
                                o.BeginTemporaryDragMode();
                                return Task.CompletedTask;
                            });
                        };

                        // Ctrl+Shift+F7 / Shift+F7
                        hotkey.OnSetStartNow += async () =>
                        {
                            await Program.TryWithOverlayAsync(o =>
                            {
                                o.SetStartNow();
                                return Task.CompletedTask;
                            });
                        };
                        hotkey.OnPromptManualStart += async () =>
                        {
                            await Program.TryWithOverlayAsync(o =>
                            {
                                o.PromptManualStart();
                                return Task.CompletedTask;
                            });
                        };

                        // F9 / Shift+F9
                        hotkey.OnForceSync += async () =>
                        {
                            await Program.TryWithOverlayAsync(o => o.ForceSyncAsync());
                        };
                        hotkey.OnClearOverrideAndSync += async () =>
                        {
                            await Program.TryWithOverlayAsync(o => o.ClearOverrideAndSyncAsync());
                        };

                        // Scale
                        hotkey.OnScaleDown += async () => { await Program.TryWithOverlayAsync(o => { o.ScaleDown(); return Task.CompletedTask; }); };
                        hotkey.OnScaleUp += async () => { await Program.TryWithOverlayAsync(o => { o.ScaleUp(); return Task.CompletedTask; }); };
                        hotkey.OnScaleReset += async () => { await Program.TryWithOverlayAsync(o => { o.ScaleReset(); return Task.CompletedTask; }); };

                        // Opacity
                        hotkey.OnOpacityDown += async () => { await Program.TryWithOverlayAsync(o => { o.OpacityDown(); return Task.CompletedTask; }); };
                        hotkey.OnOpacityUp += async () => { await Program.TryWithOverlayAsync(o => { o.OpacityUp(); return Task.CompletedTask; }); };
                        hotkey.OnOpacityReset += async () => { await Program.TryWithOverlayAsync(o => { o.OpacityReset(); return Task.CompletedTask; }); };
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

                    contextMenu.MenuItems.Add("Оверлей (F6)", async (sender, e) =>
                    {
                        await Program.TryWithOverlayAsync(o =>
                        {
                            if (o.Visible) o.Hide(); else o.Show();
                            return Task.CompletedTask;
                        });
                    });

                    contextMenu.MenuItems.Add("Кліки крізь (Shift+F8)", async (sender, e) =>
                    {
                        await Program.TryWithOverlayAsync(o =>
                        {
                            o.ToggleClickThrough();
                            return Task.CompletedTask;
                        });
                    });

                    contextMenu.MenuItems.Add("Менший (Ctrl+–)", async (s, e) => { await Program.TryWithOverlayAsync(o => { o.ScaleDown(); return Task.CompletedTask; }); });
                    contextMenu.MenuItems.Add("Більший до 100% (Ctrl+=)", async (s, e) => { await Program.TryWithOverlayAsync(o => { o.ScaleUp(); return Task.CompletedTask; }); });
                    contextMenu.MenuItems.Add("Масштаб 100% (Ctrl+0)", async (s, e) => { await Program.TryWithOverlayAsync(o => { o.ScaleReset(); return Task.CompletedTask; }); });

                    contextMenu.MenuItems.Add("Прозоріше (Ctrl+Alt+–)", async (s, e) => { await Program.TryWithOverlayAsync(o => { o.OpacityDown(); return Task.CompletedTask; }); });
                    contextMenu.MenuItems.Add("Менш прозоре (Ctrl+Alt+=)", async (s, e) => { await Program.TryWithOverlayAsync(o => { o.OpacityUp(); return Task.CompletedTask; }); });
                    contextMenu.MenuItems.Add("Прозорість 0.92 (Ctrl+Alt+0)", async (s, e) => { await Program.TryWithOverlayAsync(o => { o.OpacityReset(); return Task.CompletedTask; }); });

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
                        // close main form; ApplicationExit will handle cleanup
                        mainForm.Close();
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
