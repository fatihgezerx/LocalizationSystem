using System;
using System.IO;
using System.Text;
using Veneer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationSystem
{
    /// <summary>
    /// <c>Tools &gt; Language Data</c> in the studio look: a dockable window with the translation table (one row per key, one column per
    /// language, inline editing, missing-translation highlights, search and filters) and a Settings tab (languages, translation provider
    /// and API key, Game Context). Every edit only changes the in-memory copy; nothing touches disk until "Save Data" is clicked.
    /// </summary>
    /// <remarks>
    /// Replaces the plain IMGUI window of the same name that is used while Veneer is not installed. Both use
    /// <see cref="LanguageDataLogic"/> and <see cref="TranslationBatch"/>, so they behave the same.
    /// </remarks>
    public sealed partial class LanguageDataWindow : VeneerWindow
    {
        private const string Title = "Language Data";

        private enum EntryFilter
        {
            All,
            Complete,
            Incomplete,
        }

        private LocalizationData data;
        private bool isDirty;
        private bool isBusy;
        private int viewIndex;

        // Window chrome that changes with the state.
        private VisualElement tablePage;
        private VisualElement settingsPage;
        private VisualElement tabBar;
        private VeneerStyle.TabSwitcher pageSwitcher;
        private Button syncButton;
        private Button clearButton;
        private Button saveButton;
        private Button translateButton;
        private Label saveLabel;
        private Label translateLabel;
        private Label keysValue;
        private Label completeValue;
        private Label missingValue;
        private Label languagesValue;
        private VisualElement quotaBar;
        private Label quotaLabel;

        /// <inheritdoc />
        protected override VeneerBrand Brand => LocalizationBrand.Value;

        /// <inheritdoc />
        protected override string WindowTitle => Title;

        [MenuItem("Tools/Language Data")]
        private static void Open()
        {
            var window = GetWindow<LanguageDataWindow>(Title);
            window.Show();
        }

        private void OnEnable()
        {
            data = LocalizationDataStore.Load();
            isDirty = false;

            // Re-check for flag PNGs every time the window opens, so dropping files into the Flags
            // folder and refocusing the window picks them up without needing a script recompile.
            LanguageFlags.Clear();
        }

        // Reloads when this window regains focus, so picking up external changes (e.g. someone else
        // editing the JSON directly) works - but never while there are unsaved edits, or a translation
        // batch is still running in the background, or we'd silently discard/orphan that work.
        private void OnFocus()
        {
            if (isDirty || isBusy || RootElement == null)
            {
                return;
            }

            LocalizationData loaded = LocalizationDataStore.Load();
            if (JsonUtility.ToJson(loaded) != JsonUtility.ToJson(data))
            {
                data = loaded;
                RefreshAll();
            }
        }

        /// <inheritdoc />
        protected override void BuildContent(VisualElement content)
        {
            data ??= LocalizationDataStore.Load();

            var body = new VisualElement();
            body.AddToClassList("loc-body");
            content.Add(body);

            body.Add(BuildActionRow());
            body.Add(BuildStatsRow());

            tabBar = VeneerStyle.Tabs(
                new[] { "list", "settings" },
                new[] { "Translations", "Settings" },
                new[] { "Every key and its text in each language", "Languages, translation provider and Game Context" },
                viewIndex,
                SelectView,
                Brand);
            body.Add(tabBar);

            tablePage = BuildTablePage();
            settingsPage = BuildSettingsPage();
            body.Add(tablePage);
            body.Add(settingsPage);

            pageSwitcher = new VeneerStyle.TabSwitcher(body, new[] { tablePage, settingsPage });
            pageSwitcher.Initialize(tabBar, viewIndex);
            pageSwitcher.Snap(viewIndex);

            content.Add(BuildFooter());

            Every(1000, RefreshRateLimit);
            RefreshAll();
        }

        // ------------------------------------------------------------------ action row and stats

        private VisualElement BuildActionRow()
        {
            var row = new VisualElement();
            row.AddToClassList("loc-actions");

            syncButton = VeneerStyle.Button("Sync Project", "refresh",
                "Scan the open scene for Text / TMP_Text and the project for [Localize] string fields, and add what is new to the table.",
                SyncProject, false, Brand);
            row.Add(syncButton);
            row.Add(VeneerStyle.Button("Export CSV", "download", "Write the table to a CSV file for a human translator.", ExportCsv, false, Brand));
            row.Add(VeneerStyle.Button("Import CSV", "upload", "Read translations back from a CSV file.", ImportCsv, false, Brand));
            row.Add(VeneerStyle.Spacer());
            clearButton = VeneerStyle.Button("Clear Data", "trash",
                "Remove every language and entry from the loaded table and every Localized Text component Sync Project added.",
                ClearData, false, Brand);
            row.Add(clearButton);
            return row;
        }

        private VisualElement BuildStatsRow()
        {
            var row = new VisualElement();
            row.AddToClassList("loc-stats");
            row.Add(VeneerStyle.Kpi("Keys", "key", out keysValue, Brand));
            row.Add(VeneerStyle.Kpi("Complete", "check-circle", out completeValue, Brand));
            row.Add(VeneerStyle.Kpi("Missing", "warning", out missingValue, Brand));
            row.Add(VeneerStyle.Kpi("Languages", "globe", out languagesValue, Brand));
            return row;
        }

        private void SelectView(int index)
        {
            viewIndex = index;
            if (tabBar != null && VeneerStyle.GetSegmentedSelected(tabBar) != index)
            {
                // The tab bar keeps its own selection when clicked; this keeps it right when the view is changed from code.
                VeneerStyle.SetSegmentedSelected(tabBar, index);
            }

            if (pageSwitcher != null)
            {
                pageSwitcher.Switch(index);
                return;
            }

            VeneerStyle.SetDisplay(tablePage, index == 0);
            VeneerStyle.SetDisplay(settingsPage, index == 1);
        }

        private void OpenSettings()
        {
            SelectView(1);
        }

        // ------------------------------------------------------------------ footer

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement();
            footer.AddToClassList("loc-footer");

            saveButton = VeneerStyle.Button("Save Data", "save", "Write the table to LocalizationData.json.", SaveData, false, Brand);
            saveLabel = saveButton.Q<Label>(className: "vn-button__label");
            footer.Add(saveButton);
            footer.Add(VeneerStyle.Spacer());

            var quota = new VisualElement();
            quota.AddToClassList("loc-quota");
            quotaBar = VeneerStyle.Bar();
            quotaBar.AddToClassList("loc-quota__bar");
            quota.Add(quotaBar);
            quotaLabel = new Label();
            quotaLabel.AddToClassList("loc-quota__label");
            quota.Add(quotaLabel);
            footer.Add(quota);

            translateButton = VeneerStyle.Button("Translate", "globe",
                "Fill every empty cell from the source language with the selected provider.", TranslateMissing, true, Brand);
            translateLabel = translateButton.Q<Label>(className: "vn-button__label");
            footer.Add(translateButton);
            return footer;
        }

        private void RefreshRateLimit()
        {
            if (quotaLabel == null)
            {
                return;
            }

            AiProvider provider = AiProviderSettings.GetProvider();
            ITranslationProvider translationProvider = TranslationProviders.All[provider];
            RateLimitInfo rateLimit = translationProvider.LastKnownRateLimit;
            string name = translationProvider.DisplayName;

            if (!rateLimit.HasData)
            {
                VeneerStyle.SetDisplay(quotaBar, false);
                quotaLabel.text = $"{name}: quota unknown until first request";
                quotaLabel.tooltip = string.Empty;
                quotaLabel.EnableInClassList("loc-quota__label--exhausted", false);
            }
            else
            {
                string prefix = string.Empty;
                bool hasBar = rateLimit.RemainingRequests.HasValue && rateLimit.LimitRequests.HasValue;
                VeneerStyle.SetDisplay(quotaBar, hasBar);
                if (hasBar)
                {
                    int limit = rateLimit.LimitRequests.Value;
                    int used = Mathf.Max(0, limit - rateLimit.RemainingRequests.Value);
                    VeneerStyle.SetBarFill(quotaBar, limit > 0 ? (float)used / limit : 0f);
                    VeneerStyle.SetBarKind(quotaBar, rateLimit.IsExhausted ? "error" : null);
                    prefix = $"{used}/{limit}  ";
                }

                string status = name;
                if (rateLimit.IsExhausted && rateLimit.ResetAt.HasValue)
                {
                    status = $"{name}: exhausted, resets {LanguageDataLogic.FormatResetTime(rateLimit.ResetAt.Value)}";
                }
                else if (rateLimit.IsExhausted)
                {
                    status = $"{name}: exhausted";
                }
                else if (rateLimit.ResetAt.HasValue)
                {
                    status = $"{name}: resets {LanguageDataLogic.FormatResetTime(rateLimit.ResetAt.Value)}";
                }

                quotaLabel.text = prefix + status;
                quotaLabel.tooltip = rateLimit.Caveat ?? string.Empty;
                quotaLabel.EnableInClassList("loc-quota__label--exhausted", rateLimit.IsExhausted);
            }

            UpdateButtons(LanguageDataLogic.IsBlocked(rateLimit));
        }

        private void UpdateButtons(bool? blocked = null)
        {
            if (translateButton == null)
            {
                return;
            }

            bool isBlocked = blocked ?? LanguageDataLogic.IsBlocked(TranslationProviders.All[AiProviderSettings.GetProvider()].LastKnownRateLimit);
            syncButton.SetEnabled(!isBusy);
            clearButton.SetEnabled(!isBusy);
            saveButton.SetEnabled(isDirty && !isBusy);
            saveButton.EnableInClassList("vn-button--primary", isDirty);
            saveLabel.text = isDirty ? "Save Data*" : "Save Data";
            translateButton.SetEnabled(!isBusy && !isBlocked);
            translateLabel.text = isBusy ? "Translating..." : "Translate";
        }

        private void UpdateChrome()
        {
            titleContent = new GUIContent(isDirty ? Title + "*" : Title, Brand.Logo);
            if (isBusy)
            {
                SetPill("Translating", "info");
            }
            else if (isDirty)
            {
                SetPill("Unsaved changes", "warn");
            }
            else
            {
                SetPill("Saved", "ok");
            }

            UpdateButtons();
        }

        private void SetDirty(bool dirty)
        {
            if (isDirty == dirty)
            {
                return;
            }

            isDirty = dirty;
            if (RootElement != null)
            {
                UpdateChrome();
            }
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            if (RootElement != null)
            {
                UpdateChrome();
            }
        }

        // ------------------------------------------------------------------ commands

        private void SaveData()
        {
            LocalizationDataStore.Save(data);
            SetDirty(false);
            ShowNotification(new GUIContent("Language Data saved."));
        }

        private void SyncProject()
        {
            var (sceneFound, sceneAdded) = SceneScanner.Scan(data);
            var (fieldsFound, fieldsAdded) = AttributeFieldScanner.Scan(data);
            int totalFound = sceneFound + fieldsFound;
            int totalAdded = sceneAdded + fieldsAdded;

            if (totalAdded > 0)
            {
                SetDirty(true);
                RefreshAll();
            }

            ShowNotification(new GUIContent(
                $"Sync complete - found {totalFound} string{(totalFound == 1 ? "" : "s")}, " +
                $"added {totalAdded} new entr{(totalAdded == 1 ? "y" : "ies")}."));
        }

        private void ClearData()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Clear All Language Data?",
                "This removes every language and every text entry currently loaded (in-memory only " +
                "- nothing on disk changes unless you click \"Save Data\" afterwards), and also " +
                "removes every \"Localized Text\" component that \"Sync Project\" added to scene " +
                "objects (this part does change the open scene, and is not undoable). [Localize] " +
                "attributes on your own scripts are untouched - those just mark fields for the " +
                "next sync to pick up again.",
                "Clear All",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            data = new LocalizationData();
            LanguageDataLogic.RemoveAllLocalizedTextComponents();
            SetDirty(true);
            RefreshAll();
        }

        private void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel("Export Language Data as CSV", "", "LocalizationData.csv", "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // Encoding.UTF8 (not a plain "new UTF8Encoding()") writes a byte-order mark, which is
            // what makes Excel on Windows reliably detect UTF-8 instead of misreading non-Latin text.
            File.WriteAllText(path, LanguageDataCsv.Export(data), Encoding.UTF8);
            ShowNotification(new GUIContent($"Exported {data.Entries.Count} entries to {Path.GetFileName(path)}."));
        }

        private void ImportCsv()
        {
            string path = EditorUtility.OpenFilePanel("Import Language Data from CSV", "", "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var (rowsMatched, valuesUpdated) = LanguageDataCsv.Import(data, File.ReadAllText(path, Encoding.UTF8));
                if (valuesUpdated > 0)
                {
                    SetDirty(true);
                    RefreshAll();
                }

                ShowNotification(new GUIContent($"Imported {rowsMatched} row(s), updated {valuesUpdated} value(s)."));
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Import Failed", ex.Message, "OK");
            }
        }

        private async void TranslateMissing()
        {
            if (isBusy)
            {
                return;
            }

            SetBusy(true);
            string summary = null;
            try
            {
                summary = await TranslationBatch.RunAsync(data, OnBatchDataChanged);
            }
            finally
            {
                // The window may have been closed while the batch ran.
                if (this != null)
                {
                    SetBusy(false);
                    // A detected source language reorders the columns, so rebuild them once the batch is over.
                    RefreshAll();
                    RefreshRateLimit();
                }
            }

            if (this != null && summary != null)
            {
                ShowNotification(new GUIContent(summary));
            }
        }

        private void OnBatchDataChanged()
        {
            if (this == null)
            {
                return;
            }

            SetDirty(true);
            QueueTableRefresh();
        }

        // Re-reads everything the window shows from the data: columns, rows, counters, the Settings page.
        private void RefreshAll()
        {
            if (RootElement == null)
            {
                return;
            }

            RebuildTable();
            RefreshSettings();
            UpdateChrome();
        }
    }
}
