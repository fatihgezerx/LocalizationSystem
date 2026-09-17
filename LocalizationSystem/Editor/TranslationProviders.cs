using System.Collections.Generic;

namespace LocalizationSystem
{
    /// <summary>The one instance of each <see cref="ITranslationProvider"/>, keyed by which <see cref="AiProvider"/> it implements.</summary>
    internal static class TranslationProviders
    {
        public static readonly IReadOnlyDictionary<AiProvider, ITranslationProvider> All = new Dictionary<AiProvider, ITranslationProvider>
        {
            { AiProvider.Gemini, new GeminiTranslationProvider() },
            { AiProvider.OpenAi, new OpenAiTranslationProvider() },
            { AiProvider.Claude, new ClaudeTranslationProvider() },
            { AiProvider.GoogleTranslate, new GoogleTranslateProvider() },
            { AiProvider.DeepL, new DeepLTranslationProvider() },
        };
    }
}
