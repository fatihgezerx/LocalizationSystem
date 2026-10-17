using Veneer;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>The Localization System brand for the Veneer look: blue accent, logo and banner from <c>Editor/Brand</c>.</summary>
    internal static class LocalizationBrand
    {
        /// <summary>The accent class the brand style sheet answers to (the exact blue, instead of a kit preset).</summary>
        internal const string AccentClass = "loc-accent";

        /// <summary>The cached brand instance; use it for every Inspector and window of the system.</summary>
        internal static readonly VeneerBrand Value = VeneerBrand.FromAssets(
            name: "Localization System",
            subtitle: "Scan, translate and switch languages",
            accent: new Color32(0x3E, 0x63, 0xDD, 255),
            logoPath: "Assets/Scripts/LocalizationSystem/Editor/Brand/logo.png",
            bannerPath: "Assets/Scripts/LocalizationSystem/Editor/Brand/banner.png",
            iconFolder: null,
            styleSheetPath: "Assets/Scripts/LocalizationSystem/Editor/Brand/LocalizationBrand.uss",
            accentClass: AccentClass);
    }
}
