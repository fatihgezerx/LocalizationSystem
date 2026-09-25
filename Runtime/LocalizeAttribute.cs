using System;

namespace LocalizationSystem
{
    /// <summary>
    /// Marks a <c>string</c> (or <c>string[]</c>/<c>List&lt;string&gt;</c>) field - public or
    /// <c>[SerializeField]</c> private, on any <see cref="UnityEngine.ScriptableObject"/> or
    /// <see cref="UnityEngine.Component"/> - as text that should be picked up by "Sync Project" and
    /// offered for translation. This only marks the field for scanning: the field itself still just
    /// holds plain text (whatever was typed into it, in whatever ScriptableObject/prefab/scene object
    /// it lives on) - actually showing the translated version at runtime means calling
    /// <see cref="LocalizationRuntime.Get"/> with that field's value wherever you display it, e.g.
    /// <c>myLabel.text = LocalizationRuntime.Get(myField);</c>, or calling
    /// <see cref="LocalizedText.SetKey"/> if that display should also live-update when the language
    /// changes while it's on screen.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class LocalizeAttribute : Attribute
    {
    }
}
