using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// Runs one <see cref="PerLanguageResult"/>-producing call per target language in parallel and
    /// combines whatever succeeded into a single <see cref="TranslationResult"/> - shared by
    /// <see cref="GoogleTranslateProvider"/> and <see cref="DeepLTranslationProvider"/> so a transient
    /// failure on one language (rate limit, unsupported code, etc.) never throws away translations
    /// that already succeeded for the others in the same entry.
    /// </summary>
    internal static class PerLanguageTranslation
    {
        public static async Task<TranslationResult> RunAsync(
            string providerDisplayName,
            IReadOnlyList<SystemLanguage> targetLanguages,
            IReadOnlyDictionary<SystemLanguage, string> supportedLanguageCodes,
            Func<SystemLanguage, Task<PerLanguageResult>> translateOne)
        {
            var supportedLanguages = targetLanguages.Where(supportedLanguageCodes.ContainsKey).ToList();
            var outcomes = await Task.WhenAll(supportedLanguages.Select(translateOne));

            var result = new TranslationResult();
            foreach (var outcome in outcomes)
            {
                if (!outcome.Success)
                {
                    continue;
                }

                result.Translations[LocalizationRuntime.DisplayName(outcome.Language)] = outcome.Text;
                result.DetectedSourceLanguage ??= outcome.DetectedSourceLanguage;
            }

            if (result.Translations.Count == 0 && supportedLanguages.Count > 0)
            {
                throw new InvalidOperationException($"{providerDisplayName} failed for every requested language - see Console for the specific error(s).");
            }

            return result;
        }
    }
}
