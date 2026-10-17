using System;
using System.Collections.Generic;
using Veneer;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationSystem
{
    public sealed partial class LanguageDataWindow
    {
        private const float LanguageColumnWidth = 220f;
        private const float StatusColumnWidth = 96f;
        private const float RowHeight = 28f;

        private static readonly string[] FilterNames = { "All", "Fully translated", "Missing" };

        // One per language cell: which language the cell's column shows and which entry the recycled row is bound to right now.
        private sealed class CellState
        {
            public SystemLanguage Language;
            public LocalizationEntry Entry;
        }

        private readonly List<LocalizationEntry> filtered = new();
        private readonly HashSet<LocalizationEntry> touchedEntries = new();
        private readonly HashSet<SystemLanguage> hiddenLanguages = new();
        private readonly List<VisualElement> filterChips = new(3);

        private EntryFilter entryFilter = EntryFilter.All;
        private string searchText = string.Empty;
        private bool showKeys;
        private bool refreshQueued;
        private int totalCount;
        private int completeCount;
        private int missingCount;

        private MultiColumnListView table;
        private VisualElement filterBar;
        private VisualElement languageChipRow;
        private VisualElement keysChip;
        private VisualElement emptyNoLanguages;
        private VisualElement emptyNoEntries;
        private VisualElement emptyNoMatch;
        private IVisualElementScheduledItem searchJob;
        private IVisualElementScheduledItem editJob;

        // ------------------------------------------------------------------ construction

        private VisualElement BuildTablePage()
        {
            var page = new VisualElement();
            page.AddToClassList("loc-page");

            filterBar = new VisualElement();
            filterBar.AddToClassList("loc-filter");

            VisualElement search = VeneerStyle.SearchField(OnSearchChanged, out TextField _, "Search keys and text", Brand);
            search.AddToClassList("loc-filter__search");
            filterBar.Add(search);

            filterChips.Clear();
            for (int i = 0; i < FilterNames.Length; i++)
            {
                int index = i;
                VisualElement chip = VeneerStyle.Chip(FilterNames[i], null, i == (int)entryFilter, on => SetFilter(index), Brand);
                filterChips.Add(chip);
                filterBar.Add(chip);
            }

            var columnsLabel = new Label("COLUMNS");
            columnsLabel.AddToClassList("loc-filter__label");
            filterBar.Add(columnsLabel);
            keysChip = VeneerStyle.Chip("Keys", "key", showKeys, on =>
            {
                showKeys = on;
                BuildColumns();
            }, Brand);
            keysChip.tooltip = "Show the internal key of each row.";
            filterBar.Add(keysChip);

            languageChipRow = new VisualElement();
            languageChipRow.AddToClassList("loc-filter");
            languageChipRow.style.marginTop = 0f;
            languageChipRow.style.marginBottom = 0f;

            table = new MultiColumnListView
            {
                fixedItemHeight = RowHeight,
                selectionType = SelectionType.None,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                itemsSource = filtered,
            };
            table.AddToClassList("loc-table");
            table.columns.reorderable = false;
            // Every column keeps its own width (none is stretchable); when the columns together are wider than the window the table scrolls
            // sideways, with its scroll bar at the bottom edge of the table, just above the footer.
            table.horizontalScrollingEnabled = true;

            emptyNoLanguages = VeneerStyle.EmptyState("globe", "No languages yet",
                "Pick the languages your project supports in Settings. Each one becomes a column.", Brand);
            emptyNoLanguages.Add(VeneerStyle.Button("Open Settings", "settings", "Go to the Settings tab.", OpenSettings, true, Brand));

            emptyNoEntries = VeneerStyle.EmptyState("list", "No text entries yet",
                "Click Sync Project to find Text and TMP_Text in the open scene and [Localize]-marked string fields in the project.", Brand);
            emptyNoEntries.Add(VeneerStyle.Button("Sync Project", "refresh", "Scan the scene and the project.", SyncProject, true, Brand));

            emptyNoMatch = VeneerStyle.EmptyState("search", "No entries match",
                "Nothing fits the current search and filter.", Brand);

            page.Add(filterBar);
            page.Add(languageChipRow);
            page.Add(emptyNoLanguages);
            page.Add(emptyNoEntries);
            page.Add(emptyNoMatch);
            page.Add(table);
            return page;
        }

        // ------------------------------------------------------------------ refresh

        // Rebuilds columns, language chips, rows and counters. Cheap enough to call after any structural change.
        private void RebuildTable()
        {
            if (table == null)
            {
                return;
            }

            RebuildLanguageChips();
            BuildColumns();
            RefreshRows();
        }

        // Recomputes which rows pass the search and the filter, rebinds the visible ones and updates counters and empty states.
        private void RefreshRows()
        {
            ComputeStats();

            filtered.Clear();
            for (int i = 0; i < data.Entries.Count; i++)
            {
                LocalizationEntry entry = data.Entries[i];
                bool complete = LanguageDataLogic.IsFullyTranslated(data, entry);
                if (entryFilter == EntryFilter.Complete && !complete)
                {
                    continue;
                }

                if (entryFilter == EntryFilter.Incomplete && complete)
                {
                    continue;
                }

                if (searchText.Length > 0 && !MatchesSearch(entry))
                {
                    continue;
                }

                filtered.Add(entry);
            }

            table.RefreshItems();
            UpdateStats();
            UpdateTableState();
        }

        private bool MatchesSearch(LocalizationEntry entry)
        {
            if (entry.Key != null && entry.Key.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            for (int i = 0; i < entry.Values.Count; i++)
            {
                string text = entry.Values[i].Text;
                if (text != null && text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void ComputeStats()
        {
            totalCount = data.Entries.Count;
            completeCount = 0;
            for (int i = 0; i < data.Entries.Count; i++)
            {
                if (LanguageDataLogic.IsFullyTranslated(data, data.Entries[i]))
                {
                    completeCount++;
                }
            }

            missingCount = totalCount - completeCount;
        }

        private void UpdateStats()
        {
            keysValue.text = totalCount.ToString();
            completeValue.text = completeCount.ToString();
            missingValue.text = missingCount.ToString();
            languagesValue.text = data.Languages.Count.ToString();

            int[] counts = { totalCount, completeCount, missingCount };
            for (int i = 0; i < filterChips.Count; i++)
            {
                VeneerStyle.ChipLabel(filterChips[i]).text = $"{FilterNames[i]} ({counts[i]})";
                VeneerStyle.SetChipOn(filterChips[i], i == (int)entryFilter);
            }
        }

        private void UpdateTableState()
        {
            bool noLanguages = data.Languages.Count == 0;
            bool noEntries = !noLanguages && data.Entries.Count == 0;
            bool noMatch = !noLanguages && !noEntries && filtered.Count == 0;
            VeneerStyle.SetDisplay(emptyNoLanguages, noLanguages);
            VeneerStyle.SetDisplay(emptyNoEntries, noEntries);
            VeneerStyle.SetDisplay(emptyNoMatch, noMatch);
            VeneerStyle.SetDisplay(filterBar, !noLanguages && !noEntries);
            VeneerStyle.SetDisplay(languageChipRow, !noLanguages && !noEntries);
            VeneerStyle.SetDisplay(table, !noLanguages && !noEntries && !noMatch);
        }

        // Batches many data changes (a translation run touches one entry at a time) into one repaint of the visible rows.
        private void QueueTableRefresh()
        {
            if (refreshQueued || RootElement == null)
            {
                return;
            }

            refreshQueued = true;
            RootElement.schedule.Execute(() =>
            {
                refreshQueued = false;
                if (RootElement != null && table != null)
                {
                    RefreshRows();
                }
            }).ExecuteLater(150);
        }

        // ------------------------------------------------------------------ search, filter, language chips

        private void OnSearchChanged(string text)
        {
            searchText = text ?? string.Empty;
            searchJob?.Pause();
            searchJob = RootElement.schedule.Execute(RefreshRows);
            searchJob.ExecuteLater(150);
        }

        private void SetFilter(int index)
        {
            entryFilter = (EntryFilter)index;
            RefreshRows();
        }

        private void RebuildLanguageChips()
        {
            languageChipRow.Clear();
            var label = new Label("SHOW");
            label.AddToClassList("loc-filter__label");
            languageChipRow.Add(label);

            foreach (SystemLanguage language in data.Languages)
            {
                SystemLanguage captured = language;
                VisualElement chip = VeneerStyle.Chip(LanguageDataLogic.DisplayName(language), null, !hiddenLanguages.Contains(language), on =>
                {
                    if (on)
                    {
                        hiddenLanguages.Remove(captured);
                    }
                    else
                    {
                        hiddenLanguages.Add(captured);
                    }

                    BuildColumns();
                    table.RefreshItems();
                }, Brand);
                chip.tooltip = "Show or hide this language's column (view only, the data is not changed).";
                languageChipRow.Add(chip);
            }
        }

        // ------------------------------------------------------------------ columns

        private void BuildColumns()
        {
            table.columns.Clear();

            table.columns.Add(new Column
            {
                name = "status",
                title = "Status",
                width = StatusColumnWidth,
                minWidth = StatusColumnWidth,
                maxWidth = StatusColumnWidth,
                stretchable = false,
                resizable = false,
                makeHeader = () => MakeTextHeader("Status"),
                makeCell = MakeStatusCell,
                bindCell = BindStatusCell,
            });

            if (showKeys)
            {
                table.columns.Add(new Column
                {
                    name = "key",
                    title = "Key",
                    width = 180f,
                    minWidth = 80f,
                    stretchable = false,
                    resizable = true,
                    makeHeader = () => MakeTextHeader("Key"),
                    makeCell = MakeKeyCell,
                    bindCell = BindKeyCell,
                });
            }

            for (int i = 0; i < data.Languages.Count; i++)
            {
                SystemLanguage language = data.Languages[i];
                if (hiddenLanguages.Contains(language))
                {
                    continue;
                }

                int position = i;
                table.columns.Add(new Column
                {
                    name = "lang-" + (int)language,
                    title = LanguageDataLogic.DisplayName(language),
                    width = LanguageColumnWidth,
                    minWidth = 120f,
                    stretchable = false,
                    resizable = true,
                    makeHeader = () => MakeLanguageHeader(language, position),
                    makeCell = () => MakeLanguageCell(language),
                    bindCell = BindLanguageCell,
                });
            }
        }

        // Header of a column that only has a title (Status, Key); same box as the language headers so the header row is one height.
        private static VisualElement MakeTextHeader(string title)
        {
            var head = new VisualElement();
            head.AddToClassList("loc-head");
            var name = new Label(title);
            name.AddToClassList("loc-head__name");
            head.Add(name);
            return head;
        }

        private VisualElement MakeLanguageHeader(SystemLanguage language, int position)
        {
            bool isSource = language == data.SourceLanguage;
            var head = new VisualElement();
            head.AddToClassList("loc-head");
            head.Add(MakeFlag(language));

            var name = new Label(LanguageDataLogic.DisplayName(language));
            name.AddToClassList("loc-head__name");
            head.Add(name);

            if (isSource)
            {
                Label pill = VeneerStyle.Pill("Source", "special");
                pill.tooltip = "Translations are made from this language. Change it in Settings.";
                head.Add(pill);
                return head;
            }

            // Index 0 is always the pinned source, so the first other column (index 1) can never move left,
            // and the last column can never move right - those buttons are left out.
            if (position > 1)
            {
                head.Add(VeneerStyle.IconButton("chevron-left", "Move this column left", () => MoveColumn(language, -1), Brand));
            }

            if (position < data.Languages.Count - 1)
            {
                head.Add(VeneerStyle.IconButton("chevron-right", "Move this column right", () => MoveColumn(language, 1), Brand));
            }

            return head;
        }

        private VisualElement MakeFlag(SystemLanguage language)
        {
            Texture2D flag = LanguageFlags.Get(language);
            if (flag == null)
            {
                return VeneerStyle.IconElement("globe", "vn-icon--sm", Brand);
            }

            var element = new VisualElement();
            element.AddToClassList("loc-flag");
            element.pickingMode = PickingMode.Ignore;
            element.style.backgroundImage = new StyleBackground(flag);
            return element;
        }

        private void MoveColumn(SystemLanguage language, int direction)
        {
            if (!LanguageDataLogic.MoveLanguage(data, language, direction))
            {
                return;
            }

            SetDirty(true);
            // The header buttons belong to the columns being replaced, so rebuild after the click has finished.
            RootElement.schedule.Execute(RefreshAll);
        }

        // ------------------------------------------------------------------ cells

        private VisualElement MakeStatusCell()
        {
            var cell = new VisualElement();
            cell.AddToClassList("loc-td");
            cell.AddToClassList("loc-status");
            cell.Add(VeneerStyle.Pill(string.Empty, "idle"));
            return cell;
        }

        private void BindStatusCell(VisualElement cell, int index)
        {
            LocalizationEntry entry = filtered[index];
            var pill = (Label)cell[0];
            int missing = LanguageDataLogic.CountMissing(data, entry);
            if (missing == 0)
            {
                VeneerStyle.SetPill(pill, "ok", "Complete");
            }
            else
            {
                VeneerStyle.SetPill(pill, "warn", missing + " missing");
            }

            cell.tooltip = entry.Key;
        }

        private VisualElement MakeKeyCell()
        {
            // Every cell sits in a "loc-td" box that draws the column separator and the row line; the content is its first child.
            var cell = new VisualElement();
            cell.AddToClassList("loc-td");
            var label = new Label();
            label.AddToClassList("loc-key");
            cell.Add(label);
            return cell;
        }

        private void BindKeyCell(VisualElement cell, int index)
        {
            var label = (Label)cell[0];
            label.text = filtered[index].Key;
            label.tooltip = filtered[index].Key;
        }

        private VisualElement MakeLanguageCell(SystemLanguage language)
        {
            var cell = new VisualElement();
            cell.AddToClassList("loc-td");
            var field = new TextField();
            field.AddToClassList("loc-cell");
            field.userData = new CellState { Language = language };
            field.RegisterValueChangedCallback(OnCellChanged);
            // The focus belongs to the inner text element, so listen while the event travels down to it.
            field.RegisterCallback<FocusOutEvent>(OnCellBlur, TrickleDown.TrickleDown);
            cell.Add(field);
            return cell;
        }

        private void BindLanguageCell(VisualElement cell, int index)
        {
            var field = (TextField)cell[0];
            var state = (CellState)field.userData;
            state.Entry = filtered[index];
            string text = state.Entry.GetValue(state.Language) ?? string.Empty;
            field.SetValueWithoutNotify(text);
            field.EnableInClassList("loc-cell--missing", text.Length == 0);
        }

        private void OnCellChanged(ChangeEvent<string> evt)
        {
            var field = (TextField)evt.currentTarget;
            var state = (CellState)field.userData;
            if (state.Entry == null)
            {
                return;
            }

            // In-memory only - no disk write per keystroke. Committed by "Save Data".
            state.Entry.SetValue(state.Language, evt.newValue);
            touchedEntries.Add(state.Entry);
            field.EnableInClassList("loc-cell--missing", string.IsNullOrEmpty(evt.newValue));
            SetDirty(true);
        }

        // Applying the consequences of an edit (status pill, counters, filter, dropping an emptied row) waits until the user leaves the
        // cell, so a row never vanishes or jumps while its text is being typed.
        private void OnCellBlur(FocusOutEvent evt)
        {
            if (touchedEntries.Count == 0 || RootElement == null)
            {
                return;
            }

            editJob?.Pause();
            editJob = RootElement.schedule.Execute(ApplyEdits);
            editJob.ExecuteLater(50);
        }

        private void ApplyEdits()
        {
            bool removed = false;
            foreach (LocalizationEntry entry in touchedEntries)
            {
                // Clearing every language down to nothing leaves a row with no content worth keeping - drop the whole entry.
                if (LanguageDataLogic.IsEmptyRow(data, entry))
                {
                    data.Entries.Remove(entry);
                    removed = true;
                }
            }

            touchedEntries.Clear();
            if (removed)
            {
                SetDirty(true);
            }

            RefreshRows();
        }
    }
}
