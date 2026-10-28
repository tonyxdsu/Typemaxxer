using System.Collections.Generic;

namespace Typemaxxer.Game;

/// <summary>Everything Typemaxxer read from the game during one frame.</summary>
/// <param name="InputOpen">Whether the mini-game's input box is open. It only takes typing while open; Enter opens it.</param>
/// <param name="InputText">What the input box shows, or null when it can't be read.</param>
/// <param name="InputLockout">The countdown shown while typing is disabled as a punishment ("Inputs disabled!"), else null.</param>
public sealed record KtgFrame(
    bool InKeyboundBrawler,
    IReadOnlyList<KtgAddon> Addons,
    IReadOnlyList<ActiveWord> Words,
    bool TextInputActive,
    string? FocusedAddon,
    bool InputOpen,
    string? InputText,
    string? InputLockout,
    bool EnterDown,
    bool GameWindowFocused)
{
    public static readonly KtgFrame Empty = new(false, [], [], false, null, false, null, null, false, false);
}
