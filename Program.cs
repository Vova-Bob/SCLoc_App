using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using ExecutiveHangarOverlay; // HangarOverlayForm, HotkeyMessageFilter
using ApplicationConfiguration = SCLOCUA.Manual.ApplicationConfiguration;

namespace SCLOCUA
{
    static class Program
    {
        // Єдиний mutex щоб не запускати декілька копій
        private const string AppMutex = "{EA6A248E-8F44-4C82-92F6-03F6A055E637}";

        [STAThread]
        static void Main()
        {
            using (var mutex = new Mutex(initiallyOwned: true, name: AppMutex, createdNew: out bool created))
            {
                if (!created) return;

                ApplicationConfiguration.Initialize();

                var mainForm = new Form1();

                // Трей-іконка живе стільки, скільки триває Run(mainForm)
                using (var notifyIcon = new NotifyIcon())
                {
                    notifyIcon.Icon = mainForm.Icon;
                    notifyIcon.Text = "Українізатор Star Citizen";

                    // Подвійний клік по трей-іконці — показати головне вікно
                    notifyIcon.DoubleClick += (s, e) =>
                    {
                        ShowMainWindow(mainForm);
                    };

                    // ---------- EX-Hangar Overlay (ліниве створення + глобальні хоткеї) ----------
                    long startMs = StartTimeProvider.ResolveAsync().GetAwaiter().GetResult();

                    HangarOverlayForm overlay = null;
                    Action ensureOverlay = () =>
                    {
                        if (overlay == null || overlay.IsDisposed)
                            overlay = new HangarOverlayForm(startMs);
                    };

                    HotkeyMessageFilter hotkey = null;

                    // Реєструємо глобальні хоткеї коли у форми з’явився Handle
                    mainForm.HandleCreated += (s, e) =>
                    {
                        if (hotkey != null) return;

                        hotkey = new HotkeyMessageFilter(mainForm.Handle);

                        // F6 — показ/приховати оверлей
                        hotkey.OnToggleOverlay += () => SafeInvoke(() =>
                        {
                            ensureOverlay();
                            if (overlay.Visible) overlay.Hide(); else overlay.Show();
                        });

                        // F8 — кліки «крізь» вікно
                        hotkey.OnToggleClickThrough += () => SafeInvoke(() =>
                        {
                            ensureOverlay();
                            overlay.ToggleClickThrough();
                        });

                        // Ctrl+F8 — тимчасовий режим перетягування (коли кліки «крізь» увімкнені)
                        hotkey.OnBeginTempDrag += () => SafeInvoke(() =>
                        {
                            ensureOverlay();
                            overlay.BeginTemporaryDragMode();
                        });

                        // F7 / Shift+F7 — локальне встановлення старту
                        hotkey.OnSetStartNow += () => SafeInvoke(() =>
                        {
                            ensureOverlay();
                            overlay.SetStartNow();
                        });
                        hotkey.OnPromptManualStart += () => SafeInvoke(() =>
                        {
                            ensureOverlay();
                            overlay.PromptManualStart();
                        });

                        // F9 / Shift+F9 — синхронізація з URL
                        hotkey.OnForceSync += async () => await SafeInvokeAsync(async () =>
                        {
                            ensureOverlay();
                            await overlay.ForceSyncAsync();
                        });
                        hotkey.OnClearOverrideAndSync += async () => await SafeInvokeAsync(async () =>
                        {
                            ensureOverlay();
                            await overlay.ClearOverrideAndSyncAsync();
                        });

                        // Масштаб
                        hotkey.OnScaleDown += () => SafeInvoke(() => { ensureOverlay(); overlay.ScaleDown(); });
                        hotkey.OnScaleUp += () => SafeInvoke(() => { ensureOverlay(); overlay.ScaleUp(); });
                        hotkey.OnScaleReset += () => SafeInvoke(() => { ensureOverlay(); overlay.ScaleReset(); });

                        // Прозорість
                        hotkey.OnOpacityDown += () => SafeInvoke(() => { ensureOverlay(); overlay.OpacityDown(); });
                        hotkey.OnOpacityUp += () => SafeInvoke(() => { ensureOverlay(); overlay.OpacityUp(); });
                        hotkey.OnOpacityReset += () => SafeInvoke(() => { ensureOverlay(); overlay.OpacityReset(); });
                    };

                    // Акуратно звільняємо ресурси на виході
                    Application.ApplicationExit += (s, e) =>
                    {
                        try
                        {
                            hotkey?.Dispose();
                            if (overlay != null && !overlay.IsDisposed) overlay.Dispose();
                            notifyIcon.Visible = false;
                        }
                        catch { /* ігноруємо при завершенні процесу */ }
                    };
                    // ---------------------------------------------------------------------------

                    // Меню в треї
                    var menu = new ContextMenuStrip();

                    menu.Items.Add(new ToolStripMenuItem("Відкрити", null, (_, __) => ShowMainWindow(mainForm)));

                    menu.Items.Add(new ToolStripMenuItem("EX-Hangar (F6)", null, (_, __) =>
                    {
                        ensureOverlay();
                        if (overlay.Visible) overlay.Hide(); else overlay.Show();
                    }));

                    menu.Items.Add(new ToolStripMenuItem("Кліки «крізь» (F8)", null, (_, __) =>
                    {
                        ensureOverlay();
                        overlay.ToggleClickThrough();
                    }));

                    // Масштаб
                    menu.Items.Add(new ToolStripMenuItem("Менший (Ctrl+–)", null, (_, __) => { ensureOverlay(); overlay.ScaleDown(); }));
                    menu.Items.Add(new ToolStripMenuItem("Більший до 100% (Ctrl+=)", null, (_, __) => { ensureOverlay(); overlay.ScaleUp(); }));
                    menu.Items.Add(new ToolStripMenuItem("Масштаб 100% (Ctrl+0)", null, (_, __) => { ensureOverlay(); overlay.ScaleReset(); }));

                    // Прозорість
                    menu.Items.Add(new ToolStripMenuItem("Прозоріше (Ctrl+Alt+–)", null, (_, __) => { ensureOverlay(); overlay.OpacityDown(); }));
                    menu.Items.Add(new ToolStripMenuItem("Менш прозоре (Ctrl+Alt+=)", null, (_, __) => { ensureOverlay(); overlay.OpacityUp(); }));
                    menu.Items.Add(new ToolStripMenuItem("Прозорість 0.92 (Ctrl+Alt+0)", null, (_, __) => { ensureOverlay(); overlay.OpacityReset(); }));

                    // Перевірка оновлень вручну
                    menu.Items.Add(new ToolStripMenuItem("Перевірити оновлення…", null, async (_, __) =>
                    {
                        await UpdateChecker.CheckOnceAsync(force: true);
                    }));

                    // Автозапуск
                    var startupItem = new ToolStripMenuItem("Запускати при старті");
                    bool startupEnabled = IsStartupEnabled();
                    startupItem.Checked = startupEnabled;
                    startupItem.Click += (_, __) =>
                    {
                        startupEnabled = !startupEnabled;
                        SetStartup(startupEnabled);
                        startupItem.Checked = startupEnabled;
                    };
                    menu.Items.Add(startupItem);

                    // Вихід
                    menu.Items.Add(new ToolStripMenuItem("Вихід", null, (_, __) =>
                    {
                        notifyIcon.Visible = false;
                        Application.Exit();
                    }));

                    notifyIcon.ContextMenuStrip = menu;

                    // Мінімізовано — до трею
                    mainForm.Resize += (s, e) =>
                    {
                        if (mainForm.WindowState == FormWindowState.Minimized)
                        {
                            mainForm.Hide();
                            notifyIcon.Visible = true;
                        }
                    };

                    // Одноразова перевірка оновлення програми на старті (fire-and-forget)
                    _ = UpdateChecker.CheckOnceAsync();

                    // Запуск головної форми
                    Application.Run(mainForm);
                }

                mutex.ReleaseMutex();
            }
        }

        // Показати головне вікно
        private static void ShowMainWindow(Form1 mainForm)
        {
            try
            {
                mainForm.Show();
                mainForm.WindowState = FormWindowState.Normal;
                mainForm.Activate();
            }
            catch { /* без паніки */ }
        }

        // ---- Обгортки для безпечних викликів з хоткеїв ----
        private static void SafeInvoke(Action action)
        {
            try { action(); } catch { /* ховаємо помилку від глобального хоткея */ }
        }

        private static async Task SafeInvokeAsync(Func<Task> action)
        {
            try { await action(); } catch { /* ігноруємо помилки у фонових задачах */ }
        }

        // ---- Автозапуск через реєстр ----
        static bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppConfig.RegistryKeyPath, writable: false))
            {
                return key != null && key.GetValue(AppConfig.AppName) != null;
            }
        }

        static void SetStartup(bool enable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppConfig.RegistryKeyPath, writable: true))
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
