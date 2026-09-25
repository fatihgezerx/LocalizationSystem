using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// Attach to any GameObject with a <c>Text</c>/<c>TMP_Text</c> to tell "Sync Project" to leave it
    /// alone entirely - it won't be added to the translation table and won't get a
    /// <see cref="LocalizedText"/> component wired onto it, no matter what its current text says.
    /// </summary>
    /// <remarks>
    /// The automatic exclusion for Unity's own <c>Dropdown</c>/<c>TMP_Dropdown</c> caption/item labels
    /// only recognizes those two specific components - this is the general, manual escape hatch for
    /// everything else that should never be scanned: a dropdown from a different UI kit, a game
    /// logo/title that must stay as-is, debug-only text, etc. It's a pure marker with no fields and no
    /// runtime behavior of its own - only the Editor-side scanners look for it.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ExcludeFromLocalization : MonoBehaviour
    {
    }
}
