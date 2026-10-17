using System;
using Veneer;

namespace LocalizationSystem
{
    /// <summary>
    /// The design of the <see cref="ExcludeFromLocalization"/> Inspector that Localization System ships: a header with an Excluded pill
    /// and one card that explains the marker. With Veneer installed a project can copy it in the Inspector Designer and change the copy;
    /// this default is never saved as an asset.
    /// </summary>
    internal sealed class ExcludeFromLocalizationDefaultLayout : IVeneerDefaultLayoutProvider
    {
        /// <summary>Id of the header state pill (header badge).</summary>
        public const string StateId = "loc.exclude.state";

        /// <inheritdoc />
        public Type TargetType => typeof(ExcludeFromLocalization);

        /// <inheritdoc />
        public VeneerLayout CreateLayout()
        {
            VeneerLayout layout = new VeneerLayoutBuilder(
                    typeof(ExcludeFromLocalization), "Exclude From Localization", "Marker: this text is never translated", "eye-off")
                .Page("Exclude From Localization", "eye-off")
                .Card("What it does", "info")
                .Custom(StateId)
                .Note(
                    "Sync Project leaves this object out of the translation table and never puts a Localized Text on it, whatever its text says. " +
                    "Use it for a logo, a number, debug text or a dropdown from another UI kit. It has no settings and no runtime behaviour.")
                .Build();

            layout.ThemeBrand = LocalizationBrand.Value.Name;
            return layout;
        }

        /// <inheritdoc />
        public void RegisterItems()
        {
            VeneerCustomItems.Register(typeof(ExcludeFromLocalization), new VeneerCustomItem
            {
                Id = StateId,
                Label = "State",
                Icon = "eye-off",
                Description = "A pill in the header: Excluded.",
                Slot = VeneerCustomItemSlot.HeaderBadge,
                Build = context =>
                {
                    context.Builder.StatePill(() => ("Excluded", "idle"));
                    return null;
                },
            });
        }
    }
}
