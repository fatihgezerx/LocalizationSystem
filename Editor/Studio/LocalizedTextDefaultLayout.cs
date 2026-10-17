using System;
using Veneer;

namespace LocalizationSystem
{
    /// <summary>
    /// The design of the <see cref="LocalizedText"/> Inspector that Localization System ships: a Key card (read-only key and fallback
    /// text plus a hint when the key is missing from the table), a Translations card (the label in every language) and the Open
    /// Language Data button under the cards. With Veneer installed a project can copy it in the Inspector Designer and change the copy;
    /// this default is never saved as an asset.
    /// </summary>
    internal sealed class LocalizedTextDefaultLayout : IVeneerDefaultLayoutProvider
    {
        /// <inheritdoc />
        public Type TargetType => typeof(LocalizedText);

        /// <inheritdoc />
        public VeneerLayout CreateLayout()
        {
            VeneerLayout layout = new VeneerLayoutBuilder(typeof(LocalizedText), "Localized Text", "Shows this label in the current language", "globe")
                .Page("Localized Text", "globe")
                .Card("Key", "key")
                .Custom(LocalizedTextItems.StateId)
                .Field("Key", "Key", LocalizedTextItems.KeyTooltip)
                .Field("FallbackText", "Fallback", LocalizedTextItems.FallbackTooltip)
                .Custom(LocalizedTextItems.KeyProblemId)
                .Card("Translations", "list")
                .Custom(LocalizedTextItems.TranslationsId)
                .Custom(LocalizedTextItems.OpenDataId)
                .Build();

            layout.ThemeBrand = LocalizationBrand.Value.Name;
            return layout;
        }

        /// <inheritdoc />
        public void RegisterItems()
        {
            LocalizedTextItems.Register();
        }
    }
}
