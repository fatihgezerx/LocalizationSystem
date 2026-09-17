using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// Adds (or updates) a <see cref="LocalizedText"/> component on a <see cref="GameObject"/> so it
    /// updates at runtime when the language changes - shared by <c>SceneScanner</c> (a generated,
    /// stable GUID key, since scanned scene text has nothing more meaningful to key off) and
    /// <c>AttributeFieldScanner</c> (keyed by a <c>[Localize]</c> field's own text instead).
    /// </summary>
    internal static class LocalizedTextComponentSync
    {
        /// <summary>Points the GameObject's <see cref="LocalizedText"/> at exactly <paramref name="key"/>, overwriting whatever was there before.</summary>
        public static void Ensure(GameObject gameObject, string key, string fallbackText)
        {
            var serializedObject = new SerializedObject(GetOrAddComponent(gameObject));
            var keyProperty = serializedObject.FindProperty("Key");
            var fallbackProperty = serializedObject.FindProperty("FallbackText");

            var changed = false;
            if (keyProperty.stringValue != key)
            {
                keyProperty.stringValue = key;
                changed = true;
            }
            if (fallbackProperty.stringValue != fallbackText)
            {
                fallbackProperty.stringValue = fallbackText;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            MarkDirty(gameObject);
        }

        /// <summary>
        /// Returns this GameObject's existing key if it already has one, or generates and stores a
        /// new GUID if it doesn't - unlike <see cref="Ensure"/>, there's no "correct" value to
        /// converge on here, so once a key exists it's left alone forever (that's what makes it stable
        /// across rescans, even if the object is renamed or moved in the hierarchy). The fallback text
        /// is always kept in sync with whatever's currently on screen, though, since that's just a
        /// display safety net, not an identity.
        /// </summary>
        public static string EnsureStableKey(GameObject gameObject, string fallbackText)
        {
            var serializedObject = new SerializedObject(GetOrAddComponent(gameObject));
            var keyProperty = serializedObject.FindProperty("Key");
            var fallbackProperty = serializedObject.FindProperty("FallbackText");

            var changed = false;
            var key = keyProperty.stringValue;
            if (string.IsNullOrEmpty(key))
            {
                key = Guid.NewGuid().ToString("N");
                keyProperty.stringValue = key;
                changed = true;
            }

            if (fallbackProperty.stringValue != fallbackText)
            {
                fallbackProperty.stringValue = fallbackText;
                changed = true;
            }

            if (changed)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                MarkDirty(gameObject);
            }

            return key;
        }

        private static LocalizedText GetOrAddComponent(GameObject gameObject)
        {
            var localizedText = gameObject.GetComponent<LocalizedText>();
            return localizedText != null ? localizedText : gameObject.AddComponent<LocalizedText>();
        }

        // A scene GameObject needs the scene marked dirty to get saved; a prefab asset's root (loaded
        // via AssetDatabase, not part of any open scene) needs the asset itself dirtied instead -
        // gameObject.scene is invalid in that case.
        private static void MarkDirty(GameObject gameObject)
        {
            if (gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
            else
            {
                EditorUtility.SetDirty(gameObject);
            }
        }
    }
}
