using System;
using System.Collections.Generic;
using System.Linq;

namespace Typemaxxer.Game;

/// <summary>How the game is showing a prompt, going by its colour.</summary>
public enum WordKind
{
    Normal,

    /// <summary>Green: a phrase with a special purpose, such as reinforcing the barrier against a boss attack.</summary>
    Special,

    /// <summary>Pale yellow: the enemy is casting an attack; defeating it first stops the attack.</summary>
    Casting,

    /// <summary>Grey: on its way out (the enemy was defeated, often by another player).</summary>
    Dimmed,
}

/// <summary>A prompt currently shown above an enemy.</summary>
public sealed record ActiveWord(uint RowId, string Text, float X, float Y, DateTime FirstSeen, WordKind Kind);

/// <summary>Finds the prompts on screen: visible text in the enemy-word window that matches a known prompt.</summary>
public sealed class WordReader
{
    // Prompt colours as the game draws them (RGBA, as read from the text node).
    private const uint SpecialColor = 0xFF74F8A0;
    private const uint CastingColor = 0xFFB0F8FF;
    private const uint DimmedColor = 0xFFCCCCCC;

    // Prompts can blink out for a moment (e.g. while "Phrases partially obscured!"); remember them this long so they
    // keep their place in line.
    private static readonly TimeSpan RememberFor = TimeSpan.FromSeconds(3);

    private readonly WordDictionary dictionary;
    private readonly Dictionary<uint, (DateTime FirstSeen, DateTime LastSeen)> seen = [];

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

                var firstSeen = seen.TryGetValue(rowId, out var times) ? times.FirstSeen : now;
                seen[rowId] = (firstSeen, now);
                words[rowId] = new ActiveWord(rowId, WordDictionary.Normalize(node.Text), node.X, node.Y, firstSeen, KindOf(node.Color));
            }
        }

        foreach (var rowId in seen.Where(s => now - s.Value.LastSeen > RememberFor).Select(s => s.Key).ToList())
            seen.Remove(rowId);

        return words.Values.OrderBy(w => w.FirstSeen).ThenBy(w => w.X).ToList();
    }

    private static WordKind KindOf(uint color) => color switch
    {
        SpecialColor => WordKind.Special,
        CastingColor => WordKind.Casting,
        DimmedColor => WordKind.Dimmed,
        _ => WordKind.Normal,
    };
}
