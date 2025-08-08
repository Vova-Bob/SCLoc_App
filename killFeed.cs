using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SCLOCUA
{
    public partial class killFeed : Form
    {
        private string logPath;
        private bool visibleState = true;
        private bool isDragging = false;
        private Point dragStartPoint;
        private SoundPlayer soundPlayer;
        private string soundFilePath;

        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;

        private const int HOTKEY_TOGGLE = 1;
        private const int HOTKEY_SCALE_UP = 2;
        private const int HOTKEY_SCALE_DOWN = 3;
        private const int HOTKEY_OPACITY_DEC = 4;
        private const int HOTKEY_OPACITY_INC = 5;

        private const int MOD_ALT = 0x0001;
        private const int MOD_CTRL = 0x0002;

        private const int VK_F9 = 0x78;
        private const int VK_UP = 0x26;
        private const int VK_DOWN = 0x28;
        private const int VK_LEFT = 0x25;
        private const int VK_RIGHT = 0x27;

        private const int WM_HOTKEY = 0x0312;
        private const int WM_DESTROY = 0x0002;

        private float scaleFactor = 1.0f;
        private const float scaleStep = 0.1f;
        private float opacityLevel = 0.6f; // Початкова прозорість фону
        private const float opacityStep = 0.1f;

        private CancellationTokenSource logCts;
        private Task logTask;

        public killFeed(string folderPath)
        {
            InitializeComponent();

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.BackColor = Color.Black;
            this.Opacity = opacityLevel; // Задаємо прозорість фону
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(100, 100);
            this.Size = new Size(400, 200);

            SetTopMost();

            this.MouseEnter += (EventHandler)((s, e) => Cursor.Hide());
            this.MouseLeave += (s, e) => Cursor.Show();

            this.MouseDown += Form_MouseDown;
            this.MouseMove += Form_MouseMove;
            this.MouseUp += Form_MouseUp;
            this.FormClosing += killFeed_FormClosing;

            logPath = Path.Combine(folderPath, "game.log");

            logCts = new CancellationTokenSource();
            logTask = ReadLogAsync(logCts.Token);

            soundFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "killSound.wav");
            if (File.Exists(soundFilePath))
                soundPlayer = new SoundPlayer(soundFilePath);
            else
                MessageBox.Show($"Файл звуку не знайдено: {soundFilePath}", "Помилка звуку", MessageBoxButtons.OK, MessageBoxIcon.Error);

            InitializeTray();
            RegisterGlobalHotKeys();
        }

        private void InitializeTray()
        {
            // Створення контекстного меню для іконки в треї
            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Відкрити", ShowOverlay);
            trayMenu.MenuItems.Add("Вихід", ExitApplication);

            // Створення та налаштування іконки в треї
            trayIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Information, // Ви можете встановити власну іконку
                ContextMenu = trayMenu,
                Visible = true,
                Text = "SCLOCUA Kill Feed"
            };

            // Додавання події для кліку на іконку
            trayIcon.DoubleClick += (sender, e) => ShowOverlay(sender, e);
        }

        // Реєстрація гарячих клавіш на рівні системи
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        protected override bool ShowWithoutActivation => true;

        private void SetTopMost()
        {
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private void RegisterGlobalHotKeys()
        {
            if (RegisterHotKey(this.Handle, HOTKEY_TOGGLE, MOD_CTRL | MOD_ALT, VK_F9) == 0 ||
                RegisterHotKey(this.Handle, HOTKEY_SCALE_UP, MOD_ALT, VK_UP) == 0 ||
                RegisterHotKey(this.Handle, HOTKEY_SCALE_DOWN, MOD_ALT, VK_DOWN) == 0 ||
                RegisterHotKey(this.Handle, HOTKEY_OPACITY_DEC, MOD_ALT, VK_LEFT) == 0 ||
                RegisterHotKey(this.Handle, HOTKEY_OPACITY_INC, MOD_ALT, VK_RIGHT) == 0)
            {
                MessageBox.Show("Не вдалося зареєструвати одну з гарячих клавіш", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                switch (m.WParam.ToInt32())
                {
                    case HOTKEY_TOGGLE:
                        ToggleVisibility();
                        break;
                    case HOTKEY_SCALE_UP:
                        scaleFactor = Math.Min(2.0f, scaleFactor + scaleStep);
                        UpdateFontAndWindowSize();
                        SetTopMost();
                        break;
                    case HOTKEY_SCALE_DOWN:
                        scaleFactor = Math.Max(0.5f, scaleFactor - scaleStep);
                        UpdateFontAndWindowSize();
                        SetTopMost();
                        break;
                    case HOTKEY_OPACITY_DEC:
                        opacityLevel = Math.Max(0.1f, opacityLevel - opacityStep);
                        this.Opacity = opacityLevel;
                        SetTopMost();
                        break;
                    case HOTKEY_OPACITY_INC:
                        opacityLevel = Math.Min(1.0f, opacityLevel + opacityStep);
                        this.Opacity = opacityLevel;
                        SetTopMost();
                        break;
                }
            }
            else if (m.Msg == WM_DESTROY)
            {
                // Блокування можливого залишку гарячих клавіш при закритті вікна
                UnregisterHotKey(this.Handle, HOTKEY_TOGGLE);
                UnregisterHotKey(this.Handle, HOTKEY_SCALE_UP);
                UnregisterHotKey(this.Handle, HOTKEY_SCALE_DOWN);
                UnregisterHotKey(this.Handle, HOTKEY_OPACITY_DEC);
                UnregisterHotKey(this.Handle, HOTKEY_OPACITY_INC);
            }

            base.WndProc(ref m);
        }

        // Функція для обробки прихованого/показаного стану
        public void ToggleVisibility()
        {
            this.Invoke((Action)(() =>
            {
                visibleState = !visibleState;
                if (visibleState)
                {
                    this.Show();
                    this.BringToFront();
                    this.Enabled = true; // Дозволяємо взаємодію з вікном
                    SetTopMost();
                }
                else
                {
                    this.Hide();
                    this.Enabled = false; // Блокуємо взаємодію з вікном
                }
            }));
        }

        private void ShowOverlay(object sender, EventArgs e)
        {
            this.Visible = true;
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            SetTopMost();
        }

        private void ExitApplication(object sender, EventArgs e)
        {
            this.Close();
        }

        private async void killFeed_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (logCts != null)
            {
                logCts.Cancel();
                try
                {
                    if (logTask != null)
                        await logTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            UnregisterHotKey(this.Handle, HOTKEY_TOGGLE);
            UnregisterHotKey(this.Handle, HOTKEY_SCALE_UP);
            UnregisterHotKey(this.Handle, HOTKEY_SCALE_DOWN);
            UnregisterHotKey(this.Handle, HOTKEY_OPACITY_DEC);
            UnregisterHotKey(this.Handle, HOTKEY_OPACITY_INC);

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            soundPlayer?.Dispose();
            soundPlayer = null;
        }

        private async Task ReadLogAsync(CancellationToken token)
        {
            try
            {
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var reader = new StreamReader(fs))
                    {
                        fs.Seek(0, SeekOrigin.End);

                        while (!token.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync();
                            if (line != null)
                            {
                                var result = ParseLogLine(line);
                                if (result != null)
                                {
                                    AppendLine(result);
                                    PlayKillSound();
                                }
                            }
                            else
                            {
                                await Task.Delay(100, token);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка читання логу: " + ex.Message);
            }
        }

        private string ParseLogLine(string line)
        {
            var timeMatch = Regex.Match(line, @"<(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z)>");
            if (!timeMatch.Success || !DateTime.TryParse(timeMatch.Groups[1].Value, out DateTime ts))
                return null;

            string timestamp = ts.ToString("HH:mm");

            var match = Regex.Match(line, @"'(.*?)'.*?killed by '(.*?)'.*?damage type '(.*?)'");
            if (!match.Success) return null;

            string victim = match.Groups[1].Value;
            string killer = match.Groups[2].Value;

            if (killer == "unknown" || !IsPlayer(victim) || !IsPlayer(killer)) return null;

            return (victim == killer)
                ? $"[{timestamp}] {victim} помер (самогубство)"
                : $"[{timestamp}] {killer} вбив {victim}";
        }

        private bool IsPlayer(string name) => !string.IsNullOrEmpty(name) && !name.Contains("NPC");

        private void AppendLine(string text)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => AppendLine(text)));
                return;
            }

            int lineHeight = (int)(20 * scaleFactor);

            foreach (Control c in this.Controls)
                if (c is Label lbl) lbl.Top -= lineHeight;

            var newLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.White, // Завжди яскраво білий текст
                BackColor = Color.Transparent,
                Text = text,
                Font = new Font("Consolas", 10 * scaleFactor),
                Left = 5,
                Top = this.ClientSize.Height - lineHeight
            };

            this.Controls.Add(newLabel);
            newLabel.MouseEnter += (s, e) => Cursor.Hide();
            newLabel.MouseLeave += (s, e) => Cursor.Show();

            var toRemove = new System.Collections.Generic.List<Label>();
            foreach (Control control in this.Controls)
            {
                var lbl = control as Label;
                if (lbl != null && lbl.Bottom < 0)
                {
                    toRemove.Add(lbl);
                }
            }

            foreach (var lbl in toRemove)
            {
                this.Controls.Remove(lbl);
                lbl.Dispose();
            }
        }

        private void PlayKillSound()
        {
            try
            {
                if (soundPlayer != null && visibleState)
                    soundPlayer.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка відтворення звуку: " + ex.Message);
            }
        }

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Control.ModifierKeys.HasFlag(Keys.Alt) && Control.ModifierKeys.HasFlag(Keys.Shift))
            {
                isDragging = true;
                dragStartPoint = e.Location;
            }
        }

        private void Form_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                this.Location = new Point(this.Left + (e.X - dragStartPoint.X), this.Top + (e.Y - dragStartPoint.Y));
            }
        }

        private void Form_MouseUp(object sender, MouseEventArgs e) => isDragging = false;

        private void UpdateFontAndWindowSize()
        {
            this.Width = (int)(400 * scaleFactor);
            this.Height = (int)(200 * scaleFactor);

            int lineHeight = (int)(20 * scaleFactor);
            int y = this.ClientSize.Height - lineHeight;

            for (int i = this.Controls.Count - 1; i >= 0; i--)
            {
                Control c = this.Controls[i];
                Label lbl = c as Label;
                if (lbl != null)
                {
                    lbl.Font = new Font("Consolas", 10 * scaleFactor);
                    lbl.Top = y;
                    lbl.Left = 5;
                    y -= lineHeight;
                }
            }
        }
    }
}
