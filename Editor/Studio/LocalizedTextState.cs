using UnityEditor;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// The translation table as the <see cref="LocalizedText"/> Inspector reads it: one cached copy shared by the Inspector's items, so
    /// the pill, the key hint and the translations list load the JSON once. The copy is dropped after Save Data and when it is older
    /// than a short time at the start of an Inspector build, so a freshly selected label never shows an old table. It is a plain
    /// object, not a Unity object, so keeping it in a static is safe across selections.
    /// </summary>
    internal static class LocalizedTextTable
    {
        private const double ReuseSeconds = 0.5;

        private static LocalizationData data;
        private static bool stale = true;
        private static double loadedAt;

        /// <summary>Starts watching <see cref="LocalizationDataStore.DataSaved"/>. Safe to call again.</summary>
        public static void Hook()
        {
            LocalizationDataStore.DataSaved -= Invalidate;
            LocalizationDataStore.DataSaved += Invalidate;
        }

        /// <summary>Marks the copy old when it was loaded a while ago: call at the start of building an item.</summary>
        public static void BeginBuild()
        {
            if (EditorApplication.timeSinceStartup - loadedAt > ReuseSeconds)
            {
                stale = true;
            }
        }

        /// <summary>Drops the cached copy: the next <see cref="Get"/> loads the file again.</summary>
        public static void Invalidate()
        {
            stale = true;
        }

        /// <summary>The table, loaded when there is no fresh copy.</summary>
        public static LocalizationData Get()
        {
            if (stale || data == null)
            {
                data = LocalizationDataStore.Load();
                stale = false;
                loadedAt = EditorApplication.timeSinceStartup;
            }

            return data;
        }
    }

    /// <summary>
    /// What one <see cref="LocalizedText"/> Inspector says about its key: the pill (translated, missing, not in the table) and the key
    /// hint. Made per item, it reads the key from the serialized object and the table from <see cref="LocalizedTextTable"/>; the
    /// "n missing" text is cached so polling allocates nothing while the count does not change.
    /// </summary>
    internal sealed class LocalizedTextState
    {
        private const string NoKeyText =
            "No key yet: this label shows its own text. Run Sync Project, or call SetKey from code.";

        private const string NotInTableText =
            "This key is not in the translation table. Run Sync Project in the Language Data window (or save the table if you just added it).";

        private readonly SerializedObject serializedObject;
        private readonly SerializedProperty key;
        private int cachedMissing = -1;
        private string cachedMissingText;

        /// <summary>Makes the state of the label edited by <paramref name="serializedObject"/>.</summary>
        public LocalizedTextState(SerializedObject serializedObject)
        {
            this.serializedObject = serializedObject;
            key = serializedObject.FindProperty("Key");
        }

        /// <summary>The label's key, or null when there is none.</summary>
        public string CurrentKey()
        {
            if (key == null || serializedObject.targetObject == null)
            {
                return null;
            }

            serializedObject.UpdateIfRequiredOrScript();
            return key.stringValue;
        }

        /// <summary>The pill: text and kind (<c>ok</c>, <c>warn</c>, <c>error</c>).</summary>
        public (string text, string kind) PillState()
        {
            string current = CurrentKey();
            if (string.IsNullOrEmpty(current))
            {
                return ("No key", "warn");
            }

            LocalizationData table = LocalizedTextTable.Get();
            LocalizationEntry entry = table.FindEntry(current);
            if (entry == null)
            {
                return ("Not in table", "error");
            }

            int missing = LanguageDataLogic.CountMissing(table, entry);
            if (missing == 0)
            {
                return ("Translated", "ok");
            }

            if (missing != cachedMissing)
            {
                cachedMissing = missing;
                cachedMissingText = missing + " missing";
            }

            return (cachedMissingText, "warn");
        }

        /// <summary>The key hint text, or null when the key is fine.</summary>
        public string KeyProblem()
        {
            string current = CurrentKey();
            if (string.IsNullOrEmpty(current))
            {
                return NoKeyText;
            }

            return LocalizedTextTable.Get().FindEntry(current) == null ? NotInTableText : null;
        }
    }
}
