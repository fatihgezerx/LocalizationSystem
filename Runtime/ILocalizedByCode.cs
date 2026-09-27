using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// Implemented by a component whose code fills texts itself, already in the current language - e.g. a prompt
    /// showing whichever <c>[Localize]</c> string is current through <see cref="LocalizationRuntime.Get"/>, one
    /// label for a hundred different lines. "Sync Project" leaves those texts alone: no entry for what they show
    /// right now, and no <see cref="LocalizedText"/>, which would overwrite them with that one line.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ExcludeFromLocalization"/>, nothing is added to the object: the code says which texts it
    /// fills. The strings it shows are still translated, as <c>[Localize]</c> fields or entries of their own.
    /// </remarks>
    public interface ILocalizedByCode
    {
        /// <summary>Whether this component's code fills <paramref name="text"/> (a <c>Text</c> or <c>TMP_Text</c>) itself.</summary>
        bool Fills(Component text);
    }
}
