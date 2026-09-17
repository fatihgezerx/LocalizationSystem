using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// The prompt text and response parsing shared by every <see cref="ITranslationProvider"/> - kept
    /// in one place so switching AI providers never changes what's actually asked for or how the
    /// answer is read back, only which API delivers it.
    /// </summary>
    internal static class TranslationPromptBuilder
    {
        [Serializable]
        private sealed class TranslationItem
        {
            public string language;
            public string text;
        }

        [Serializable]
        private sealed class TranslationBatch
        {
            public string detectedSourceLanguage;
            public TranslationItem[] translations;
        }

        public static string Build(string sourceText, IReadOnlyList<SystemLanguage> targetLanguages, string gameContext)
        {
            var targetLanguageNames = targetLanguages.Select(LocalizationRuntime.DisplayName).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("You are translating a short UI text string (a button label, menu title, or similar) for a video game.");

            if (!string.IsNullOrWhiteSpace(gameContext))
            {
                sb.AppendLine($"Context about the game, to help you pick the right tone and terminology: {gameContext}");
            }

            sb.AppendLine($"Source text: \"{sourceText}\"");
            sb.AppendLine("Detect the actual language the source text is written in yourself - do not assume it matches any label given elsewhere, it may be wrong. Report what you detected.");
            sb.AppendLine($"Translate it into each of these languages: {string.Join(", ", targetLanguageNames)}.");
            sb.AppendLine("Keep each translation short and natural for game UI - not an overly literal, word-for-word translation.");
            sb.AppendLine("If the source text is a standard, common game-UI term (e.g. New Game, Load Game, Continue, " +
                          "Settings, Options, Exit, Quit, Save, Back, Confirm, Cancel, Play, Pause, Resume), use the " +
                          "conventional, standard translation players in that language already expect from other games - " +
                          "do not stylize or get creative with these, even if the game's context calls for a distinctive " +
                          "tone. Apply that tone only to narrative, dialogue, or flavor text, not to standard menu labels.");
            sb.AppendLine("Match the letter-case style of the source text exactly in every translation: if the source " +
                          "is fully UPPERCASE, make every translation fully uppercase too (using that language's own " +
                          "uppercase rules); if it's Title Case or normal sentence case, match that instead. Never " +
                          "normalize a stylized source down to plain sentence case.");
            sb.AppendLine("Respond with ONLY raw JSON, no markdown code fences, no extra commentary, in exactly this shape:");
            sb.Append("{\"detectedSourceLanguage\":\"<language name you detected>\"," +
                      "\"translations\":[{\"language\":\"<language name exactly as given above>\",\"text\":\"<translation>\"}]}");

            return sb.ToString();
        }

        public static TranslationResult ParseResponse(string rawText)
        {
            var batch = JsonUtility.FromJson<TranslationBatch>(StripMarkdownFence(rawText));
            var result = new TranslationResult { DetectedSourceLanguage = batch?.detectedSourceLanguage };

            if (batch?.translations != null)
            {
                foreach (var item in batch.translations)
                {
                    if (!string.IsNullOrEmpty(item.language) && !string.IsNullOrEmpty(item.text))
                    {
                        result.Translations[item.language] = item.text;
                    }
                }
            }

            return result;
        }

        // Models very commonly wrap JSON answers in ```json ... ``` even when told not to - strip it
        // defensively rather than fail parsing over formatting.
        private static string StripMarkdownFence(string text)
        {
            var trimmed = text.Trim();
            if (!trimmed.StartsWith("```"))
            {
                return trimmed;
            }

            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                trimmed = trimmed[..closingFence];
            }

            return trimmed.Trim();
        }
    }
}
