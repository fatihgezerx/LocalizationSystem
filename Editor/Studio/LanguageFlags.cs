using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// Flag pictures for the language columns. A file named like the language's display name (for example <c>Turkish.png</c>) in
    /// <c>Editor/Flags/</c> is used automatically; a language without a picture simply shows no flag.
    /// </summary>
    internal static class LanguageFlags
    {
        private static string flagsFolder;
        private static readonly Dictionary<SystemLanguage, Texture2D> Cache = new();

        // The folder is found next to the editor assembly's asmdef, wherever LocalizationSystem was copied to.
        private static string FlagsFolder => flagsFolder ??=
            Path.GetDirectoryName(CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("LocalizationSystem.Editor"))!
                .Replace('\\', '/') + "/Flags";

        /// <summary>Forgets the loaded pictures, so files dropped into the Flags folder are picked up when the window opens.</summary>
        public static void Clear() => Cache.Clear();

        /// <summary>The flag picture of a language, or null when there is none.</summary>
        public static Texture2D Get(SystemLanguage language)
        {
            if (Cache.TryGetValue(language, out var cached))
            {
                return cached;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{FlagsFolder}/{LanguageDataLogic.DisplayName(language)}.png");
            Cache[language] = texture;
            return texture;
        }
    }
}
