// StartTimeProvider.cs (Newtonsoft.Json, .NET Framework 4.8)
using System;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace ExecutiveHangarOverlay
{
    /// <summary>
    /// Computes hangar cycle start by parsing the official Executive Hangar app.js.
    /// Falls back to a cached/override value when the primary source is unavailable.
    ///
    /// Source priority:
    ///  - forceRemote=true: Primary app.js URL -> Registry -> Env -> App.config -> UtcNow
    ///  - forceRemote=false: Registry -> Primary app.js URL (cached 5m) -> Env -> App.config -> UtcNow
    /// </summary>
    public static class StartTimeProvider
    {
        private const string RegPath = @"Software\SCLocUA";
        private const string RegName = "CycleStartMs";

        // Primary source: official Executive Hangar app.js
        private const string DefaultAppJsUrl = "https://exec.xyxyll.com/app.js";
        private const string AppSettingAppJsUrl = "APP_JS_URL";

        // Fallback configuration keys
        private const string AppSettingFallbackKey = "TIME_START_CYCLE";
        private const string EnvKey = "VITE_TIME_START_CYCLE";

        // Official website constants (must stay in sync with https://exec.xyxyll.com/app.js)
        private const int DESIGN_ONLINE_MIN = 65;
        private const int DESIGN_OFFLINE_MIN = 120;
        private const int CYCLE_DRIFT_MS = 226;
        private const int OFFSET_HOURS = -2; // business rule shift used by timesync.py

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
            Http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        public static async Task<long> ResolveAsync(bool forceRemote = false)
        {
            string url = System.Configuration.ConfigurationManager.AppSettings[AppSettingAppJsUrl]
                         ?? DefaultAppJsUrl;

            if (forceRemote)
            {
                // 1) Primary source first (ignore registry override)
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var remote = await TryComputeFromAppJsAsync(url).ConfigureAwait(false);
                    _lastRemote = DateTime.UtcNow;
                    if (remote.HasValue) return _cached = remote.Value;

                    MessageBox.Show(
                        $"Не вдалося синхронізувати час із {url}\n" +
                        "Перевірте доступність URL і наявність INITIAL_OPEN_TIME у app.js.",
                        "Помилка синхронізації",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                // 2) Fallbacks
                var reg = TryReadRegistry(); if (reg.HasValue) return _cached = reg.Value;
                if (long.TryParse(Environment.GetEnvironmentVariable(EnvKey), out var envMs)) return _cached = envMs;
                var cfg = System.Configuration.ConfigurationManager.AppSettings[AppSettingFallbackKey];
                if (long.TryParse(cfg, out var cfgMs)) return _cached = cfgMs;
                return _cached = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            else
            {
                // Normal path: Registry first
                var reg = TryReadRegistry(); if (reg.HasValue) return _cached = reg.Value;

                // Primary source with 5m cache
                bool needRemote = !string.IsNullOrWhiteSpace(url) &&
                                  (DateTime.UtcNow - _lastRemote) > TimeSpan.FromMinutes(5);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (needRemote)
                    {
                        var remote = await TryComputeFromAppJsAsync(url).ConfigureAwait(false);
                        _lastRemote = DateTime.UtcNow;
                        if (remote.HasValue) return _cached = remote.Value;
                    }
                    else if (_cached > 0) return _cached;
                }

                if (long.TryParse(Environment.GetEnvironmentVariable(EnvKey), out var envMs)) return _cached = envMs;
                var cfg = System.Configuration.ConfigurationManager.AppSettings[AppSettingFallbackKey];
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

        /// <summary>
        /// Downloads app.js, extracts INITIAL_OPEN_TIME, and computes the aligned cycle start
        /// using the exact formulas from the official website plus the configured offset.
        /// </summary>
        private static async Task<long?> TryComputeFromAppJsAsync(string url)
        {
            try
            {
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

                string js = await Http.GetStringAsync(url).ConfigureAwait(false);

                // Extract: const INITIAL_OPEN_TIME = new Date('2026-06-18T20:11:10.025-04:00');
                var match = Regex.Match(js, @"INITIAL_OPEN_TIME\s*=\s*new Date\('([^']+)'\)");
                if (!match.Success)
                    throw new Exception("INITIAL_OPEN_TIME not found in app.js.");

                if (!DateTimeOffset.TryParse(match.Groups[1].Value, out var initialOpenTime))
                    throw new Exception($"Cannot parse INITIAL_OPEN_TIME: {match.Groups[1].Value}");

                long cycleStartMs = ComputeCycleStartMs(initialOpenTime.UtcDateTime, DateTime.UtcNow);
                return cycleStartMs;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"HTTP/JS помилка:\n{ex.Message}",
                    "Помилка синхронізації",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return null;
            }
        }

        /// <summary>
        /// Mirrors the calculation in https://exec.xyxyll.com/app.js and applies
        /// the same 2-hour shift used by timesync.py.
        /// </summary>
        internal static long ComputeCycleStartMs(DateTime initialOpenTimeUtc, DateTime nowUtc)
        {
            long designCycleMs = (DESIGN_ONLINE_MIN + DESIGN_OFFLINE_MIN) * 60L * 1000L;
            long cycleDuration = designCycleMs + CYCLE_DRIFT_MS;

            long openDuration = (long)Math.Round(cycleDuration * DESIGN_ONLINE_MIN * 60.0 * 1000.0 / designCycleMs);
            long closeDuration = cycleDuration - openDuration;

            long deltaMs = (long)((nowUtc - initialOpenTimeUtc).TotalMilliseconds);
            long cycles = Math.Max(0, deltaMs / cycleDuration);
            DateTime lastOpenStart = initialOpenTimeUtc.AddMilliseconds(cycles * cycleDuration);

            // If we are already past the current open window, move to the next cycle's open start
            if ((long)((nowUtc - lastOpenStart).TotalMilliseconds) > openDuration)
            {
                lastOpenStart = lastOpenStart.AddMilliseconds(cycleDuration);
            }

            // Apply business-rule shift (2 hours earlier)
            DateTime correctedStart = lastOpenStart.AddHours(OFFSET_HOURS);

            return (long)(correctedStart - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        private sealed class RemoteDto
        {
            [JsonProperty("cycleStartMs")]
            public long CycleStartMs { get; set; }
        }
    }
}
