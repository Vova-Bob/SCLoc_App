using System;
using System.IO;
using System.Linq; // <— ДОДАНО для LINQ
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Collections.Generic; // <— ДОДАНО для Dictionary

namespace SCLOCUA
{
    public partial class Form1 : Form
    {
        private const string UserCfgFileName = "user.cfg";
        private const string GlobalIniFileName = "global.ini";
        private const string LocalizationPath = "Data/Localization/korean_(south_korea)";
        private const string GithubGistUrlPattern = @"https://gist.github.com/\w+/\w+";
        private const string GithubReleasesApiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/releases";

        private WikiForm wikiForm = null;                 // secondary form
        private readonly HttpClient httpClient;
        private ToolTip toolTip = new ToolTip();          // tooltips
        private string selectedFolderPath = "";
        private AntiAFK _antiAFK;

        private bool antiAfkEnabled = false;              // simple AntiAFK flag
        private killFeed overlayForm;                     // KillFeed overlay form

        // helper ensures overlay operations are safe and run on UI thread
        private void TryWithOverlay(Action<killFeed> action)
        {
            if (overlayForm == null || overlayForm.IsDisposed) return;
            if (overlayForm.InvokeRequired)
                overlayForm.BeginInvoke(new Action(() => action(overlayForm)));
            else
                action(overlayForm);
        }

        // ===== Перевірка кешу при старті =====
        private bool _cacheCheckedOnce = false;
        private const long TWO_GB = 3L * 1024 * 1024 * 1024;          // large dir threshold (UI shows 3 GB)
        private const long LATEST_OK_BYTES = 2L * 1024 * 1024 * 1024;      // OK size for latest cache (change as needed)

        public Form1()
        {
            InitializeComponent();

            httpClient = HttpClientService.Client;

            // --- Ensure sane defaults for HTTP (in case HttpClientService didn't set them) ---
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls13;
            }
            catch { /* ignore if not supported */ }

            if (!httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("SCLOCUA/1.0"))
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SCLOCUA");

            if (httpClient.Timeout < TimeSpan.FromSeconds(20))
                httpClient.Timeout = TimeSpan.FromSeconds(30);
            // -------------------------------------------------------------------------------

            this.MaximizeBox = false;
            this.Icon = Properties.Resources.Icon;
            selectedFolderPath = Properties.Settings.Default.LastSelectedFolderPath;

            // Tooltips
            toolTip.SetToolTip(pictureBox2, "Хочеш підтримати проект - Жми кота! кожна чашка кави наближає нас до завершення перекладу!");
            toolTip.SetToolTip(buttonClearCache, "Очистити кеш шейдерів гри Star Citizen");
            toolTip.SetToolTip(buttonWiki, "Відкрити/Закрити SC_Wiki");
            toolTip.SetToolTip(checkBox1, "Файл конфігурації, якщо немає");
            toolTip.SetToolTip(button1, "LIVE, EPTU, PTU, 4.0_PREVIEW");
            toolTip.SetToolTip(button2, "Встановити / Оновити файли локалізації");
            toolTip.SetToolTip(button3, "Видалити файли локалізації");

            // AntiAFK
            _antiAFK = new AntiAFK();
            toolTip.SetToolTip(buttonAntiAFK, "Увімкнути/Вимкнути Anti-AFK");
            buttonAntiAFK.Click += ButtonAntiAFK_Click;

            this.FormClosing += Form1_FormClosing;

            InitializeUI();
            InitializeEvents();
        }

        // === Simple HTTP helpers to normalize timeouts & retries ===

        // Get string with local CTS timeout; convert TaskCanceledException -> TimeoutException
        private async Task<string> GetStringWithTimeoutAsync(string url, int timeoutMs = 30000)
        {
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    var resp = await httpClient.GetAsync(url, cts.Token);
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync();
                }
                catch (TaskCanceledException ex) when (!cts.IsCancellationRequested)
                {
                    throw new TimeoutException("HTTP timeout", ex);
                }
            }
        }

        // Generic retry for transient HTTP/timeout errors
        private async Task<T> RetryAsync<T>(Func<Task<T>> op, int attempts = 3, int delayMs = 800)
        {
            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                try { return await op(); }
                catch (TimeoutException ex) { last = ex; }
                catch (HttpRequestException ex) { last = ex; }
                await Task.Delay(delayMs);
            }
            throw last ?? new Exception("Unknown error");
        }

        // --- Small DRY helper to paint a button + status bar consistently ---
        private void SetUi(Button btn, bool on, string onText, string offText, string statusPrefix)
        {
            btn.BackColor = on ? Color.LightGreen : Color.IndianRed;
            btn.Text = on ? onText : offText;

            toolStripStatusLabel1.Text = statusPrefix + (on ? "увімкнено" : "вимкнено");
            toolStripStatusLabel1.ForeColor = on ? Color.Green : Color.Maroon;
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

            AssignLink(linkLabel1, "https://docs.google.com/forms/d/e/1FAIpQLSdcNr1EdqUU6K63MVwKyDX7-twxDsCQDw8PfgмDSu_D1q9GRA/viewform");
            AssignLink(linkLabel2, "https://discord.gg/TEwrDands4");
            AssignLink(linkLabel3, "https://github.com/Vova-Bob/SC_localization_UA");
            AssignLink(linkLabel4, "https://docs.google.com/forms/d/e/1FAIpQLSfWRo63MgESTmzr-C0кPVkfgHSxZW2eЗelTtGsw0htoMe_6A/viewform");
            AssignLink(linkLabel5, "https://gitlab.com/valdeus/sc_localization_ua");
            AssignLink(pictureBox1, "https://scloc.pp.ua");
        }

        // --- Select folder ---
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

        // --- Install / Update localization ---
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
            catch (TimeoutException)
            {
                ShowErrorMessage("Помилка: таймаут HTTP-запиту. Спробуйте ще раз.");
            }
            catch (TaskCanceledException)
            {
                ShowErrorMessage("Помилка: запит скасовано (ймовірно таймаут).");
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
                return "https://github.com/Vova-Bob/SC_localization_UA/releases/latest/download/global.ini";

            if (selectedFolderPath.Contains("PTU") || selectedFolderPath.Contains("EPTU") || selectedFolderPath.Contains("4.0_PREVIEW"))
            {
                var tagName = await GetLatestReleaseTagAsync();
                return string.IsNullOrEmpty(tagName)
                    ? string.Empty
                    : $"https://github.com/Vova-Bob/SC_localization_UA/releases/download/{tagName}/global.ini";
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
                string body = await RetryAsync(
                    () => GetStringWithTimeoutAsync(GithubReleasesApiUrl + "?per_page=10", 30000),
                    attempts: 3, delayMs: 800);

                var releases = JArray.Parse(body);

                if (releases.Count > 0)
                {
                    var first = releases[0];
                    return first["tag_name"].ToString();
                }
                else
                {
                    ShowErrorMessage("Релізи не знайдено.");
                    return string.Empty;
                }
            }
            catch (TimeoutException)
            {
                ShowErrorMessage("Таймаут під час отримання списку релізів GitHub.");
                return string.Empty;
            }
            catch (HttpRequestException ex)
            {
                ShowErrorMessage($"Помилка мережі (GitHub релізи): {ex.Message}");
                return string.Empty;
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
                using (var cts = new CancellationTokenSource(30000))
                {
                    var response = await httpClient.GetAsync(url, cts.Token);
                    response.EnsureSuccessStatusCode();

                    byte[] fileData = await response.Content.ReadAsByteArrayAsync();

                    string directoryPath = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(directoryPath))
                        Directory.CreateDirectory(directoryPath);

                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        await fs.WriteAsync(fileData, 0, fileData.Length);
                }
            }
            catch (TaskCanceledException)
            {
                ShowErrorMessage("Таймаут під час завантаження файлу з GitHub.");
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
                link.LinkClicked += OpenLink;
            else
                control.Click += OpenLink;
        }

        private void OpenLink(object sender, EventArgs e)
        {
            if (sender is Control ctrl && ctrl.Tag is string url)
            {
                OpenUrl(url);
            }
        }

        // UseShellExecute=true is the robust way to open URLs from WinForms
        private void OpenUrl(string url)
        {
            if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }
                );
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
                string body = await RetryAsync(
                    () => GetStringWithTimeoutAsync(GithubReleasesApiUrl, 30000),
                    attempts: 3, delayMs: 800);

                var releases = JArray.Parse(body);

                if (releases.Count > 0)
                {
                    var latestRelease = releases[0];
                    string latestTag = latestRelease["tag_name"].ToString();
                    string releaseName = latestRelease["name"]?.ToString();
                    string releaseBody = latestRelease["body"]?.ToString();

                    string savedTag = Properties.Settings.Default.LatestCheckedReleaseTag;

                    if (savedTag != latestTag)
                    {
                        Properties.Settings.Default.LatestCheckedReleaseTag = latestTag;
                        Properties.Settings.Default.Save();

                        string message = $"Доступний новий реліз: {releaseName}\n" +
                                         $"Опис релізу: {releaseBody}\n" +
                                         $"Оновіть переклад через додаток";
                        MessageBox.Show(message, "Новий реліз доступний", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (TimeoutException)
            {
                ShowErrorMessage("Таймаут під час перевірки нових релізів GitHub.");
            }
            catch (HttpRequestException ex)
            {
                ShowErrorMessage($"Помилка мережі під час перевірки релізів: {ex.Message}");
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

            AppUpdater appUpdater = new AppUpdater();

            // Run both checks in parallel
            var updatesTask = appUpdater.CheckForUpdatesAsync();
            var releasesTask = CheckForNewReleasesAsync();
            await Task.WhenAll(updatesTask, releasesTask);

            // Перевірка кешу при старті (1 раз)
            await OfferCacheCleanupIfLargeAsync();

            Console.WriteLine("Перевірка оновлення додатка та релізів перекладу завершена.");
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {
            OpenUrl("https://send.monobank.ua/jar/44HXkQkorg");
        }

        // --- Wiki button ---
        private void buttonWiki_Click(object sender, EventArgs e)
        {
            if (wikiForm == null || wikiForm.IsDisposed)
            {
                wikiForm = new WikiForm();

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

        // --- Clear cache button (async, move-then-delete) ---
        private async void buttonClearCache_Click(object sender, EventArgs e)
        {
            string cachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "star citizen");

            try
            {
                if (!Directory.Exists(cachePath))
                {
                    MessageBox.Show("Папка кешу не знайдена!", "Помилка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var result = MessageBox.Show(
                    $"Ви справді хочете очистити кеш шейдерів гри Star Citizen?\n{cachePath}",
                    "Очистка кешу",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes) return;

                buttonClearCache.Enabled = false;
                toolStripStatusLabel1.Text = "Очищення кешу…";

                // 1) Move folder out quickly
                string parent = Path.GetDirectoryName(cachePath);
                string tomb = Path.Combine(parent, $"star citizen._del_{DateTime.Now:yyyyMMdd_HHmmss}");

                try
                {
                    Directory.Move(cachePath, tomb);
                    Directory.CreateDirectory(cachePath);
                }
                catch (IOException)
                {
                    tomb = null;
                }
                catch (UnauthorizedAccessException)
                {
                    tomb = null;
                }

                // 2) Delete in background
                await Task.Run(() =>
                {
                    string pathToDelete = tomb ?? cachePath;
                    SafeDeleteDirectory(pathToDelete);
                });

                toolStripStatusLabel1.Text = "Кеш успішно очищено";
                MessageBox.Show("Кеш успішно очищено!", "Успіх",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Сталася помилка при очищенні кешу: {ex.Message}",
                    "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                buttonClearCache.Enabled = true;
            }
        }

        // Рекурсивне видалення з нормалізацією атрибутів та кількома спробами
        private static void SafeDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            const int attempts = 3;
            for (int tryNo = 1; tryNo <= attempts; tryNo++)
            {
                try
                {
                    NormalizeAttributes(path);
                    Directory.Delete(path, true);
                    return;
                }
                catch (IOException)
                {
                    if (tryNo == attempts) throw;
                    Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException)
                {
                    if (tryNo == attempts) throw;
                    Thread.Sleep(200);
                }
            }
        }

        // знімаємо ReadOnly/Hidden з файлів і тек, щоб Delete не падав
        private static void NormalizeAttributes(string root)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(dir, FileAttributes.Normal); } catch { }
                }
                try { File.SetAttributes(root, FileAttributes.Normal); } catch { }
            }
            catch { /* ignore enum errors */ }
        }

        // ===================== helpers =====================

        // True only for shader-cache folders. We accept names like:
        // "starcitizen_(sc-alpha-4.3.0)_pqhvcd_0" OR "starcitizen, (sc-alpha-4.3.0), pqhvcd_0".
        private static bool IsShaderCacheDirName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            return name.Contains("starcitizen") && name.Contains("sc-alpha-");
        }

        // Computes sizes for all given directories safely and returns a map path->size
        private static Dictionary<string, long> GetDirSizesSafe(IEnumerable<DirectoryInfo> dirs)
        {
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs)
                map[d.FullName] = GetDirectorySizeSafe(d.FullName);
            return map;
        }

        // ===== One-shot cache check on app start =====
        // Trigger if: (1) old shader-cache folders exist,
        //             (2) latest shader-cache > LATEST_OK_BYTES,
        //             (3) ANY shader-cache dir >= TWO_GB (large folder).
        private async Task OfferCacheCleanupIfLargeAsync()
        {
            if (_cacheCheckedOnce) return;
            _cacheCheckedOnce = true;

            string cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "star citizen"
            );

            if (!Directory.Exists(cacheRoot)) return;

            // Consider ONLY shader cache folders; ignore service/foreign dirs like "crashes", "test", etc.
            var subdirs = new DirectoryInfo(cacheRoot)
                .GetDirectories("*", SearchOption.TopDirectoryOnly)
                .Where(d => IsShaderCacheDirName(d.Name))
                .OrderByDescending(d => d.LastWriteTimeUtc) // newest first by timestamp
                .ToList();

            if (subdirs.Count == 0) return; // no shader caches — nothing to do

            var latest = subdirs[0];
            var older = subdirs.Skip(1).ToList(); // may be empty

            // Compute sizes for all subdirs off-UI thread
            var sizes = await Task.Run(() => GetDirSizesSafe(subdirs));
            long latestSize = sizes[latest.FullName];
            long oldSize = older.Sum(d => sizes[d.FullName]);
            long totalSize = sizes.Values.Sum();

            bool hasOldFolders = older.Count > 0;
            bool latestTooBig = latestSize > LATEST_OK_BYTES;  // current cache grew too much
            bool anyBigFolder = sizes.Values.Any(sz => sz >= TWO_GB);

            // If none of the reasons apply — do nothing
            if (!hasOldFolders && !latestTooBig && !anyBigFolder) return;

            var answer = MessageBox.Show(
                $"Знайдено кеш-папок (шейдери): {subdirs.Count} (остання: \"{latest.Name}\" ≈ {FormatBytes(latestSize)}).\n" +
                (hasOldFolders ? $"Старі папки: {older.Count}, сумарно ≈ {FormatBytes(oldSize)}.\n" : "") +
                (latestTooBig ? $"Остання тека більша за {FormatBytes(LATEST_OK_BYTES)}\n" : "") +
                (anyBigFolder ? $"Виявлено теку(и) ≥ {FormatBytes(TWO_GB)}. Загалом: {FormatBytes(totalSize)}.\n" : "") +
                "\nТак — видалити СТАРІ (залишити останню)\n" +
                "Ні — видалити ВСЕ (включно з останньою)\n" +
                "Скасувати — нічого не робити",
                "Очищення кешу Star Citizen",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel) return;

            try
            {
                buttonClearCache.Enabled = false;
                toolStripStatusLabel1.Text = "Очищення кешу…";

                if (answer == DialogResult.Yes)
                {
                    // Delete only old cache folders
                    if (older.Count > 0)
                    {
                        await Task.Run(() =>
                        {
                            foreach (var d in older) SafeDeleteDirectory(d.FullName);
                        });
                        toolStripStatusLabel1.Text = "Старий кеш очищено";
                    }
                    else
                    {
                        toolStripStatusLabel1.Text = "Немає старих папок для видалення";
                    }
                }
                else // DialogResult.No — delete everything (including latest)
                {
                    await Task.Run(() =>
                    {
                        foreach (var d in subdirs) SafeDeleteDirectory(d.FullName);
                        Directory.CreateDirectory(cacheRoot); // recreate root
                    });
                    toolStripStatusLabel1.Text = "Увесь кеш очищено";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка під час очищення: {ex.Message}",
                    "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                buttonClearCache.Enabled = true;
            }
        }

        // Підрахунок розміру директорії з обробкою помилок
        private static long GetDirectorySizeSafe(string path)
        {
            try
            {
                long size = 0;
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(f).Length; } catch { }
                }
                return size;
            }
            catch { return 0; }
        }

        // Users формат байтів
        private static string FormatBytes(long bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;

            if (bytes >= GB) return $"{bytes / GB:0.##} ГБ";
            if (bytes >= MB) return $"{bytes / MB:0.##} МБ";
            if (bytes >= KB) return $"{bytes / KB:0.##} КБ";
            return $"{bytes} Б";
        }

        // --- AntiAFK button ---
        private void ButtonAntiAFK_Click(object sender, EventArgs e)
        {
            antiAfkEnabled = !antiAfkEnabled;
            _antiAFK.ToggleAntiAFK(toolStripStatusLabel1);
            SetUi(buttonAntiAFK, antiAfkEnabled, "AntiAFK ON", "AntiAFK OFF", "AntiAFK: ");
        }

        // --- Form closing ---
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_antiAFK != null) _antiAFK.Dispose();
            // do not manually close overlay to avoid modifying Application.OpenForms
            // it will close automatically when owner form is closed
        }

        // --- KillFeed button ---
        private void buttonkillfeed_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                MessageBox.Show("Спочатку оберіть теку з грою.", "KillFeed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (overlayForm == null || overlayForm.IsDisposed)
            {
                overlayForm = new killFeed(selectedFolderPath) { Owner = this }; // set owner so it closes with main form
                overlayForm.Show();
                SetUi(buttonkillfeed, true, "KillFeed ON", "KillFeed OFF", "KillFeed: ");
            }
            else
            {
                TryWithOverlay(of =>
                {
                    of.ToggleVisibility();
                    SetUi(buttonkillfeed, of.Visible, "KillFeed ON", "KillFeed OFF", "KillFeed: ");
                });
            }
        }

        // --- Hangar overlay button (через Program.TryWithOverlayAsync) ---
        private async void ButtonHangar_Click(object sender, EventArgs e)
        {
            await Program.TryWithOverlayAsync(o =>
            {
                bool on;
                if (!o.Visible) { o.Show(); on = true; }
                else { o.Hide(); on = false; }

                SetUi(buttonHangar, on, "EX-Hangar ON", "EX-Hangar OFF", "EX-Hangar: ");
                return Task.CompletedTask;
            });
        }
    }
}
