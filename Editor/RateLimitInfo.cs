using System;

namespace LocalizationSystem
{
    /// <summary>
    /// What a translation provider most recently reported about its own rate limit/quota - only as
    /// accurate as what that provider's API actually exposes. OpenAI and Anthropic send this on every
    /// response, so it's known before you'd ever hit the limit. Gemini's free tier doesn't report
    /// anything proactively - the only way to know it's exhausted is a failed call, so
    /// <see cref="HasData"/> stays false until that happens.
    /// </summary>
    internal sealed class RateLimitInfo
    {
        public bool HasData;
        public int? RemainingRequests;
        public int? LimitRequests;
        public DateTimeOffset? ResetAt;
        public bool IsExhausted;

        /// <summary>Set when a reported reset time isn't guaranteed accurate for every quota the provider enforces (e.g. Gemini's daily cap).</summary>
        public string Caveat;
    }
}
