using System;
using Veneer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationSystem
{
    /// <summary>
    /// The pieces of the <see cref="LocalizedText"/> Inspector that are computed or imperative, registered with Veneer so the default
    /// layout (and a user's copy of it) can place them: the key-state pill, the key hint, the translations list and the Open Language
    /// Data button, plus the read-only widgets of the <c>Key</c> and <c>FallbackText</c> fields. They can be moved or hidden in the
    /// Inspector Designer, not changed.
    /// </summary>
    internal static class LocalizedTextItems
    {
        /// <summary>Id of the header state pill (header badge).</summary>
        public const string StateId = "loc.text.state";

        /// <summary>Id of the key hint (card item).</summary>
        public const string KeyProblemId = "loc.text.key-problem";

        /// <summary>Id of the translations list (card item).</summary>
        public const string TranslationsId = "loc.text.translations";

        /// <summary>Id of the Open Language Data button (footer, under the cards).</summary>
        public const string OpenDataId = "loc.text.open-data";

        /// <summary>The tooltip of the Key row.</summary>
        public const string KeyTooltip =
            "The lookup key: a generated id for scene text, or the source text for a [Localize] field. Managed by Sync Project and SetKey.";

        /// <summary>The tooltip of the Fallback row.</summary>
        public const string FallbackTooltip = "Shown when the key is missing from the table.";

        private const float LanguageNameWidth = 96f;

        /// <summary>Registers the custom items and widgets of <see cref="LocalizedText"/>. Safe to call again (items are replaced).</summary>
        public static void Register()
        {
            Type type = typeof(LocalizedText);
            LocalizedTextTable.Hook();

            VeneerCustomItems.Register(type, new VeneerCustomItem
            {
                Id = StateId,
                Label = "Key state",
                Icon = "check-circle",
                Description = "A pill in the header: No key, Not in table, n missing or Translated.",
                Slot = VeneerCustomItemSlot.HeaderBadge,
                Build = BuildState,
            });

            VeneerCustomItems.Register(type, new VeneerCustomItem
            {
                Id = KeyProblemId,
                Label = "Key check",
                Icon = "warning",
                Description = "A warning while the label has no key or the key is not in the translation table.",
                Slot = VeneerCustomItemSlot.Item,
                Build = BuildKeyProblem,
            });

            VeneerCustomItems.Register(type, new VeneerCustomItem
            {
                Id = TranslationsId,
                Label = "Translations",
                Icon = "list",
                Description = "What the label says in every language of the project; refreshes after Save Data.",
                Slot = VeneerCustomItemSlot.Item,
                Build = BuildTranslations,
            });

            VeneerCustomItems.Register(type, new VeneerCustomItem
            {
                Id = OpenDataId,
                Label = "Open Language Data button",
                Icon = "globe",
                Description = "Opens the Language Data window. Drawn at the bottom, whatever the cards.",
                Slot = VeneerCustomItemSlot.Footer,
                Build = BuildOpenData,
            });

            VeneerCustomItems.RegisterWidget(type, "Key", (context, property) => ReadOnlyRow("Key", property, KeyTooltip));
            VeneerCustomItems.RegisterWidget(type, "FallbackText", (context, property) => ReadOnlyRow("Fallback", property, FallbackTooltip));
        }

        private static VisualElement ReadOnlyRow(string label, SerializedProperty property, string tooltip)
        {
            var field = new TextField { isReadOnly = true, bindingPath = property.propertyPath };
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            return VeneerStyle.FieldRow(label, field, tooltip, out Label _);
        }

        private static VisualElement BuildState(VeneerCustomItemContext context)
        {
            LocalizedTextTable.BeginBuild();
            var state = new LocalizedTextState(context.SerializedObject);
            context.Builder.StatePill(state.PillState);
            return null;
        }

        private static VisualElement BuildKeyProblem(VeneerCustomItemContext context)
        {
            LocalizedTextTable.BeginBuild();
            var state = new LocalizedTextState(context.SerializedObject);
            context.Builder.ConditionalHint(state.KeyProblem, "warn", true);
            return null;
        }

        private static VisualElement BuildOpenData(VeneerCustomItemContext context)
        {
            context.Builder.Button(
                "Open Language Data",
                "globe",
                () => EditorApplication.ExecuteMenuItem("Tools/Language Data"),
                "Open the translation table.",
                true);
            return null;
        }

        private static VisualElement BuildTranslations(VeneerCustomItemContext context)
        {
            LocalizedTextTable.BeginBuild();
            var state = new LocalizedTextState(context.SerializedObject);
            var list = new VisualElement();
            FillTranslations(list, state);

            // Refreshes after "Save Data" in the Language Data window; unsubscribes when the Inspector goes away.
            Action refresh = () =>
            {
                LocalizedTextTable.Invalidate();
                FillTranslations(list, state);
            };
            list.RegisterCallback<AttachToPanelEvent>(_ => LocalizationDataStore.DataSaved += refresh);
            list.RegisterCallback<DetachFromPanelEvent>(_ => LocalizationDataStore.DataSaved -= refresh);
            return list;
        }

        private static void FillTranslations(VisualElement host, LocalizedTextState state)
        {
            host.Clear();
            string key = state.CurrentKey();
            LocalizationData table = LocalizedTextTable.Get();
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
                name.style.width = LanguageNameWidth;
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
