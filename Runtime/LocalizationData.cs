using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>A user-named "Game Context" prompt, saved for reuse from the "AI Settings..." preset picker.</summary>
    [Serializable]
    public sealed class NamedPrompt
    {
        public string Name;
        public string Text;
    }

    /// <summary>
    /// The full localization table for the project: every language the project supports, and every
    /// text entry (usually one per scanned UI label) with its translation for each of those languages.
    /// This is plain data - see <c>LocalizationDataStore</c> (Editor) for how it's loaded from and
    /// saved to disk as JSON.
    /// </summary>
    [Serializable]
    public sealed class LocalizationData
    {
        /// <summary>
        /// The language new text is assumed to be written in when first scanned from the scene, and
        /// the language future AI-assisted translation will translate <i>from</i>.
        /// </summary>
        public SystemLanguage SourceLanguage = SystemLanguage.English;

        /// <summary>Every language currently selected for this project.</summary>
        public List<SystemLanguage> Languages = new();

        /// <summary>Every localizable text entry.</summary>
        public List<LocalizationEntry> Entries = new();

        /// <summary>
        /// Optional free-text description of the game (genre, tone, terminology conventions), sent
        /// along with every AI translation request to help the model pick the right register - e.g.
        /// "Fantasy RPG, epic/formal tone, similar to Skyrim." Not sensitive - stored and saved like
        /// any other Language Data field, unlike the AI provider's API key.
        /// </summary>
        public string GameContext = string.Empty;

        /// <summary>User-named Game Context prompts saved for reuse, shown alongside the built-in genre presets.</summary>
        public List<NamedPrompt> CustomGameContextPresets = new();

        /// <summary>Finds the entry with the given key, or null if none exists yet.</summary>
        public LocalizationEntry FindEntry(string key)
        {
            foreach (var entry in Entries)
            {
                if (entry.Key == key)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
