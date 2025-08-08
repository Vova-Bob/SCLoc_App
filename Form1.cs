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
        private const string UserCfgFileName = "user.cfg";
        private const string GlobalIniFileName = "global.ini";
        private const string LocalizationPath = "Data/Localization/korean_(south_korea)";
        private const string GithubGistUrlPattern = @"https://gist.github.com/\w+/\w+";
        private const string GithubReleasesApiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/releases";  // API для отримання релізів
        private WikiForm wikiForm = null; // Оголошуємо змінну для форми
        private readonly HttpClient httpClient;
        private ToolTip toolTip = new ToolTip(); // Створення об'єкта ToolTip
        private string selectedFolderPath = "";
        private AntiAFK _antiAFK;
        private HangarTimerOverlay _hangarOverlay;

        public Form1()
        {
            InitializeComponent();

            httpClient = HttpClientService.Client;

            this.MaximizeBox = false;
            this.Icon = Properties.Resources.Icon;
            selectedFolderPath = Properties.Settings.Default.LastSelectedFolderPath;
            toolTip.SetToolTip(pictureBox2, "Хочеш підтримати проект - Жми кота! кожна чашка кави наближає нас до завершення перекладу!");
            toolTip.SetToolTip(buttonClearCache, "Очистити кеш шейдерів гри Star Citizen"); // Підказка для кнопки Clear Cache
            toolTip.SetToolTip(buttonWiki, "Відкрити/Закрити SC_Wiki"); // Підказка для кнопки Wiki
            toolTip.SetToolTip(checkBox1, "Файл конфігурації, якщо немає");
            toolTip.SetToolTip(button1, "LIVE, EPTU, PTU, 4.0_PREVIEW");
            toolTip.SetToolTip(button2, "Встановити / Оновити файли локалізації");
            toolTip.SetToolTip(button3, "Видалити файли локалізації");

            // Ініціалізація функції анти-AFK
            _antiAFK = new AntiAFK();
            toolTip.SetToolTip(buttonAntiAFK, "Увімкнути/Вимкнути Anti-AFK"); // Підказка для кнопки

            // Підключення обробника кліку кнопки
            buttonAntiAFK.Click += ButtonAntiAFK_Click;
            this.FormClosing += Form1_FormClosing;

            InitializeUI();
            InitializeEvents();
        }

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
                button2.Text = userCfgExists || globalIniExists ? "Оновити локалізацію" : "Встановити локалізацію";
            }
        }

        private void InitializeEvents()
        {
            button1.Click += SelectFolderButtonClick;
            button2.Click += UpdateLocalizationButtonClick;
            button3.Click += DeleteFilesButtonClick;
            buttonExHangar.Click += buttonExHangar_Click;

            AssignLink(linkLabel1, "https://docs.google.com/forms/d/e/1FAIpQLSdcNr1EdqUU6K63MVwKyDX7-twxDsCQDw8PfgmDSu_D1q9GRA/viewform");
            AssignLink(linkLabel2, "https://discord.gg/QVV2G2aKzf");
            AssignLink(linkLabel3, "https://github.com/Vova-Bob/SC_localization_UA");
            AssignLink(linkLabel4, "https://docs.google.com/forms/d/e/1FAIpQLSfWRo63MgESTmzr-Cr0kPVkfgHSxZW2eZelTtGsw0htoMe_6A/viewform");
            AssignLink(linkLabel5, "https://gitlab.com/valdeus/sc_localization_ua");
            AssignLink(pictureBox1, "https://usf.42web.io");
        }

        // Обробник натискання кнопки вибору папки
        private void SelectFolderButtonClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.SelectedPath = selectedFolderPath;
                DialogResult result = folderDialog.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
                {
                    selectedFolderPath = folderDialog.SelectedPath;
                    UpdateLabel();
                    bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                    checkBox1.Checked = !userCfgExists;

                    toolStripStatusLabel1.Text = "Перейдіть до встановлення локалізації";
                    button2.Enabled = true;

                    Properties.Settings.Default.LastSelectedFolderPath = selectedFolderPath;
                    Properties.Settings.Default.Save();
                }
            }
        }

        private async void UpdateLocalizationButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
                return;

            toolStripProgressBar1.Maximum = checkBox1.Checked ? 2 : 1;
            toolStripProgressBar1.Value = 0;
            button2.Enabled = false;

            try
            {
                await EnsureUserCfgAsync();
                string githubReleaseUrl = await GetGithubReleaseUrlAsync();

                if (!string.IsNullOrEmpty(githubReleaseUrl))
                {
                    string localFilePath = Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName);
                    await DownloadFileAsync(githubReleaseUrl, localFilePath);
                    toolStripProgressBar1.Value++;
                }

                string gistContent = await ReadFileWithTimeoutAsync(Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName));
                toolStripStatusLabel1.Text = DetectGithubGistUrl(gistContent) ? "Знайдено URL до Github gist" : "Готово";
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

        private async Task<string> GetGithubReleaseUrlAsync()
        {
            if (selectedFolderPath.Contains("LIVE"))
            {
                return "https://github.com/Vova-Bob/SC_localization_UA/releases/latest/download/global.ini";
            }
            if (selectedFolderPath.Contains("PTU") || selectedFolderPath.Contains("EPTU") || selectedFolderPath.Contains("4.0_PREVIEW"))
            {
                var tagName = await GetLatestReleaseTagAsync();
                return $"https://github.com/Vova-Bob/SC_localization_UA/releases/download/{tagName}/global.ini";
            }
            return string.Empty;
        }

        private async Task<string> ReadFileWithTimeoutAsync(string path, int timeout = 5000)
        {
            var task = Task.Run(() => File.ReadAllText(path));
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Читання файлу перевищило час очікування");
            return await task;
        }

        private async Task CopyFileAsync(string sourceFileName, string destinationPath, int timeout = 5000)
        {
            var task = Task.Run(() => File.Copy(sourceFileName, destinationPath, true));
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Копіювання файлу перевищило час очікування");
        }

        private async Task DeleteFileAsync(string path, int timeout = 5000)
        {
            var task = Task.Run(() => { if (File.Exists(path)) File.Delete(path); });
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Видалення файлу перевищило час очікування");
        }

        private async Task<string> GetLatestReleaseTagAsync()
        {
            try
            {
                // Оновлений запит для отримання лише пререлізів
                HttpResponseMessage response = await httpClient.GetAsync(GithubReleasesApiUrl + "?prerelease=true");
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();

                // Розбираємо отриману відповідь
                var releases = JArray.Parse(responseBody);

                // Якщо є хоча б один пререліз, беремо перший
                if (releases.Count > 0)
                {
                    var latestPreRelease = releases[0]; // Отримуємо перший пререліз
                    return latestPreRelease["tag_name"].ToString();
                }
                else
                {
                    ShowErrorMessage("Пререлізи не знайдено.");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Помилка при отриманні релізів з GitHub: {ex.Message}");
                return string.Empty;
            }
        }

        private bool DetectGithubGistUrl(string content)
        {
            string regexPattern = GithubGistUrlPattern;
            Match match = Regex.Match(content, regexPattern);
            return match.Success;
        }

        private async Task DownloadFileAsync(string url, string filePath)
        {
            try
            {
                byte[] fileData = await httpClient.GetByteArrayAsync(url);
                string directoryPath = Path.GetDirectoryName(filePath);

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    await fs.WriteAsync(fileData, 0, fileData.Length);
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

        private async void DeleteFilesButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
                return;

            try
            {
                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                bool globalIniExists = File.Exists(Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName));

                if (userCfgExists || globalIniExists)
                {
                    if (userCfgExists)
                    {
                        await DeleteFileAsync(Path.Combine(selectedFolderPath, UserCfgFileName));
                    }
                    if (globalIniExists)
                    {
                        await DeleteFileAsync(Path.Combine(selectedFolderPath, LocalizationPath, GlobalIniFileName));
                    }

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

        private void UpdateLabel()
        {
            label1.Text = selectedFolderPath;
        }

        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void AssignLink(Control control, string url)
        {
            control.Tag = url;
            if (control is LinkLabel link)
            {
                link.LinkClicked += OpenLink;
            }
            else
            {
                control.Click += OpenLink;
            }
        }

        private void OpenLink(object sender, EventArgs e)
        {
            if (sender is Control ctrl && ctrl.Tag is string url)
            {
                OpenUrl(url);
            }
        }

        private void OpenUrl(string url)
        {
            if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                System.Diagnostics.Process.Start(url);
            }
            else
            {
                ShowErrorMessage("Некоректне посилання");
            }
        }

        private async Task CheckForNewReleasesAsync()
        {
            try
            {
                // Отримуємо останні релізи (включаючи пререлізи)
                HttpResponseMessage response = await httpClient.GetAsync(GithubReleasesApiUrl);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
                var releases = JArray.Parse(responseBody);

                if (releases.Count > 0)
                {
                    var latestRelease = releases[0];
                    string latestTag = latestRelease["tag_name"].ToString();
                    string releaseName = latestRelease["name"]?.ToString();  // Отримуємо ім'я релізу
                    string releaseBody = latestRelease["body"]?.ToString();  // Отримуємо опис релізу

                    string savedTag = Properties.Settings.Default.LatestCheckedReleaseTag;

                    if (savedTag != latestTag)
                    {
                        // Зберігаємо новий тег
                        Properties.Settings.Default.LatestCheckedReleaseTag = latestTag;
                        Properties.Settings.Default.Save();

                        // Показуємо сповіщення з додатковою інформацією
                        string message = $"Доступний новий реліз: {releaseName}\n" +
                                         $"Опис релізу: {releaseBody}\n" +
                                         $"Оновіть переклад через додаток";

                        MessageBox.Show(message, "Новий реліз доступний", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Помилка при перевірці нових релізів: {ex.Message}");
            }
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                checkBox1.Checked = !userCfgExists;
            }

            // Створюємо екземпляр AppUpdater
            AppUpdater appUpdater = new AppUpdater();

            // Викликаємо обидва методи паралельно
            var updatesTask = appUpdater.CheckForUpdatesAsync(); // Перевірка оновлення додатка
            var releasesTask = CheckForNewReleasesAsync(); // Перевірка нових релізів

            // Чекаємо на завершення обох методів
            await Task.WhenAll(updatesTask, releasesTask);

            // Якщо потрібно додатково логувати чи показувати повідомлення
            Console.WriteLine("Перевірка оновлення додатка та релізів перекладу завершена.");
        }
        private void pictureBox2_Click(object sender, EventArgs e)
        {
            OpenUrl("https://send.monobank.ua/jar/44HXkQkorg");
        }

        // Обробник натискання кнопки Wiki
        private void buttonWiki_Click(object sender, EventArgs e)
        {
            if (wikiForm == null || wikiForm.IsDisposed)  // Якщо форма ще не відкрита або була закрита
            {
                wikiForm = new WikiForm();

                // Отримуємо координати верхнього лівого кута основної форми
                int x = this.Location.X;
                int y = this.Location.Y + this.Height; // Встановлюємо Y координату під основною формою

                // Встановлюємо позицію для WikiForm
                wikiForm.StartPosition = FormStartPosition.Manual; // Задаємо ручне розташування
                wikiForm.Location = new Point(x, y); // Встановлюємо координати форми

                wikiForm.Show();  // Відкриваємо нову форму
            }
            else
            {
                wikiForm.Close();  // Закриваємо існуючу форму
            }
        }

        // Обробник натискання кнопки Clear Cache
        private void buttonClearCache_Click(object sender, EventArgs e)
        {
            try
            {
                // Отримуємо шлях до папки кешу
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string cachePath = Path.Combine(userProfile, @"AppData\Local\star citizen");

                if (Directory.Exists(cachePath))
                {
                    // Запитуємо користувача про підтвердження
                    DialogResult result = MessageBox.Show(
                        $"Ви справді хочете очистити кеш шейдерів гри Star Citizen? {cachePath}",
                        "Очистка кешу",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.Yes)
                    {
                        // Очищаємо кеш
                        Directory.Delete(cachePath, true); // Видаляє всі файли і підкаталоги
                        MessageBox.Show("Кеш успішно очищено!", "Успіх", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    MessageBox.Show("Папка кешу не знайдена!", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                // Обробка помилок
                MessageBox.Show($"Сталася помилка при очищенні кешу: {ex.Message}", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        // Обробник натискання кнопки
        private void ButtonAntiAFK_Click(object sender, EventArgs e)
        {
            _antiAFK.ToggleAntiAFK(toolStripStatusLabel1);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _antiAFK.Dispose();
            _hangarOverlay?.Close();
            _hangarOverlay?.Dispose();
            _hangarOverlay = null;
        }

        private void buttonExHangar_Click(object sender, EventArgs e)
        {
            if (_hangarOverlay == null || _hangarOverlay.IsDisposed)
            {
                _hangarOverlay = new HangarTimerOverlay();
                _hangarOverlay.Show();
            }
            else
            {
                _hangarOverlay.ToggleVisibility();
            }
        }
        // Кнопка KillFeed
        private killFeed overlayForm;

        private void buttonkillfeed_Click(object sender, EventArgs e)
        {
            if (overlayForm == null || overlayForm.IsDisposed)
            {
                overlayForm = new killFeed(selectedFolderPath); // передаємо шлях
                overlayForm.Show();
            }
            else
            {
                overlayForm.ToggleVisibility();
            }
        }
    }
}