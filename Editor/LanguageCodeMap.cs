using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// Maps <see cref="SystemLanguage"/> to the language codes plain machine-translation APIs expect
    /// (Google Cloud Translation, DeepL) - unlike the LLM providers, which just get told the language
    /// name in English inside the prompt.
    /// </summary>
    internal static class LanguageCodeMap
    {
        public static readonly Dictionary<SystemLanguage, string> Google = new()
        {
            { SystemLanguage.English, "en" },
            { SystemLanguage.French, "fr" },
            { SystemLanguage.German, "de" },
            { SystemLanguage.Italian, "it" },
            { SystemLanguage.Spanish, "es" },
            { SystemLanguage.Portuguese, "pt" },
            { SystemLanguage.Polish, "pl" },
            { SystemLanguage.Russian, "ru" },
            { SystemLanguage.Turkish, "tr" },
            { SystemLanguage.ChineseSimplified, "zh-CN" },
            { SystemLanguage.ChineseTraditional, "zh-TW" },
            { SystemLanguage.Thai, "th" },
            { SystemLanguage.Ukrainian, "uk" },
            { SystemLanguage.Korean, "ko" },
            { SystemLanguage.Japanese, "ja" },
        };

        // DeepL doesn't cover every language Google does - as of writing it has no code for Thai or
        // Traditional Chinese, so those two are simply absent here. AttributeFieldScanner-scanned
        // fields or scene text needing those two just won't get filled in by DeepL; another provider
        // (Gemini/OpenAI/Claude/Google Translate) can pick up whatever's still empty afterward.
        public static readonly Dictionary<SystemLanguage, string> DeepL = new()
        {
            { SystemLanguage.English, "EN-US" },
            { SystemLanguage.French, "FR" },
            { SystemLanguage.German, "DE" },
            { SystemLanguage.Italian, "IT" },
            { SystemLanguage.Spanish, "ES" },
            { SystemLanguage.Portuguese, "PT-PT" },
            { SystemLanguage.Polish, "PL" },
            { SystemLanguage.Russian, "RU" },
            { SystemLanguage.Turkish, "TR" },
            { SystemLanguage.ChineseSimplified, "ZH" },
            { SystemLanguage.Ukrainian, "UK" },
            { SystemLanguage.Korean, "KO" },
            { SystemLanguage.Japanese, "JA" },
        };

        /// <summary>Given a provider's own code for whatever language it detected as the source, finds the matching <see cref="LocalizationRuntime.DisplayName"/> - or null if the code isn't in the map.</summary>
        public static string ReverseLookupDisplayName(IReadOnlyDictionary<SystemLanguage, string> codeMap, string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            foreach (var pair in codeMap)
            {
                if (string.Equals(pair.Value, code, StringComparison.OrdinalIgnoreCase))
                {
                    return LocalizationRuntime.DisplayName(pair.Key);
                }
            }

            return null;
        }
    }
}
