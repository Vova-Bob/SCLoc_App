using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Diagnostics; // ProcessStartInfo

namespace SCLOCUA
{
    public partial class Form1 : Form
    {
        // ---- Constants ----
        private const string UserCfgFileName = "user.cfg";
        private const string GlobalIniFileName = "global.ini";
        private const string LocalizationPath = "Data/Localization/korean_(south_korea)";
        private const string GithubGistUrlPattern = @"https://gist.github.com/\w+/\w+";
        private const string GithubReleasesApiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/releases";

        // ---- Fields ----
        private WikiForm wikiForm = null;
        private readonly HttpClient httpClient;
        private readonly ToolTip toolTip = new ToolTip();
        private string selectedFolderPath = "";
        private AntiAFK _antiAFK;
        private killFeed overlayForm; // overlay window

        public Form1()
        {
            InitializeComponent();
            UI.UiFix.Apply(this);

            // Shared HttpClient from your project
            httpClient = HttpClientService.Client;

            // UI props
            this.MaximizeBox = false;
            this.Icon = Properties.Resources.Icon;

            // Last selected game path
            selectedFolderPath = Properties.Settings.Default.LastSelectedFolderPath ?? string.Empty;

            // Tooltips
            toolTip.SetToolTip(pictureBox2, "Хочеш підтримати проєкт — тисни на кота! Кожна чашка кави наближає нас до завершення перекладу ❤️");
            toolTip.SetToolTip(buttonClearCache, "Очистити кеш шейдерів гри Star Citizen");
            toolTip.SetToolTip(buttonWiki, "Відкрити/закрити SC_Wiki");
            toolTip.SetToolTip(checkBox1, "Створити файл user.cfg, якщо його немає");
            toolTip.SetToolTip(button1, "Обрати: LIVE, EPTU, PTU, 4.0_PREVIEW");
            toolTip.SetToolTip(button2, "Встановити / Оновити файли локалізації");
            toolTip.SetToolTip(button3, "Видалити файли локалізації");
            toolTip.SetToolTip(buttonkillfeed, "Увімкнути/вимкнути KillFeed-оверлей");

            // Anti-AFK
            _antiAFK = new AntiAFK();
            toolTip.SetToolTip(buttonAntiAFK, "Увімкнути/вимкнути Anti-AFK");
            buttonAntiAFK.Click += ButtonAntiAFK_Click;

            // Form closing
            this.FormClosing += Form1_FormClosing;

            InitializeUI();
            InitializeEvents();
        }

        // ---- Initial UI state ----
        private void InitializeUI()
        {
            label1.Text = "Виберіть шлях до папки StarCitizen/LIVE або /PTU/EPTU";
            button2.Text = "Встановити локалізацію";
            button2.Enabled = false;

            if (!string.IsNullOrWhiteSpace(selectedFolderPath) && Directory.Exists(selectedFolderPath))
            {
                UpdateLabel();
                button2.Enabled = true;

                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                bool globalIniExists = File.Exists(Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName));

                button2.Text = (userCfgExists || globalIniExists) ? "Оновити локалізацію" : "Встановити локалізацію";
            }

            // Default visual state for KillFeed button
            UpdateKillFeedButtonUi(false);
        }

        // ---- Event bindings ----
        private void InitializeEvents()
        {
            button1.Click += SelectFolderButtonClick;
            button2.Click += UpdateLocalizationButtonClick;
            button3.Click += DeleteFilesButtonClick;

            AssignLink(linkLabel1, "https://docs.google.com/forms/d/e/1FAIpQLSdcNr1EdqUU6K63MVwKyDX7-twxDsCQDw8PfgmDSu_D1q9GRA/viewform");
            AssignLink(linkLabel2, "https://discord.gg/QVV2G2aKzf");
            AssignLink(linkLabel3, "https://github.com/Vova-Bob/SC_localization_UA");
            AssignLink(linkLabel4, "https://docs.google.com/forms/d/e/1FAIpQLSfWRo63MgESTmzr-C0kPVkfgHSxZW2eZelTtGsw0htoMe_6A/viewform");
            AssignLink(linkLabel5, "https://gitlab.com/valdeus/sc_localization_ua");
            AssignLink(pictureBox1, "https://usf.42web.io");
        }

        // ---- Select game folder ----
        private void SelectFolderButtonClick(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.SelectedPath = selectedFolderPath;
                var result = folderDialog.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
                {
                    selectedFolderPath = folderDialog.SelectedPath;
                    UpdateLabel();

                    bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                    checkBox1.Checked = !userCfgExists;

                    toolStripStatusLabel1.Text = "Перейдіть до встановлення локалізації";
                    button2.Enabled = true;

                    // Save path
                    Properties.Settings.Default.LastSelectedFolderPath = selectedFolderPath;
                    Properties.Settings.Default.Save();
                }
            }
        }

        // ---- Install/Update localization ----
        private async void UpdateLocalizationButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath) || !Directory.Exists(selectedFolderPath))
            {
                ShowErrorMessage("Спочатку оберіть теку гри.");
                return;
            }

            toolStripProgressBar1.Maximum = checkBox1.Checked ? 2 : 1;
            toolStripProgressBar1.Value = 0;
            button2.Enabled = false;

            try
            {
                // 1) user.cfg (optional)
                await EnsureUserCfgAsync();

                // 2) global.ini URL
                string githubReleaseUrl = await GetGithubReleaseUrlAsync();

                if (!string.IsNullOrEmpty(githubReleaseUrl))
                {
                    string localFilePath = Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName);
                    await DownloadFileAsync(githubReleaseUrl, localFilePath);
                    toolStripProgressBar1.Value++;
                }

                // 3) Optional: detect gist link inside global.ini
                string gistContentPath = Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName);
                if (File.Exists(gistContentPath))
                {
                    string gistContent = await ReadFileWithTimeoutAsync(gistContentPath);
                    toolStripStatusLabel1.Text = DetectGithubGistUrl(gistContent) ? "Знайдено URL до GitHub Gist" : "Готово";
                }
                else
                {
                    toolStripStatusLabel1.Text = "Готово";
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Помилка при встановленні локалізації: {ex.Message}");
            }
            finally
            {
                button2.Enabled = true;
            }
        }

        // Create user.cfg if needed
        private async Task EnsureUserCfgAsync()
        {
            bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
            if (!userCfgExists && checkBox1.Checked)
            {
                await CopyFileAsync(UserCfgFileName, Path.Combine(selectedFolderPath, UserCfgFileName));
                toolStripProgressBar1.Value++;
                checkBox1.Checked = false;
            }
        }

        // Resolve proper URL for global.ini
        private async Task<string> GetGithubReleaseUrlAsync()
        {
            if (selectedFolderPath.IndexOf("LIVE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // For LIVE — latest stable release
                return "https://github.com/Vova-Bob/SC_localization_UA/releases/latest/download/global.ini";
            }

            if (selectedFolderPath.IndexOf("PTU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                selectedFolderPath.IndexOf("EPTU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                selectedFolderPath.IndexOf("4.0_PREVIEW", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // For test branches — prefer latest prerelease, otherwise latest release
                var tagName = await GetLatestReleaseTagAsync();
                return string.IsNullOrEmpty(tagName)
                    ? ""
                    : $"https://github.com/Vova-Bob/SC_localization_UA/releases/download/{tagName}/global.ini";
            }

            return string.Empty;
        }

        // Safe file read with timeout
        private async Task<string> ReadFileWithTimeoutAsync(string path, int timeout = 5000)
        {
            var task = Task.Run(() => File.ReadAllText(path));
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Читання файлу перевищило час очікування.");
            return await task;
        }

        // Copy file with timeout
        private async Task CopyFileAsync(string sourceFileName, string destinationPath, int timeout = 5000)
        {
            var task = Task.Run(() => File.Copy(sourceFileName, destinationPath, true));
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Копіювання файлу перевищило час очікування.");
        }

        // Delete file with timeout
        private async Task DeleteFileAsync(string path, int timeout = 5000)
        {
            var task = Task.Run(() => { if (File.Exists(path)) File.Delete(path); });
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Видалення файлу перевищило час очікування.");
        }

        // Get latest prerelease tag, else first release tag
        private async Task<string> GetLatestReleaseTagAsync()
        {
            try
            {
                // GitHub returns newest first
                var resp = await httpClient.GetAsync(GithubReleasesApiUrl);
                resp.EnsureSuccessStatusCode();

                var json = await resp.Content.ReadAsStringAsync();
                var releases = JArray.Parse(json);

                if (releases.Count == 0)
                {
                    ShowErrorMessage("Релізи не знайдені.");
                    return string.Empty;
                }

                // First prerelease if any
                foreach (var r in releases)
                {
                    if (r.Value<bool?>("prerelease") == true)
                        return r.Value<string>("tag_name");
                }

                // Otherwise first stable release
                return releases[0].Value<string>("tag_name");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Помилка при отриманні релізів з GitHub: {ex.Message}");
                return string.Empty;
            }
        }

        // Gist URL detection
        private bool DetectGithubGistUrl(string content)
        {
            var match = Regex.Match(content ?? string.Empty, GithubGistUrlPattern);
            return match.Success;
        }

        // Download file to path
        private async Task DownloadFileAsync(string url, string filePath)
        {
            try
            {
                byte[] data = await httpClient.GetByteArrayAsync(url);
                string dir = Path.GetDirectoryName(filePath);

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    await fs.WriteAsync(data, 0, data.Length);
                }
            }
            catch (HttpRequestException ex)
            {
                ShowErrorMessage($"Помилка мережевого запиту: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Помилка при завантаженні файлу: {ex.Message}");
            }
        }

        // ---- Delete localization files ----
        private async void DeleteFilesButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath)) return;

            try
            {
                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                bool globalIniExists = File.Exists(Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName));

                if (userCfgExists || globalIniExists)
                {
                    if (userCfgExists)
                        await DeleteFileAsync(Path.Combine(selectedFolderPath, UserCfgFileName));

                    if (globalIniExists)
                        await DeleteFileAsync(Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName));

                    toolStripProgressBar1.Value = 0;
                    toolStripStatusLabel1.Text = "Файли видалено";
                    button2.Text = "Встановити локалізацію";
                    checkBox1.Checked = true;
                    button2.Enabled = true;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Файли вже видалені або відсутні";
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Помилка при видаленні файлів: {ex.Message}");
            }
        }

        // ---- Label with selected path ----
        private void UpdateLabel()
        {
            label1.Text = selectedFolderPath;
        }

        // ---- Error message helper ----
        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ---- Assign a clickable URL to a control ----
        private void AssignLink(Control control, string url)
        {
            control.Tag = url;
            if (control is LinkLabel ll) ll.LinkClicked += OpenLink;
            else control.Click += OpenLink;
        }

        private void OpenLink(object sender, EventArgs e)
        {
            if (sender is Control ctrl && ctrl.Tag is string url) OpenUrl(url);
        }

        private void OpenUrl(string url)
        {
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                ShowErrorMessage("Некоректне посилання.");
                return;
            }
            try
            {
                // UseShellExecute = true for URLs (works across .NETs)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Не вдалося відкрити посилання: {ex.Message}");
            }
        }

        // ---- Release notification (non-blocking) ----
        private async Task CheckForNewReleasesAsync()
        {
            try
            {
                var resp = await httpClient.GetAsync(GithubReleasesApiUrl);
                resp.EnsureSuccessStatusCode();

                string body = await resp.Content.ReadAsStringAsync();
                var releases = JArray.Parse(body);

                if (releases.Count == 0) return;

                var latestRelease = releases[0];
                string latestTag = latestRelease.Value<string>("tag_name") ?? "";
                string releaseName = latestRelease.Value<string>("name") ?? latestTag;
                string releaseBody = latestRelease.Value<string>("body") ?? "(без опису)";

                string savedTag = Properties.Settings.Default.LatestCheckedReleaseTag;

                // Show once per new tag
                if (!string.Equals(savedTag, latestTag, StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.LatestCheckedReleaseTag = latestTag;
                    Properties.Settings.Default.Save();

                    string msg =
                        $"Доступний новий реліз перекладу:\n" +
                        $"{releaseName}\n\n" +
                        $"Опис:\n{releaseBody}\n\n" +
                        $"Оновіть локалізацію через застосунок.";
                    MessageBox.Show(msg, "Новий реліз перекладу", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                // Non-critical
                ShowErrorMessage($"Помилка при перевірці нових релізів: {ex.Message}");
            }
        }

        // ---- Form events ----
        private void Form1_Load(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                checkBox1.Checked = !userCfgExists;
            }

            // One-shot, non-blocking
            _ = CheckForNewReleasesAsync();

            // Sync KillFeed button state on load (no dependency on killFeed API)
            UpdateKillFeedButtonUi(overlayForm != null && overlayForm.Visible && !overlayForm.IsDisposed);
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {
            OpenUrl("https://send.monobank.ua/jar/44HXkQkorg");
        }

        // Wiki toggle
        private void buttonWiki_Click(object sender, EventArgs e)
        {
            if (wikiForm == null || wikiForm.IsDisposed)
            {
                wikiForm = new WikiForm();

                // Place Wiki under main window
                int x = this.Location.X;
                int y = this.Location.Y + this.Height;

                wikiForm.StartPosition = FormStartPosition.Manual;
                wikiForm.Location = new Point(x, y);
                wikiForm.Show();
            }
            else
            {
                wikiForm.Close();
            }
        }

        // Clear shaders cache
        private void buttonClearCache_Click(object sender, EventArgs e)
        {
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string cachePath = Path.Combine(userProfile, @"AppData\Local\star citizen");

                if (!Directory.Exists(cachePath))
                {
                    MessageBox.Show("Папка кешу не знайдена.", "Інформація", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var result = MessageBox.Show(
                    $"Ви справді хочете очистити кеш шейдерів гри Star Citizen?\n{cachePath}",
                    "Очищення кешу",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    Directory.Delete(cachePath, true);
                    MessageBox.Show("Кеш успішно очищено!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Сталася помилка при очищенні кешу: {ex.Message}");
            }
        }

        // Anti-AFK toggle
        private void ButtonAntiAFK_Click(object sender, EventArgs e)
        {
            _antiAFK.ToggleAntiAFK(toolStripStatusLabel1);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _antiAFK.Dispose();
            // Close overlay if still open (avoid zombie handle)
            try { if (overlayForm != null && !overlayForm.IsDisposed) overlayForm.Close(); } catch { }
        }

        // ---- KillFeed (overlay) ----
        private void buttonkillfeed_Click(object sender, EventArgs e)
        {
            if (overlayForm == null || overlayForm.IsDisposed)
            {
                if (string.IsNullOrWhiteSpace(selectedFolderPath))
                {
                    ShowErrorMessage("Спочатку оберіть теку гри (LIVE/PTU/EPTU).");
                    return;
                }

                overlayForm = new killFeed(selectedFolderPath);

                // Keep reference fresh
                overlayForm.FormClosed += (_, __) =>
                {
                    overlayForm = null;
                    UpdateKillFeedButtonUi(false);
                };

                overlayForm.Show();
                overlayForm.BringToFront();
                UpdateKillFeedButtonUi(true);
            }
            else
            {
                // No dependency on killFeed API: toggle by Show/Hide directly
                if (overlayForm.Visible)
                {
                    overlayForm.Hide();
                    UpdateKillFeedButtonUi(false);
                }
                else
                {
                    overlayForm.Show();
                    overlayForm.BringToFront();
                    UpdateKillFeedButtonUi(true);
                }
            }
        }

        // Visual feedback on KillFeed button
        private void UpdateKillFeedButtonUi(bool on)
        {
            // Simple and readable UI feedback
            buttonkillfeed.Text = on ? "KillFeed: ON" : "KillFeed: OFF";
            buttonkillfeed.BackColor = on ? Color.FromArgb(40, 160, 90) : SystemColors.Control;
            buttonkillfeed.ForeColor = on ? Color.White : SystemColors.ControlText;
        }
    }
}
