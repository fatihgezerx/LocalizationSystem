using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// One target language's outcome for a provider that has to call its API once per language
    /// (Google Translate, DeepL) rather than once for a whole batch the way the LLM providers do.
    /// </summary>
    internal readonly struct PerLanguageResult
    {
        public readonly bool Success;
        public readonly SystemLanguage Language;
        public readonly string Text;
        public readonly string DetectedSourceLanguage;

        private PerLanguageResult(bool success, SystemLanguage language, string text, string detectedSourceLanguage)
        {
            Success = success;
            Language = language;
            Text = text;
            DetectedSourceLanguage = detectedSourceLanguage;
        }

        public static PerLanguageResult Ok(SystemLanguage language, string text, string detectedSourceLanguage) =>
            new(true, language, text, detectedSourceLanguage);

        public static PerLanguageResult Failed(SystemLanguage language) => new(false, language, null, null);
    }
}
