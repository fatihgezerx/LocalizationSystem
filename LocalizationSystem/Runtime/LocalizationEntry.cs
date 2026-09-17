using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>One language's text for a given <see cref="LocalizationEntry"/>.</summary>
    [Serializable]
    public sealed class LocalizationValue
    {
        public SystemLanguage Language;
        public string Text;
    }

    /// <summary>
    /// A single localizable piece of text (e.g. a UI label), identified by a stable <see cref="Key"/>,
    /// with one <see cref="LocalizationValue"/> per language it has been translated into so far.
    /// </summary>
    [Serializable]
    public sealed class LocalizationEntry
    {
        public string Key;
        public List<LocalizationValue> Values = new();

        /// <summary>The text for <paramref name="language"/>, or null if it hasn't been filled in yet.</summary>
        public string GetValue(SystemLanguage language)
        {
            foreach (var value in Values)
            {
                if (value.Language == language)
                {
                    return value.Text;
                }
            }

            return null;
        }

        /// <summary>Sets (or overwrites) the text for <paramref name="language"/>.</summary>
        public void SetValue(SystemLanguage language, string text)
        {
            foreach (var value in Values)
            {
                if (value.Language == language)
                {
                    value.Text = text;
                    return;
                }
            }

            Values.Add(new LocalizationValue { Language = language, Text = text });
        }

        /// <summary>Removes the text for <paramref name="language"/> entirely, if present. A no-op otherwise.</summary>
        public void RemoveValue(SystemLanguage language)
        {
            Values.RemoveAll(value => value.Language == language);
        }
    }
}
