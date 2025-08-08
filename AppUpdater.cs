using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace SCLOCUA
{
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
                    MessageBox.Show("Не вдалося отримати інформацію про релізи.", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var latestVersion = latestReleaseInfo.Version;
                var downloadUrl = latestReleaseInfo.DownloadUrl;
                var releaseNotes = latestReleaseInfo.ReleaseNotes;

                string truncatedReleaseNotes = releaseNotes.Length > 500
                    ? releaseNotes.Substring(0, 500) + "...\n(Детальніше дивіться на сторінці GitHub)"
                    : releaseNotes;

                string currentVersion = Application.ProductVersion;

                Console.WriteLine($"Поточна версія: {currentVersion}, Остання версія: {latestVersion}");

                Version currentVer = new Version(currentVersion);
                Version latestVer = new Version(latestVersion.TrimStart('v'));

                if (latestVer > currentVer)
                {
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
                        await StartAppUpdate(downloadUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не вдалося перевірити наявність оновлень: " + ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private class ReleaseInfo
        {
            public string Version { get; set; }
            public string DownloadUrl { get; set; }
            public string ReleaseNotes { get; set; }
        }

        private async Task<ReleaseInfo> GetLatestReleaseInfoAsync()
        {
            try
            {
                var client = HttpClientService.Client;
                var response = await client.GetStringAsync(GitHubApiUrl);

                Console.WriteLine("Отримано дані про реліз: " + response);

                var jsonResponse = JObject.Parse(response);
                return new ReleaseInfo
                {
                    Version = jsonResponse["tag_name"].ToString(),
                    DownloadUrl = jsonResponse["assets"][0]["browser_download_url"].ToString(),
                    ReleaseNotes = jsonResponse["body"].ToString()
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка при отриманні даних про реліз: " + ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private async Task StartAppUpdate(string installerUrl)
        {
            try
            {
                string tempInstallerPath = Path.Combine(Path.GetTempPath(), "SCLocAppInstaller.exe");

                var client = HttpClientService.Client;
                var response = await client.GetAsync(installerUrl);
                response.EnsureSuccessStatusCode();

                await using (var fs = new FileStream(tempInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    await response.Content.CopyToAsync(fs);
                }

                if (!HasValidSignature(tempInstallerPath))
                {
                    MessageBox.Show("Цифровий підпис завантаженого файлу недійсний. Оновлення скасовано.", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Process.Start(tempInstallerPath);
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка при завантаженні оновлення: " + ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool HasValidSignature(string filePath)
        {
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                    return chain.Build(cert);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }
}
