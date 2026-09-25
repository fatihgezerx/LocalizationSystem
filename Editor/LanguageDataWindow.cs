using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// <c>Tools &gt; Localization System &gt; Language Data</c>: a dockable window (drag it next to
    /// Scene/Game like any other Unity panel) showing every scanned text entry as a spreadsheet -
    /// one row per key, one column per selected language. Every edit (typing a cell, scanning the
    /// scene, adding/removing a language, translating) only changes the in-memory copy; nothing
    /// touches disk until "Save Data" is clicked.
    /// </summary>
    public sealed class LanguageDataWindow : EditorWindow
    {
        private const float LanguageColumnWidth = 220f;
        private const float RowHeight = 20f;
        private const float HeaderHeight = 56f;

        private enum ViewMode
        {
            Table,
            Settings,
        }

        private enum EntryFilter
        {
            All,
            Complete,
            Incomplete,
        }

        // SystemLanguage.Chinese predates the Simplified/Traditional split and is ambiguous - offer
        // only the two specific variants instead. Everything else below is excluded because its
        // share of Steam's actual userbase is negligible - per Steam's own Hardware & Software
        // Survey (2026), the languages left selectable (English, ChineseSimplified, Russian,
        // Spanish, Portuguese, German, French, Japanese, Polish, Korean, Turkish,
        // ChineseTraditional, Thai, Ukrainian, Italian) each sit at roughly 0.6% of users or more,
        // while everything excluded here (e.g. Vietnamese ~0.09%, Dutch ~0.30%, Swedish ~0.29%,
        // Arabic ~0% as a Steam interface language) is far below that. Re-check the survey if this
        // list needs revisiting later - language share shifts over time (Simplified Chinese only
        // overtook English platform-wide in 2024).
        private static readonly HashSet<SystemLanguage> ExcludedLanguages = new()
        {
            SystemLanguage.Unknown,
            SystemLanguage.Chinese,
            SystemLanguage.Afrikaans,
            SystemLanguage.Arabic,
            SystemLanguage.Basque,
            SystemLanguage.Belarusian,
            SystemLanguage.Bulgarian,
            SystemLanguage.Catalan,
            SystemLanguage.Czech,
            SystemLanguage.Danish,
            SystemLanguage.Dutch,
            SystemLanguage.Estonian,
            SystemLanguage.Faroese,
            SystemLanguage.Finnish,
            SystemLanguage.Greek,
            SystemLanguage.Hebrew,
            SystemLanguage.Hindi,
            SystemLanguage.Hungarian,
            SystemLanguage.Icelandic,
            SystemLanguage.Indonesian,
            SystemLanguage.Latvian,
            SystemLanguage.Lithuanian,
            SystemLanguage.Norwegian,
            SystemLanguage.Romanian,
            SystemLanguage.SerboCroatian,
            SystemLanguage.Slovak,
            SystemLanguage.Slovenian,
            SystemLanguage.Swedish,
            SystemLanguage.Vietnamese,
        };

        private LocalizationData _data;
        private Vector2 _tableScroll;
        private Vector2 _languagePickerScroll;
        private ViewMode _viewMode = ViewMode.Table;
        private bool _isDirty;
        private bool _isBusy;
        private string _apiKeyCache;
        private int _selectedPresetIndex = -1;
        private string _presetNameInput = string.Empty;
        private EntryFilter _entryFilter = EntryFilter.All;

        private GUIStyle _headerLabelStyle;
        private GUIStyle _headerBadgeStyle;
        private GUIStyle _reorderButtonStyle;

        // GUILayout.Button's default style carries its own non-zero margin, same as GUI.skin.box did
        // for the header cells - left as-is, each button silently claims a few extra pixels beyond its
        // GUILayout.Width, which is exactly what was making the reorder-button row drift out of column
        // sync with the header and data rows above/below it (worse the more buttons accumulated).
        private GUIStyle ReorderButtonStyle => _reorderButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            margin = new RectOffset(0, 0, 0, 0),
        };

        // Same fix as ReorderButtonStyle above, applied to the actual data-row controls:
        // EditorGUILayout.TextField carries non-zero margin in Unity's default skin, so without this
        // every column drifts a little further out of sync with the header the more columns there
        // are - the more languages added, the worse the "not a real column" misalignment gets.
        private GUIStyle _cellTextFieldStyle;
        private GUIStyle CellTextFieldStyle => _cellTextFieldStyle ??= new GUIStyle(EditorStyles.textField)
        {
            margin = new RectOffset(0, 0, 0, 0),
        };

        private GUIStyle HeaderLabelStyle => _headerLabelStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false,
            clipping = TextClipping.Clip,
        };

        private GUIStyle HeaderBadgeStyle => _headerBadgeStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic,
            wordWrap = false,
            clipping = TextClipping.Clip,
        };

        // A tiny procedural flag icon per language, used instead of emoji - Unity's IMGUI editor font
        // can't reliably render color flag emoji (it falls back to showing the raw two-letter region
        // code as text), so these are drawn by hand with EditorGUI.DrawRect. Three shapes cover most
        // real flags reasonably: flat stripes, a solid field with a centered accent (sun/star/circle -
        // Japan, Turkey, Vietnam, Korea, China), or a Nordic-style off-center cross (Scandinavia).
        private enum FlagKind { Bands, Dot, Cross }

        private readonly struct FlagSwatch
        {
            public readonly FlagKind Kind;
            public readonly bool Horizontal;
            public readonly Color[] Bands;
            public readonly Color AccentColor;
            public readonly Color BorderColor;
            public readonly bool HasBorder;
            public readonly float AccentOffsetX;

            private FlagSwatch(FlagKind kind, bool horizontal, Color[] bands, Color accentColor, Color borderColor, bool hasBorder, float accentOffsetX)
            {
                Kind = kind;
                Horizontal = horizontal;
                Bands = bands;
                AccentColor = accentColor;
                BorderColor = borderColor;
                HasBorder = hasBorder;
                AccentOffsetX = accentOffsetX;
            }

            public static FlagSwatch StripesH(params Color[] bands) => new(FlagKind.Bands, true, bands, default, default, false, 0.5f);
            public static FlagSwatch StripesV(params Color[] bands) => new(FlagKind.Bands, false, bands, default, default, false, 0.5f);
            public static FlagSwatch WithDot(Color field, Color dot, float offsetX = 0.5f) => new(FlagKind.Dot, true, new[] { field }, dot, default, false, offsetX);
            public static FlagSwatch NordicCross(Color field, Color cross, Color? border = null) =>
                new(FlagKind.Cross, true, new[] { field }, cross, border ?? Color.clear, border.HasValue, 0.35f);
        }

        private static readonly FlagSwatch DefaultFlagSwatch = FlagSwatch.StripesH(new Color(0.5f, 0.5f, 0.5f), new Color(0.35f, 0.35f, 0.35f));

        private static readonly Dictionary<SystemLanguage, FlagSwatch> LanguageFlagSwatches = new()
        {
            { SystemLanguage.Afrikaans, FlagSwatch.StripesH(new Color(0f, 0.6f, 0.3f), new Color(1f, 0.85f, 0f), new Color(0f, 0.15f, 0.5f)) },
            { SystemLanguage.Arabic, FlagSwatch.StripesH(new Color(0f, 0.4f, 0.15f)) },
            { SystemLanguage.Belarusian, FlagSwatch.StripesH(new Color(0.8f, 0f, 0f), new Color(0.8f, 0f, 0f), new Color(0.1f, 0.5f, 0.2f)) },
            { SystemLanguage.Bulgarian, FlagSwatch.StripesH(Color.white, new Color(0f, 0.5f, 0.25f), new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.Catalan, FlagSwatch.StripesH(new Color(1f, 0.8f, 0f), new Color(0.8f, 0f, 0f), new Color(1f, 0.8f, 0f), new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.ChineseSimplified, FlagSwatch.WithDot(new Color(0.8f, 0f, 0f), new Color(1f, 0.85f, 0f), 0.3f) },
            { SystemLanguage.ChineseTraditional, FlagSwatch.WithDot(new Color(0.8f, 0f, 0f), new Color(0.1f, 0.2f, 0.6f), 0.3f) },
            { SystemLanguage.Czech, FlagSwatch.StripesH(Color.white, new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.Danish, FlagSwatch.NordicCross(new Color(0.77f, 0.06f, 0.2f), Color.white) },
            { SystemLanguage.Dutch, FlagSwatch.StripesH(new Color(0.8f, 0f, 0.1f), Color.white, new Color(0.1f, 0.2f, 0.6f)) },
            { SystemLanguage.English, FlagSwatch.StripesH(new Color(0.1f, 0.15f, 0.45f), Color.white, new Color(0.75f, 0f, 0.1f)) },
            { SystemLanguage.Estonian, FlagSwatch.StripesH(new Color(0.1f, 0.3f, 0.7f), Color.black, Color.white) },
            { SystemLanguage.Faroese, FlagSwatch.NordicCross(Color.white, new Color(0.8f, 0f, 0.1f), new Color(0.1f, 0.3f, 0.6f)) },
            { SystemLanguage.Finnish, FlagSwatch.NordicCross(Color.white, new Color(0.1f, 0.2f, 0.55f)) },
            { SystemLanguage.French, FlagSwatch.StripesV(new Color(0.1f, 0.2f, 0.6f), Color.white, new Color(0.8f, 0f, 0.1f)) },
            { SystemLanguage.German, FlagSwatch.StripesH(Color.black, new Color(0.8f, 0f, 0f), new Color(1f, 0.8f, 0f)) },
            { SystemLanguage.Greek, FlagSwatch.StripesH(new Color(0.1f, 0.3f, 0.7f), Color.white, new Color(0.1f, 0.3f, 0.7f), Color.white) },
            { SystemLanguage.Hebrew, FlagSwatch.StripesH(new Color(0.1f, 0.3f, 0.6f), Color.white, new Color(0.1f, 0.3f, 0.6f)) },
            { SystemLanguage.Hindi, FlagSwatch.StripesH(new Color(1f, 0.6f, 0.2f), Color.white, new Color(0.1f, 0.5f, 0.2f)) },
            { SystemLanguage.Hungarian, FlagSwatch.StripesH(new Color(0.8f, 0f, 0.1f), Color.white, new Color(0.1f, 0.5f, 0.2f)) },
            { SystemLanguage.Icelandic, FlagSwatch.NordicCross(new Color(0.1f, 0.2f, 0.55f), new Color(0.8f, 0f, 0.1f), Color.white) },
            { SystemLanguage.Indonesian, FlagSwatch.StripesH(new Color(0.8f, 0f, 0f), Color.white) },
            { SystemLanguage.Italian, FlagSwatch.StripesV(new Color(0f, 0.5f, 0.25f), Color.white, new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.Japanese, FlagSwatch.WithDot(Color.white, new Color(0.8f, 0f, 0.1f)) },
            { SystemLanguage.Korean, FlagSwatch.WithDot(Color.white, new Color(0.75f, 0f, 0.15f)) },
            { SystemLanguage.Latvian, FlagSwatch.StripesH(new Color(0.5f, 0f, 0.1f), Color.white, new Color(0.5f, 0f, 0.1f)) },
            { SystemLanguage.Lithuanian, FlagSwatch.StripesH(new Color(1f, 0.8f, 0f), new Color(0f, 0.5f, 0.25f), new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.Norwegian, FlagSwatch.NordicCross(new Color(0.75f, 0f, 0.1f), new Color(0.1f, 0.2f, 0.6f), Color.white) },
            { SystemLanguage.Polish, FlagSwatch.StripesH(Color.white, new Color(0.8f, 0f, 0.1f)) },
            { SystemLanguage.Portuguese, FlagSwatch.StripesV(new Color(0f, 0.5f, 0.25f), new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.Romanian, FlagSwatch.StripesV(new Color(0.1f, 0.2f, 0.6f), new Color(1f, 0.8f, 0f), new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.Russian, FlagSwatch.StripesH(Color.white, new Color(0.1f, 0.2f, 0.6f), new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.SerboCroatian, FlagSwatch.StripesH(new Color(0.8f, 0f, 0f), new Color(0.1f, 0.2f, 0.6f), Color.white) },
            { SystemLanguage.Slovak, FlagSwatch.StripesH(Color.white, new Color(0.1f, 0.2f, 0.6f), new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.Slovenian, FlagSwatch.StripesH(Color.white, new Color(0.1f, 0.2f, 0.6f), new Color(0.8f, 0f, 0f)) },
            { SystemLanguage.Spanish, FlagSwatch.StripesH(new Color(0.8f, 0f, 0.1f), new Color(1f, 0.8f, 0f), new Color(0.8f, 0f, 0.1f)) },
            { SystemLanguage.Swedish, FlagSwatch.NordicCross(new Color(0.1f, 0.3f, 0.65f), new Color(1f, 0.8f, 0f)) },
            { SystemLanguage.Thai, FlagSwatch.StripesH(new Color(0.8f, 0f, 0.1f), Color.white, new Color(0.1f, 0.2f, 0.5f), Color.white, new Color(0.8f, 0f, 0.1f)) },
            { SystemLanguage.Turkish, FlagSwatch.WithDot(new Color(0.85f, 0f, 0.1f), Color.white, 0.35f) },
            { SystemLanguage.Ukrainian, FlagSwatch.StripesH(new Color(0.1f, 0.3f, 0.75f), new Color(1f, 0.85f, 0f)) },
            { SystemLanguage.Vietnamese, FlagSwatch.WithDot(new Color(0.8f, 0f, 0.1f), new Color(1f, 0.8f, 0f)) },
        };

        // Optional real flag PNGs. Drop a file named e.g. "Turkish.png" (matching DisplayName exactly)
        // into LocalizationSystem/Editor/Flags/ and it's used automatically in place of the procedural
        // swatch below - no code changes needed. Missing files just fall back. The folder is found next
        // to this assembly's asmdef, wherever LocalizationSystem was copied to.
        private static string _flagsFolder;

        private static string FlagsFolder => _flagsFolder ??=
            Path.GetDirectoryName(CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("LocalizationSystem.Editor"))!
                .Replace('\\', '/') + "/Flags";

        private readonly Dictionary<SystemLanguage, Texture2D> _flagTextureCache = new();

        private Texture2D GetFlagTexture(SystemLanguage language)
        {
            if (_flagTextureCache.TryGetValue(language, out var cached))
            {
                return cached;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{FlagsFolder}/{DisplayName(language)}.png");
            _flagTextureCache[language] = texture;
            return texture;
        }

        private void DrawFlag(Rect rect, SystemLanguage language)
        {
            var texture = GetFlagTexture(language);
            if (texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
                return;
            }

            DrawFlagSwatch(rect, language);
        }

        private static void DrawFlagSwatch(Rect rect, SystemLanguage language)
        {
            var swatch = LanguageFlagSwatches.TryGetValue(language, out var found) ? found : DefaultFlagSwatch;

            switch (swatch.Kind)
            {
                case FlagKind.Dot:
                {
                    EditorGUI.DrawRect(rect, swatch.Bands[0]);
                    var dotSize = Mathf.Min(rect.width, rect.height) * 0.55f;
                    var dotRect = new Rect(
                        rect.x + rect.width * swatch.AccentOffsetX - dotSize / 2f,
                        rect.y + rect.height / 2f - dotSize / 2f,
                        dotSize,
                        dotSize);
                    EditorGUI.DrawRect(dotRect, swatch.AccentColor);
                    break;
                }

                case FlagKind.Cross:
                {
                    EditorGUI.DrawRect(rect, swatch.Bands[0]);
                    var vThickness = rect.width * 0.22f;
                    var hThickness = rect.height * 0.3f;
                    var vX = rect.x + rect.width * swatch.AccentOffsetX - vThickness / 2f;
                    var hY = rect.y + rect.height / 2f - hThickness / 2f;

                    if (swatch.HasBorder)
                    {
                        const float borderPad = 1.5f;
                        EditorGUI.DrawRect(new Rect(vX - borderPad, rect.y, vThickness + borderPad * 2f, rect.height), swatch.BorderColor);
                        EditorGUI.DrawRect(new Rect(rect.x, hY - borderPad, rect.width, hThickness + borderPad * 2f), swatch.BorderColor);
                    }

                    EditorGUI.DrawRect(new Rect(vX, rect.y, vThickness, rect.height), swatch.AccentColor);
                    EditorGUI.DrawRect(new Rect(rect.x, hY, rect.width, hThickness), swatch.AccentColor);
                    break;
                }

                default:
                {
                    var bands = swatch.Bands;
                    if (swatch.Horizontal)
                    {
                        var bandHeight = rect.height / bands.Length;
                        for (var i = 0; i < bands.Length; i++)
                        {
                            EditorGUI.DrawRect(new Rect(rect.x, rect.y + i * bandHeight, rect.width, bandHeight), bands[i]);
                        }
                    }
                    else
                    {
                        var bandWidth = rect.width / bands.Length;
                        for (var i = 0; i < bands.Length; i++)
                        {
                            EditorGUI.DrawRect(new Rect(rect.x + i * bandWidth, rect.y, bandWidth, rect.height), bands[i]);
                        }
                    }
                    break;
                }
            }

            var border = new Color(0f, 0f, 0f, 0.5f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);
        }

        [MenuItem("Tools/Language Data")]
        private static void Open()
        {
            var window = GetWindow<LanguageDataWindow>("Language Data");
            window.Show();
        }

        private void OnEnable()
        {
            _data = LocalizationDataStore.Load();
            _apiKeyCache = AiProviderSettings.GetApiKey(AiProviderSettings.GetProvider());
            SetDirty(false);

            // Re-check for flag PNGs every time the window opens, so dropping files into the Flags
            // folder and refocusing the window picks them up without needing a script recompile.
            _flagTextureCache.Clear();
        }

        // Reloads when this window regains focus, so picking up external changes (e.g. someone else
        // editing the JSON directly) works - but never while there are unsaved edits, or a translation
        // batch is still running in the background, or we'd silently discard/orphan that work.
        private void OnFocus()
        {
            if (!_isDirty && !_isBusy)
            {
                _data = LocalizationDataStore.Load();
            }
        }

        private void OnGUI()
        {
            _data ??= LocalizationDataStore.Load();

            DrawToolbar();
            EditorGUILayout.Space(4);

            switch (_viewMode)
            {
                case ViewMode.Settings:
                    DrawSettings();
                    return;
                default:
                    DrawTable();
                    DrawFooter();
                    break;
            }
        }

        private static string DisplayName(SystemLanguage language) => LocalizationRuntime.DisplayName(language);

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label($"Source language: {DisplayName(_data.SourceLanguage)}", EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();

            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.9f, 0.45f, 0.45f);
            GUI.enabled = !_isBusy;
            if (GUILayout.Button("Clear Data", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                var confirmed = EditorUtility.DisplayDialog(
                    "Clear All Language Data?",
                    "This removes every language and every text entry currently loaded (in-memory only " +
                    "- nothing on disk changes unless you click \"Save Data\" afterwards), and also " +
                    "removes every \"Localized Text\" component that \"Sync Project\" added to scene " +
                    "objects (this part does change the open scene, and is not undoable). [Localize] " +
                    "attributes on your own scripts are untouched - those just mark fields for the " +
                    "next sync to pick up again.",
                    "Clear All",
                    "Cancel");

                if (confirmed)
                {
                    _data = new LocalizationData();
                    RemoveAllLocalizedTextComponents();
                    SetDirty(true);
                }
            }
            GUI.enabled = true;
            GUI.backgroundColor = previousColor;

            GUI.enabled = !_isBusy;
            if (GUILayout.Button("Sync Project", EditorStyles.toolbarButton, GUILayout.Width(110)))
            {
                var (sceneFound, sceneAdded) = SceneScanner.Scan(_data);
                var (fieldsFound, fieldsAdded) = AttributeFieldScanner.Scan(_data);
                var totalFound = sceneFound + fieldsFound;
                var totalAdded = sceneAdded + fieldsAdded;

                if (totalAdded > 0)
                {
                    SetDirty(true);
                }
                ShowNotification(new GUIContent(
                    $"Sync complete - found {totalFound} string{(totalFound == 1 ? "" : "s")}, " +
                    $"added {totalAdded} new entr{(totalAdded == 1 ? "y" : "ies")}."));
            }
            GUI.enabled = true;

            if (GUILayout.Button("Export CSV", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                ExportCsv();
            }

            if (GUILayout.Button("Import CSV", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                ImportCsv();
            }

            if (GUILayout.Button(_viewMode == ViewMode.Settings ? "Back to Table" : "Settings", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                _viewMode = _viewMode == ViewMode.Settings ? ViewMode.Table : ViewMode.Settings;
            }
            EditorGUILayout.EndHorizontal();
        }

        // "Clear Data" nukes every entry, so any LocalizedText left pointing at one of those now-gone
        // keys would just show its raw key on screen forever - remove them too, so the scene ends up
        // consistent with the (now-empty) data. [Localize] attributes are untouched - those live in
        // code, not on scene objects, and just mark fields for the next "Sync Project" to find again.
        private static void RemoveAllLocalizedTextComponents()
        {
            foreach (var localizedText in UnityEngine.Object.FindObjectsByType<LocalizedText>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(localizedText);
            }
        }

        private void ExportCsv()
        {
            var path = EditorUtility.SaveFilePanel("Export Language Data as CSV", "", "LocalizationData.csv", "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // Encoding.UTF8 (not a plain "new UTF8Encoding()") writes a byte-order mark, which is
            // what makes Excel on Windows reliably detect UTF-8 instead of misreading non-Latin text
            // (Turkish, Chinese, Japanese, Korean, Thai, ...) as garbled Latin-1.
            File.WriteAllText(path, LanguageDataCsv.Export(_data), Encoding.UTF8);
            ShowNotification(new GUIContent($"Exported {_data.Entries.Count} entries to {Path.GetFileName(path)}."));
        }

        private void ImportCsv()
        {
            var path = EditorUtility.OpenFilePanel("Import Language Data from CSV", "", "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var (rowsMatched, valuesUpdated) = LanguageDataCsv.Import(_data, File.ReadAllText(path, Encoding.UTF8));
                if (valuesUpdated > 0)
                {
                    SetDirty(true);
                }
                ShowNotification(new GUIContent($"Imported {rowsMatched} row(s), updated {valuesUpdated} value(s)."));
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Import Failed", ex.Message, "OK");
            }
        }

        private const float LanguagePickerPanelWidth = 340f;

        // A single "Settings..." view instead of two separate ones - languages on the left, AI
        // provider/API key/Game Context on the right, side by side rather than one full-window panel
        // at a time.
        private void DrawSettings()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(LanguagePickerPanelWidth));
            DrawLanguagePicker();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            DrawAiSettings();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLanguagePicker()
        {
            EditorGUILayout.HelpBox(
                "Choose which languages this project supports. Adding one adds an empty column to " +
                "the table; removing one deletes its translations for every entry. Click \"Save Data\" " +
                "when you're done to write these changes to disk.",
                MessageType.Info);

            _languagePickerScroll = EditorGUILayout.BeginScrollView(_languagePickerScroll);

            var seenValues = new HashSet<int>();
            foreach (SystemLanguage language in System.Enum.GetValues(typeof(SystemLanguage)))
            {
                if (ExcludedLanguages.Contains(language))
                {
                    continue;
                }

                // SystemLanguage has a couple of legacy aliases that share the same underlying value
                // as another member (e.g. Hungarian) - skip repeats so each real language is listed once.
                if (!seenValues.Add((int)language))
                {
                    continue;
                }

                var included = _data.Languages.Contains(language);

                EditorGUILayout.BeginHorizontal();
                var newIncluded = EditorGUILayout.ToggleLeft(DisplayName(language), included, GUILayout.Width(220));

                var isSource = _data.SourceLanguage == language;
                GUI.enabled = included && !isSource;
                if (GUILayout.Button(isSource ? "Source" : "Set as Source", GUILayout.Width(110)))
                {
                    SetSourceLanguage(language);
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (newIncluded != included)
                {
                    if (newIncluded)
                    {
                        _data.Languages.Add(language);
                    }
                    else
                    {
                        _data.Languages.Remove(language);
                        if (_data.SourceLanguage == language && _data.Languages.Count > 0)
                        {
                            _data.SourceLanguage = _data.Languages[0];
                        }
                    }
                    SetDirty(true);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawAiSettings()
        {
            EditorGUILayout.LabelField("Translation Provider", EditorStyles.boldLabel);

            var provider = AiProviderSettings.GetProvider();
            EditorGUI.BeginChangeCheck();
            var newProvider = (AiProvider)EditorGUILayout.EnumPopup(provider);
            if (EditorGUI.EndChangeCheck())
            {
                AiProviderSettings.SetProvider(newProvider);
                provider = newProvider;
                _apiKeyCache = AiProviderSettings.GetApiKey(provider);
            }

            var translationProvider = TranslationProviders.All[provider];

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"{translationProvider.DisplayName} API Key", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Stored in this machine's Editor preferences only - never written into the project, " +
                "so it can never end up committed to git or shared if this project is published. " +
                "Each provider keeps its own key, so switching providers above doesn't lose the others.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var newKey = EditorGUILayout.PasswordField(_apiKeyCache);
            if (EditorGUI.EndChangeCheck())
            {
                _apiKeyCache = newKey;
                AiProviderSettings.SetApiKey(provider, newKey);
            }

            if (GUILayout.Button("Get API Key", GUILayout.Width(110)))
            {
                Application.OpenURL(translationProvider.ApiKeyHelpUrl);
            }
            EditorGUILayout.EndHorizontal();

            DrawUnsupportedLanguagesWarning(provider);

            EditorGUILayout.Space(16);
            EditorGUILayout.LabelField("Game Context (optional)", EditorStyles.boldLabel);

            if (!translationProvider.SupportsGameContext)
            {
                EditorGUILayout.HelpBox(
                    $"{translationProvider.DisplayName} is plain machine translation, not an AI model - " +
                    "it has no concept of tone or genre, so Game Context below is saved but has no " +
                    "effect on its output. Switch to Gemini, OpenAI, or Claude to make use of it.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Describe your game so the AI translates with the right tone and terminology, e.g. " +
                    "\"Fantasy RPG, epic/formal tone, similar to Skyrim.\" Saved with the rest of Language " +
                    "Data via \"Save Data\" - this isn't a secret, unlike the API key above. Pick a genre " +
                    "preset below to fill this in automatically, then tweak it as you like.",
                    MessageType.Info);
            }

            // Plain machine translation has no concept of tone/genre, so there's nothing for these
            // controls to actually do - disable them rather than leaving them editable but inert.
            GUI.enabled = translationProvider.SupportsGameContext;
            DrawGameContextPresetPicker();

            EditorGUI.BeginChangeCheck();
            var newContext = EditorGUILayout.TextArea(_data.GameContext ?? string.Empty, GUILayout.Height(60));
            if (EditorGUI.EndChangeCheck())
            {
                _data.GameContext = newContext;
                SetDirty(true);
            }

            DrawSavePromptRow();
            GUI.enabled = true;
        }

        // Shown right under the API key for a per-language provider (Google Translate, DeepL) that
        // doesn't cover every language this project might use - computed from LanguageCodeMap rather
        // than hardcoded, so it stays correct if the project's language list changes later.
        private void DrawUnsupportedLanguagesWarning(AiProvider provider)
        {
            IReadOnlyDictionary<SystemLanguage, string> codeMap = provider switch
            {
                AiProvider.GoogleTranslate => LanguageCodeMap.Google,
                AiProvider.DeepL => LanguageCodeMap.DeepL,
                _ => null,
            };

            if (codeMap == null)
            {
                return;
            }

            var unsupported = _data.Languages.Where(language => !codeMap.ContainsKey(language)).Select(DisplayName).ToList();
            if (unsupported.Count == 0)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                $"{TranslationProviders.All[provider].DisplayName} doesn't support: {string.Join(", ", unsupported)}. " +
                "Those columns will stay empty when translating with this provider - use a different one for them.",
                MessageType.Warning);
        }

        // Built-in genre presets (GameContextPresets) followed by the project's own saved custom
        // ones - picking any of them overwrites the Game Context text below with that prompt.
        // Re-picking (even the same one again) always re-applies the original text, discarding
        // whatever was typed in the meantime - "Save Prompt" below is how you keep an edited version.
        private (string Name, string Prompt)[] AllGameContextPresets() =>
            GameContextPresets.BuiltIn
                .Concat(_data.CustomGameContextPresets.Select(p => (p.Name, p.Text)))
                .ToArray();

        private void DrawGameContextPresetPicker()
        {
            var presets = AllGameContextPresets();
            var names = presets.Select(p => p.Name).ToArray();
            var displayIndex = _selectedPresetIndex >= 0 && _selectedPresetIndex < names.Length ? _selectedPresetIndex : -1;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Preset", GUILayout.Width(70));
            var newIndex = EditorGUILayout.Popup(displayIndex, names);
            EditorGUILayout.EndHorizontal();

            if (newIndex != displayIndex && newIndex >= 0)
            {
                _selectedPresetIndex = newIndex;
                _data.GameContext = presets[newIndex].Prompt;
                SetDirty(true);
                GUI.FocusControl(null);
            }
        }

        private void DrawSavePromptRow()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("New preset name", GUILayout.Width(110));
            _presetNameInput = EditorGUILayout.TextField(_presetNameInput);

            // Compose with (not overwrite) whatever GUI.enabled the caller already set - DrawAiSettings
            // disables this whole row when the active provider doesn't support Game Context at all.
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !string.IsNullOrWhiteSpace(_presetNameInput) && !string.IsNullOrWhiteSpace(_data.GameContext);
            if (GUILayout.Button("Save Prompt", GUILayout.Width(110)))
            {
                SaveCustomGameContextPreset(_presetNameInput.Trim(), _data.GameContext);
                _presetNameInput = string.Empty;
                GUI.FocusControl(null);
            }
            GUI.enabled = wasEnabled;
            EditorGUILayout.EndHorizontal();
        }

        // Overwrites an existing custom preset with the same name rather than creating a duplicate,
        // so re-saving under a name you've already used updates it instead of cluttering the list.
        private void SaveCustomGameContextPreset(string name, string prompt)
        {
            var existing = _data.CustomGameContextPresets.FirstOrDefault(p => p.Name == name);
            if (existing != null)
            {
                existing.Text = prompt;
            }
            else
            {
                _data.CustomGameContextPresets.Add(new NamedPrompt { Name = name, Text = prompt });
            }

            _selectedPresetIndex = System.Array.IndexOf(AllGameContextPresets().Select(p => p.Name).ToArray(), name);
            SetDirty(true);
        }

        private void DrawTable()
        {
            if (_data.Languages.Count == 0)
            {
                EditorGUILayout.HelpBox("No languages selected yet. Click \"Settings...\" above to add some.", MessageType.Info);
                return;
            }

            if (_data.Entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No text entries yet. Click \"Sync Project\" to find text in your scene and any " +
                    "[Localize]-marked string fields in your project's ScriptableObjects, prefabs, and scene objects.",
                    MessageType.Info);
                return;
            }

            var completeCount = _data.Entries.Count(IsFullyTranslated);
            DrawFilterBar(_data.Entries.Count, completeCount, _data.Entries.Count - completeCount);

            var visibleEntries = GetFilteredEntries();
            if (visibleEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("No entries match the current filter.", MessageType.Info);
                return;
            }

            _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll);

            SystemLanguage? moveLeft = null;
            SystemLanguage? moveRight = null;

            EditorGUILayout.BeginHorizontal();

            // Every header cell is positioned with plain Rect math (no GUIStyle-driven BeginVertical)
            // and reserved with the exact same GUILayout.Width/Height call the data-row TextFields
            // below use - so there is no style margin/padding to silently drift the header out of
            // alignment with its column over several columns.
            for (var i = 0; i < _data.Languages.Count; i++)
            {
                var language = _data.Languages[i];
                var isSource = language == _data.SourceLanguage;
                var headerRect = GUILayoutUtility.GetRect(LanguageColumnWidth, HeaderHeight, GUILayout.Width(LanguageColumnWidth), GUILayout.Height(HeaderHeight));

                if (Event.current.type != EventType.Repaint)
                {
                    continue;
                }

                var boxColor = isSource ? new Color(0.55f, 0.45f, 0.2f) : new Color(0.24f, 0.24f, 0.24f);
                EditorGUI.DrawRect(headerRect, boxColor);

                var border = new Color(0f, 0f, 0f, 0.5f);
                EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, headerRect.width, 1f), border);
                EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1f, headerRect.width, 1f), border);
                EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 1f, headerRect.height), border);
                EditorGUI.DrawRect(new Rect(headerRect.xMax - 1f, headerRect.y, 1f, headerRect.height), border);

                var flagRect = new Rect(headerRect.x + headerRect.width / 2f - 14f, headerRect.y + 4f, 28f, 16f);
                DrawFlag(flagRect, language);

                var nameRect = new Rect(headerRect.x + 2f, flagRect.yMax + 2f, headerRect.width - 4f, 16f);
                GUI.Label(nameRect, DisplayName(language), HeaderLabelStyle);

                if (isSource)
                {
                    var badgeRect = new Rect(headerRect.x + 2f, nameRect.yMax + 2f, headerRect.width - 4f, 16f);
                    GUI.Label(badgeRect, "Source", HeaderBadgeStyle);
                }
            }
            EditorGUILayout.EndHorizontal();

            // Reorder buttons live in their own row, entirely separate from the boxed header above -
            // plain sequential GUILayout calls, the same reliable pattern the data-row cells and the
            // Save/Translate buttons already use. Nesting GUILayout.BeginArea inside the header row's
            // BeginHorizontal (an earlier attempt) silently broke rendering - GUILayout.BeginArea starts
            // its own top-level layout root and doesn't compose reliably nested inside another group's
            // automatic flow, so the buttons never actually appeared.
            EditorGUILayout.BeginHorizontal();

            for (var i = 0; i < _data.Languages.Count; i++)
            {
                var language = _data.Languages[i];
                var halfWidth = LanguageColumnWidth / 2f;

                if (language == _data.SourceLanguage)
                {
                    GUILayout.Space(LanguageColumnWidth);
                    continue;
                }

                // Index 0 is always the pinned source, so the first non-source column (index 1) can
                // never move left, and the last column can never move right - hide those buttons
                // entirely rather than showing a disabled one.
                var showLeft = i > 1;
                var showRight = i < _data.Languages.Count - 1;

                if (showLeft)
                {
                    if (GUILayout.Button("◀", ReorderButtonStyle, GUILayout.Width(halfWidth), GUILayout.Height(18f)))
                    {
                        moveLeft = language;
                    }
                }
                else
                {
                    GUILayout.Space(halfWidth);
                }

                if (showRight)
                {
                    if (GUILayout.Button("▶", ReorderButtonStyle, GUILayout.Width(halfWidth), GUILayout.Height(18f)))
                    {
                        moveRight = language;
                    }
                }
                else
                {
                    GUILayout.Space(halfWidth);
                }
            }
            EditorGUILayout.EndHorizontal();

            // Deferred until after the loop finishes - mutating _data.Languages mid-foreach would throw.
            if (moveLeft.HasValue)
            {
                MoveLanguage(moveLeft.Value, -1);
            }
            if (moveRight.HasValue)
            {
                MoveLanguage(moveRight.Value, 1);
            }

            DrawVirtualizedRows(visibleEntries);

            EditorGUILayout.EndScrollView();
        }

        private bool IsFullyTranslated(LocalizationEntry entry) =>
            _data.Languages.All(language => !string.IsNullOrEmpty(entry.GetValue(language)));

        private List<LocalizationEntry> GetFilteredEntries() => _entryFilter switch
        {
            EntryFilter.Complete => _data.Entries.Where(IsFullyTranslated).ToList(),
            EntryFilter.Incomplete => _data.Entries.Where(entry => !IsFullyTranslated(entry)).ToList(),
            _ => _data.Entries,
        };

        private static readonly string[] EntryFilterLabels = { "Show All", "Fully Translated", "Missing Translations" };

        private void DrawFilterBar(int totalCount, int completeCount, int incompleteCount)
        {
            var labels = new[]
            {
                $"{EntryFilterLabels[0]} ({totalCount})",
                $"{EntryFilterLabels[1]} ({completeCount})",
                $"{EntryFilterLabels[2]} ({incompleteCount})",
            };

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Filter", GUILayout.Width(40));
            var selected = GUILayout.Toolbar((int)_entryFilter, labels);
            EditorGUILayout.EndHorizontal();

            if (selected != (int)_entryFilter)
            {
                _entryFilter = (EntryFilter)selected;
            }
        }

        // Only the rows currently scrolled into view get real GUILayout controls - with hundreds or
        // thousands of entries, laying out every single row on every OnGUI call (Unity does this on
        // far more than just Repaint - mouse moves, etc.) is exactly the kind of thing that makes the
        // Editor UI grind to a halt or "hang". Off-screen rows are represented by a single
        // GUILayout.Space matching their total height instead, so the scrollbar's size/position stay
        // correct without those rows actually existing as controls.
        private const int RowRenderBuffer = 5;

        private void DrawVirtualizedRows(List<LocalizationEntry> visibleEntries)
        {
            var totalRows = visibleEntries.Count;

            // How far scrolled past the header + reorder-buttons row (both always fully rendered
            // above this point) into the actual data rows.
            var rowsTopOffset = HeaderHeight + 18f;
            var scrollWithinRows = Mathf.Max(0f, _tableScroll.y - rowsTopOffset);

            // There's no cheap way to ask GUILayout for the scroll view's exact visible pixel height
            // before laying out its contents, so this uses the whole window's height as a generous
            // upper bound - that only means a handful of extra off-screen rows get rendered too, never
            // that a row that should be visible gets skipped.
            var estimatedVisibleRowCount = Mathf.CeilToInt(position.height / RowHeight) + RowRenderBuffer * 2;
            var firstVisible = Mathf.Max(0, Mathf.FloorToInt(scrollWithinRows / RowHeight) - RowRenderBuffer);
            var lastVisible = Mathf.Min(totalRows, firstVisible + estimatedVisibleRowCount);

            GUILayout.Space(firstVisible * RowHeight);

            // Rows that are scrolled off-screen right now simply aren't checked this frame - an
            // all-empty row only gets cleaned up once it's actually scrolled into view. Harmless: it's
            // just inert data sitting in the table until then, not a correctness problem.
            var entriesToRemove = new List<LocalizationEntry>();
            for (var i = firstVisible; i < lastVisible; i++)
            {
                DrawEntryRow(visibleEntries[i], entriesToRemove);
            }

            GUILayout.Space(Mathf.Max(0, totalRows - lastVisible) * RowHeight);

            if (entriesToRemove.Count > 0)
            {
                foreach (var entry in entriesToRemove)
                {
                    _data.Entries.Remove(entry);
                }
                SetDirty(true);
            }
        }

        private void DrawEntryRow(LocalizationEntry entry, List<LocalizationEntry> entriesToRemove)
        {
            EditorGUILayout.BeginHorizontal();

            foreach (var language in _data.Languages)
            {
                var current = entry.GetValue(language) ?? string.Empty;
                EditorGUI.BeginChangeCheck();
                var updated = EditorGUILayout.TextField(current, CellTextFieldStyle, GUILayout.Width(LanguageColumnWidth), GUILayout.Height(RowHeight));
                if (EditorGUI.EndChangeCheck())
                {
                    // In-memory only - no disk write per keystroke. Committed by "Save Data".
                    entry.SetValue(language, updated);
                    SetDirty(true);
                }
            }

            EditorGUILayout.EndHorizontal();

            // Clearing every language down to nothing leaves a row with no content worth keeping -
            // drop the whole entry instead of leaving an empty row behind.
            if (_data.Languages.All(language => string.IsNullOrEmpty(entry.GetValue(language))))
            {
                entriesToRemove.Add(entry);
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = _isDirty && !_isBusy;
            if (GUILayout.Button(_isDirty ? "Save Data*" : "Save Data", GUILayout.Width(140), GUILayout.Height(26)))
            {
                LocalizationDataStore.Save(_data);
                SetDirty(false);
                ShowNotification(new GUIContent("Language Data saved."));
            }

            GUILayout.FlexibleSpace();

            var provider = AiProviderSettings.GetProvider();
            var translationProvider = TranslationProviders.All[provider];
            var rateLimit = translationProvider.LastKnownRateLimit;
            DrawRateLimitStatus(rateLimit, translationProvider.DisplayName);

            var isBlocked = rateLimit.IsExhausted && (!rateLimit.ResetAt.HasValue || rateLimit.ResetAt.Value > DateTimeOffset.UtcNow);
            GUI.enabled = !_isBusy && !isBlocked;
            if (GUILayout.Button(_isBusy ? "Translating..." : "Translate", GUILayout.Width(140), GUILayout.Height(26)))
            {
                TranslateMissingAsync();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        // Always visible right next to Translate, for every provider - no click needed. Providers
        // that never report quota proactively (Gemini, before a call has failed for hitting it) still
        // get a line here, just one that says there's nothing to report yet instead of real numbers.
        private static void DrawRateLimitStatus(RateLimitInfo rateLimit, string providerName)
        {
            if (!rateLimit.HasData)
            {
                GUILayout.Label($"{providerName}: quota unknown until first request", EditorStyles.miniLabel, GUILayout.Width(230));
                GUILayout.Space(8);
                return;
            }

            if (rateLimit.RemainingRequests.HasValue && rateLimit.LimitRequests.HasValue)
            {
                var limit = rateLimit.LimitRequests.Value;
                var remaining = rateLimit.RemainingRequests.Value;
                var used = Mathf.Max(0, limit - remaining);
                var fraction = limit > 0 ? Mathf.Clamp01((float)used / limit) : 0f;

                var barRect = GUILayoutUtility.GetRect(130f, 18f, GUILayout.Width(130), GUILayout.Height(18));
                EditorGUI.ProgressBar(barRect, fraction, $"{used}/{limit}");
                GUILayout.Space(6);
            }

            var statusParts = new List<string>();
            if (rateLimit.IsExhausted)
            {
                statusParts.Add("exhausted");
            }
            if (rateLimit.ResetAt.HasValue)
            {
                statusParts.Add($"resets {FormatResetTime(rateLimit.ResetAt.Value)}");
            }

            var statusText = statusParts.Count > 0 ? $"{providerName}: {string.Join(", ", statusParts)}" : providerName;

            // The full caveat (when there is one, e.g. Gemini's "this reset time isn't guaranteed
            // exact") shows as a native tooltip on hover instead of taking up footer space outright.
            var content = new GUIContent(statusText, rateLimit.Caveat);

            var previousColor = GUI.color;
            GUI.color = rateLimit.IsExhausted ? new Color(1f, 0.6f, 0.6f) : previousColor;
            GUILayout.Label(content, EditorStyles.miniLabel, GUILayout.Width(210));
            GUI.color = previousColor;

            GUILayout.Space(8);
        }

        private static string FormatResetTime(DateTimeOffset resetAt)
        {
            var remaining = resetAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return "now";
            }

            return remaining.TotalMinutes < 2
                ? $"in {(int)remaining.TotalSeconds}s"
                : $"in {(int)remaining.TotalMinutes}m";
        }

        // Every entry that has source text but is still missing at least one other language, paired
        // with exactly the languages it's missing. Re-run after a source-language correction, since
        // that can turn a previously-"complete" entry's old source cell into a fresh gap.
        private List<(LocalizationEntry Entry, List<SystemLanguage> Targets)> BuildPendingTranslations()
        {
            var pending = new List<(LocalizationEntry, List<SystemLanguage>)>();
            foreach (var entry in _data.Entries)
            {
                if (string.IsNullOrEmpty(entry.GetValue(_data.SourceLanguage)))
                {
                    continue;
                }

                var targets = _data.Languages
                    .Where(language => language != _data.SourceLanguage && string.IsNullOrEmpty(entry.GetValue(language)))
                    .ToList();

                if (targets.Count > 0)
                {
                    pending.Add((entry, targets));
                }
            }

            return pending;
        }

        private async void TranslateMissingAsync()
        {
            var provider = AiProviderSettings.GetProvider();
            var translationProvider = TranslationProviders.All[provider];
            var apiKey = AiProviderSettings.GetApiKey(provider);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                EditorUtility.DisplayDialog(
                    $"{translationProvider.DisplayName} API Key Required",
                    $"Open \"Settings...\" and paste in a {translationProvider.DisplayName} API key first.",
                    "OK");
                return;
            }

            var pending = BuildPendingTranslations();
            if (pending.Count == 0)
            {
                ShowNotification(new GUIContent("Nothing to translate - every cell already has text."));
                return;
            }

            _isBusy = true;
            var translatedFields = 0;
            var failedEntries = 0;
            var sourceLanguageChecked = false;
            var stoppedForQuota = false;
            var exhaustedProviders = new HashSet<AiProvider> { provider };
            var switchLog = new List<string>();

            try
            {
                var i = 0;
                while (i < pending.Count)
                {
                    var (entry, targets) = pending[i];
                    var cancelled = EditorUtility.DisplayCancelableProgressBar(
                        $"Translating with {translationProvider.DisplayName}",
                        $"{entry.Key} ({i + 1}/{pending.Count})",
                        (float)i / pending.Count);

                    if (cancelled)
                    {
                        break;
                    }

                    var sourceText = entry.GetValue(_data.SourceLanguage);

                    try
                    {
                        var result = await translationProvider.TranslateAsync(sourceText, targets, _data.GameContext, apiKey);

                        // Only act on the first entry's detection - later entries could disagree
                        // (typos, ambiguous short strings) and we don't want to flip-flop mid-batch.
                        if (!sourceLanguageChecked)
                        {
                            sourceLanguageChecked = true;
                            if (ApplyDetectedSourceLanguage(result.DetectedSourceLanguage))
                            {
                                // The language everyone assumed was source is now just a normal,
                                // empty column - rebuild the whole batch so it gets filled in too,
                                // instead of being left blank.
                                pending = BuildPendingTranslations();
                                i = -1;
                            }
                        }

                        foreach (var language in targets)
                        {
                            if (result.Translations.TryGetValue(DisplayName(language), out var text))
                            {
                                entry.SetValue(language, text);
                                translatedFields++;
                            }
                        }

                        if (result.Translations.Count > 0)
                        {
                            SetDirty(true);
                            Repaint();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        failedEntries++;
                        Debug.LogError($"[LocalizationSystem] Translating '{entry.Key}' failed: {ex.Message}");

                        // The provider itself just reported it's out of quota - every remaining entry
                        // would fail the exact same way (after its own pointless retry/backoff wait).
                        // Rather than just stopping, look for another provider that has an API key
                        // configured and keep going with that one instead - only give up entirely once
                        // none are left (this never assumes a fallback exists; it's a plain null check).
                        var rateLimit = translationProvider.LastKnownRateLimit;
                        var isExhausted = rateLimit.IsExhausted && (!rateLimit.ResetAt.HasValue || rateLimit.ResetAt.Value > DateTimeOffset.UtcNow);
                        if (isExhausted)
                        {
                            exhaustedProviders.Add(provider);
                            var nextProvider = FindNextAvailableProvider(exhaustedProviders);

                            if (nextProvider.HasValue)
                            {
                                var nextTranslationProvider = TranslationProviders.All[nextProvider.Value];
                                switchLog.Add($"{translationProvider.DisplayName} → {nextTranslationProvider.DisplayName}");

                                provider = nextProvider.Value;
                                translationProvider = nextTranslationProvider;
                                apiKey = AiProviderSettings.GetApiKey(provider);

                                // Retry this same entry with the new provider - don't advance i, and
                                // skip the pacing delay below since the new provider hasn't made a
                                // single request yet.
                                continue;
                            }

                            stoppedForQuota = true;
                            break;
                        }
                    }

                    i++;

                    // Pace requests so a big batch doesn't immediately hit the free tier's rate limit.
                    if (i < pending.Count)
                    {
                        await Task.Delay(translationProvider.RequestDelay);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isBusy = false;
            }

            var summary = $"Translated {translatedFields} field(s).";
            if (failedEntries > 0)
            {
                summary += $" {failedEntries} entr{(failedEntries == 1 ? "y" : "ies")} failed - see Console.";
            }
            if (switchLog.Count > 0)
            {
                summary += $" Switched provider after hitting a quota ({string.Join(", ", switchLog)}).";
            }
            if (stoppedForQuota)
            {
                summary += $" Stopped early - {translationProvider.DisplayName} quota is exhausted and no other provider has an API key set.";
            }
            ShowNotification(new GUIContent(summary));
        }

        // Walks every provider (in enum declaration order) looking for one that isn't already known
        // to be exhausted this batch and has an API key configured - returns null if none qualify, so
        // the caller can fall back to stopping the batch instead of assuming a provider is available.
        private static AiProvider? FindNextAvailableProvider(HashSet<AiProvider> exhaustedProviders)
        {
            foreach (AiProvider candidate in Enum.GetValues(typeof(AiProvider)))
            {
                if (exhaustedProviders.Contains(candidate))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(AiProviderSettings.GetApiKey(candidate)))
                {
                    return candidate;
                }
            }

            return null;
        }

        // Swaps a language column with its adjacent neighbor. The source column's position is fixed
        // at index 0 and is never passed to this method (DrawTable doesn't render buttons for it),
        // and the direction-1/+1 clamp below keeps every other column from ever landing on index 0.
        private void MoveLanguage(SystemLanguage language, int direction)
        {
            var index = _data.Languages.IndexOf(language);
            if (index < 0)
            {
                return;
            }

            var targetIndex = index + direction;
            if (targetIndex < 1 || targetIndex >= _data.Languages.Count)
            {
                return;
            }

            (_data.Languages[index], _data.Languages[targetIndex]) = (_data.Languages[targetIndex], _data.Languages[index]);
            SetDirty(true);
        }

        // Pins `language` as the source, always at index 0 of the language list so it's rendered as
        // the leftmost, fixed column - whatever was previously first shifts right to make room.
        private void SetSourceLanguage(SystemLanguage language)
        {
            _data.Languages.Remove(language);
            _data.Languages.Insert(0, language);
            _data.SourceLanguage = language;
            SetDirty(true);
        }

        // Reverse lookup from a display name (as returned by Gemini) back to a SystemLanguage value -
        // restricted to languages this picker actually offers, unlike LocalizationRuntime.ParseDisplayName
        // itself (used as-is, unrestricted, by CSV import - a translator's file might legitimately
        // reference a language outside this project's current Steam-relevance shortlist).
        private static SystemLanguage? ParseLanguageName(string name)
        {
            var language = LocalizationRuntime.ParseDisplayName(name);
            return language.HasValue && !ExcludedLanguages.Contains(language.Value) ? language : null;
        }

        // If Gemini detected the source text is actually written in a different language than
        // _data.SourceLanguage claims, fix that up automatically: move every entry's source-column
        // text over to the correct language column and repoint SourceLanguage at it. The old language
        // is left in Languages (now just an empty regular column) rather than removed, so this never
        // destroys a language the user deliberately added.
        // Returns true if a correction was actually applied, so the caller knows to redo the pending
        // translation batch (the old source language just became a normal, empty column too).
        private bool ApplyDetectedSourceLanguage(string detectedName)
        {
            var detected = ParseLanguageName(detectedName);
            if (detected == null || detected.Value == _data.SourceLanguage)
            {
                return false;
            }

            var oldSource = _data.SourceLanguage;
            var newSource = detected.Value;

            foreach (var entry in _data.Entries)
            {
                var text = entry.GetValue(oldSource);
                if (text == null)
                {
                    continue;
                }

                entry.SetValue(newSource, text);
                entry.RemoveValue(oldSource);
            }

            // Pins the real source at index 0, which naturally pushes the old (wrongly-assumed)
            // source - previously first - to sit right next to it at index 1.
            SetSourceLanguage(newSource);

            ShowNotification(new GUIContent(
                $"Source language was set to {DisplayName(oldSource)}, but the text looked like " +
                $"{DisplayName(newSource)} - corrected automatically."));

            return true;
        }

        private void SetDirty(bool dirty)
        {
            if (_isDirty == dirty)
            {
                return;
            }

            _isDirty = dirty;
            titleContent = new GUIContent(dirty ? "Language Data*" : "Language Data");
        }
    }
}
