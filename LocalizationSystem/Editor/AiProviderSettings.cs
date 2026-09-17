using System;
using UnityEditor;

namespace LocalizationSystem
{
    // Appended, not inserted - these are persisted as raw ints in EditorPrefs, so inserting a new
    // value in the middle would silently reassign whatever provider someone already had selected.
    internal enum AiProvider
    {
        Gemini,
        OpenAi,
        Claude,
        GoogleTranslate,
        DeepL,
    }

    /// <summary>
    /// Stores which AI provider is active, and each provider's own API key, in <see cref="EditorPrefs"/>
    /// - the OS-level Editor preferences store, not a project file. This is deliberate: anything under
    /// <c>Assets/</c> can end up committed and pushed to a (possibly public) git repository, which is
    /// exactly how this project's other systems get shared. A secret key must never be able to end up
    /// there. Each provider gets its own stored key so switching providers in "AI Settings..." doesn't
    /// lose the other ones.
    /// </summary>
    internal static class AiProviderSettings
    {
        private const string ProviderPrefKey = "LocalizationSystem.AiProvider";

        // "GeminiApiKey" (not "Gemini_ApiKey" or similar) matches the pref key this project used
        // before other providers existed, so an already-saved Gemini key isn't orphaned by this.
        private const string GeminiKeyPrefKey = "LocalizationSystem.GeminiApiKey";
        private const string OpenAiKeyPrefKey = "LocalizationSystem.OpenAiApiKey";
        private const string ClaudeKeyPrefKey = "LocalizationSystem.ClaudeApiKey";
        private const string GoogleTranslateKeyPrefKey = "LocalizationSystem.GoogleTranslateApiKey";
        private const string DeepLKeyPrefKey = "LocalizationSystem.DeepLApiKey";

        public static AiProvider GetProvider() => (AiProvider)EditorPrefs.GetInt(ProviderPrefKey, (int)AiProvider.Gemini);

        public static void SetProvider(AiProvider provider) => EditorPrefs.SetInt(ProviderPrefKey, (int)provider);

        public static string GetApiKey(AiProvider provider) => EditorPrefs.GetString(PrefKeyFor(provider), string.Empty);

        public static void SetApiKey(AiProvider provider, string apiKey) => EditorPrefs.SetString(PrefKeyFor(provider), apiKey ?? string.Empty);

        private static string PrefKeyFor(AiProvider provider) => provider switch
        {
            AiProvider.Gemini => GeminiKeyPrefKey,
            AiProvider.OpenAi => OpenAiKeyPrefKey,
            AiProvider.Claude => ClaudeKeyPrefKey,
            AiProvider.GoogleTranslate => GoogleTranslateKeyPrefKey,
            AiProvider.DeepL => DeepLKeyPrefKey,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
    }
}
