using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LocalizationSystem
{
    /// <summary>
    /// Finds every <see cref="Text"/> and <see cref="TMP_Text"/> in the active scene, adds a new entry
    /// to <paramref name="data"/> for each one that isn't already tracked (using its current on-screen
    /// text as the <see cref="LocalizationData.SourceLanguage"/> value), and makes sure each one has a
    /// <see cref="LocalizedText"/> component wired to that same key - that component is what actually
    /// swaps the on-screen text when the language changes at runtime. Adding/updating that component
    /// does modify and dirty the scene (unlike the JSON data, which stays in-memory until "Save Data"
    /// is clicked). Triggered as part of "Sync Project" in the Language Data window. Anything carrying
    /// an <see cref="ExcludeFromLocalization"/> component is skipped entirely - the manual way to keep
    /// a Dropdown's own caption/item labels, a game logo/title, or any other text out of the table.
    /// </summary>
    /// <remarks>
    /// The key is a GUID generated once and then left alone (via
    /// <see cref="LocalizedTextComponentSync.EnsureStableKey"/>) - not derived from the object's name
    /// or hierarchy path, so renaming or moving it later never orphans its translations. An object
    /// scanned before this was the case keeps whatever key it already has (its old hierarchy-path
    /// string), since that's already stable once assigned - only objects scanned for the first time
    /// get a fresh GUID.
    /// </remarks>
    internal static class SceneScanner
    {
        public static (int Found, int Added) Scan(LocalizationData data)
        {
            if (!data.Languages.Contains(data.SourceLanguage))
            {
                data.Languages.Insert(0, data.SourceLanguage);
            }

            var found = CollectTextComponents();
            var added = 0;

            try
            {
                for (var i = 0; i < found.Count; i++)
                {
                    var (label, content, gameObject) = found[i];
                    EditorUtility.DisplayProgressBar("Scanning Scene", label, found.Count == 0 ? 0f : (float)i / found.Count);

                    var key = LocalizedTextComponentSync.EnsureStableKey(gameObject, content);
                    var entry = data.FindEntry(key);

                    if (entry == null)
                    {
                        entry = new LocalizationEntry { Key = key };
                        data.Entries.Add(entry);
                        added++;
                    }

                    // The key is stable regardless of content, so - unlike relying on a path derived
                    // from the object's name - it's always safe to keep the source-language value in
                    // sync with whatever's actually on screen right now, the same way
                    // AttributeFieldScanner already does for [Localize] fields. Without this, editing
                    // the text directly on the scene object (instead of through this window) would
                    // silently drift out of sync with the saved data forever.
                    if (entry.GetValue(data.SourceLanguage) != content)
                    {
                        entry.SetValue(data.SourceLanguage, content);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return (found.Count, added);
        }

        private static List<(string Label, string Content, GameObject GameObject)> CollectTextComponents()
        {
            var results = new List<(string, string, GameObject)>();

            foreach (var text in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (string.IsNullOrWhiteSpace(text.text) || IsExcluded(text.gameObject))
                {
                    continue;
                }

                results.Add((text.name, text.text, text.gameObject));
            }

            // TMP_Text is the shared base class for both TextMeshProUGUI (canvas) and TextMeshPro
            // (world-space) components, so this one query covers both.
            foreach (var tmp in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (string.IsNullOrWhiteSpace(tmp.text) || IsExcluded(tmp.gameObject))
                {
                    continue;
                }

                results.Add((tmp.name, tmp.text, tmp.gameObject));
            }

            return results;
        }

        // The manual escape hatch for anything that should never be scanned: a Dropdown's own
        // caption/item labels (Unity's built-in or any other UI kit's), a game logo/title, debug-only
        // text, etc. Left entirely to the user to mark - there's no way to guess this automatically
        // for every possible UI system.
        private static bool IsExcluded(GameObject gameObject) => gameObject.GetComponent<ExcludeFromLocalization>() != null;
    }
}
