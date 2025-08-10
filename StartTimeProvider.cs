// StartTimeProvider.cs (Newtonsoft.Json, .NET Framework 4.8)
using System;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Forms; // for MessageBox
using Microsoft.Win32;
using Newtonsoft.Json;

namespace ExecutiveHangarOverlay
{
    /// <summary>
    /// Source priority:
    ///  - forceRemote=true: Remote URL -> Registry -> Env -> App.config -> UtcNow
    ///  - forceRemote=false: Registry -> Remote (cached 5m) -> Env -> App.config -> UtcNow
    /// </summary>
    public static class StartTimeProvider
    {
        private const string RegPath = @"Software\SCLocUA";
        private const string RegName = "CycleStartMs";

        private const string AppSettingRemoteUrl = "START_TIME_URL";
        private const string AppSettingKey = "TIME_START_CYCLE";
        private const string EnvKey = "VITE_TIME_START_CYCLE";

        private static readonly HttpClient Http;
        private static long _cached;
        private static DateTime _lastRemote;

        static StartTimeProvider()
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            Http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        }

        public static async Task<long> ResolveAsync(bool forceRemote = false)
        {
            string url = System.Configuration.ConfigurationManager.AppSettings[AppSettingRemoteUrl];

            if (forceRemote)
            {
                // 1) Remote first (ignore registry override)
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var remote = await TryFetchRemoteAsync(url).ConfigureAwait(false);
                    _lastRemote = DateTime.UtcNow;
                    if (remote.HasValue) return _cached = remote.Value;

                    MessageBox.Show(
                        $"Не вдалося синхронізувати час із {url}\n" +
                        "Перевірте доступність URL, сертифікат і формат JSON (\"cycleStartMs\").",
                        "Помилка синхронізації",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                // 2) Fallbacks
                var reg = TryReadRegistry(); if (reg.HasValue) return _cached = reg.Value;
                if (long.TryParse(Environment.GetEnvironmentVariable(EnvKey), out var envMs)) return _cached = envMs;
                var cfg = System.Configuration.ConfigurationManager.AppSettings[AppSettingKey];
                if (long.TryParse(cfg, out var cfgMs)) return _cached = cfgMs;
                return _cached = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            else
            {
                // Normal path: Registry first
                var reg = TryReadRegistry(); if (reg.HasValue) return _cached = reg.Value;

                // Remote with 5m cache
                bool needRemote = !string.IsNullOrWhiteSpace(url) &&
                                  (DateTime.UtcNow - _lastRemote) > TimeSpan.FromMinutes(5);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (needRemote)
                    {
                        var remote = await TryFetchRemoteAsync(url).ConfigureAwait(false);
                        _lastRemote = DateTime.UtcNow;
                        if (remote.HasValue) return _cached = remote.Value;
                    }
                    else if (_cached > 0) return _cached;
                }

                if (long.TryParse(Environment.GetEnvironmentVariable(EnvKey), out var envMs)) return _cached = envMs;
                var cfg = System.Configuration.ConfigurationManager.AppSettings[AppSettingKey];
                if (long.TryParse(cfg, out var cfgMs)) return _cached = cfgMs;
                return _cached = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        public static Task<long> ForceResyncAsync() => ResolveAsync(forceRemote: true);

        public static void SetLocalOverride(long startMs)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RegPath, true))
                key?.SetValue(RegName, startMs.ToString(), RegistryValueKind.String);
            _cached = startMs;
        }

        public static void ClearLocalOverride()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegPath, writable: true))
                key?.DeleteValue(RegName, false);
        }

        private static long? TryReadRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    var val = key?.GetValue(RegName)?.ToString();
                    if (long.TryParse(val, out var ms)) return ms;
                }
            }
            catch (SecurityException) { }
            return null;
        }

        private sealed class RemoteDto
        {
            [JsonProperty("cycleStartMs")]
            public long CycleStartMs { get; set; }
        }

        private static async Task<long?> TryFetchRemoteAsync(string url)
        {
            try
            {
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
                var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                var dto = JsonConvert.DeserializeObject<RemoteDto>(json);
                if (dto == null || dto.CycleStartMs <= 0)
                    throw new Exception("JSON parsed but value is missing or invalid.");
                return dto.CycleStartMs;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"HTTP/JSON помилка:\n{ex.Message}",
                    "Помилка синхронізації",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }
    }
}
