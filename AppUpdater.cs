using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace SCLOCUA
{
    /// <summary>
    /// Simple GitHub release updater: checks latest tag, downloads installer, runs it, exits app.
    /// NOTE: Digital signature validation was intentionally removed per request.
    /// </summary>
    internal class AppUpdater
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/Vova-Bob/SCLoc_App/releases/latest";

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var latestReleaseInfo = await GetLatestReleaseInfoAsync();
                if (latestReleaseInfo == null)
                {
                    MessageBox.Show("Не вдалося отримати інформацію про релізи.", "Помилка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var latestVersion = latestReleaseInfo.Version;
                var downloadUrl = latestReleaseInfo.DownloadUrl;
                var releaseNotes = latestReleaseInfo.ReleaseNotes;

                string truncatedReleaseNotes = releaseNotes.Length > 500
                    ? releaseNotes.Substring(0, 500) + "...\n(Детальніше дивіться на сторінці GitHub)"
                    : releaseNotes;

                string currentVersion = Application.ProductVersion;
                Version currentVer = new Version(currentVersion);
                Version latestVer = new Version(latestVersion.TrimStart('v'));

                if (latestVer > currentVer)
                {
                    var result = MessageBox.Show(
                        $"Доступна нова версія програми: {latestVersion}\n" +
                        $"Ваша версія: {currentVersion}\n\n" +
                        $"Опис оновлення:\n{truncatedReleaseNotes}\n\n" +
                        "Бажаєте оновити додаток?",
                        "Оновлення доступне",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                        await StartAppUpdate(downloadUrl);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не вдалося перевірити наявність оновлень: " + ex.Message, "Помилка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private sealed class ReleaseInfo
        {
            public string Version { get; set; }
            public string DownloadUrl { get; set; }
            public string ReleaseNotes { get; set; }
        }

        /// <summary>
        /// Calls GitHub Releases API and returns latest tag, asset URL and notes.
        /// </summary>
        private async Task<ReleaseInfo> GetLatestReleaseInfoAsync()
        {
            try
            {
                var client = HttpClientService.Client; // reuse shared HttpClient
                var response = await client.GetStringAsync(GitHubApiUrl);

                var json = JObject.Parse(response);
                return new ReleaseInfo
                {
                    Version = json["tag_name"]?.ToString(),
                    DownloadUrl = json["assets"]?[0]?["browser_download_url"]?.ToString(),
                    ReleaseNotes = json["body"]?.ToString() ?? ""
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка при отриманні даних про реліз: " + ex.Message, "Помилка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        /// <summary>
        /// Downloads installer to temp and runs it. No signature checks.
        /// </summary>
        private async Task StartAppUpdate(string installerUrl)
        {
            try
            {
                string tempInstallerPath = Path.Combine(Path.GetTempPath(), "SCLocAppInstaller.exe");

                var client = HttpClientService.Client;
                using (var resp = await client.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(tempInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        await resp.Content.CopyToAsync(fs);
                }

                // Run installer and exit current app
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempInstallerPath,
                    UseShellExecute = true,
                    Verb = "runas" // request elevation if needed
                });

                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка при завантаженні оновлення: " + ex.Message, "Помилка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
