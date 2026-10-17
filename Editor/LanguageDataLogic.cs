using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// The UI-free rules of the Language Data window (which languages can be picked, what counts as translated, moving and pinning
    /// language columns, Game Context presets, source-language correction). Shared by the default IMGUI window and the studio-look
    /// window so both behave identically.
    /// </summary>
    internal static class LanguageDataLogic
    {
        // SystemLanguage.Chinese predates the Simplified/Traditional split and is ambiguous - offer
        // only the two specific variants instead. Everything else below is excluded because its
        // share of Steam's actual userbase is negligible - per Steam's own Hardware & Software
        // Survey (2026), the languages left selectable (English, ChineseSimplified, Russian,
        // Spanish, Portuguese, German, French, Japanese, Polish, Korean, Turkish,
        // ChineseTraditional, Thai, Ukrainian, Italian) each sit at roughly 0.6% of users or more,
        // while everything excluded here (e.g. Vietnamese ~0.09%, Dutch ~0.30%, Swedish ~0.29%,
        // Arabic ~0% as a Steam interface language) is far below that. Re-check the survey if this
        // list needs revisiting later - language share shifts over time (Simplified Chinese only
        // overtook English platform-wide in 2024).
        private static readonly HashSet<SystemLanguage> ExcludedLanguages = new()
        {
            SystemLanguage.Unknown,
            SystemLanguage.Chinese,
            SystemLanguage.Afrikaans,
            SystemLanguage.Arabic,
            SystemLanguage.Basque,
            SystemLanguage.Belarusian,
            SystemLanguage.Bulgarian,
            SystemLanguage.Catalan,
            SystemLanguage.Czech,
            SystemLanguage.Danish,
            SystemLanguage.Dutch,
            SystemLanguage.Estonian,
            SystemLanguage.Faroese,
            SystemLanguage.Finnish,
            SystemLanguage.Greek,
            SystemLanguage.Hebrew,
            SystemLanguage.Hindi,
            SystemLanguage.Hungarian,
            SystemLanguage.Icelandic,
            SystemLanguage.Indonesian,
            SystemLanguage.Latvian,
            SystemLanguage.Lithuanian,
            SystemLanguage.Norwegian,
            SystemLanguage.Romanian,
            SystemLanguage.SerboCroatian,
            SystemLanguage.Slovak,
            SystemLanguage.Slovenian,
            SystemLanguage.Swedish,
            SystemLanguage.Vietnamese,
        };

        /// <summary>The player-facing name of a language (the same one the table and the AI prompts use).</summary>
        public static string DisplayName(SystemLanguage language) => LocalizationRuntime.DisplayName(language);

        /// <summary>True when the language is offered in the language picker.</summary>
        public static bool IsSelectable(SystemLanguage language) => !ExcludedLanguages.Contains(language);

        /// <summary>Every language the picker offers, once each (legacy enum aliases that share a value are skipped).</summary>
        public static List<SystemLanguage> SelectableLanguages()
        {
            var result = new List<SystemLanguage>();
            var seenValues = new HashSet<int>();
            foreach (SystemLanguage language in Enum.GetValues(typeof(SystemLanguage)))
            {
                if (ExcludedLanguages.Contains(language) || !seenValues.Add((int)language))
                {
                    continue;
                }

                result.Add(language);
            }

            return result;
        }

        /// <summary>True when the entry has text in every language of the project.</summary>
        public static bool IsFullyTranslated(LocalizationData data, LocalizationEntry entry)
        {
            foreach (var language in data.Languages)
            {
                if (string.IsNullOrEmpty(entry.GetValue(language)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>How many of the project's languages the entry has no text for.</summary>
        public static int CountMissing(LocalizationData data, LocalizationEntry entry)
        {
            var missing = 0;
            foreach (var language in data.Languages)
            {
                if (string.IsNullOrEmpty(entry.GetValue(language)))
                {
                    missing++;
                }
            }

            return missing;
        }

        /// <summary>True when the entry has no text in any language (a row with no content worth keeping).</summary>
        public static bool IsEmptyRow(LocalizationData data, LocalizationEntry entry) =>
            CountMissing(data, entry) == data.Languages.Count;

        // "Clear Data" nukes every entry, so any LocalizedText left pointing at one of those now-gone
        // keys would just show its raw key on screen forever - remove them too, so the scene ends up
        // consistent with the (now-empty) data. [Localize] attributes are untouched - those live in
        // code, not on scene objects, and just mark fields for the next "Sync Project" to find again.
        /// <summary>Destroys every <see cref="LocalizedText"/> in the open scene(s); used by "Clear Data".</summary>
        public static void RemoveAllLocalizedTextComponents()
        {
            foreach (var localizedText in UnityEngine.Object.FindObjectsByType<LocalizedText>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(localizedText);
            }
        }

        // Swaps a language column with its adjacent neighbor. The source column's position is fixed
        // at index 0 and is never passed to this method, and the clamp below keeps every other column
        // from ever landing on index 0.
        /// <summary>Moves a language column one step left (-1) or right (+1); the pinned source column at index 0 never moves. Returns true when something moved.</summary>
        public static bool MoveLanguage(LocalizationData data, SystemLanguage language, int direction)
        {
            var index = data.Languages.IndexOf(language);
            if (index < 0)
            {
                return false;
            }

            var targetIndex = index + direction;
            if (targetIndex < 1 || targetIndex >= data.Languages.Count)
            {
                return false;
            }

            (data.Languages[index], data.Languages[targetIndex]) = (data.Languages[targetIndex], data.Languages[index]);
            return true;
        }

        /// <summary>Pins <paramref name="language"/> as the source, always at index 0 of the language list; whatever was first shifts right.</summary>
        public static void SetSourceLanguage(LocalizationData data, SystemLanguage language)
        {
            data.Languages.Remove(language);
            data.Languages.Insert(0, language);
            data.SourceLanguage = language;
        }

        /// <summary>Adds or removes a language. Removing the source makes the first remaining language the source. Returns true when the list changed.</summary>
        public static bool SetLanguageIncluded(LocalizationData data, SystemLanguage language, bool included)
        {
            var current = data.Languages.Contains(language);
            if (current == included)
            {
                return false;
            }

            if (included)
            {
                data.Languages.Add(language);
            }
            else
            {
                data.Languages.Remove(language);
                if (data.SourceLanguage == language && data.Languages.Count > 0)
                {
                    data.SourceLanguage = data.Languages[0];
                }
            }

            return true;
        }

        // Reverse lookup from a display name (as returned by the AI) back to a SystemLanguage value -
        // restricted to languages the picker actually offers, unlike LocalizationRuntime.ParseDisplayName
        // itself (used as-is, unrestricted, by CSV import - a translator's file might legitimately
        // reference a language outside this project's current Steam-relevance shortlist).
        /// <summary>Maps a language display name back to a selectable <see cref="SystemLanguage"/>, or null.</summary>
        public static SystemLanguage? ParseLanguageName(string name)
        {
            var language = LocalizationRuntime.ParseDisplayName(name);
            return language.HasValue && !ExcludedLanguages.Contains(language.Value) ? language : null;
        }

        // If the AI detected the source text is actually written in a different language than
        // data.SourceLanguage claims, fix that up automatically: move every entry's source-column
        // text over to the correct language column and repoint SourceLanguage at it. The old language
        // is left in Languages (now just an empty regular column) rather than removed, so this never
        // destroys a language the user deliberately added.
        /// <summary>Corrects the source language when a translation call detected another one. Returns true when a correction was applied.</summary>
        public static bool ApplyDetectedSourceLanguage(LocalizationData data, string detectedName, out SystemLanguage oldSource, out SystemLanguage newSource)
        {
            oldSource = data.SourceLanguage;
            newSource = data.SourceLanguage;

            var detected = ParseLanguageName(detectedName);
            if (detected == null || detected.Value == data.SourceLanguage)
            {
                return false;
            }

            oldSource = data.SourceLanguage;
            newSource = detected.Value;

            foreach (var entry in data.Entries)
            {
                var text = entry.GetValue(oldSource);
                if (text == null)
                {
                    continue;
                }

                entry.SetValue(newSource, text);
                entry.RemoveValue(oldSource);
            }

            // Pins the real source at index 0, which naturally pushes the old (wrongly-assumed)
            // source - previously first - to sit right next to it at index 1.
            SetSourceLanguage(data, newSource);
            return true;
        }

        // Every entry that has source text but is still missing at least one other language, paired
        // with exactly the languages it's missing. Re-run after a source-language correction, since
        // that can turn a previously-"complete" entry's old source cell into a fresh gap.
        /// <summary>The entries a translation run would fill, each with the languages it lacks.</summary>
        public static List<(LocalizationEntry Entry, List<SystemLanguage> Targets)> BuildPendingTranslations(LocalizationData data)
        {
            var pending = new List<(LocalizationEntry, List<SystemLanguage>)>();
            foreach (var entry in data.Entries)
            {
                if (string.IsNullOrEmpty(entry.GetValue(data.SourceLanguage)))
                {
                    continue;
                }

                var targets = data.Languages
                    .Where(language => language != data.SourceLanguage && string.IsNullOrEmpty(entry.GetValue(language)))
                    .ToList();

                if (targets.Count > 0)
                {
                    pending.Add((entry, targets));
                }
            }

            return pending;
        }

        // Walks every provider (in enum declaration order) looking for one that isn't already known
        // to be exhausted this batch and has an API key configured - returns null if none qualify, so
        // the caller can fall back to stopping the batch instead of assuming a provider is available.
        /// <summary>The next provider with an API key that is not in <paramref name="exhaustedProviders"/>, or null.</summary>
        public static AiProvider? FindNextAvailableProvider(HashSet<AiProvider> exhaustedProviders)
        {
            foreach (AiProvider candidate in Enum.GetValues(typeof(AiProvider)))
            {
                if (exhaustedProviders.Contains(candidate))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(AiProviderSettings.GetApiKey(candidate)))
                {
                    return candidate;
                }
            }

            return null;
        }

        // Built-in genre presets (GameContextPresets) followed by the project's own saved custom
        // ones - picking any of them overwrites the Game Context text with that prompt.
        /// <summary>The built-in Game Context presets followed by the project's saved custom ones.</summary>
        public static (string Name, string Prompt)[] AllGameContextPresets(LocalizationData data) =>
            GameContextPresets.BuiltIn
                .Concat(data.CustomGameContextPresets.Select(p => (p.Name, p.Text)))
                .ToArray();

        // Overwrites an existing custom preset with the same name rather than creating a duplicate.
        /// <summary>Saves (or overwrites by name) a custom Game Context preset; returns its index in <see cref="AllGameContextPresets"/>.</summary>
        public static int SaveCustomGameContextPreset(LocalizationData data, string name, string prompt)
        {
            var existing = data.CustomGameContextPresets.FirstOrDefault(p => p.Name == name);
            if (existing != null)
            {
                existing.Text = prompt;
            }
            else
            {
                data.CustomGameContextPresets.Add(new NamedPrompt { Name = name, Text = prompt });
            }

            return Array.IndexOf(AllGameContextPresets(data).Select(p => p.Name).ToArray(), name);
        }

        // Computed from LanguageCodeMap rather than hardcoded, so it stays correct if the project's language list changes later.
        /// <summary>The project languages a per-language provider (Google Translate, DeepL) cannot translate into; empty for the others.</summary>
        public static List<string> UnsupportedLanguages(LocalizationData data, AiProvider provider)
        {
            IReadOnlyDictionary<SystemLanguage, string> codeMap = provider switch
            {
                AiProvider.GoogleTranslate => LanguageCodeMap.Google,
                AiProvider.DeepL => LanguageCodeMap.DeepL,
                _ => null,
            };

            if (codeMap == null)
            {
                return new List<string>();
            }

            return data.Languages.Where(language => !codeMap.ContainsKey(language)).Select(DisplayName).ToList();
        }

        /// <summary>"now", "in 40s" or "in 12m" for a quota reset time.</summary>
        public static string FormatResetTime(DateTimeOffset resetAt)
        {
            var remaining = resetAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return "now";
            }

            return remaining.TotalMinutes < 2
                ? $"in {(int)remaining.TotalSeconds}s"
                : $"in {(int)remaining.TotalMinutes}m";
        }

        /// <summary>True when the provider reported its quota as used up and the reset time has not passed.</summary>
        public static bool IsBlocked(RateLimitInfo rateLimit) =>
            rateLimit.IsExhausted && (!rateLimit.ResetAt.HasValue || rateLimit.ResetAt.Value > DateTimeOffset.UtcNow);
    }
}
