using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// Tells "Sync Project" which texts an <see cref="ILocalizedByCode"/> fills itself, so it leaves them alone. One
    /// per scan: each hierarchy's fillers are looked up once, by its root, however many texts it holds.
    /// </summary>
    internal sealed class CodeFilledTexts
    {
        private readonly Dictionary<Transform, ILocalizedByCode[]> _fillersByRoot = new();

        /// <summary>Whether a component in <paramref name="text"/>'s hierarchy fills it itself.</summary>
        public bool Contains(Component text)
        {
            var root = text.transform.root;
            if (!_fillersByRoot.TryGetValue(root, out var fillers))
            {
                fillers = root.GetComponentsInChildren<ILocalizedByCode>(true);
                _fillersByRoot.Add(root, fillers);
            }

            foreach (var filler in fillers)
            {
                if (filler.Fills(text))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Takes off a <see cref="LocalizedText"/> an earlier sync (or a hand) put on a text its code fills - it
        /// would overwrite that text with one line. Returns whether there was one.
        /// </summary>
        public static bool RemoveLocalizedText(GameObject gameObject)
        {
            if (!gameObject.TryGetComponent<LocalizedText>(out var localizedText))
            {
                return false;
            }

            Undo.DestroyObjectImmediate(localizedText);
            Debug.Log($"[Localization System] Removed the LocalizedText from '{gameObject.name}': its text is filled by code, in the current language.", gameObject);
            return true;
        }
    }
}
