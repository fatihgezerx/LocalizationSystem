using Veneer;
using UnityEditor;
using UnityEngine.UIElements;

namespace LocalizationSystem
{
    /// <summary>Inspector of the <see cref="ExcludeFromLocalization"/> marker in the studio look: it has no fields, so it explains what it does.</summary>
    [CustomEditor(typeof(ExcludeFromLocalization))]
    internal sealed class ExcludeFromLocalizationEditor : VeneerInspector<ExcludeFromLocalization>
    {
        /// <inheritdoc />
        protected override VeneerBrand Brand => LocalizationBrand.Value;

        /// <inheritdoc />
        protected override void Build(VeneerInspectorBuilder b)
        {
            b.Header("Exclude From Localization", "Marker: this text is never translated", "eye-off", out Label _);
            b.StatePill(() => ("Excluded", "idle"));
            b.Card("What it does", "info", () =>
            {
                b.Hint(
                    "Sync Project leaves this object out of the translation table and never puts a Localized Text on it, whatever its text says. " +
                    "Use it for a logo, a number, debug text or a dropdown from another UI kit. It has no settings and no runtime behaviour.",
                    "info");
            });
        }
    }
}
