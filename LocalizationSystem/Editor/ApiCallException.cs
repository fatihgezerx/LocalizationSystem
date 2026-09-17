using System;
using System.Net.Http.Headers;

namespace LocalizationSystem
{
    /// <summary>
    /// Thrown by <see cref="HttpRetry"/> when a call fails for good (not transient, or retries
    /// exhausted). Carries the raw status/body/headers so a provider can still pull rate-limit info
    /// out of a failed call (e.g. Gemini's quota details only ever show up in a 429 body) before the
    /// exception propagates up to the caller as a normal failure.
    /// </summary>
    internal sealed class ApiCallException : Exception
    {
        public int StatusCode { get; }
        public string ResponseBody { get; }
        public HttpResponseHeaders Headers { get; }

        public ApiCallException(int statusCode, string responseBody, HttpResponseHeaders headers)
            : base($"API error ({statusCode}): {responseBody}")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
            Headers = headers;
        }
    }
}
