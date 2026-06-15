using System;

namespace SCLOCUA
{
    /// <summary>
    /// Describes a localization pack that can be installed into Star Citizen.
    /// </summary>
    public sealed class LocalizationProfile
    {
        /// <summary>
        /// Internal key used for settings storage and selection matching.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Display name shown in the UI ComboBox.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Value written to user.cfg as g_language = {LanguageCode}.
        /// </summary>
        public string LanguageCode { get; }

        /// <summary>
        /// Relative path inside the Star Citizen folder where global.ini is placed.
        /// Example: Data\Localization\korean_(south_korea)
        /// </summary>
        public string RelativePath { get; }

        /// <summary>
        /// Direct download URL for raw/static packs (e.g. MrKraken).
        /// If null, the application falls back to release-based resolution.
        /// </summary>
        public string DirectUrl { get; }

        public LocalizationProfile(string key, string name, string languageCode, string relativePath, string directUrl = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            LanguageCode = languageCode ?? throw new ArgumentNullException(nameof(languageCode));
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            DirectUrl = directUrl;
        }

        /// <summary>
        /// Built-in profiles.
        /// </summary>
        public static LocalizationProfile Ukrainian { get; } = new LocalizationProfile(
            key: "ukrainian",
            name: "Українська (SC localization UA)",
            languageCode: "korean_(south_korea)",
            relativePath: @"Data\Localization\korean_(south_korea)");

        public static LocalizationProfile EnglishMrKraken { get; } = new LocalizationProfile(
            key: "english_mrkraken",
            name: "English Mod (MrKraken / StarStrings)",
            languageCode: "english",
            relativePath: @"Data\Localization\english",
            directUrl: "https://raw.githubusercontent.com/MrKraken/StarStrings/refs/heads/master/Data/Localization/english/global.ini");

        public static LocalizationProfile[] All { get; } = { Ukrainian, EnglishMrKraken };
    }
}
