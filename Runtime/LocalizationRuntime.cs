using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// The runtime half of the localization system: loads the <see cref="LocalizationData"/> authored
    /// via the "Language Data" Editor window, tracks which language is currently active, and looks up
    /// text by key for it. <see cref="LocalizedText"/> and any hand-written code calling
    /// <see cref="Get"/> directly both go through this - it's the single place that decides what
    /// "the current language" means.
    /// </summary>
    public static class LocalizationRuntime
    {
        private const string ResourcePath = "LocalizationData";
        private const string LanguagePrefKey = "LocalizationSystem.Language";

        private static LocalizationData _data;
        private static SystemLanguage? _currentLanguage;

        /// <summary>Raised after <see cref="CurrentLanguage"/> changes, so listeners (like <see cref="LocalizedText"/>) can refresh.</summary>
        public static event Action LanguageChanged;

        private static LocalizationData Data => _data ??= Load();

        public static SystemLanguage CurrentLanguage
        {
            get
            {
                if (_currentLanguage.HasValue)
                {
                    return _currentLanguage.Value;
                }

                _currentLanguage = ResolveInitialLanguage();
                return _currentLanguage.Value;
            }
        }

        /// <summary>Every language this project has data for, in the order set up in the "Language Data" window.</summary>
        public static SystemLanguage[] AvailableLanguages => Data.Languages.ToArray();

        public static void SetLanguage(SystemLanguage language)
        {
            if (_currentLanguage == language)
            {
                return;
            }

            _currentLanguage = language;
            PlayerPrefs.SetInt(LanguagePrefKey, (int)language);
            PlayerPrefs.Save();
            LanguageChanged?.Invoke();
        }

        /// <summary>
        /// The text for <paramref name="key"/> in <see cref="CurrentLanguage"/>. Falls back to the
        /// project's source language, then to <paramref name="fallback"/>, then to <paramref name="key"/>
        /// itself - a lookup should never silently return an empty string, a broken key should be
        /// obvious on screen instead of invisible.
        /// </summary>
        public static string Get(string key, string fallback = null)
        {
            var entry = Data.FindEntry(key);
            if (entry == null)
            {
                return fallback ?? key;
            }

            return entry.GetValue(CurrentLanguage)
                   ?? entry.GetValue(Data.SourceLanguage)
                   ?? fallback
                   ?? key;
        }

        // SystemLanguage.Hungarian.ToString() returns "Hugarian" - a decades-old Unity enum typo kept
        // for backward compatibility on the enum itself. Correct it here once so every UI surface
        // (this dropdown, the Language Data window) shows the right spelling.
        public static string DisplayName(SystemLanguage language) =>
            language == SystemLanguage.Hungarian ? "Hungarian" : language.ToString();

        /// <summary>Reverse of <see cref="DisplayName"/> - finds the <see cref="SystemLanguage"/> whose display name matches <paramref name="name"/> (case-insensitive), or null if none does.</summary>
        public static SystemLanguage? ParseDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var seenValues = new HashSet<int>();
            foreach (SystemLanguage language in Enum.GetValues(typeof(SystemLanguage)))
            {
                // Chinese predates the Simplified/Traditional split (ambiguous) and a couple of other
                // enum members are legacy aliases sharing a value with another one (e.g. Hungarian) -
                // skip both so each real language only ever matches once.
                if (language == SystemLanguage.Unknown || language == SystemLanguage.Chinese || !seenValues.Add((int)language))
                {
                    continue;
                }

                if (string.Equals(DisplayName(language), name, StringComparison.OrdinalIgnoreCase))
                {
                    return language;
                }
            }

            return null;
        }

        private static SystemLanguage ResolveInitialLanguage()
        {
            if (PlayerPrefs.HasKey(LanguagePrefKey))
            {
                return (SystemLanguage)PlayerPrefs.GetInt(LanguagePrefKey);
            }

            var systemLanguage = Application.systemLanguage;
            return Data.Languages.Contains(systemLanguage) ? systemLanguage : Data.SourceLanguage;
        }

        // With "Enter Play Mode Options" skipping the domain reload, statics survive from one play
        // session to the next: without this, a session would keep the data (and language) loaded by an
        // earlier one, ignoring anything saved in the Language Data window since - and its listeners.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _data = null;
            _currentLanguage = null;
            LanguageChanged = null;
        }

        private static LocalizationData Load()
        {
            var textAsset = Resources.Load<TextAsset>(ResourcePath);
            if (textAsset == null)
            {
                Debug.LogWarning($"[LocalizationSystem] No LocalizationData found at Resources/{ResourcePath} - using empty data.");
                return new LocalizationData();
            }

            return JsonUtility.FromJson<LocalizationData>(textAsset.text) ?? new LocalizationData();
        }
    }
}
