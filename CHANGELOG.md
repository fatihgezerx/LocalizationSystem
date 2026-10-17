# Changelog

## [Unreleased]

### Editor
- Fixed the Language Data window squeezing its action row, counters and tab bar when the Settings tab was open (a stray line through the tab labels, a shifted blue Settings tab, squashed counter cards). Both pages now start at zero height and only fill the room under the tab bar, and the fixed-height rows never shrink, so the two tabs look the same and nothing jumps when you switch.
- The Translations list now looks like a table: a header row with its own background and bottom line, 1 px separators between columns, row lines, comfortable cell padding, a fixed narrow Status column, language columns of a fixed width (adding or removing a language never resizes the others; when they do not fit the window the table scrolls sideways, with the scroll bar just above the footer), a rounded table border and a row hover highlight. Behaviour is unchanged.
- New studio-look **Language Data** window (Veneer, `Tools > Language Data`, same menu path): title bar with logo and a Saved / Unsaved / Translating pill, action row (Sync Project, Export CSV, Import CSV, Clear Data), counters, and two tabs.
  The Translations tab is a virtualized multi-column table with inline editing, flags, missing-cell highlight, a status pill per row, search over keys and text, All / Fully translated / Missing filter chips, per-language column visibility chips, an optional Key column and the column move arrows. The Settings tab holds the language picker with source selection, provider chips with API key and unsupported-language warning, and Game Context with built-in and custom presets. The bottom bar has Save Data, the provider quota and the primary Translate button.
- Studio-look Inspectors for `LocalizedText` (key state pill, read-only key and fallback, translations per language, Open Language Data button) and `ExcludeFromLocalization`.
- Shared UI-free logic moved to `LanguageDataLogic` and `TranslationBatch` (Editor assembly), used by both windows. The IMGUI `LanguageDataWindow` stays as the fallback, compiled only while Veneer is missing; its procedural flag drawings are replaced by the PNG flags (grey box without a file).
- The studio code lives in its own gated assembly `LocalizationSystem.Editor.Studio` (`HAS_VENEER`); `LocalizationSystem.Editor` keeps everything that must work without the kit. New `LocalizationBrand` and `Editor/Brand/LocalizationBrand.uss` (blue `#3E63DD`).
- `DependencyGuard` lists Veneer as an optional dependency.
- Behaviour notes: a row emptied completely is removed when the cell loses focus (before: while typing); filter and status refresh the same way; a source language corrected by the AI is reported in the Translate summary; Save Data and Translate are also available on the Settings tab.
