using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>Result of one translation call: what language the source text was detected to actually be written in (if known), plus the translations themselves, keyed by <see cref="LocalizationRuntime.DisplayName"/>.</summary>
    internal sealed class TranslationResult
    {
        public string DetectedSourceLanguage;
        public Dictionary<string, string> Translations = new();
    }

    /// <summary>
    /// One backend capable of translating a piece of source text into several languages at once - an
    /// LLM prompted for all of them together (<see cref="GeminiTranslationProvider"/>,
    /// <see cref="OpenAiTranslationProvider"/>, <see cref="ClaudeTranslationProvider"/>, sharing their
    /// prompt wording and response parsing via <see cref="TranslationPromptBuilder"/>), or a plain
    /// machine-translation API that only takes one target language per call
    /// (<see cref="GoogleTranslateProvider"/>, <see cref="DeepLTranslationProvider"/>, which call
    /// themselves once per language internally). Either way, callers just get back one
    /// <see cref="TranslationResult"/> covering every language that succeeded.
    /// </summary>
    internal interface ITranslationProvider
    {
        string DisplayName { get; }
        string ApiKeyHelpUrl { get; }

        /// <summary>True for plain machine-translation APIs that have no concept of tone/genre, so the Game Context field has no effect on their output.</summary>
        bool SupportsGameContext { get; }

        /// <summary>Whatever this provider learned about its own rate limit/quota from the most recent call, if anything.</summary>
        RateLimitInfo LastKnownRateLimit { get; }

        /// <summary>
        /// How long to wait after one entry finishes translating before starting the next, tuned to
        /// this provider's own published rate limits - a value safe for Gemini's free tier would
        /// needlessly slow down a paid OpenAI/Claude account capable of far higher throughput.
        /// </summary>
        TimeSpan RequestDelay { get; }

        Task<TranslationResult> TranslateAsync(
            string sourceText, IReadOnlyList<SystemLanguage> targetLanguages, string gameContext, string apiKey);
    }
}
