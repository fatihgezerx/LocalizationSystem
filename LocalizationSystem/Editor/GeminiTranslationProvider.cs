using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LocalizationSystem
{
    /// <summary>Translates via Google's Gemini API.</summary>
    internal sealed class GeminiTranslationProvider : ITranslationProvider
    {
        // gemini-2.0-flash was retired by Google (API returned 404 pointing here as the replacement).
        // Swap this one constant if Google renames or retires it again - nothing else here depends
        // on the exact model id.
        private const string Model = "gemini-3.6-flash";

        public string DisplayName => "Google Gemini";
        public string ApiKeyHelpUrl => "https://aistudio.google.com/api-keys";
        public bool SupportsGameContext => true;

        // Free-tier Gemini flash models are documented around 10-15 requests/minute - 4.5s keeps a
        // safe margin under that without being needlessly slow for a paid tier's much higher limits.
        public TimeSpan RequestDelay => TimeSpan.FromSeconds(4.5);

        public RateLimitInfo LastKnownRateLimit { get; private set; } = new();

        [Serializable] private sealed class GeminiPart { public string text; }
        [Serializable] private sealed class GeminiContent { public GeminiPart[] parts; }
        [Serializable] private sealed class GeminiRequestBody { public GeminiContent[] contents; }
        [Serializable] private sealed class GeminiCandidate { public GeminiContent content; }
        [Serializable] private sealed class GeminiResponseBody { public GeminiCandidate[] candidates; }

        public async Task<TranslationResult> TranslateAsync(
            string sourceText, IReadOnlyList<SystemLanguage> targetLanguages, string gameContext, string apiKey)
        {
            var prompt = TranslationPromptBuilder.Build(sourceText, targetLanguages, gameContext);
            var requestBody = new GeminiRequestBody
            {
                contents = new[] { new GeminiContent { parts = new[] { new GeminiPart { text = prompt } } } },
            };

            var requestJson = JsonUtility.ToJson(requestBody);
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={UnityWebRequest.EscapeURL(apiKey)}";

            HttpJsonResponse response;
            try
            {
                response = await HttpRetry.PostJsonAsync(url, requestJson);
            }
            catch (ApiCallException ex)
            {
                LastKnownRateLimit = ParseRateLimitFromError(ex);
                throw;
            }

            // Gemini doesn't send any rate-limit headers on success - the free tier's remaining
            // quota is simply unknown until a call actually fails for exceeding it.
            LastKnownRateLimit = new RateLimitInfo();

            var parsedResponse = JsonUtility.FromJson<GeminiResponseBody>(response.Body);
            string rawText = null;
            if (parsedResponse?.candidates is { Length: > 0 } candidates)
            {
                var parts = candidates[0].content?.parts;
                if (parts is { Length: > 0 })
                {
                    rawText = parts[0].text;
                }
            }

            if (string.IsNullOrEmpty(rawText))
            {
                throw new InvalidOperationException("Gemini returned an empty response.");
            }

            return TranslationPromptBuilder.ParseResponse(rawText);
        }

        // Gemini's 429 body embeds a retryDelay like "50s" and a quotaValue like "20" - pulled out
        // with a light-touch regex rather than a full JSON model, since the exact shape varies by
        // which quota was hit and isn't worth a dedicated DTO just for this.
        private static RateLimitInfo ParseRateLimitFromError(ApiCallException ex)
        {
            var info = new RateLimitInfo { HasData = true, IsExhausted = ex.StatusCode == 429 };

            var retryMatch = Regex.Match(ex.ResponseBody, "\"retryDelay\"\\s*:\\s*\"(\\d+)s\"");
            if (retryMatch.Success && int.TryParse(retryMatch.Groups[1].Value, out var retrySeconds))
            {
                info.ResetAt = DateTimeOffset.UtcNow.AddSeconds(retrySeconds);
                info.Caveat = "Gemini only reports this for the specific limit the last call hit - " +
                              "a daily quota's real reset time may be later than this.";
            }

            var quotaMatch = Regex.Match(ex.ResponseBody, "\"quotaValue\"\\s*:\\s*\"(\\d+)\"");
            if (quotaMatch.Success && int.TryParse(quotaMatch.Groups[1].Value, out var quotaValue))
            {
                info.LimitRequests = quotaValue;
                info.RemainingRequests = 0;
            }

            return info;
        }
    }
}
