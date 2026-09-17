using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LocalizationSystem
{
    /// <summary>
    /// Attached (by <c>SceneScanner</c>/<c>AttributeFieldScanner</c>, as part of "Sync Project") next
    /// to a <see cref="Text"/> or <see cref="TMP_Text"/> component, this is what actually makes a
    /// scanned label change when the player switches language at runtime - the "Language Data" window
    /// and its JSON only hold the data, this is what applies it.
    /// </summary>
    /// <remarks>
    /// That covers a field that's always shown by the same GameObject. For text your own code decides
    /// to show at runtime instead (a dialogue box cycling through different lines, an item
    /// description that changes with the selected item, etc.), add this component once and call
    /// <see cref="SetKey"/> whenever you decide what it should display right now - a
    /// <c>[Localize]</c>-attributed string's own value works directly as the key, since that's the
    /// whole point of how those are keyed (see <c>AttributeFieldScanner</c>). The attribute alone
    /// never makes a plain assignment like <c>myText.text = someLocalizedField;</c> language-aware -
    /// only <see cref="LocalizationRuntime.Get"/> (which this calls) or this component do that.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LocalizedText : MonoBehaviour
    {
        // Just an opaque lookup key (a generated GUID for scene text, or the source text itself for
        // [Localize]-attributed fields) - never meant to be hand-edited, so it's kept out of the
        // default Inspector rather than shown as a raw string.
        [HideInInspector] [SerializeField] internal string Key;

        // What to show if the key's entry is ever missing (data file deleted, entry removed by
        // mistake, etc.) - without this, LocalizationRuntime.Get falls all the way back to returning
        // Key itself, which is a random-looking GUID for scene text. Showing the last-known real text
        // instead is far less confusing than a garbled string appearing on screen.
        [HideInInspector] [SerializeField] internal string FallbackText;

        private Text _text;
        private TMP_Text _tmpText;

        private void Awake()
        {
            _text = GetComponent<Text>();
            _tmpText = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            Apply();
            LocalizationRuntime.LanguageChanged += Apply;
        }

        private void OnDisable()
        {
            LocalizationRuntime.LanguageChanged -= Apply;
        }

        /// <summary>
        /// Changes what this component displays, right now, in the current language - and keeps it
        /// updated automatically if the language changes later while this same key is still showing.
        /// For a <c>[Localize]</c>-attributed field, pass the field's own value directly (it doubles
        /// as its own fallback, since that value already <i>is</i> the source-language text).
        /// </summary>
        public void SetKey(string key, string fallbackText = null)
        {
            Key = key;
            FallbackText = fallbackText ?? key;
            Apply();
        }

        private void Apply()
        {
            if (string.IsNullOrEmpty(Key))
            {
                return;
            }

            var value = LocalizationRuntime.Get(Key, FallbackText);
            if (_text != null)
            {
                _text.text = value;
            }

            if (_tmpText != null)
            {
                _tmpText.text = value;
            }
        }
    }
}
