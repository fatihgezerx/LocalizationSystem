#if HAS_EASYUI
using System.Collections.Generic;
using EasyUI;

namespace LocalizationSystem
{
    /// <summary>
    /// The roles an Easy UI panel's texts can have for Localization System, picked in Easy UI (Add Role, at the top
    /// of an element's inspector) and added as components when the panel is built. The two can't be on one text.
    /// </summary>
    public static class LocalizationRoles
    {
        public const string LocalizedText = "localization.localized-text";
        public const string ExcludeFromLocalization = "localization.exclude";
    }

    /// <summary>Lists <see cref="LocalizationRoles"/> in Easy UI's Add Role menu, under Localization, on Texts only.</summary>
    internal sealed class LocalizationRoleProvider : IEasyUIRoleProvider
    {
        public IEnumerable<EasyUIRole> GetRoles()
        {
            yield return new EasyUIRole(LocalizationRoles.LocalizedText, "Localization/Localized Text",
                "Shows its text in the current language: gets a LocalizedText, and \"Sync Project\" puts its text in the table.",
                false, EasyUIElementType.Text)
            {
                Component = typeof(LocalizedText),
                Conflicts = new[] { LocalizationRoles.ExcludeFromLocalization }
            };

            yield return new EasyUIRole(LocalizationRoles.ExcludeFromLocalization, "Localization/Exclude From Localization",
                "Never translated: gets an ExcludeFromLocalization, so \"Sync Project\" leaves it out (e.g. a logo, a number).",
                false, EasyUIElementType.Text)
            {
                Component = typeof(ExcludeFromLocalization)
            };
        }
    }
}
#endif
