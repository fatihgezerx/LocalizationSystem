using System.Collections.Generic;
using Veneer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationSystem
{
    public sealed partial class LanguageDataWindow
    {
        private readonly List<System.Action> languageUpdaters = new();
        private readonly List<VisualElement> providerChips = new();

        private int selectedPresetIndex = -1;
        private string presetNameInput = string.Empty;
        private string apiKeyCache;

        private Label apiKeyTitle;
        private TextField apiKeyField;
        private VisualElement unsupportedHint;
        private Label unsupportedLabel;
        private VisualElement contextCard;
        private VisualElement contextHint;
        private Label contextHintLabel;
        private Button presetButton;
        private Label presetButtonLabel;
        private TextField contextField;
        private TextField presetNameField;
        private Button savePromptButton;

        // ------------------------------------------------------------------ construction

        private VisualElement BuildSettingsPage()
        {
            languageUpdaters.Clear();
            providerChips.Clear();

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("loc-settings");
            scroll.Add(BuildLanguagesCard());
            scroll.Add(BuildProviderCard());
            scroll.Add(BuildContextCard());
            return scroll;
        }

        private VisualElement BuildLanguagesCard()
        {
            VisualElement card = VeneerStyle.Card("Languages", "globe", Brand);
            card.Add(VeneerStyle.Hint(
                "Choose which languages this project supports. Adding one adds an empty column to the table; removing one deletes its " +
                "translations for every entry. Click Save Data when you are done to write these changes to disk.",
                "info", Brand));

            foreach (SystemLanguage language in LanguageDataLogic.SelectableLanguages())
            {
                card.Add(BuildLanguageRow(language));
            }

            return card;
        }

        private VisualElement BuildLanguageRow(SystemLanguage language)
        {
            var row = new VisualElement();
            row.AddToClassList("loc-lang");
            row.Add(MakeFlag(language));

            var name = new Label(LanguageDataLogic.DisplayName(language));
            name.AddToClassList("loc-lang__name");
            row.Add(name);

            VisualElement includeChip = null;
            includeChip = VeneerStyle.Chip("Included", "check", false, on =>
            {
                if (LanguageDataLogic.SetLanguageIncluded(data, language, on))
                {
                    SetDirty(true);
                    RebuildTable();
                    UpdateLanguageRows();
                }
            }, Brand);
            includeChip.AddToClassList("loc-lang__chip");
            row.Add(includeChip);

            var sourceHost = new VisualElement();
            sourceHost.AddToClassList("loc-lang__source");
            Label sourcePill = VeneerStyle.Pill("Source", "special");
            Button sourceButton = VeneerStyle.Button("Set as Source", null, "Translate from this language.", () =>
            {
                LanguageDataLogic.SetSourceLanguage(data, language);
                SetDirty(true);
                RebuildTable();
                UpdateLanguageRows();
            }, false, Brand);
            sourceHost.Add(sourcePill);
            sourceHost.Add(sourceButton);
            row.Add(sourceHost);

            languageUpdaters.Add(() =>
            {
                bool included = data.Languages.Contains(language);
                bool isSource = data.SourceLanguage == language && included;
                VeneerStyle.SetChipOn(includeChip, included);
                VeneerStyle.SetDisplay(sourcePill, isSource);
                VeneerStyle.SetDisplay(sourceButton, !isSource);
                sourceButton.SetEnabled(included);
            });
            return row;
        }

        private VisualElement BuildProviderCard()
        {
            VisualElement card = VeneerStyle.Card("Translation provider", "sliders", Brand);

            var chips = new VisualElement();
            chips.AddToClassList("loc-provider");
            foreach (AiProvider provider in System.Enum.GetValues(typeof(AiProvider)))
            {
                AiProvider captured = provider;
                VisualElement chip = VeneerStyle.Chip(TranslationProviders.All[provider].DisplayName, null, false, on => SelectProvider(captured), Brand);
                providerChips.Add(chip);
                chips.Add(chip);
            }

            card.Add(chips);

            apiKeyTitle = new Label();
            apiKeyTitle.AddToClassList("vn-label-muted");
            card.Add(apiKeyTitle);
            card.Add(VeneerStyle.Hint(
                "Stored in this machine's Editor preferences only - never written into the project, so it can never end up committed to git " +
                "or shared if this project is published. Each provider keeps its own key, so switching providers does not lose the others.",
                "info", Brand));

            var keyRow = new VisualElement();
            keyRow.AddToClassList("loc-key-row");
            apiKeyField = new TextField { isPasswordField = true, maskChar = '*' };
            apiKeyField.RegisterValueChangedCallback(evt =>
            {
                apiKeyCache = evt.newValue;
                AiProviderSettings.SetApiKey(AiProviderSettings.GetProvider(), evt.newValue);
            });
            keyRow.Add(apiKeyField);
            keyRow.Add(VeneerStyle.Button("Get API Key", "external-link", "Open the provider's page for creating a key.", () =>
                Application.OpenURL(TranslationProviders.All[AiProviderSettings.GetProvider()].ApiKeyHelpUrl), false, Brand));
            card.Add(keyRow);

            unsupportedHint = VeneerStyle.Hint(string.Empty, "warn", out unsupportedLabel, Brand);
            card.Add(unsupportedHint);
            return card;
        }

        private VisualElement BuildContextCard()
        {
            contextCard = VeneerStyle.Card("Game context (optional)", "book", Brand);
            contextHint = VeneerStyle.Hint(string.Empty, "info", out contextHintLabel, Brand);
            contextCard.Add(contextHint);

            var presetRow = new VisualElement();
            presetRow.AddToClassList("loc-preset-row");
            presetButton = VeneerStyle.Button("Choose a preset", "preset",
                "Fill the text below with a genre prompt. Picking one again restores its original text.", ShowPresetMenu, false, Brand);
            presetButtonLabel = presetButton.Q<Label>(className: "vn-button__label");
            presetRow.Add(presetButton);
            contextCard.Add(presetRow);

            contextField = new TextField { multiline = true };
            contextField.AddToClassList("loc-context");
            contextField.RegisterValueChangedCallback(evt =>
            {
                data.GameContext = evt.newValue;
                SetDirty(true);
                UpdateSavePromptState();
            });
            contextCard.Add(contextField);

            var saveRow = new VisualElement();
            saveRow.AddToClassList("loc-preset-row");
            presetNameField = new TextField("New preset name");
            presetNameField.RegisterValueChangedCallback(evt =>
            {
                presetNameInput = evt.newValue;
                UpdateSavePromptState();
            });
            saveRow.Add(presetNameField);
            savePromptButton = VeneerStyle.Button("Save Prompt", "save", "Keep the text above as a preset of this project.", SaveCustomPreset, false, Brand);
            saveRow.Add(savePromptButton);
            contextCard.Add(saveRow);
            return contextCard;
        }

        // ------------------------------------------------------------------ refresh

        // Brings every control of the Settings page in line with the data and the provider. Nothing is rebuilt, so the scroll position stays.
        private void RefreshSettings()
        {
            if (settingsPage == null)
            {
                return;
            }

            UpdateLanguageRows();
            RefreshProviderUi();
            contextField.SetValueWithoutNotify(data.GameContext ?? string.Empty);
            selectedPresetIndex = -1;
            UpdatePresetButton();
            UpdateSavePromptState();
        }

        private void UpdateLanguageRows()
        {
            for (int i = 0; i < languageUpdaters.Count; i++)
            {
                languageUpdaters[i]();
            }

            UpdateUnsupportedHint();
        }

        private void RefreshProviderUi()
        {
            AiProvider provider = AiProviderSettings.GetProvider();
            ITranslationProvider translationProvider = TranslationProviders.All[provider];

            int index = 0;
            foreach (AiProvider candidate in System.Enum.GetValues(typeof(AiProvider)))
            {
                VeneerStyle.SetChipOn(providerChips[index], candidate == provider);
                index++;
            }

            apiKeyCache = AiProviderSettings.GetApiKey(provider);
            apiKeyTitle.text = $"{translationProvider.DisplayName} API key";
            apiKeyField.SetValueWithoutNotify(apiKeyCache);

            // Plain machine translation has no concept of tone/genre, so there is nothing for these controls to do:
            // disable them rather than leaving them editable but inert.
            bool supportsContext = translationProvider.SupportsGameContext;
            contextHintLabel.text = supportsContext
                ? "Describe your game so the AI translates with the right tone and terminology, e.g. \"Fantasy RPG, epic/formal tone, similar to Skyrim.\" " +
                  "Saved with the rest of the table via Save Data - this is not a secret, unlike the API key above. Pick a genre preset to fill it in, then tweak it."
                : $"{translationProvider.DisplayName} is plain machine translation, not an AI model - it has no concept of tone or genre, so the Game Context " +
                  "is saved but has no effect on its output. Switch to Gemini, OpenAI or Claude to make use of it.";
            presetButton.SetEnabled(supportsContext);
            contextField.SetEnabled(supportsContext);
            presetNameField.SetEnabled(supportsContext);
            UpdateSavePromptState();
            UpdateUnsupportedHint();
            RefreshRateLimit();
        }

        // Shown for a per-language provider (Google Translate, DeepL) that does not cover every language the project uses.
        private void UpdateUnsupportedHint()
        {
            if (unsupportedHint == null)
            {
                return;
            }

            AiProvider provider = AiProviderSettings.GetProvider();
            List<string> unsupported = LanguageDataLogic.UnsupportedLanguages(data, provider);
            VeneerStyle.SetDisplay(unsupportedHint, unsupported.Count > 0);
            if (unsupported.Count > 0)
            {
                unsupportedLabel.text =
                    $"{TranslationProviders.All[provider].DisplayName} doesn't support: {string.Join(", ", unsupported)}. " +
                    "Those columns will stay empty when translating with this provider - use a different one for them.";
            }
        }

        private void SelectProvider(AiProvider provider)
        {
            AiProviderSettings.SetProvider(provider);
            RefreshProviderUi();
        }

        // ------------------------------------------------------------------ Game Context presets

        // A menu rather than a dropdown field, so picking the same preset again re-applies its original text.
        private void ShowPresetMenu()
        {
            (string Name, string Prompt)[] presets = LanguageDataLogic.AllGameContextPresets(data);
            var menu = new GenericMenu();
            for (int i = 0; i < presets.Length; i++)
            {
                int index = i;
                menu.AddItem(new GUIContent(presets[i].Name), i == selectedPresetIndex, () =>
                {
                    selectedPresetIndex = index;
                    data.GameContext = presets[index].Prompt;
                    contextField.SetValueWithoutNotify(data.GameContext);
                    SetDirty(true);
                    UpdatePresetButton();
                    UpdateSavePromptState();
                });
            }

            menu.DropDown(presetButton.worldBound);
        }

        private void UpdatePresetButton()
        {
            (string Name, string Prompt)[] presets = LanguageDataLogic.AllGameContextPresets(data);
            presetButtonLabel.text = selectedPresetIndex >= 0 && selectedPresetIndex < presets.Length
                ? "Preset: " + presets[selectedPresetIndex].Name
                : "Choose a preset";
        }

        private void UpdateSavePromptState()
        {
            if (savePromptButton == null)
            {
                return;
            }

            bool supportsContext = TranslationProviders.All[AiProviderSettings.GetProvider()].SupportsGameContext;
            savePromptButton.SetEnabled(supportsContext && !string.IsNullOrWhiteSpace(presetNameInput) && !string.IsNullOrWhiteSpace(data.GameContext));
        }

        private void SaveCustomPreset()
        {
            selectedPresetIndex = LanguageDataLogic.SaveCustomGameContextPreset(data, presetNameInput.Trim(), data.GameContext);
            SetDirty(true);
            presetNameInput = string.Empty;
            presetNameField.SetValueWithoutNotify(string.Empty);
            UpdatePresetButton();
            UpdateSavePromptState();
        }
    }
}
