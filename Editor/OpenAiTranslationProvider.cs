using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>Translates via OpenAI's Chat Completions API.</summary>
    internal sealed class OpenAiTranslationProvider : ITranslationProvider
    {
        // Swap this one constant if OpenAI renames or retires it - nothing else here depends on the
        // exact model id.
        private const string Model = "gpt-4o-mini";
        private const string Url = "https://api.openai.com/v1/chat/completions";

        public string DisplayName => "OpenAI (ChatGPT)";
        public string ApiKeyHelpUrl => "https://platform.openai.com/api-keys";
        public bool SupportsGameContext => true;

        // Tier 1 (after a first successful payment) allows around 500 requests/minute for
        // gpt-4o-mini - 0.5s is comfortably under that, no reason to pace this as cautiously as
        // Gemini's free tier.
        public TimeSpan RequestDelay => TimeSpan.FromSeconds(0.5);

        public RateLimitInfo LastKnownRateLimit { get; private set; } = new();

        [Serializable] private sealed class Message { public string role; public string content; }
        [Serializable] private sealed class ResponseFormat { public string type = "json_object"; }
        [Serializable] private sealed class RequestBody { public string model; public Message[] messages; public ResponseFormat response_format; }

        [Serializable] private sealed class Choice { public Message message; }
        [Serializable] private sealed class ResponseBody { public Choice[] choices; }

        public async Task<TranslationResult> TranslateAsync(
            string sourceText, IReadOnlyList<SystemLanguage> targetLanguages, string gameContext, string apiKey)
        {
            var prompt = TranslationPromptBuilder.Build(sourceText, targetLanguages, gameContext);
            var requestBody = new RequestBody
            {
                model = Model,
                messages = new[] { new Message { role = "user", content = prompt } },
                response_format = new ResponseFormat(),
            };

            var requestJson = JsonUtility.ToJson(requestBody);

            HttpJsonResponse response;
            try
            {
                response = await HttpRetry.PostJsonAsync(Url, requestJson, request =>
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                });
            }
            catch (ApiCallException ex)
            {
                LastKnownRateLimit = ReadRateLimit(ex.Headers, ex.StatusCode == 429);
                throw;
            }

            LastKnownRateLimit = ReadRateLimit(response.Headers, isExhausted: false);

            var parsedResponse = JsonUtility.FromJson<ResponseBody>(response.Body);
            var rawText = parsedResponse?.choices is { Length: > 0 } choices ? choices[0].message?.content : null;

            if (string.IsNullOrEmpty(rawText))
            {
                throw new InvalidOperationException("OpenAI returned an empty response.");
            }

            return TranslationPromptBuilder.ParseResponse(rawText);
        }

        // OpenAI sends these on every response (docs.openai.com rate-limit headers), so unlike
        // Gemini this is known in advance rather than only after a failure.
        private static RateLimitInfo ReadRateLimit(HttpResponseHeaders headers, bool isExhausted)
        {
            var info = new RateLimitInfo { IsExhausted = isExhausted };

            if (int.TryParse(HttpRetry.GetHeader(headers, "x-ratelimit-remaining-requests"), out var remaining))
            {
                info.HasData = true;
                info.RemainingRequests = remaining;
            }

            if (int.TryParse(HttpRetry.GetHeader(headers, "x-ratelimit-limit-requests"), out var limit))
            {
                info.HasData = true;
                info.LimitRequests = limit;
            }

            var resetIn = ParseResetDuration(HttpRetry.GetHeader(headers, "x-ratelimit-reset-requests"));
            if (resetIn.HasValue)
            {
                info.HasData = true;
                info.ResetAt = DateTimeOffset.UtcNow + resetIn.Value;
            }

            return info;
        }

        // OpenAI reports resets as a short duration string like "6m0s" or "1s", not an absolute
        // timestamp - parsed here into a TimeSpan so the caller can turn it into one.
        private static TimeSpan? ParseResetDuration(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var match = Regex.Match(value, @"^(?:(\d+)h)?(?:(\d+)m)?(?:([\d.]+)s)?$");
            if (!match.Success)
            {
                return null;
            }

            var hours = match.Groups[1].Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var minutes = match.Groups[2].Success ? double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            var seconds = match.Groups[3].Success ? double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;

            return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }
    }
}
