using Veneer;
using UnityEditor;

namespace LocalizationSystem
{
    /// <summary>
    /// Inspector of <see cref="LocalizedText"/> in the studio look, drawn from a Veneer layout: the project's own copy made in the
    /// Inspector Designer when there is one, otherwise <see cref="LocalizedTextDefaultLayout"/> (the key and fallback text, what the
    /// label says in every language of the project, and a button that opens the Language Data window). The key-state pill, the key
    /// hint, the translations list and the button are custom items (<see cref="LocalizedTextItems"/>).
    /// </summary>
    [CustomEditor(typeof(LocalizedText))]
    internal sealed class LocalizedTextEditor : VeneerLayoutDrivenInspector<LocalizedText>
    {
        /// <inheritdoc />
        protected override VeneerBrand Brand => LocalizationBrand.Value;
    }
}
