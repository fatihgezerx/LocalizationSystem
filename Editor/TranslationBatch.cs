using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// One "Translate" run: fills every missing cell of the table through the selected provider, paces the requests, shows a
    /// cancelable progress bar, switches to another provider with an API key when one runs out of quota and corrects the source
    /// language when the AI detects another one. Shared by both Language Data windows.
    /// </summary>
    internal static class TranslationBatch
    {
        /// <summary>
        /// Runs the batch. <paramref name="dataChanged"/> is called after every entry that received translations (the window marks itself
        /// dirty and repaints). Returns the summary text to show, or null when a dialog already told the user what is missing.
        /// </summary>
        public static async Task<string> RunAsync(LocalizationData data, Action dataChanged)
        {
            var provider = AiProviderSettings.GetProvider();
            var translationProvider = TranslationProviders.All[provider];
            var apiKey = AiProviderSettings.GetApiKey(provider);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                EditorUtility.DisplayDialog(
                    $"{translationProvider.DisplayName} API Key Required",
                    $"Open \"Settings\" and paste in a {translationProvider.DisplayName} API key first.",
                    "OK");
                return null;
            }

            var pending = LanguageDataLogic.BuildPendingTranslations(data);
            if (pending.Count == 0)
            {
                return "Nothing to translate - every cell already has text.";
            }

            var translatedFields = 0;
            var failedEntries = 0;
            var sourceLanguageChecked = false;
            var stoppedForQuota = false;
            var exhaustedProviders = new HashSet<AiProvider> { provider };
            var switchLog = new List<string>();
            string sourceCorrection = null;

            try
            {
                var i = 0;
                while (i < pending.Count)
                {
                    var (entry, targets) = pending[i];
                    var cancelled = EditorUtility.DisplayCancelableProgressBar(
                        $"Translating with {translationProvider.DisplayName}",
                        $"{entry.Key} ({i + 1}/{pending.Count})",
                        (float)i / pending.Count);

                    if (cancelled)
                    {
                        break;
                    }

                    var sourceText = entry.GetValue(data.SourceLanguage);

                    try
                    {
                        var result = await translationProvider.TranslateAsync(sourceText, targets, data.GameContext, apiKey);

                        // Only act on the first entry's detection - later entries could disagree
                        // (typos, ambiguous short strings) and we don't want to flip-flop mid-batch.
                        if (!sourceLanguageChecked)
                        {
                            sourceLanguageChecked = true;
                            if (LanguageDataLogic.ApplyDetectedSourceLanguage(data, result.DetectedSourceLanguage, out var oldSource, out var newSource))
                            {
                                sourceCorrection =
                                    $"Source language was set to {LanguageDataLogic.DisplayName(oldSource)}, but the text looked like " +
                                    $"{LanguageDataLogic.DisplayName(newSource)} - corrected automatically.";

                                // The language everyone assumed was source is now just a normal,
                                // empty column - rebuild the whole batch so it gets filled in too,
                                // instead of being left blank.
                                pending = LanguageDataLogic.BuildPendingTranslations(data);
                                i = -1;
                            }
                        }

                        foreach (var language in targets)
                        {
                            if (result.Translations.TryGetValue(LanguageDataLogic.DisplayName(language), out var text))
                            {
                                entry.SetValue(language, text);
                                translatedFields++;
                            }
                        }

                        if (result.Translations.Count > 0)
                        {
                            dataChanged?.Invoke();
                        }
                    }
                    catch (Exception ex)
                    {
                        failedEntries++;
                        Debug.LogError($"[LocalizationSystem] Translating '{entry.Key}' failed: {ex.Message}");

                        // The provider itself just reported it's out of quota - every remaining entry
                        // would fail the exact same way (after its own pointless retry/backoff wait).
                        // Rather than just stopping, look for another provider that has an API key
                        // configured and keep going with that one instead - only give up entirely once
                        // none are left (this never assumes a fallback exists; it's a plain null check).
                        if (LanguageDataLogic.IsBlocked(translationProvider.LastKnownRateLimit))
                        {
                            exhaustedProviders.Add(provider);
                            var nextProvider = LanguageDataLogic.FindNextAvailableProvider(exhaustedProviders);

                            if (nextProvider.HasValue)
                            {
                                var nextTranslationProvider = TranslationProviders.All[nextProvider.Value];
                                switchLog.Add($"{translationProvider.DisplayName} → {nextTranslationProvider.DisplayName}");

                                provider = nextProvider.Value;
                                translationProvider = nextTranslationProvider;
                                apiKey = AiProviderSettings.GetApiKey(provider);

                                // Retry this same entry with the new provider - don't advance i, and
                                // skip the pacing delay below since the new provider hasn't made a
                                // single request yet.
                                continue;
                            }

                            stoppedForQuota = true;
                            break;
                        }
                    }

                    i++;

                    // Pace requests so a big batch doesn't immediately hit the free tier's rate limit.
                    if (i < pending.Count)
                    {
                        await Task.Delay(translationProvider.RequestDelay);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var summary = $"Translated {translatedFields} field(s).";
            if (failedEntries > 0)
            {
                summary += $" {failedEntries} entr{(failedEntries == 1 ? "y" : "ies")} failed - see Console.";
            }

            if (switchLog.Count > 0)
            {
                summary += $" Switched provider after hitting a quota ({string.Join(", ", switchLog)}).";
            }

            if (stoppedForQuota)
            {
                summary += $" Stopped early - {translationProvider.DisplayName} quota is exhausted and no other provider has an API key set.";
            }

            if (sourceCorrection != null)
            {
                summary += " " + sourceCorrection;
            }

            return summary;
        }
    }
}
