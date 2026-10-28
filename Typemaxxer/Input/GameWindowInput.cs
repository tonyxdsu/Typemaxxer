using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Typemaxxer.Input;

/// <summary>Finds the game's window and posts keyboard messages to it, or presses keys through Windows itself.</summary>
internal static unsafe class GameWindowInput
{
    // Keys pressed through Windows that still have to be let go, and when.
    private static readonly List<(ushort VirtualKey, ushort ScanCode, DateTime ReleaseAt)> HeldKeys = [];

    private static nint cachedHandle;

    public static nint Handle
    {
        get
        {
            if (cachedHandle != 0 && NativeMethods.IsWindow(cachedHandle))
                return cachedHandle;

            cachedHandle = 0;
            try
            {
                var gameWindow = FFXIVClientStructs.FFXIV.Client.System.Framework.GameWindow.Instance();
                if (gameWindow != null)
                    cachedHandle = gameWindow->WindowHandle;
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "Could not read the game window handle; falling back to the process main window.");
            }

            if (cachedHandle == 0)
            {
                using var process = Process.GetCurrentProcess();
                cachedHandle = process.MainWindowHandle;
            }

            return cachedHandle;
        }
    }

    public static bool IsForeground()
    {
        var handle = Handle;
        return handle != 0 && NativeMethods.GetForegroundWindow() == handle;
    }

    public static bool Post(uint message, nuint wParam, nint lParam)
    {
        var handle = Handle;
        return handle != 0 && NativeMethods.PostMessageW(handle, message, wParam, lParam);
    }

    public static bool PostKeyDown(ushort virtualKey) => Post(NativeMethods.WmKeyDown, virtualKey, KeyLParam(virtualKey, false));

    public static bool PostKeyUp(ushort virtualKey) => Post(NativeMethods.WmKeyUp, virtualKey, KeyLParam(virtualKey, true));

    // Like a real key press: the game's message loop turns the key-down into the matching character itself.
    public static bool PostKeyPress(ushort virtualKey) => PostKeyDown(virtualKey) && PostKeyUp(virtualKey);

    /// <summary>
    /// Presses a key through Windows itself, exactly like a real keyboard, and lets go of it after <paramref name="hold"/>.
    /// Unlike a posted message, the game counts this as you being active. Only possible while the game window is in
    /// front, since Windows delivers it to whichever window is.
    /// </summary>
    public static bool PressRealKey(ushort virtualKey, TimeSpan hold)
    {
        if (!IsForeground())
            return false;

        ReleaseHeldKeys(all: true);
        return Press(virtualKey, hold);
    }

    /// <summary>
    /// Types a character with real key presses through Windows, holding Shift when the keyboard layout needs it, so the
    /// game handles it exactly like your own typing. False when the game isn't in front, or the character has no key on
    /// this layout (or needs Ctrl or AltGr); the caller then posts it instead.
    /// </summary>
    public static bool TypeRealChar(char c, TimeSpan hold)
    {
        if (!IsForeground() || !TryGetVirtualKey(c, out var virtualKey, out var needsShift, out var needsCtrlOrAlt) || needsCtrlOrAlt)
            return false;

        // Let go of the previous key first, so a Shift still held for it can't change this character.
        ReleaseHeldKeys(all: true);
        if (needsShift && !Press(NativeMethods.VkShift, hold))
            return false;
        return Press(virtualKey, hold);
    }

    // Keys are let go in reverse order (see ReleaseHeldKeys), so a letter comes up before the Shift held for it.
    private static bool Press(ushort virtualKey, TimeSpan hold)
    {
        var scanCode = (ushort)NativeMethods.MapVirtualKeyW(virtualKey, NativeMethods.MapVkVkToVsc);
        if (!SendKeyboardInput(virtualKey, scanCode, 0))
            return false;

        HeldKeys.Add((virtualKey, scanCode, DateTime.UtcNow + hold));
        return true;
    }

    /// <summary>Lets go of keys whose hold time is up; with <paramref name="all"/>, of every held key. Runs every frame.</summary>
    public static void ReleaseHeldKeys(bool all = false)
    {
        var now = DateTime.UtcNow;
        for (var i = HeldKeys.Count - 1; i >= 0; i--)
        {
            var key = HeldKeys[i];
            if (!all && now < key.ReleaseAt)
                continue;

            SendKeyboardInput(key.VirtualKey, key.ScanCode, NativeMethods.KeyEventFKeyUp);
            HeldKeys.RemoveAt(i);
        }
    }

    public static bool SendKeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        var inputs = new[]
        {
            new NativeMethods.Input
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeyboardInput { VirtualKey = virtualKey, ScanCode = scanCode, Flags = flags },
                },
            },
        };
        return NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.Input>()) == 1;
    }

    /// <summary>Flashes the game's taskbar button until the game window is brought to the front.</summary>
    public static void FlashTaskbarButton()
    {
        var info = new NativeMethods.FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.FlashWindowInfo>(),
            Window = Handle,
            Flags = NativeMethods.FlashAll | NativeMethods.FlashUntilForeground,
        };
        NativeMethods.FlashWindowEx(ref info);
    }

    /// <summary>How long ago Windows last saw real keyboard or mouse input, in any application.</summary>
    public static TimeSpan SystemIdleTime()
    {
        var info = new NativeMethods.LastInputInfo { Size = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>() };
        return NativeMethods.GetLastInputInfo(ref info)
                   ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time))
                   : TimeSpan.Zero;
    }

    /// <summary>The virtual key for a character on the current keyboard layout, and which modifiers it needs.</summary>
    public static bool TryGetVirtualKey(char c, out ushort virtualKey, out bool needsShift, out bool needsCtrlOrAlt)
    {
        var result = NativeMethods.VkKeyScanW(c);
        virtualKey = (ushort)(result & 0xFF);
        needsShift = (result & 0x100) != 0;
        needsCtrlOrAlt = (result & 0x600) != 0;
        return result != -1;
    }

    // Repeat count 1, the key's scan code, and for key-up the "previous state" and "transition" bits.
    public static nint KeyLParam(ushort virtualKey, bool keyUp)
    {
        var scanCode = NativeMethods.MapVirtualKeyW(virtualKey, NativeMethods.MapVkVkToVsc);
        var lParam = 1L | ((long)scanCode << 16);
        if (keyUp)
            lParam |= (1L << 30) | (1L << 31);
        return (nint)lParam;
    }
}
