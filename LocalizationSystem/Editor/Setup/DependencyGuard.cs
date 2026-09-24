using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace LocalizationSystem.Setup
{
    /// <summary>
    /// Keeps Localization System from ever breaking a project that doesn't have its dependencies yet.
    /// </summary>
    /// <remarks>
    /// This assembly references nothing, so it always compiles. On every script reload it looks for each
    /// dependency's assembly definition and sets or clears that dependency's scripting define symbol
    /// (e.g. <c>HAS_UNITASK</c>). Localization System's own assemblies list those symbols as Define Constraints,
    /// so while a dependency is missing they are simply left out of compilation - no errors - and this
    /// guard offers to install what's missing. Once it is installed, the symbol is set and Localization System
    /// compiles on its own. Works whether the dependencies were installed as packages or copied into Assets.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class DependencyGuard : AssetPostprocessor, IActiveBuildTargetChanged
    {
        internal const string SystemName = "Localization System";
        private const string MenuPath = "Tools/Localization System/Check Dependencies";
        private const string DontAskKey = "LocalizationSystem.Setup.DontAskForDependencies";
        private const string PromptedKey = "LocalizationSystem.Setup.PromptedForDependencies";

        /// <summary>Everything Localization System uses. Optional ones (with a purpose) only enable extra features.</summary>
        internal static readonly Dependency[] Dependencies =
        {
            new Dependency("uGUI", "UnityEngine.UI", "HAS_UGUI", "com.unity.ugui", "com.unity.ugui"),
            new Dependency("TextMeshPro", "Unity.TextMeshPro", "HAS_TEXTMESHPRO", "com.unity.ugui", "com.unity.ugui"),
            new Dependency("UniMVC", "UniMVC.Runtime", "HAS_UNIMVC", "https://github.com/fatihgezerx/UniMVC.git", "com.fatihgezer.unimvc", "for the language dropdown"),
        };

        private static AddAndRemoveRequest _installRequest;

        static DependencyGuard()
        {
            Events.registeringPackages += OnRegisteringPackages;
            EditorApplication.delayCall += () => Refresh(true);
        }

        public int callbackOrder => 0;

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) => Refresh(false);

        [MenuItem(MenuPath, false, 1000)]
        private static void CheckFromMenu()
        {
            EditorUserSettings.SetConfigValue(DontAskKey, null);
            SessionState.EraseBool(PromptedKey);
            if (!Refresh(true))
            {
                Debug.Log($"[{SystemName}] Every dependency is installed.");
            }
        }

        // An assembly definition appeared or disappeared (a package or folder added / deleted): re-check.
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (ContainsAsmdef(imported) || ContainsAsmdef(deleted) || ContainsAsmdef(moved))
            {
                EditorApplication.delayCall += () => Refresh(false);
            }
        }

        // A package is about to be removed: clear its symbol before its code disappears, so nothing
        // tries to compile against it in between.
        private static void OnRegisteringPackages(PackageRegistrationEventArgs args)
        {
            var symbols = new Dictionary<string, bool>();
            foreach (var removed in args.removed)
            {
                foreach (var dependency in Dependencies)
                {
                    if (dependency.PackageName == removed.name)
                    {
                        symbols[dependency.Define] = false;
                    }
                }
            }

            ApplyDefines(symbols);
        }

        /// <summary>Updates every symbol; returns true if anything is missing. Offers to install it when <paramref name="prompt"/>.</summary>
        internal static bool Refresh(bool prompt)
        {
            var assemblies = FindAssemblyDefinitions();
            var symbols = new Dictionary<string, bool>();
            var missing = new List<Dependency>();

            foreach (var dependency in Dependencies)
            {
                var present = assemblies.ContainsKey(dependency.Assembly);
                symbols[dependency.Define] = present;
                if (!present)
                {
                    missing.Add(dependency);
                }
            }

            ApplyDefines(symbols);

            if (missing.Count == 0)
            {
                return false;
            }

            if (prompt && _installRequest == null && !Application.isBatchMode
                && !SessionState.GetBool(PromptedKey, false) && EditorUserSettings.GetConfigValue(DontAskKey) == null)
            {
                SessionState.SetBool(PromptedKey, true);
                Prompt(missing);
            }

            return true;
        }

        /// <summary>Whether every dependency, optional ones included, is installed.</summary>
        internal static bool AllPresent()
        {
            var assemblies = FindAssemblyDefinitions();
            foreach (var dependency in Dependencies)
            {
                if (!assemblies.ContainsKey(dependency.Assembly))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Every assembly definition in Assets and Packages: file name (the assembly name, by convention) -> path.</summary>
        internal static Dictionary<string, string> FindAssemblyDefinitions()
        {
            var result = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                result[Path.GetFileNameWithoutExtension(path)] = path;
            }

            return result;
        }

        private static void Prompt(List<Dependency> missing)
        {
            var required = new StringBuilder();
            var optional = new StringBuilder();
            foreach (var dependency in missing)
            {
                if (dependency.IsOptional)
                {
                    optional.Append("• ").Append(dependency.Name).Append(" - ").AppendLine(dependency.Purpose);
                }
                else
                {
                    required.Append("• ").AppendLine(dependency.Name);
                }
            }

            var message = new StringBuilder();
            if (required.Length > 0)
            {
                message.Append(SystemName).AppendLine(" needs these packages:").AppendLine().Append(required).AppendLine();
            }

            if (optional.Length > 0)
            {
                message.AppendLine("Optional:").AppendLine().Append(optional).AppendLine();
            }

            message.Append("Until they are installed, the parts of ").Append(SystemName)
                .Append(" that use them are left out of compilation, so the project keeps compiling.");

            var choice = EditorUtility.DisplayDialogComplex(SystemName, message.ToString(), "Install", "Not now", "Don't ask again");
            if (choice == 0)
            {
                Install(missing);
            }
            else if (choice == 2)
            {
                EditorUserSettings.SetConfigValue(DontAskKey, "true");
                Debug.Log($"[{SystemName}] Won't ask about dependencies again. Use {MenuPath} to check them later.");
            }
        }

        private static void Install(List<Dependency> dependencies)
        {
            var ids = new string[dependencies.Count];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = dependencies[i].InstallId;
            }

            Debug.Log($"[{SystemName}] Installing: {string.Join(", ", ids)}");
            _installRequest = Client.AddAndRemove(ids);
            EditorApplication.update += WaitForInstall;
        }

        private static void WaitForInstall()
        {
            if (_installRequest == null || !_installRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= WaitForInstall;
            if (_installRequest.Status == StatusCode.Failure)
            {
                Debug.LogError($"[{SystemName}] Couldn't install the dependencies: {_installRequest.Error?.message}\n" +
                               "Git URLs need Git installed. You can also add them by hand in Window > Package Manager > + > Add package from git URL.");
            }

            _installRequest = null;
        }

        private static void ApplyDefines(Dictionary<string, bool> symbols)
        {
            if (symbols.Count == 0)
            {
                return;
            }

            var target = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            PlayerSettings.GetScriptingDefineSymbols(target, out var current);

            var defines = new List<string>(current);
            var changed = false;
            foreach (var pair in symbols)
            {
                var has = defines.Contains(pair.Key);
                if (pair.Value && !has)
                {
                    defines.Add(pair.Key);
                    changed = true;
                }
                else if (!pair.Value && has)
                {
                    defines.Remove(pair.Key);
                    changed = true;
                }
            }

            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbols(target, defines.ToArray());
            }
        }

        private static bool ContainsAsmdef(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".asmdef"))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>One package Localization System uses.</summary>
    internal readonly struct Dependency
    {
        /// <summary>Shown to the user.</summary>
        public readonly string Name;

        /// <summary>Its assembly definition's name; the dependency counts as installed when it exists.</summary>
        public readonly string Assembly;

        /// <summary>Scripting define symbol set while it is installed.</summary>
        public readonly string Define;

        /// <summary>What Package Manager installs: a package name or a git URL.</summary>
        public readonly string InstallId;

        /// <summary>Its package name, to notice it being removed.</summary>
        public readonly string PackageName;

        /// <summary>Null for required dependencies; for optional ones, what they enable.</summary>
        public readonly string Purpose;

        public Dependency(string name, string assembly, string define, string installId, string packageName, string purpose = null)
        {
            Name = name;
            Assembly = assembly;
            Define = define;
            InstallId = installId;
            PackageName = packageName;
            Purpose = purpose;
        }

        public bool IsOptional => Purpose != null;
    }
}
