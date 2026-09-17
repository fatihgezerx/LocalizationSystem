using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LocalizationSystem
{
    /// <summary>
    /// Finds every <c>[Localize]</c>-marked field, on every <see cref="ScriptableObject"/> asset,
    /// prefab component, and scene component in the project, and adds an entry to
    /// <paramref name="data"/> for each distinct value found - the counterpart to
    /// <c>SceneScanner</c> for text that doesn't live on a scene <c>Text</c>/<c>TMP_Text</c>
    /// component (dialogue lines, item descriptions, anything a project keeps as plain data).
    /// Triggered as part of "Sync Project" in the Language Data window.
    /// </summary>
    /// <remarks>
    /// There's no separate wrapper type or stored key here - a marked field's own current text
    /// <i>is</i> its key (classic gettext-style keying). That's what makes scanning ScriptableObjects,
    /// prefabs, and scene objects uniformly possible without needing to attach anything to them: at
    /// runtime, <see cref="LocalizationRuntime.Get"/> is simply called with that same field's value.
    /// The trade-off is the usual one for this style of system - editing a line later means it's
    /// treated as a new string needing translation, not an update to the old one.
    /// </remarks>
    internal static class AttributeFieldScanner
    {
        private static readonly string[] SearchFolders = { "Assets" };

        public static (int Found, int Added) Scan(LocalizationData data)
        {
            if (!data.Languages.Contains(data.SourceLanguage))
            {
                data.Languages.Insert(0, data.SourceLanguage);
            }

            var found = 0;
            var added = 0;

            // Loaded once and reused across every marked (type, field) pair below, instead of
            // re-querying AssetDatabase and re-loading the same prefabs/ScriptableObjects once per
            // marked field - with several [Localize]-marked fields in a project (even a few on the
            // same type), that redundant work adds up fast.
            var allScriptableObjects = LoadAllAssets<ScriptableObject>("t:ScriptableObject");
            var allPrefabRoots = LoadAllAssets<GameObject>("t:Prefab");
            var ownersByType = new Dictionary<Type, List<UnityEngine.Object>>();

            foreach (var (type, propertyPath) in FindLocalizedFields())
            {
                if (!ownersByType.TryGetValue(type, out var owners))
                {
                    owners = CollectOwners(type, allScriptableObjects, allPrefabRoots);
                    ownersByType[type] = owners;
                }

                foreach (var owner in owners)
                {
                    if (owner == null)
                    {
                        continue;
                    }

                    var property = new SerializedObject(owner).FindProperty(propertyPath);
                    if (property != null)
                    {
                        ProcessProperty(property, owner, data, ref found, ref added);
                    }
                }
            }

            return (found, added);
        }

        // Finds every [Localize]-marked field on every UnityEngine.Object-derived type (a
        // ScriptableObject or Component) - including ones nested one or more levels inside a plain
        // [Serializable] class embedded as a field (e.g. a MonoBehaviour holding a
        // `[SerializeField] private SomeDataClass data;`, with the [Localize] field actually declared
        // on SomeDataClass). The returned string is the dotted SerializedProperty path (e.g.
        // "data.SomeField") needed to reach it from the owning Component/ScriptableObject -
        // SerializedObject.FindProperty understands that syntax natively for nested serializable data.
        private static IEnumerable<(Type OwnerType, string PropertyPath)> FindLocalizedFields()
        {
            foreach (var assembly in GetAssembliesUsingLocalize())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    if (!typeof(UnityEngine.Object).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    foreach (var path in FindLocalizedFieldPaths(type, string.Empty, new HashSet<Type>()))
                    {
                        yield return (type, path);
                    }
                }
            }
        }

        // [Localize] is a custom attribute defined in this project's own Runtime assembly - using it
        // anywhere requires a compile-time reference to that assembly, no exceptions. That makes this
        // an exact, not a heuristic, filter: it skips every Unity engine/editor assembly and every
        // installed package outright, rather than reflecting over the whole AppDomain (which in a
        // typical project is a couple hundred assemblies, the vast majority of which could never
        // possibly contain a [Localize] field) just to throw almost all of it away a moment later.
        private static IEnumerable<Assembly> GetAssembliesUsingLocalize()
        {
            var localizeAssemblyName = typeof(LocalizeAttribute).Assembly.GetName().Name;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == localizeAssemblyName)
                {
                    yield return assembly;
                    continue;
                }

                if (assembly.GetReferencedAssemblies().Any(referenced => referenced.Name == localizeAssemblyName))
                {
                    yield return assembly;
                }
            }
        }

        // Recurses into nested plain [Serializable] class fields (not into UnityEngine.Object
        // references, which get their own top-level scan instead, and not into lists/arrays of
        // nested classes - Unity's [SerializeField]/[Localize] convention here is a single embedded
        // object, not a collection of them). The visited set is a recursion-stack guard against
        // cyclic type references, not a global one - it's removed on the way back out so the same
        // type can still appear via a different sibling field.
        private static IEnumerable<string> FindLocalizedFieldPaths(Type type, string pathPrefix, HashSet<Type> visited)
        {
            if (!visited.Add(type))
            {
                yield break;
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.DeclaringType != type)
                {
                    continue;
                }

                var path = pathPrefix + field.Name;

                if (field.GetCustomAttribute<LocalizeAttribute>() != null)
                {
                    yield return path;
                    continue;
                }

                var fieldType = field.FieldType;
                if (fieldType.IsClass && fieldType != typeof(string)
                    && !typeof(UnityEngine.Object).IsAssignableFrom(fieldType)
                    && fieldType.IsDefined(typeof(SerializableAttribute), false))
                {
                    foreach (var nestedPath in FindLocalizedFieldPaths(fieldType, path + ".", visited))
                    {
                        yield return nestedPath;
                    }
                }
            }

            visited.Remove(type);
        }

        private static List<T> LoadAllAssets<T>(string filter) where T : UnityEngine.Object
        {
            var results = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets(filter, SearchFolders))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                {
                    results.Add(asset);
                }
            }

            return results;
        }

        // Every place a value of this exact type is actually stored: a ScriptableObject asset of
        // that type, a prefab component of that type, or a live component of that type in the open
        // scene (a placed prefab instance might override the field's value there). Computed once per
        // type and reused across every [Localize] field that type happens to have.
        private static List<UnityEngine.Object> CollectOwners(Type type, List<ScriptableObject> allScriptableObjects, List<GameObject> allPrefabRoots)
        {
            var owners = new List<UnityEngine.Object>();

            if (typeof(ScriptableObject).IsAssignableFrom(type))
            {
                owners.AddRange(allScriptableObjects.Where(type.IsInstanceOfType));
            }
            else if (typeof(Component).IsAssignableFrom(type))
            {
                foreach (var prefabRoot in allPrefabRoots)
                {
                    owners.AddRange(prefabRoot.GetComponentsInChildren(type, true));
                }

                owners.AddRange(UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None));
            }

            return owners;
        }

        private static void ProcessProperty(SerializedProperty property, UnityEngine.Object owner, LocalizationData data, ref int found, ref int added)
        {
            // SerializedProperty.isArray is (confusingly) also true for plain strings, since Unity
            // represents them internally as a char array - checking propertyType first avoids
            // treating every marked string field as if it were a string list.
            if (property.propertyType == SerializedPropertyType.String)
            {
                ProcessValue(property.stringValue, owner, data, ref found, ref added);
                return;
            }

            if (!property.isArray)
            {
                return;
            }

            // A list/array field can't map to a single Text component the way one plain string field
            // can, so its elements only ever feed the translation table - no auto-wiring for these.
            for (var i = 0; i < property.arraySize; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                if (element.propertyType == SerializedPropertyType.String)
                {
                    ProcessValue(element.stringValue, null, data, ref found, ref added);
                }
            }
        }

        private static void ProcessValue(string value, UnityEngine.Object owner, LocalizationData data, ref int found, ref int added)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            found++;

            if (data.FindEntry(value) == null)
            {
                var entry = new LocalizationEntry { Key = value };
                entry.SetValue(data.SourceLanguage, value);
                data.Entries.Add(entry);
                added++;
            }

            TryWireDisplayComponent(owner, value);
        }

        // If the [Localize] field lives on a Component that's also sitting on a GameObject with a
        // Text/TMP_Text (the common "this script's job is to display its own line" pattern), wire
        // that label up the same way SceneScanner wires scanned scene text, so it updates
        // automatically when the language changes without the author doing anything extra. Fields
        // displayed some other way (pushed to a Text elsewhere, used in code without ever being
        // shown directly, etc.) still get collected for translation above - they just don't get this
        // automatic wiring, since there's no way to guess where they end up on screen.
        private static void TryWireDisplayComponent(UnityEngine.Object owner, string key)
        {
            if (owner is not Component component)
            {
                return;
            }

            var gameObject = component.gameObject;
            if (gameObject.GetComponent<ExcludeFromLocalization>() != null)
            {
                return;
            }

            if (gameObject.GetComponent<Text>() != null || gameObject.GetComponent<TMP_Text>() != null)
            {
                // The key already is the source text, so it doubles as its own fallback.
                LocalizedTextComponentSync.Ensure(gameObject, key, key);
            }
        }
    }
}
