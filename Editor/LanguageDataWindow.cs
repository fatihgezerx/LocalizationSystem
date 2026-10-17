#if !HAS_VENEER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// <c>Tools &gt; Language Data</c>: a dockable window showing every scanned text entry as a spreadsheet -
    /// one row per key, one column per selected language. Every edit (typing a cell, scanning the
    /// scene, adding/removing a language, translating) only changes the in-memory copy; nothing
    /// touches disk until "Save Data" is clicked.
    /// </summary>
    /// <remarks>
    /// This is the plain IMGUI version, used only while the optional Veneer is not in the project. With the kit, the
    /// studio-look window in <c>Editor/Studio</c> replaces it (same menu item, same data, same logic from <see cref="LanguageDataLogic"/>).
    /// </remarks>
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
        private GUIStyle _cellTextFieldStyle;

        // GUILayout.Button's default style carries its own non-zero margin; left as-is, each button silently claims a
        // few extra pixels beyond its GUILayout.Width and the reorder-button row drifts out of column sync.
        private GUIStyle ReorderButtonStyle => _reorderButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            margin = new RectOffset(0, 0, 0, 0),
        };

        // Same fix for the data-row controls: EditorGUILayout.TextField carries non-zero margin in Unity's default skin.
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

        // Optional real flag PNGs. Drop a file named e.g. "Turkish.png" (matching DisplayName exactly)
        // into LocalizationSystem/Editor/Flags/ and it's used automatically. Missing files draw a grey box.
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

            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f));
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

        private static string DisplayName(SystemLanguage language) => LanguageDataLogic.DisplayName(language);

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
                    LanguageDataLogic.RemoveAllLocalizedTextComponents();
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

        private void ExportCsv()
        {
            var path = EditorUtility.SaveFilePanel("Export Language Data as CSV", "", "LocalizationData.csv", "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // Encoding.UTF8 (not a plain "new UTF8Encoding()") writes a byte-order mark, which is
            // what makes Excel on Windows reliably detect UTF-8 instead of misreading non-Latin text.
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

        // Languages on the left, AI provider/API key/Game Context on the right, side by side.
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

            foreach (var language in LanguageDataLogic.SelectableLanguages())
            {
                var included = _data.Languages.Contains(language);

                EditorGUILayout.BeginHorizontal();
                var newIncluded = EditorGUILayout.ToggleLeft(DisplayName(language), included, GUILayout.Width(220));

                var isSource = _data.SourceLanguage == language;
                GUI.enabled = included && !isSource;
                if (GUILayout.Button(isSource ? "Source" : "Set as Source", GUILayout.Width(110)))
                {
                    LanguageDataLogic.SetSourceLanguage(_data, language);
                    SetDirty(true);
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (newIncluded != included && LanguageDataLogic.SetLanguageIncluded(_data, language, newIncluded))
                {
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

            var unsupported = LanguageDataLogic.UnsupportedLanguages(_data, provider);
            if (unsupported.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{translationProvider.DisplayName} doesn't support: {string.Join(", ", unsupported)}. " +
                    "Those columns will stay empty when translating with this provider - use a different one for them.",
                    MessageType.Warning);
            }

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

        // Re-picking (even the same one again) always re-applies the original text, discarding
        // whatever was typed in the meantime - "Save Prompt" below is how you keep an edited version.
        private void DrawGameContextPresetPicker()
        {
            var presets = LanguageDataLogic.AllGameContextPresets(_data);
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

            // Compose with (not overwrite) whatever GUI.enabled the caller already set.
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !string.IsNullOrWhiteSpace(_presetNameInput) && !string.IsNullOrWhiteSpace(_data.GameContext);
            if (GUILayout.Button("Save Prompt", GUILayout.Width(110)))
            {
                _selectedPresetIndex = LanguageDataLogic.SaveCustomGameContextPreset(_data, _presetNameInput.Trim(), _data.GameContext);
                SetDirty(true);
                _presetNameInput = string.Empty;
                GUI.FocusControl(null);
            }
            GUI.enabled = wasEnabled;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTable()
        {
            if (_data.Languages.Count == 0)
            {
                EditorGUILayout.HelpBox("No languages selected yet. Click \"Settings\" above to add some.", MessageType.Info);
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

            var completeCount = _data.Entries.Count(entry => LanguageDataLogic.IsFullyTranslated(_data, entry));
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

            for (var i = 0; i < _data.Languages.Count; i++)
            {
                var language = _data.Languages[i];
                var isSource = language == _data.SourceLanguage;
                var headerRect = GUILayoutUtility.GetRect(LanguageColumnWidth, HeaderHeight, GUILayout.Width(LanguageColumnWidth), GUILayout.Height(HeaderHeight));

                if (Event.current.type != EventType.Repaint)
                {
                    continue;
                }

                EditorGUI.DrawRect(headerRect, isSource ? new Color(0.55f, 0.45f, 0.2f) : new Color(0.24f, 0.24f, 0.24f));

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
                // never move left, and the last column can never move right.
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

            // Deferred until after the loop finishes - mutating Languages mid-foreach would throw.
            if (moveLeft.HasValue && LanguageDataLogic.MoveLanguage(_data, moveLeft.Value, -1))
            {
                SetDirty(true);
            }
            if (moveRight.HasValue && LanguageDataLogic.MoveLanguage(_data, moveRight.Value, 1))
            {
                SetDirty(true);
            }

            DrawVirtualizedRows(visibleEntries);

            EditorGUILayout.EndScrollView();
        }

        private List<LocalizationEntry> GetFilteredEntries() => _entryFilter switch
        {
            EntryFilter.Complete => _data.Entries.Where(entry => LanguageDataLogic.IsFullyTranslated(_data, entry)).ToList(),
            EntryFilter.Incomplete => _data.Entries.Where(entry => !LanguageDataLogic.IsFullyTranslated(_data, entry)).ToList(),
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

        // Only the rows currently scrolled into view get real GUILayout controls; off-screen rows are
        // represented by a single GUILayout.Space matching their total height.
        private const int RowRenderBuffer = 5;

        private void DrawVirtualizedRows(List<LocalizationEntry> visibleEntries)
        {
            var totalRows = visibleEntries.Count;

            var rowsTopOffset = HeaderHeight + 18f;
            var scrollWithinRows = Mathf.Max(0f, _tableScroll.y - rowsTopOffset);

            var estimatedVisibleRowCount = Mathf.CeilToInt(position.height / RowHeight) + RowRenderBuffer * 2;
            var firstVisible = Mathf.Max(0, Mathf.FloorToInt(scrollWithinRows / RowHeight) - RowRenderBuffer);
            var lastVisible = Mathf.Min(totalRows, firstVisible + estimatedVisibleRowCount);

            GUILayout.Space(firstVisible * RowHeight);

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
            if (LanguageDataLogic.IsEmptyRow(_data, entry))
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

            GUI.enabled = !_isBusy && !LanguageDataLogic.IsBlocked(rateLimit);
            if (GUILayout.Button(_isBusy ? "Translating..." : "Translate", GUILayout.Width(140), GUILayout.Height(26)))
            {
                TranslateMissingAsync();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        // Always visible right next to Translate, for every provider - no click needed.
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
                statusParts.Add($"resets {LanguageDataLogic.FormatResetTime(rateLimit.ResetAt.Value)}");
            }

            var statusText = statusParts.Count > 0 ? $"{providerName}: {string.Join(", ", statusParts)}" : providerName;
            var content = new GUIContent(statusText, rateLimit.Caveat);

            var previousColor = GUI.color;
            GUI.color = rateLimit.IsExhausted ? new Color(1f, 0.6f, 0.6f) : previousColor;
            GUILayout.Label(content, EditorStyles.miniLabel, GUILayout.Width(210));
            GUI.color = previousColor;

            GUILayout.Space(8);
        }

        private async void TranslateMissingAsync()
        {
            _isBusy = true;
            try
            {
                var summary = await TranslationBatch.RunAsync(_data, () =>
                {
                    SetDirty(true);
                    Repaint();
                });

                if (summary != null)
                {
                    ShowNotification(new GUIContent(summary));
                }
            }
            finally
            {
                _isBusy = false;
            }
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
#endif
