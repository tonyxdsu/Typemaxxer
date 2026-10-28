using System;
using System.Collections.Generic;
using System.Text;
using Lumina.Excel;

namespace Typemaxxer.Game;

/// <summary>
/// Every Keybound Brawler prompt, read from the game's own data: column 0 of the KTGTypeWordTextData sheet holds the
/// text shown on screen (column 1 is the Japanese kana reading).
/// </summary>
public sealed class WordDictionary
{
    private const string SheetName = "KTGTypeWordTextData";

    // Prompts are matched without regard to case, like the game does.
    private readonly Dictionary<string, uint> rowIdByText = new(StringComparer.OrdinalIgnoreCase);

    public WordDictionary()
    {
        try
        {
            var sheet = Plugin.DataManager.Excel.GetSheet<RawRow>(language: null, name: SheetName);
            foreach (var row in sheet)
            {
                RowCount++;
                var text = Normalize(row.ReadStringColumn(0).ExtractText());
                if (text.Length > 0)
                    rowIdByText.TryAdd(text, row.RowId);
            }
        }
        catch (Exception e)
        {
            LoadError = e.Message;
            Plugin.Log.Error(e, $"Could not read the {SheetName} sheet.");
        }
    }

    public int RowCount { get; }
    public int PromptCount => rowIdByText.Count;
    public string? LoadError { get; }

    public bool TryGetRowId(string text, out uint rowId) => rowIdByText.TryGetValue(Normalize(text), out rowId);

    /// <summary>Trims the text and turns any run of whitespace (including line breaks) into a single space.</summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(c);
        }

        return builder.ToString();
    }
}
