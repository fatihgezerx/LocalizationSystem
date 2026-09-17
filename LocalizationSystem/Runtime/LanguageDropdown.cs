using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LocalizationSystem
{
    /// <summary>
    /// Drop-in language picker: put this next to a <see cref="Dropdown"/> or <see cref="TMP_Dropdown"/>
    /// (whichever the project's UI uses - both are supported the same way <see cref="LocalizedText"/>
    /// supports both <c>Text</c> and <c>TMP_Text</c>) and it fills the options from
    /// <see cref="LocalizationRuntime.AvailableLanguages"/>, pre-selects the current language, and
    /// calls <see cref="LocalizationRuntime.SetLanguage"/> whenever the player picks a different one -
    /// no wiring beyond adding the component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LanguageDropdown : MonoBehaviour
    {
        private Dropdown _dropdown;
        private TMP_Dropdown _tmpDropdown;
        private SystemLanguage[] _languages;

        private void Awake()
        {
            _dropdown = GetComponent<Dropdown>();
            _tmpDropdown = GetComponent<TMP_Dropdown>();
        }

        private void OnEnable()
        {
            Populate();
            _dropdown?.onValueChanged.AddListener(OnValueChanged);
            _tmpDropdown?.onValueChanged.AddListener(OnValueChanged);
        }

        private void OnDisable()
        {
            _dropdown?.onValueChanged.RemoveListener(OnValueChanged);
            _tmpDropdown?.onValueChanged.RemoveListener(OnValueChanged);
        }

        private void Populate()
        {
            _languages = LocalizationRuntime.AvailableLanguages;

            var labels = new List<string>(_languages.Length);
            foreach (var language in _languages)
            {
                labels.Add(LocalizationRuntime.DisplayName(language));
            }

            var currentIndex = System.Array.IndexOf(_languages, LocalizationRuntime.CurrentLanguage);
            currentIndex = Mathf.Max(currentIndex, 0);

            if (_dropdown != null)
            {
                _dropdown.ClearOptions();
                _dropdown.AddOptions(labels);
                _dropdown.SetValueWithoutNotify(currentIndex);
            }

            if (_tmpDropdown != null)
            {
                _tmpDropdown.ClearOptions();
                _tmpDropdown.AddOptions(labels);
                _tmpDropdown.SetValueWithoutNotify(currentIndex);
            }
        }

        private void OnValueChanged(int index)
        {
            if (index >= 0 && index < _languages.Length)
            {
                LocalizationRuntime.SetLanguage(_languages[index]);
            }
        }
    }
}
