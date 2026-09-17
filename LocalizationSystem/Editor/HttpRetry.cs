using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace LocalizationSystem
{
    /// <summary>The body and headers of a successful API response.</summary>
    internal sealed class HttpJsonResponse
    {
        public string Body;
        public HttpResponseHeaders Headers;
    }

    /// <summary>
    /// POSTs JSON with a shared retry policy - used by every <see cref="ITranslationProvider"/> so the
    /// "model overloaded / rate limited, wait and try again" behavior is identical no matter which AI
    /// backend is selected, instead of being reimplemented per provider. Also surfaces response
    /// headers (on success) or an <see cref="ApiCallException"/> (on failure) with the raw
    /// status/body/headers still attached, since that's where OpenAI/Anthropic/Gemini each report
    /// their own rate-limit info.
    /// </summary>
    internal static class HttpRetry
    {
        // 503 = model temporarily overloaded, 429 = rate limit hit - both are transient and worth
        // retrying on their own; anything else (bad API key, malformed request, daily quota
        // exhausted, etc.) is a real failure that retrying won't fix, so it's left to throw
        // immediately instead of burning through the same limited quota on retries.
        private const int MaxAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(8);

        private static readonly HttpClient Client = new();

        public static async Task<HttpJsonResponse> PostJsonAsync(string url, string json, Action<HttpRequestMessage> configureRequest = null)
        {
            for (var attempt = 1; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                configureRequest?.Invoke(request);

                using var response = await Client.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return new HttpJsonResponse { Body = responseText, Headers = response.Headers };
                }

                var statusCode = (int)response.StatusCode;
                var isTransient = statusCode == 503 || statusCode == 429;
                if (!isTransient || attempt >= MaxAttempts)
                {
                    throw new ApiCallException(statusCode, responseText, response.Headers);
                }

                await Task.Delay(RetryDelay);
            }
        }

        /// <summary>First value of a response header by name, or null if it wasn't sent.</summary>
        public static string GetHeader(HttpResponseHeaders headers, string name) =>
            headers != null && headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
