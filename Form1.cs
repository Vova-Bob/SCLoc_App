using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Drawing;

namespace SCLOCUA
{
    public partial class Form1 : Form
    {
        // ---- Константи ----
        private const string UserCfgFileName = "user.cfg";
        private const string GlobalIniFileName = "global.ini";
        private const string LocalizationPath = "Data/Localization/korean_(south_korea)";
        private const string GithubGistUrlPattern = @"https://gist.github.com/\w+/\w+";
        // Список релізів перекладу (офіційні релізи + пререлізи)
        private const string GithubReleasesApiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/releases";

        // ---- Поля ----
        private WikiForm wikiForm = null;
        private readonly HttpClient httpClient;
        private readonly ToolTip toolTip = new ToolTip();
        private string selectedFolderPath = "";
        private AntiAFK _antiAFK;
        private killFeed overlayForm;

        public Form1()
        {
            InitializeComponent();

            // Спільний HttpClient з твого проекту
            httpClient = HttpClientService.Client;

            // UI властивості
            this.MaximizeBox = false;
            this.Icon = Properties.Resources.Icon;

            // Востаннє вибраний шлях до гри
            selectedFolderPath = Properties.Settings.Default.LastSelectedFolderPath;

            // Підказки
            toolTip.SetToolTip(pictureBox2, "Хочеш підтримати проєкт — тисни на кота! Кожна чашка кави наближає нас до завершення перекладу ❤️");
            toolTip.SetToolTip(buttonClearCache, "Очистити кеш шейдерів гри Star Citizen");
            toolTip.SetToolTip(buttonWiki, "Відкрити/закрити SC_Wiki");
            toolTip.SetToolTip(checkBox1, "Створити файл user.cfg, якщо його немає");
            toolTip.SetToolTip(button1, "Обрати: LIVE, EPTU, PTU, 4.0_PREVIEW");
            toolTip.SetToolTip(button2, "Встановити / Оновити файли локалізації");
            toolTip.SetToolTip(button3, "Видалити файли локалізації");

            // Анти-AFK
            _antiAFK = new AntiAFK();
            toolTip.SetToolTip(buttonAntiAFK, "Увімкнути/вимкнути Anti-AFK");
            buttonAntiAFK.Click += ButtonAntiAFK_Click;

            // Підписка на закриття форми
            this.FormClosing += Form1_FormClosing;

            InitializeUI();
            InitializeEvents();
        }

        // ---- Початковий стан UI ----
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
        }

        // ---- Прив'язка подій ----
        private void InitializeEvents()
        {
            button1.Click += SelectFolderButtonClick;
            button2.Click += UpdateLocalizationButtonClick;
            button3.Click += DeleteFilesButtonClick;

            AssignLink(linkLabel1, "https://docs.google.com/forms/d/e/1FAIpQLSdcNr1EdqUU6K63MVwKyDX7-twxDsCQDw8PfgmDSu_D1q9GRA/viewform");
            AssignLink(linkLabel2, "https://discord.gg/QVV2G2aKzf");
            AssignLink(linkLabel3, "https://github.com/Vova-Bob/SC_localization_UA");
            AssignLink(linkLabel4, "https://docs.google.com/forms/d/e/1FAIpQLSfWRo63MgESTmzr-Cr0kPVkfgHSxZW2eZelTtGsw0htoMe_6A/viewform");
            AssignLink(linkLabel5, "https://gitlab.com/valdeus/sc_localization_ua");
            AssignLink(pictureBox1, "https://usf.42web.io");
        }

        // ---- Вибір папки з грою ----
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

                    // Зберігаємо шлях
                    Properties.Settings.Default.LastSelectedFolderPath = selectedFolderPath;
                    Properties.Settings.Default.Save();
                }
            }
        }

        // ---- Встановити/оновити локалізацію ----
        private async void UpdateLocalizationButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath)) return;

            toolStripProgressBar1.Maximum = checkBox1.Checked ? 2 : 1;
            toolStripProgressBar1.Value = 0;
            button2.Enabled = false;

            try
            {
                // 1) user.cfg (за потреби)
                await EnsureUserCfgAsync();

                // 2) URL на global.ini для відповідної гілки гри
                string githubReleaseUrl = await GetGithubReleaseUrlAsync();

                if (!string.IsNullOrEmpty(githubReleaseUrl))
                {
                    string localFilePath = Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName);
                    await DownloadFileAsync(githubReleaseUrl, localFilePath);
                    toolStripProgressBar1.Value++;
                }

                // 3) Перевірка, чи всередині global.ini є посилання на gist (опційно)
                string gistContentPath = Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName);
                string gistContent = await ReadFileWithTimeoutAsync(gistContentPath);
                toolStripStatusLabel1.Text = DetectGithubGistUrl(gistContent) ? "Знайдено URL до GitHub Gist" : "Готово";
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

        // Створити user.cfg (якщо треба)
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

        // Повертає правильний URL для завантаження global.ini
        private async Task<string> GetGithubReleaseUrlAsync()
        {
            if (selectedFolderPath.Contains("LIVE"))
            {
                // Для LIVE — завжди останній стабільний реліз
                return "https://github.com/Vova-Bob/SC_localization_UA/releases/latest/download/global.ini";
            }

            if (selectedFolderPath.Contains("PTU") || selectedFolderPath.Contains("EPTU") || selectedFolderPath.Contains("4.0_PREVIEW"))
            {
                // Для тестових — беремо останній пререліз (якщо він є), інакше — останній реліз
                var tagName = await GetLatestReleaseTagAsync();
                return string.IsNullOrEmpty(tagName)
                    ? ""
                    : $"https://github.com/Vova-Bob/SC_localization_UA/releases/download/{tagName}/global.ini";
            }

            return string.Empty;
        }

        // Безпечне читання файлу з тайм-аутом
        private async Task<string> ReadFileWithTimeoutAsync(string path, int timeout = 5000)
        {
            var task = Task.Run(() => File.ReadAllText(path));
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Читання файлу перевищило час очікування.");
            return await task;
        }

        // Копіювання файлу з тайм-аутом
        private async Task CopyFileAsync(string sourceFileName, string destinationPath, int timeout = 5000)
        {
            var task = Task.Run(() => File.Copy(sourceFileName, destinationPath, true));
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Копіювання файлу перевищило час очікування.");
        }

        // Видалення файлу з тайм-аутом
        private async Task DeleteFileAsync(string path, int timeout = 5000)
        {
            var task = Task.Run(() => { if (File.Exists(path)) File.Delete(path); });
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Видалення файлу перевищило час очікування.");
        }

        // Отримати тег останнього пререлізу (якщо немає — взяти перший реліз)
        private async Task<string> GetLatestReleaseTagAsync()
        {
            try
            {
                // Забираємо список релізів (GitHub віддає у порядку від нового до старого)
                var resp = await httpClient.GetAsync(GithubReleasesApiUrl);
                resp.EnsureSuccessStatusCode();

                var json = await resp.Content.ReadAsStringAsync();
                var releases = JArray.Parse(json);

                if (releases.Count == 0)
                {
                    ShowErrorMessage("Релізи не знайдені.");
                    return string.Empty;
                }

                // Перший пререліз, якщо є
                foreach (var r in releases)
                {
                    if (r.Value<bool?>("prerelease") == true)
                        return r.Value<string>("tag_name");
                }

                // Інакше — перший стабільний реліз
                return releases[0].Value<string>("tag_name");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Помилка при отриманні релізів з GitHub: {ex.Message}");
                return string.Empty;
            }
        }

        // Пошук URL Gist у вмісті
        private bool DetectGithubGistUrl(string content)
        {
            var match = Regex.Match(content, GithubGistUrlPattern);
            return match.Success;
        }

        // Завантаження файлу за URL
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

        // ---- Видалити файли локалізації ----
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

        // Оновлення тексту з вибраною текою
        private void UpdateLabel()
        {
            label1.Text = selectedFolderPath;
        }

        // Стандартний показ помилок
        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // Прив’язати клік/линк до URL
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
                System.Diagnostics.Process.Start(url);
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Не вдалося відкрити посилання: {ex.Message}");
            }
        }

        // ---- Легка нотифікація про нові релізи перекладу (не апдейт програми!) ----
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

                // Показуємо лише один раз для нового тега
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
                // Не критично — просто показуємо помилку
                ShowErrorMessage($"Помилка при перевірці нових релізів: {ex.Message}");
            }
        }

        // ---- Події форми ----
        private void Form1_Load(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                checkBox1.Checked = !userCfgExists;
            }

            // one-shot, не блокуємо UI
            _ = CheckForNewReleasesAsync();
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {
            OpenUrl("https://send.monobank.ua/jar/44HXkQkorg");
        }

        // Кнопка Wiki
        private void buttonWiki_Click(object sender, EventArgs e)
        {
            if (wikiForm == null || wikiForm.IsDisposed)
            {
                wikiForm = new WikiForm();

                // Позиціонуємо Wiki під головним вікном
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

        // Очистити кеш шейдерів
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

        // Перемикач Anti-AFK
        private void ButtonAntiAFK_Click(object sender, EventArgs e)
        {
            _antiAFK.ToggleAntiAFK(toolStripStatusLabel1);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _antiAFK.Dispose();
        }

        // KillFeed (оверлей)
        private void buttonkillfeed_Click(object sender, EventArgs e)
        {
            if (overlayForm == null || overlayForm.IsDisposed)
            {
                overlayForm = new killFeed(selectedFolderPath);
                overlayForm.Show();
            }
            else
            {
                overlayForm.ToggleVisibility();
            }
        }
    }
}
