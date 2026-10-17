using Veneer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationSystem
{
    /// <summary>
    /// Inspector of <see cref="LocalizedText"/> in the studio look: shows how complete the label's key is in the translation table,
    /// the key and fallback text (read-only, they are managed by Sync Project and <c>SetKey</c>), what the label says in every language
    /// of the project, and a button that opens the Language Data window.
    /// </summary>
    [CustomEditor(typeof(LocalizedText))]
    internal sealed class LocalizedTextEditor : VeneerInspector<LocalizedText>
    {
        private LocalizationData tableData;
        private bool tableStale = true;

        /// <inheritdoc />
        protected override VeneerBrand Brand => LocalizationBrand.Value;

        /// <inheritdoc />
        protected override void Build(VeneerInspectorBuilder b)
        {
            b.Header("Localized Text", "Shows this label in the current language", "globe", out Label _);
            b.StatePill(GetState);

            b.Card("Key", "key", () =>
            {
                b.Add(ReadOnlyRow("Key", "Key", "The lookup key: a generated id for scene text, or the source text for a [Localize] field. Managed by Sync Project and SetKey."));
                b.Add(ReadOnlyRow("Fallback", "FallbackText", "Shown when the key is missing from the table."));
                b.ConditionalHint(GetKeyProblem, "warn", true);
            });

            b.Card("Translations", "list", () =>
            {
                var list = new VisualElement();
                FillTranslations(list);
                // Refreshes after "Save Data" in the Language Data window; unsubscribes when the Inspector goes away.
                list.RegisterCallback<AttachToPanelEvent>(_ => LocalizationDataStore.DataSaved += OnTableSaved);
                list.RegisterCallback<DetachFromPanelEvent>(_ => LocalizationDataStore.DataSaved -= OnTableSaved);
                translationsHost = list;
                b.Add(list);
            });

            b.Button("Open Language Data", "globe", () => EditorApplication.ExecuteMenuItem("Tools/Language Data"),
                "Open the translation table.", true);
        }

        private VisualElement translationsHost;

        private VisualElement ReadOnlyRow(string label, string path, string tooltip)
        {
            var field = new TextField { isReadOnly = true, bindingPath = path };
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            return VeneerStyle.FieldRow(label, field, tooltip, out Label _);
        }

        private void OnTableSaved()
        {
            tableStale = true;
            if (translationsHost != null)
            {
                FillTranslations(translationsHost);
            }
        }

        private LocalizationData Table()
        {
            if (tableStale || tableData == null)
            {
                tableData = LocalizationDataStore.Load();
                tableStale = false;
            }

            return tableData;
        }

        private string CurrentKey()
        {
            SerializedProperty key = serializedObject.FindProperty("Key");
            return key != null ? key.stringValue : null;
        }

        private (string text, string kind) GetState()
        {
            string key = CurrentKey();
            if (string.IsNullOrEmpty(key))
            {
                return ("No key", "warn");
            }

            LocalizationData table = Table();
            LocalizationEntry entry = table.FindEntry(key);
            if (entry == null)
            {
                return ("Not in table", "error");
            }

            int missing = LanguageDataLogic.CountMissing(table, entry);
            return missing == 0 ? ("Translated", "ok") : (missing + " missing", "warn");
        }

        private string GetKeyProblem()
        {
            string key = CurrentKey();
            if (string.IsNullOrEmpty(key))
            {
                return "No key yet: this label shows its own text. Run Sync Project, or call SetKey from code.";
            }

            return Table().FindEntry(key) == null
                ? "This key is not in the translation table. Run Sync Project in the Language Data window (or save the table if you just added it)."
                : null;
        }

        private void FillTranslations(VisualElement host)
        {
            host.Clear();
            string key = CurrentKey();
            LocalizationData table = Table();
            LocalizationEntry entry = string.IsNullOrEmpty(key) ? null : table.FindEntry(key);
            if (entry == null)
            {
                host.Add(VeneerStyle.MutedLabel("Nothing to show: the key has no entry in the table."));
                return;
            }

            foreach (SystemLanguage language in table.Languages)
            {
                VisualElement row = VeneerStyle.Row();
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2f;

                var name = new Label(LanguageDataLogic.DisplayName(language));
                name.style.width = 96f;
                name.style.flexShrink = 0f;
                row.Add(name);

                string text = entry.GetValue(language);
                if (string.IsNullOrEmpty(text))
                {
                    row.Add(VeneerStyle.Pill("missing", "warn"));
                }
                else
                {
                    Label value = VeneerStyle.EllipsisLabel(text);
                    value.style.flexGrow = 1f;
                    value.tooltip = text;
                    row.Add(value);
                }

                host.Add(row);
            }
        }
    }
}
