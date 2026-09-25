using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LocalizationSystem
{
    /// <summary>
    /// Translates via the Google Cloud Translation API - plain machine translation, not an LLM, and a
    /// different Google product/API key than Gemini (console.cloud.google.com, not
    /// aistudio.google.com - the two are easy to mix up since both say "Google").
    /// </summary>
    /// <remarks>
    /// This endpoint translates to exactly one target language per call, so unlike the LLM providers
    /// this makes one call per target language (in parallel via <see cref="PerLanguageTranslation"/>)
    /// and combines the results. It also has no concept of "tone" or context the way an LLM prompt
    /// does - <c>gameContext</c> is accepted for interface parity but has no effect here.
    /// </remarks>
    internal sealed class GoogleTranslateProvider : ITranslationProvider
    {
        private const string Url = "https://translation.googleapis.com/language/translate/v2";

        public string DisplayName => "Google Translate";
        public string ApiKeyHelpUrl => "https://console.cloud.google.com/apis/library/translate.googleapis.com";
        public bool SupportsGameContext => false;

        // The default project quota is roughly 300,000 requests/minute, and every target language
        // for an entry is already sent in parallel (see PerLanguageTranslation), so there's little
        // reason to pace between entries much at all.
        public TimeSpan RequestDelay => TimeSpan.FromSeconds(0.5);

        public RateLimitInfo LastKnownRateLimit { get; private set; } = new();

        [Serializable] private sealed class RequestBody { public string[] q; public string target; public string format = "text"; }
        [Serializable] private sealed class Translation { public string translatedText; public string detectedSourceLanguage; }
        [Serializable] private sealed class TranslationsData { public Translation[] translations; }
        [Serializable] private sealed class ResponseBody { public TranslationsData data; }

        public Task<TranslationResult> TranslateAsync(
            string sourceText, IReadOnlyList<SystemLanguage> targetLanguages, string gameContext, string apiKey) =>
            PerLanguageTranslation.RunAsync(DisplayName, targetLanguages, LanguageCodeMap.Google,
                language => TranslateOneAsync(sourceText, language, apiKey));

        private async Task<PerLanguageResult> TranslateOneAsync(string sourceText, SystemLanguage language, string apiKey)
        {
            try
            {
                var requestBody = new RequestBody { q = new[] { sourceText }, target = LanguageCodeMap.Google[language] };
                var requestJson = JsonUtility.ToJson(requestBody);
                var url = $"{Url}?key={UnityWebRequest.EscapeURL(apiKey)}";

                var response = await HttpRetry.PostJsonAsync(url, requestJson);
                LastKnownRateLimit = new RateLimitInfo();

                var parsed = JsonUtility.FromJson<ResponseBody>(response.Body);
                var translation = parsed?.data?.translations is { Length: > 0 } translations ? translations[0] : null;

                if (translation == null || string.IsNullOrEmpty(translation.translatedText))
                {
                    Debug.LogError($"[LocalizationSystem] Google Translate returned an empty response for {language}.");
                    return PerLanguageResult.Failed(language);
                }

                var detectedName = LanguageCodeMap.ReverseLookupDisplayName(LanguageCodeMap.Google, translation.detectedSourceLanguage);
                return PerLanguageResult.Ok(language, translation.translatedText, detectedName);
            }
            catch (ApiCallException ex)
            {
                LastKnownRateLimit = new RateLimitInfo { HasData = true, IsExhausted = ex.StatusCode == 429 };
                Debug.LogError($"[LocalizationSystem] Google Translate failed for {language}: {ex.Message}");
                return PerLanguageResult.Failed(language);
            }
        }
    }
}
