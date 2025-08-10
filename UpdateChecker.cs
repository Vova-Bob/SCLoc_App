// UpdateChecker.cs (.NET Framework 4.8, Newtonsoft.Json)
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace SCLOCUA
{
    internal static class UpdateChecker
    {
        // Налаштування репозиторію GitHub
        private const string RepoOwner = "Vova-Bob";
        private const string RepoName = "SCLoc_App";
        private const string ApiLatest = "https://api.github.com/repos/{0}/{1}/releases/latest";
        private const string InstallerName = "SCLocAppInstaller.exe";

        // HTTP-клієнт для перевірки оновлень
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        // --- Захист від подвійних діалогів ---
        private static int _checkedFlag = 0; // 0 = ще не перевіряли, 1 = вже перевірили
        private static readonly SemaphoreSlim _mutex = new SemaphoreSlim(1, 1);
        private static DateTime _lastCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan _minInterval = TimeSpan.FromMinutes(5); // затримка між перевірками

        /// <summary>
        /// Перевіряє наявність оновлень (тільки один раз за запуск, якщо force==false).
        /// Безпечно викликати з різних місць.
        /// </summary>
        public static async Task CheckOnceAsync(bool force = false)
        {
            // Швидка перевірка (без блокування). Якщо не примусово і вже перевіряли — вихід.
            if (!force)
            {
                if (Interlocked.Exchange(ref _checkedFlag, 1) == 1) return;
            }

            await _mutex.WaitAsync().ConfigureAwait(false);
            try
            {
                // Пропускаємо часті виклики (крім примусових)
                if (!force && (DateTime.UtcNow - _lastCheckUtc) < _minInterval) return;

                _lastCheckUtc = DateTime.UtcNow;

                var current = GetCurrentVersion();

                var url = string.Format(ApiLatest, RepoOwner, RepoName);
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.UserAgent.ParseAdd("SCLocUA-Updater/1.0");
                    req.Headers.Accept.ParseAdd("application/vnd.github+json");

                    using (var resp = await Http.SendAsync(req).ConfigureAwait(false))
                    {
                        resp.EnsureSuccessStatusCode();
                        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var root = JObject.Parse(json);

                        var tag = root.Value<string>("tag_name") ?? "";
                        if (!TryParseVersion(tag, out var latest)) return;
                        if (latest <= current) return;

                        // Знаходимо інсталятор за назвою
                        var assets = (JArray)root["assets"];
                        if (assets == null || assets.Count == 0) return;
                        string downloadUrl = null;
                        foreach (var a in assets)
                        {
                            var name = a.Value<string>("name") ?? "";
                            if (string.Equals(name, InstallerName, StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = a.Value<string>("browser_download_url");
                                break;
                            }
                        }
                        if (string.IsNullOrEmpty(downloadUrl)) return;

                        var nameTitle = root.Value<string>("name") ?? $"Реліз {tag}";
                        var body = root.Value<string>("body") ?? "";
                        var when = root.Value<string>("published_at");
                        var preview = TrimForUi(body, 900);

                        var sb = new StringBuilder();
                        sb.AppendLine($"{nameTitle} ({tag})");
                        if (!string.IsNullOrEmpty(when)) sb.AppendLine($"Опубліковано: {when}");
                        sb.AppendLine();
                        sb.AppendLine(preview);
                        sb.AppendLine();
                        sb.AppendLine($"Поточна версія: {current}  →  Нова версія: {latest}");
                        sb.AppendLine();
                        sb.Append("Оновити зараз?");

                        var res = MessageBox.Show(sb.ToString(), "Доступне оновлення",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                        if (res == DialogResult.Yes)
                            await DownloadAndRunInstallerAsync(downloadUrl).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка перевірки оновлень:\n{ex.Message}", "Оновлення",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _mutex.Release();
            }
        }

        // --- Допоміжні методи ---
        private static bool TryParseVersion(string tag, out Version ver)
        {
            ver = new Version(0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(tag)) return false;
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
            var core = tag.Split('-')[0];
            try { ver = Version.Parse(core); return true; }
            catch
            {
                var segs = core.Split('.');
                if (segs.Length < 2) return false;
                while (segs.Length < 4) core += ".0";
                try { ver = Version.Parse(core); return true; } catch { return false; }
            }
        }

        private static Version GetCurrentVersion()
        {
            try
            {
                var pv = Application.ProductVersion;
                if (TryParseVersion(pv, out var v)) return v;
            }
            catch { }
            try { return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0); }
            catch { return new Version(0, 0, 0, 0); }
        }

        private static string TrimForUi(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "(немає опису релізу)";
            text = text.Replace("\r\n", "\n");
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "\n…";
        }

        private static async Task DownloadAndRunInstallerAsync(string url)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), InstallerName);
            using (var stream = await Http.GetStreamAsync(url).ConfigureAwait(false))
            using (var file = File.Create(tempFile))
                await stream.CopyToAsync(file).ConfigureAwait(false);

            var proceed = MessageBox.Show(
                "Інсталятор може не мати цифрового підпису. Продовжити?",
                "Попередження безпеки",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (proceed != DialogResult.Yes) return;

            Process.Start(new ProcessStartInfo { FileName = tempFile, UseShellExecute = true });
            Application.Exit();
        }
    }
}