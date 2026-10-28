using System;
using System.Collections.Generic;
using System.Linq;

namespace Typemaxxer.Game;

/// <summary>A prompt currently shown above an enemy.</summary>
public sealed record ActiveWord(uint RowId, string Text, float X, float Y, DateTime FirstSeen);

/// <summary>Finds the prompts on screen: visible text in the enemy-word window that matches a known prompt.</summary>
public sealed class WordReader
{
    private readonly WordDictionary dictionary;
    private readonly Dictionary<uint, DateTime> firstSeen = [];

    public WordReader(WordDictionary dictionary)
    {
        this.dictionary = dictionary;
    }

    public IReadOnlyList<ActiveWord> Read(IReadOnlyList<KtgAddon> addons)
    {
        var now = DateTime.UtcNow;
        var words = new Dictionary<uint, ActiveWord>();
        foreach (var addon in addons)
        {
            if (!addon.Visible || addon.Name != KtgUi.WordAddon)
                continue;

            foreach (var node in addon.TextNodes)
            {
                if (!node.Visible || !dictionary.TryGetRowId(node.Text, out var rowId) || words.ContainsKey(rowId))
                    continue;

                if (!firstSeen.TryGetValue(rowId, out var since))
                    firstSeen[rowId] = since = now;
                words[rowId] = new ActiveWord(rowId, WordDictionary.Normalize(node.Text), node.X, node.Y, since);
            }
        }

        foreach (var rowId in firstSeen.Keys.Where(id => !words.ContainsKey(id)).ToList())
            firstSeen.Remove(rowId);

        return words.Values.OrderBy(w => w.FirstSeen).ThenBy(w => w.X).ToList();
    }
}
