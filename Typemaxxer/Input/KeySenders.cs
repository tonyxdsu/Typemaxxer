using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Typemaxxer.Input;

/// <summary>The ways Typemaxxer can deliver keystrokes. <see cref="Keyboard"/> is the default; the others are for testing.</summary>
public enum InputBackend
{
    WindowMessageChar = 0,
    WindowMessageKeys = 1,
    SendInputUnicode = 2,
    Keyboard = 3,
}

public interface IKeySender
{
    string Name { get; }
    string Description { get; }
    bool SendChar(char c);
    bool SendEnter();
    bool SendBackspace();
}

public static class KeySenders
{
    private static readonly IKeySender Keyboard = new KeyboardSender();
    private static readonly IKeySender WindowMessageChar = new WindowMessageCharSender();
    private static readonly IKeySender WindowMessageKeys = new WindowMessageKeySender();
    private static readonly IKeySender SendInputUnicode = new SendInputUnicodeSender();

    public static IKeySender For(InputBackend backend) => backend switch
    {
        InputBackend.WindowMessageChar => WindowMessageChar,
        InputBackend.WindowMessageKeys => WindowMessageKeys,
        InputBackend.SendInputUnicode => SendInputUnicode,
        _ => Keyboard,
    };
}

/// <summary>
/// Types with real key presses through Windows while the game window is in front, exactly like your own typing. The
/// mini-game only counts typed letters as activity (it expels players who type nothing for about a minute and a half),
/// so this is the default. When the game isn't in front, or a character has no key on the keyboard layout, the
/// character is posted to the game window instead.
/// </summary>
internal sealed class KeyboardSender : IKeySender
{
    private static readonly WindowMessageCharSender Posted = new();

    public string Name => "Keyboard";
    public string Description => "Real key presses while the game is in front (counts as typing); posts characters otherwise.";

    public bool SendChar(char c) => GameWindowInput.TypeRealChar(c, KeyHold.Next()) || Posted.SendChar(c);

    public bool SendEnter() => Posted.SendEnter();

    public bool SendBackspace() => Posted.SendBackspace();
}

/// <summary>
/// Posts a character message for each letter, which works even when the game window isn't focused and handles
/// punctuation and accents. Enter is pressed through Windows while the game is in front, so the game counts it as you
/// being active (it expels players it thinks are idle); otherwise it is posted too.
/// </summary>
internal sealed class WindowMessageCharSender : IKeySender
{
    public string Name => "WM_CHAR";
    public string Description => "Posts characters to the game window; presses Enter like a real key while the game is in front.";

    public bool SendChar(char c)
    {
        GameWindowInput.TryGetVirtualKey(c, out var virtualKey, out _, out _);
        return GameWindowInput.Post(NativeMethods.WmChar, c, GameWindowInput.KeyLParam(virtualKey, false));
    }

    public bool SendEnter() =>
        GameWindowInput.PressRealKey(NativeMethods.VkReturn, KeyHold.Next()) || GameWindowInput.PostKeyPress(NativeMethods.VkReturn);

    public bool SendBackspace() => GameWindowInput.PostKeyPress(NativeMethods.VkBack);
}

/// <summary>How long a key is held down when pressed like a real key: about as long as a person's key press.</summary>
internal static class KeyHold
{
    public static TimeSpan Next() => TimeSpan.FromMilliseconds(Random.Shared.Next(60, 101));
}

/// <summary>Posts key-down/key-up messages, as if the keys were pressed. Characters that need Shift may come out wrong.</summary>
internal sealed class WindowMessageKeySender : IKeySender
{
    public string Name => "WM_KEYDOWN";
    public string Description => "Posts key presses to the game window. Shifted characters (! ?) may not come out right.";

    public bool SendChar(char c)
    {
        if (!GameWindowInput.TryGetVirtualKey(c, out var virtualKey, out var needsShift, out _))
            return false;

        if (needsShift)
            GameWindowInput.PostKeyDown(NativeMethods.VkShift);
        var sent = GameWindowInput.PostKeyPress(virtualKey);
        if (needsShift)
            GameWindowInput.PostKeyUp(NativeMethods.VkShift);
        return sent;
    }

    public bool SendEnter() => GameWindowInput.PostKeyPress(NativeMethods.VkReturn);

    public bool SendBackspace() => GameWindowInput.PostKeyPress(NativeMethods.VkBack);
}

/// <summary>Synthesizes keyboard input through Windows itself. Only works while the game window is focused.</summary>
internal sealed class SendInputUnicodeSender : IKeySender
{
    public string Name => "SendInput";
    public string Description => "Types through Windows like a real keyboard. Only works while the game window is focused.";

    public bool SendChar(char c) =>
        GameWindowInput.IsForeground() &&
        Send(Key(0, c, NativeMethods.KeyEventFUnicode), Key(0, c, NativeMethods.KeyEventFUnicode | NativeMethods.KeyEventFKeyUp));

    public bool SendEnter() => GameWindowInput.PressRealKey(NativeMethods.VkReturn, KeyHold.Next());

    public bool SendBackspace() => SendVirtualKey(NativeMethods.VkBack);

    private static bool SendVirtualKey(ushort virtualKey)
    {
        if (!GameWindowInput.IsForeground())
            return false;

        var scanCode = (ushort)NativeMethods.MapVirtualKeyW(virtualKey, NativeMethods.MapVkVkToVsc);
        return Send(Key(virtualKey, scanCode, 0), Key(virtualKey, scanCode, NativeMethods.KeyEventFKeyUp));
    }

    private static NativeMethods.Input Key(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeyboardInput { VirtualKey = virtualKey, ScanCode = scanCode, Flags = flags },
        },
    };

    private static bool Send(params NativeMethods.Input[] inputs) =>
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) == inputs.Length;
}
