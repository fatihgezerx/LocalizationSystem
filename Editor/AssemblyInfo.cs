using System.Runtime.CompilerServices;

// The studio-look Language Data window and Inspectors live in their own assembly (they need Veneer, which is optional)
// but reuse the data store, the scanners, the CSV code, the translation providers and the shared window logic of this one.
[assembly: InternalsVisibleTo("LocalizationSystem.Editor.Studio")]
