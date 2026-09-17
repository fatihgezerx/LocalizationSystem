using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>Translates via DeepL's API - plain machine translation, not an LLM.</summary>
    /// <remarks>
    /// Free-tier keys are suffixed ":fx" and only work against the free-tier host; Pro keys use the
    /// regular one - detected automatically from the key itself, no separate setting needed. Like
    /// Google Translate, this only takes one target language per call, so this makes one call per
    /// target language (in parallel via <see cref="PerLanguageTranslation"/>) and combines the
    /// results, and has no concept of "tone" or context - <c>gameContext</c> is accepted for
    /// interface parity but has no effect here. DeepL also doesn't support every language this
    /// project might use - see <see cref="LanguageCodeMap.DeepL"/> for what's missing.
    /// </remarks>
    internal sealed class DeepLTranslationProvider : ITranslationProvider
    {
        public string DisplayName => "DeepL";
        public string ApiKeyHelpUrl => "https://www.deepl.com/your-account/keys";
        public bool SupportsGameContext => false;

        // DeepL doesn't publish a specific requests/second figure the way OpenAI/Anthropic do - kept
        // a bit more cautious than Google Translate as a result, though every target language for an
        // entry is already sent in parallel (see PerLanguageTranslation).
        public TimeSpan RequestDelay => TimeSpan.FromSeconds(0.7);

        public RateLimitInfo LastKnownRateLimit { get; private set; } = new();

        [Serializable] private sealed class RequestBody { public string[] text; public string target_lang; }
        [Serializable] private sealed class Translation { public string text; public string detected_source_language; }
        [Serializable] private sealed class ResponseBody { public Translation[] translations; }

        public Task<TranslationResult> TranslateAsync(
            string sourceText, IReadOnlyList<SystemLanguage> targetLanguages, string gameContext, string apiKey) =>
            PerLanguageTranslation.RunAsync(DisplayName, targetLanguages, LanguageCodeMap.DeepL,
                language => TranslateOneAsync(sourceText, language, apiKey));

        private async Task<PerLanguageResult> TranslateOneAsync(string sourceText, SystemLanguage language, string apiKey)
        {
            try
            {
                var requestBody = new RequestBody { text = new[] { sourceText }, target_lang = LanguageCodeMap.DeepL[language] };
                var requestJson = JsonUtility.ToJson(requestBody);

                // Free-tier keys are suffixed ":fx" and only work against the free-tier host - using
                // the wrong host for a given key fails outright, so pick it from the key itself.
                var host = apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-free.deepl.com"
                    : "https://api.deepl.com";

                var response = await HttpRetry.PostJsonAsync($"{host}/v2/translate", requestJson, request =>
                {
                    request.Headers.Add("Authorization", $"DeepL-Auth-Key {apiKey}");
                });

                LastKnownRateLimit = new RateLimitInfo();

                var parsed = JsonUtility.FromJson<ResponseBody>(response.Body);
                var translation = parsed?.translations is { Length: > 0 } translations ? translations[0] : null;

                if (translation == null || string.IsNullOrEmpty(translation.text))
                {
                    Debug.LogError($"[LocalizationSystem] DeepL returned an empty response for {language}.");
                    return PerLanguageResult.Failed(language);
                }

                var detectedName = LanguageCodeMap.ReverseLookupDisplayName(LanguageCodeMap.DeepL, translation.detected_source_language);
                return PerLanguageResult.Ok(language, translation.text, detectedName);
            }
            catch (ApiCallException ex)
            {
                // DeepL uses the non-standard HTTP 456 for "quota exceeded" (monthly character
                // limit), on top of the usual 429 for short-term rate limiting.
                LastKnownRateLimit = new RateLimitInfo { HasData = true, IsExhausted = ex.StatusCode == 429 || ex.StatusCode == 456 };
                Debug.LogError($"[LocalizationSystem] DeepL failed for {language}: {ex.Message}");
                return PerLanguageResult.Failed(language);
            }
        }
    }
}
