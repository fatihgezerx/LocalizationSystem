using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LocalizationSystem.Setup
{
    /// <summary>
    /// Adds Localization System's UI to a project that uses UniMVC: once every dependency (UniMVC included) is
    /// installed, copies the view scripts shipped in this folder's <c>MVC</c> subfolder into the project's
    /// MVC folder - e.g. <c>MVC/Panels/</c>, <c>MVC/Buttons/</c> - creating the subfolders if needed.
    /// </summary>
    /// <remarks>
    /// The scripts become ordinary project code: edit them freely. Existing files are never overwritten;
    /// the automatic copy runs once per project, and <c>Tools/Localization System/Install MVC Scripts</c> adds any
    /// that are missing again. The MVC folder is the one holding UniMVC's <c>_Bases</c>, or
    /// <c>Assets/Scripts/MVC</c> when UniMVC was installed as a package.
    /// </remarks>
    [InitializeOnLoad]
    internal static class MvcIntegration
    {
        private const string MvcAssembly = "UniMVC.Runtime";
        private const string SetupAssembly = "LocalizationSystem.Setup";
        private const string FallbackMvcRoot = "Assets/Scripts/MVC";
        private const string MenuPath = "Tools/Localization System/Install MVC Scripts";
        private const string InstalledKey = "LocalizationSystem.Setup.MvcScriptsInstalled";

        static MvcIntegration()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorUserSettings.GetConfigValue(InstalledKey) == null && DependencyGuard.AllPresent())
                {
                    Install();
                }
            };
        }

        [MenuItem(MenuPath, false, 1001)]
        private static void InstallFromMenu()
        {
            if (!DependencyGuard.AllPresent())
            {
                Debug.LogWarning($"[{DependencyGuard.SystemName}] The MVC scripts need every dependency installed first. " +
                                 "Use Tools > " + DependencyGuard.SystemName + " > Check Dependencies.");
                return;
            }

            Install();
        }

        private static void Install()
        {
            var assemblies = DependencyGuard.FindAssemblyDefinitions();
            if (!assemblies.TryGetValue(SetupAssembly, out var setupAsmdef) || !assemblies.TryGetValue(MvcAssembly, out var mvcAsmdef))
            {
                return;
            }

            var templateRoot = Path.GetDirectoryName(setupAsmdef)!.Replace('\\', '/') + "/MVC";
            var mvcRoot = MvcRoot(mvcAsmdef);

            var written = new StringBuilder();
            var skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { templateRoot }))
            {
                var templatePath = AssetDatabase.GUIDToAssetPath(guid);
                if (!templatePath.EndsWith(".cs.txt"))
                {
                    continue;
                }

                var relative = templatePath.Substring(templateRoot.Length + 1);
                var targetPath = mvcRoot + "/" + relative.Substring(0, relative.Length - ".txt".Length);
                if (File.Exists(targetPath))
                {
                    skipped++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, AssetDatabase.LoadAssetAtPath<TextAsset>(templatePath).text);
                written.Append("\n  ").Append(targetPath);
            }

            EditorUserSettings.SetConfigValue(InstalledKey, "true");

            if (written.Length == 0)
            {
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[{DependencyGuard.SystemName}] Added its MVC scripts:{written}" +
                      (skipped > 0 ? $"\n({skipped} already existed and were kept.)" : string.Empty));
        }

        // UniMVC's runtime asmdef sits in MVC/_Bases/, so the MVC folder is its parent - unless UniMVC
        // is an (immutable) package, in which case the scripts go to the default project folder.
        private static string MvcRoot(string mvcAsmdef)
        {
            if (!mvcAsmdef.StartsWith("Assets/"))
            {
                return FallbackMvcRoot;
            }

            var bases = Path.GetDirectoryName(mvcAsmdef)!;
            return Path.GetDirectoryName(bases)!.Replace('\\', '/');
        }
    }
}
