using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>Translates via Anthropic's Claude Messages API.</summary>
    internal sealed class ClaudeTranslationProvider : ITranslationProvider
    {
        // Swap this one constant if Anthropic renames or retires it - nothing else here depends on
        // the exact model id. Haiku is the fast/cheap tier, matching the other two providers' default
        // model choice.
        private const string Model = "claude-haiku-4-5-20251001";
        private const string Url = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";
        private const int MaxTokens = 2048;

        public string DisplayName => "Anthropic Claude";
        public string ApiKeyHelpUrl => "https://console.anthropic.com/settings/keys";
        public bool SupportsGameContext => true;

        // Build Tier 1 (after a $5 credit purchase) allows around 50 requests/minute - 1.5s keeps a
        // safe margin under the ~1.2s theoretical minimum spacing.
        public TimeSpan RequestDelay => TimeSpan.FromSeconds(1.5);

        public RateLimitInfo LastKnownRateLimit { get; private set; } = new();

        [Serializable] private sealed class Message { public string role; public string content; }
        [Serializable] private sealed class RequestBody { public string model; public int max_tokens; public Message[] messages; }

        [Serializable] private sealed class ContentBlock { public string type; public string text; }
        [Serializable] private sealed class ResponseBody { public ContentBlock[] content; }

        public async Task<TranslationResult> TranslateAsync(
            string sourceText, IReadOnlyList<SystemLanguage> targetLanguages, string gameContext, string apiKey)
        {
            var prompt = TranslationPromptBuilder.Build(sourceText, targetLanguages, gameContext);
            var requestBody = new RequestBody
            {
                model = Model,
                max_tokens = MaxTokens,
                messages = new[] { new Message { role = "user", content = prompt } },
            };

            var requestJson = JsonUtility.ToJson(requestBody);

            HttpJsonResponse response;
            try
            {
                response = await HttpRetry.PostJsonAsync(Url, requestJson, request =>
                {
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", AnthropicVersion);
                });
            }
            catch (ApiCallException ex)
            {
                LastKnownRateLimit = ReadRateLimit(ex.Headers, ex.StatusCode == 429);
                throw;
            }

            LastKnownRateLimit = ReadRateLimit(response.Headers, isExhausted: false);

            var parsedResponse = JsonUtility.FromJson<ResponseBody>(response.Body);
            var textBlock = Array.Find(parsedResponse?.content ?? Array.Empty<ContentBlock>(), block => block.type == "text");
            var rawText = textBlock?.text;

            if (string.IsNullOrEmpty(rawText))
            {
                throw new InvalidOperationException("Claude returned an empty response.");
            }

            return TranslationPromptBuilder.ParseResponse(rawText);
        }

        // Anthropic sends these on every response (docs.anthropic.com rate limits), so unlike
        // Gemini this is known in advance rather than only after a failure.
        private static RateLimitInfo ReadRateLimit(HttpResponseHeaders headers, bool isExhausted)
        {
            var info = new RateLimitInfo { IsExhausted = isExhausted };

            if (int.TryParse(HttpRetry.GetHeader(headers, "anthropic-ratelimit-requests-remaining"), out var remaining))
            {
                info.HasData = true;
                info.RemainingRequests = remaining;
            }

            if (int.TryParse(HttpRetry.GetHeader(headers, "anthropic-ratelimit-requests-limit"), out var limit))
            {
                info.HasData = true;
                info.LimitRequests = limit;
            }

            var resetHeader = HttpRetry.GetHeader(headers, "anthropic-ratelimit-requests-reset");
            if (DateTimeOffset.TryParse(resetHeader, CultureInfo.InvariantCulture, DateTimeStyles.None, out var resetAt))
            {
                info.HasData = true;
                info.ResetAt = resetAt;
            }

            return info;
        }
    }
}
