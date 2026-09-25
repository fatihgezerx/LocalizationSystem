using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LocalizationSystem.Setup
{
    /// <summary>
    /// Adds Localization System's UI to a project that uses UniMVC, without being asked: copies the view scripts
    /// shipped in this folder's <c>MVC</c> subfolder into the project's MVC folder - e.g. <c>MVC/Panels/</c>,
    /// <c>MVC/Buttons/</c> - creating the subfolders if needed.
    /// </summary>
    /// <remarks>
    /// It happens on two occasions:
    /// <list type="bullet">
    /// <item>Localization System was just imported: the hidden <c>MVC/.pending</c> marker that ships with it is still
    /// there. If UniMVC is in the project the scripts are added and the marker deleted; if not, the marker
    /// waits, and the scripts are added as soon as UniMVC arrives.</item>
    /// <item>UniMVC was just imported (or imported again) into a project that already has Localization System.</item>
    /// </list>
    /// The scripts become ordinary project code: edit them freely. Existing files are never overwritten,
    /// and a script deleted on purpose isn't brought back until one of the occasions above. The MVC folder
    /// is the one holding UniMVC's <c>Bases</c>.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class MvcIntegration : AssetPostprocessor
    {
        private const string MvcAssembly = "UniMVC.Runtime";
        private const string SetupAssembly = "LocalizationSystem.Setup";
        private const string FallbackMvcRoot = "Assets/Scripts/MVC";
        private const string PendingMarker = ".pending";

        static MvcIntegration()
        {
            EditorApplication.delayCall += InstallIfPending;
        }

        // UniMVC was just imported: add this system's views to it.
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (path.EndsWith(".asmdef") && Path.GetFileNameWithoutExtension(path) == MvcAssembly)
                {
                    EditorApplication.delayCall += Install;
                    return;
                }
            }
        }

        private static void InstallIfPending()
        {
            var assemblies = DependencyGuard.FindAssemblyDefinitions();
            if (assemblies.TryGetValue(SetupAssembly, out var setupAsmdef) && File.Exists(TemplateRoot(setupAsmdef) + "/" + PendingMarker))
            {
                Install();
            }
        }

        private static void Install()
        {
            var assemblies = DependencyGuard.FindAssemblyDefinitions();

            // No UniMVC yet: the marker stays, and the scripts are added once UniMVC arrives.
            if (!assemblies.TryGetValue(SetupAssembly, out var setupAsmdef) || !assemblies.TryGetValue(MvcAssembly, out var mvcAsmdef))
            {
                return;
            }

            var templateRoot = TemplateRoot(setupAsmdef);
            var mvcRoot = MvcRoot(mvcAsmdef);

            var written = new StringBuilder();
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
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, AssetDatabase.LoadAssetAtPath<TextAsset>(templatePath).text);
                written.Append("\n  ").Append(targetPath);
            }

            var marker = templateRoot + "/" + PendingMarker;
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }

            if (written.Length == 0)
            {
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[{DependencyGuard.SystemName}] Added its MVC scripts:{written}");
        }

        // The templates sit in the MVC subfolder next to this assembly's asmdef.
        private static string TemplateRoot(string setupAsmdef) =>
            Path.GetDirectoryName(setupAsmdef)!.Replace('\\', '/') + "/MVC";

        // UniMVC's runtime asmdef sits in MVC/Bases/, so the MVC folder is its parent.
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
