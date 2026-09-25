using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// Loads and saves the project's single <see cref="LocalizationData"/> as a plain JSON file
    /// under version control - no ScriptableObject, no binary asset.
    /// </summary>
    internal static class LocalizationDataStore
    {
        // Lives under a Resources folder (rather than a plain Data folder) so the exact same file
        // that's authored here can also be loaded at runtime via Resources.Load<TextAsset> - see
        // LocalizationRuntime. No separate export/build step, and the whole LocalizationSystem
        // folder (including this file) stays self-contained for dropping into another project. The
        // folder is found next to the runtime asmdef, wherever LocalizationSystem was copied to.
        private static string _relativePath;

        private static string RelativePath => _relativePath ??=
            Path.GetDirectoryName(CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("LocalizationSystem.Runtime"))!
                .Replace('\\', '/') + "/Resources/LocalizationData.json";

        /// <summary>Loads the current data, or an empty one if the file doesn't exist yet.</summary>
        public static LocalizationData Load()
        {
            var fullPath = ToFullPath();
            if (!File.Exists(fullPath))
            {
                return new LocalizationData();
            }

            var json = File.ReadAllText(fullPath);
            return JsonUtility.FromJson<LocalizationData>(json) ?? new LocalizationData();
        }

        /// <summary>Writes <paramref name="data"/> to disk, overwriting the previous file.</summary>
        public static void Save(LocalizationData data)
        {
            var fullPath = ToFullPath();
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            var json = JsonUtility.ToJson(data, true);

            // Finish writing to a temp file before replacing the real one, so a crash or a disk-full
            // error mid-write can never leave a half-written, corrupt LocalizationData.json behind -
            // the previous, still-valid file stays in place until the new one is fully ready.
            var tempPath = fullPath + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            File.Move(tempPath, fullPath);

            AssetDatabase.ImportAsset(RelativePath);
        }

        private static string ToFullPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            return Path.Combine(projectRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
