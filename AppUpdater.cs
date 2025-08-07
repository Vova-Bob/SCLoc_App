using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Windows.Forms;

namespace SCLOCUA
{
    internal class AppUpdater
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/Vova-Bob/SCLoc_App/releases/latest";
        private const string UserAgent = "SCLocAppUpdater"; // Для GitHub API, щоб не заблокували запит

        // Метод для перевірки наявності оновлень
        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var latestReleaseInfo = await GetLatestReleaseInfoAsync();

                if (string.IsNullOrEmpty(latestReleaseInfo))
                {
                    MessageBox.Show("Не вдалося отримати інформацію про релізи.", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Розбираємо версію, URL для завантаження та опис релізу
                var releaseParts = latestReleaseInfo.Split(',');
                var latestVersion = releaseParts[0];
                var downloadUrl = releaseParts[1];
                var releaseNotes = string.Join(",", releaseParts, 2, releaseParts.Length - 2); // Опис може містити коми

                // Обрізаємо довгий опис
                string truncatedReleaseNotes = releaseNotes.Length > 500
                    ? releaseNotes.Substring(0, 500) + "...\n(Детальніше дивіться на сторінці GitHub)"
                    : releaseNotes;

                // Поточна версія програми
                string currentVersion = Application.ProductVersion;

                // Лог для перевірки версій
                Console.WriteLine($"Поточна версія: {currentVersion}, Остання версія: {latestVersion}");

                // Порівнюємо версії
                Version currentVer = new Version(currentVersion);
                Version latestVer = new Version(latestVersion.TrimStart('v'));

                if (latestVer > currentVer)
                {
                    // Якщо є нова версія
                    DialogResult result = MessageBox.Show(
                        $"Доступна нова версія програми: {latestVersion}\n" +
                        $"Ваша версія: {currentVersion}\n\n" +
                        $"Опис оновлення:\n{truncatedReleaseNotes}\n\n" +
                        "Бажаєте оновити додаток?",
                        "Оновлення доступне",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        // Якщо користувач погоджується
                        StartAppUpdate(downloadUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не вдалося перевірити наявність оновлень: " + ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Метод для отримання даних про останній реліз з GitHub
        private async Task<string> GetLatestReleaseInfoAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                    var response = await client.GetStringAsync(GitHubApiUrl);

                    // Лог для перевірки отриманих даних
                    Console.WriteLine("Отримано дані про реліз: " + response);

                    // Парсимо JSON відповідь
                    var jsonResponse = JObject.Parse(response);
                    string latestVersion = jsonResponse["tag_name"].ToString(); // Наприклад, v1.5.4.6
                    string downloadUrl = jsonResponse["assets"][0]["browser_download_url"].ToString(); // URL для завантаження інсталятора
                    string releaseNotes = jsonResponse["body"].ToString(); // Опис релізу

                    return $"{latestVersion},{downloadUrl},{releaseNotes}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка при отриманні даних про реліз: " + ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        // Метод для запуску інсталятора
        private void StartAppUpdate(string installerUrl)
        {
            try
            {
                string tempInstallerPath = Path.Combine(Path.GetTempPath(), "SCLocAppInstaller.exe");

                // Завантажуємо інсталятор
                using (var client = new WebClient())
                {
                    client.DownloadFile(installerUrl, tempInstallerPath);
                }

                // Запускаємо інсталятор
                Process.Start(tempInstallerPath);

                // Закриваємо поточну програму після запуску оновлення (опційно)
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка при завантаженні оновлення: " + ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
