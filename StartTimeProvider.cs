using System;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ExecutiveHangarOverlay
{
    /// <summary>
    /// Provides cycle start time with clear priority:
    /// Registry override -> Remote URL -> Env -> App.config -> UtcNow.
    /// Also exposes helpers to set/clear local override and force remote resync.
    /// </summary>
    public static class StartTimeProvider
    {
        // Registry path for user override
        private const string RegPath = @"Software\SCLocUA";
        private const string RegName = "CycleStartMs";

        // AppSetting keys
        private const string AppSettingRemoteUrl = "START_TIME_URL";
        private const string AppSettingKey = "TIME_START_CYCLE";
        private const string EnvKey = "VITE_TIME_START_CYCLE";

        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };

        private static long _cached;            // last resolved value
        private static DateTime _lastRemote;    // when remote was last fetched

        /// <summary>Resolve start time once (uses cached remote for 5 minutes).</summary>
        public static async Task<long> ResolveAsync(bool forceRemote = false, CancellationToken ct = default)
        {
            // 1) Registry override
            long? reg = TryReadRegistry();
            if (reg.HasValue) return _cached = reg.Value;

            // 2) Remote URL (if configured)
            string url = System.Configuration.ConfigurationManager.AppSettings[AppSettingRemoteUrl];
            bool needRemote = !string.IsNullOrWhiteSpace(url)
                              && (forceRemote || (DateTime.UtcNow - _lastRemote) > TimeSpan.FromMinutes(5));

            if (!string.IsNullOrWhiteSpace(url))
            {
                if (needRemote)
                {
                    var remote = await TryFetchRemoteAsync(url, ct).ConfigureAwait(false);
                    _lastRemote = DateTime.UtcNow;
                    if (remote.HasValue) return _cached = remote.Value;
                }
                else if (_cached > 0)
                {
                    return _cached; // reuse recent remote
                }
            }

            // 3) Env
            if (long.TryParse(Environment.GetEnvironmentVariable(EnvKey), out long envMs))
                return _cached = envMs;

            // 4) App.config
            var cfg = System.Configuration.ConfigurationManager.AppSettings[AppSettingKey];
            if (long.TryParse(cfg, out long cfgMs))
                return _cached = cfgMs;

            // 5) Fallback: now
            return _cached = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>Force a remote re-sync now (if URL configured).</summary>
        public static Task<long> ForceResyncAsync(CancellationToken ct = default) =>
            ResolveAsync(forceRemote: true, ct);

        /// <summary>Set local user override (ms since Unix epoch).</summary>
        public static void SetLocalOverride(long startMs)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RegPath, true))
                key?.SetValue(RegName, startMs, RegistryValueKind.QWord);
            _cached = startMs;
        }

        /// <summary>Remove local override.</summary>
        public static void ClearLocalOverride()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegPath, writable: true))
                key?.DeleteValue(RegName, throwOnMissingValue: false);
        }

        private static long? TryReadRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    var val = key?.GetValue(RegName);
                    if (val == null) return null;
                    if (val is long l) return l;
                    if (long.TryParse(val.ToString(), out long parsed)) return parsed;
                }
            }
            catch (SecurityException) { /* ignore */ }
            return null;
        }

        [DataContract]
        private class RemoteDto
        {
            [DataMember(Name = "cycleStartMs")] public long CycleStartMs { get; set; }
        }

        private static async Task<long?> TryFetchRemoteAsync(string url, CancellationToken ct)
        {
            using (var resp = await Http.GetAsync(url, ct).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode) return null;
                var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var ser = new DataContractJsonSerializer(typeof(RemoteDto));
                var dto = (RemoteDto)ser.ReadObject(stream);
                return dto?.CycleStartMs;
            }
        }
    }
}
