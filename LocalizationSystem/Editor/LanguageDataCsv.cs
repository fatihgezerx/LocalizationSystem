using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LocalizationSystem
{
    /// <summary>
    /// Exports/imports the table as a CSV file - the practical way to hand this off to a translator
    /// or a spreadsheet. Nothing here touches the real .xlsx binary format (that's a zipped bundle of
    /// XML files, not something worth a hand-rolled implementation), but Excel, Google Sheets, and
    /// Numbers all open and save CSV natively, so this covers the same workflow without needing a
    /// third-party Open XML library.
    /// </summary>
    internal static class LanguageDataCsv
    {
        public static string Export(LocalizationData data)
        {
            var sb = new StringBuilder();

            var header = new List<string> { "Key" };
            header.AddRange(data.Languages.Select(LocalizationRuntime.DisplayName));
            AppendRow(sb, header);

            foreach (var entry in data.Entries)
            {
                var row = new List<string> { entry.Key };
                row.AddRange(data.Languages.Select(language => entry.GetValue(language) ?? string.Empty));
                AppendRow(sb, row);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Merges rows from a CSV back into <paramref name="data"/> - existing entries (matched by
        /// the "Key" column) are updated, unrecognized keys become new entries, and entries the CSV
        /// doesn't mention at all are left untouched, so a translator can hand back a file covering
        /// only some of the rows/languages. A blank cell never erases an existing translation - it's
        /// treated as "didn't get to this one yet", not "clear it". Any language column present in
        /// the CSV that this project hasn't added yet is added automatically; columns whose header
        /// doesn't match a known language name at all are ignored.
        /// </summary>
        public static (int RowsMatched, int ValuesUpdated) Import(LocalizationData data, string csvText)
        {
            var rows = ParseCsv(csvText);
            if (rows.Count == 0)
            {
                return (0, 0);
            }

            var header = rows[0];
            var keyColumnIndex = Array.FindIndex(header, h => string.Equals(h, "Key", StringComparison.OrdinalIgnoreCase));
            if (keyColumnIndex < 0)
            {
                throw new InvalidOperationException("This CSV has no \"Key\" column - is it the right file?");
            }

            var columnLanguages = new SystemLanguage?[header.Length];
            for (var col = 0; col < header.Length; col++)
            {
                if (col == keyColumnIndex)
                {
                    continue;
                }

                var language = LocalizationRuntime.ParseDisplayName(header[col]);
                columnLanguages[col] = language;

                if (language.HasValue && !data.Languages.Contains(language.Value))
                {
                    data.Languages.Add(language.Value);
                }
            }

            var rowsMatched = 0;
            var valuesUpdated = 0;

            for (var i = 1; i < rows.Count; i++)
            {
                var fields = rows[i];
                if (keyColumnIndex >= fields.Length || string.IsNullOrEmpty(fields[keyColumnIndex]))
                {
                    continue;
                }

                var key = fields[keyColumnIndex];
                var entry = data.FindEntry(key);
                if (entry == null)
                {
                    entry = new LocalizationEntry { Key = key };
                    data.Entries.Add(entry);
                }

                rowsMatched++;

                for (var col = 0; col < fields.Length; col++)
                {
                    if (col == keyColumnIndex || !columnLanguages[col].HasValue || string.IsNullOrEmpty(fields[col]))
                    {
                        continue;
                    }

                    entry.SetValue(columnLanguages[col].Value, fields[col]);
                    valuesUpdated++;
                }
            }

            return (rowsMatched, valuesUpdated);
        }

        private static void AppendRow(StringBuilder sb, List<string> fields)
        {
            sb.Append(string.Join(",", fields.Select(EscapeField)));
            sb.Append("\r\n");
        }

        // RFC 4180-style escaping: only quote a field if it actually needs it (contains a comma,
        // quote, or newline), doubling up any quotes inside it.
        private static string EscapeField(string value)
        {
            value ??= string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // A hand-written parser rather than String.Split(',') because a quoted field can legitimately
        // contain commas and embedded newlines (a multi-line dialogue entry, for instance) - naive
        // splitting would silently corrupt those rows.
        private static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var i = 0;

            while (i < text.Length)
            {
                var c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }

                        inQuotes = false;
                        i++;
                        continue;
                    }

                    field.Append(c);
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        i++;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        i++;
                        break;
                    case '\r':
                        i++;
                        break;
                    case '\n':
                        fields.Add(field.ToString());
                        field.Clear();
                        rows.Add(fields.ToArray());
                        fields = new List<string>();
                        i++;
                        break;
                    default:
                        field.Append(c);
                        i++;
                        break;
                }
            }

            // The file doesn't necessarily end with a trailing newline - flush whatever's left.
            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }

            return rows;
        }
    }
}
