using Veneer;
using UnityEditor;

namespace LocalizationSystem
{
    /// <summary>
    /// Inspector of the <see cref="ExcludeFromLocalization"/> marker in the studio look, drawn from a Veneer layout (the project's own
    /// copy when there is one, otherwise <see cref="ExcludeFromLocalizationDefaultLayout"/>): it has no fields, so it explains what it does.
    /// </summary>
    [CustomEditor(typeof(ExcludeFromLocalization))]
    internal sealed class ExcludeFromLocalizationEditor : VeneerLayoutDrivenInspector<ExcludeFromLocalization>
    {
        /// <inheritdoc />
        protected override VeneerBrand Brand => LocalizationBrand.Value;
    }
}
