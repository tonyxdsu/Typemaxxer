using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Dalamud.Game;
using Typemaxxer.Game;
using Typemaxxer.Input;

namespace Typemaxxer.Automation;

public enum TypistState
{
    Off,
    WaitingForMatch,
    LookingForPrompts,
    Typing,
    WaitingForEnter,
    Submitting,
    Paused,
    Waiting,
}

/// <summary>
/// Types the prompts on screen, one at a time: pick the oldest prompt, pause like a person would, open the game's input
/// box (it only takes typing while open, and Enter opens it), type the prompt at the chosen pace, then wait for the
/// player's Enter (or press it, if the player asked for that). What the input box shows is treated as the truth, so
/// dropped keys are sent again and leftovers from a prompt that vanished are cleared.
/// </summary>
public sealed class Typist
{
    // A prompt can flicker out of view for a moment while typing; only drop it once it has been gone this long.
    private static readonly TimeSpan VanishGrace = TimeSpan.FromSeconds(0.15);

    // If a prompt is still on screen this long after Enter, the attack didn't land, so try again.
    private static readonly TimeSpan RetryAfterSubmit = TimeSpan.FromSeconds(0.6);

    // How long to wait for the input box to open after pressing Enter before pressing it again.
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(0.4);

    // How long the game gets to show a key in its input box before Typemaxxer trusts what the box says. The game shows
    // it on the next frame, so wait two frames and at least this long.
    private static readonly TimeSpan KeyEchoTime = TimeSpan.FromSeconds(0.04);
    private const int KeyEchoFrames = 2;

    // A prompt the game won't take, even after retrying a key, is skipped for this long.
    private static readonly TimeSpan SkipFor = TimeSpan.FromSeconds(5);

    private const int MaxKeyAttempts = 3;
    private const int MaxClearAttempts = 2;

    private readonly Plugin plugin;
    private readonly KeyTiming timing = new();
    private readonly Dictionary<uint, DateTime> skipUntil = [];

    private ActiveWord? target;
    private DateTime nextKeyAt;
    private DateTime lastKeySentAt;
    private int framesSinceKey;
    private DateTime? openSentAt;
    private DateTime? submittedAt;
    private DateTime? targetMissingSince;
    private int stuckPosition = -1;
    private int stuckAttempts;
    private int clearAttempts;
    private bool enterWasDown;
    private bool wasHolding;

    public Typist(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public bool Enabled { get; private set; }
    public TypistState State { get; private set; } = TypistState.Off;
    public string Status { get; private set; } = "Off";
    public ActiveWord? Target => target;
    public int TypedCount { get; private set; }

    public void SetEnabled(bool enabled, string? message)
    {
        if (enabled && Plugin.ClientState.ClientLanguage == ClientLanguage.Japanese)
        {
            Plugin.ChatGui.PrintError("Typemaxxer can't type Japanese prompts yet.", "Typemaxxer");
            return;
        }

        if (enabled == Enabled)
        {
            if (message == null)
                Plugin.ChatGui.Print(enabled ? "Typemaxxer is already on." : "Typemaxxer is already off.", "Typemaxxer");
            return;
        }

        Enabled = enabled;
        Forget();
        plugin.Recorder.Write("TYPIST", enabled ? "switched on" : $"switched off{(message != null ? $": {message}" : string.Empty)}");
        Plugin.ChatGui.Print(
            message ?? (enabled
                ? "Typemaxxer is on. It types the prompts during Keybound Brawler matches; press Enter after each one to attack."
                : "Typemaxxer is off."),
            "Typemaxxer");
    }

    public void Update(KtgFrame frame)
    {
        var enterPressed = frame.EnterDown && !enterWasDown;
        enterWasDown = frame.EnterDown;
        framesSinceKey++;

        if (!Enabled)
        {
            Show(TypistState.Off, "Off");
            return;
        }

        if (!frame.InKeyboundBrawler)
        {
            Forget();
            Show(TypistState.WaitingForMatch, "On - waiting for a Keybound Brawler match");
            return;
        }

        var now = DateTime.UtcNow;
        TrackTarget(frame, now);

        // Hold off while something else needs the keyboard or an enemy attack is landing; resume like a person would.
        var waitReason = WhyWait(frame);
        if (waitReason != null || plugin.PauseDetector.IsPaused)
        {
            wasHolding = true;
            if (waitReason != null)
                Show(TypistState.Waiting, waitReason);
            else
                Show(TypistState.Paused, $"Paused: {plugin.PauseDetector.Reason}");
            return;
        }

        if (wasHolding)
        {
            wasHolding = false;
            nextKeyAt = Max(nextKeyAt, now + timing.ReactionDelay());
        }

        if (target == null)
        {
            target = frame.Words.FirstOrDefault(w => !IsSkipped(w.RowId, now));
            if (target == null)
            {
                Show(TypistState.LookingForPrompts, "Looking for prompts...");
                return;
            }

            BeginPrompt(now, "target");
        }

        if (submittedAt != null)
        {
            WaitForAttack(frame, now);
            return;
        }

        if (now < nextKeyAt)
        {
            ShowTyping();
            return;
        }

        if (!frame.InputOpen)
        {
            OpenInputBox(now);
            return;
        }

        openSentAt = null;

        // Once the game has had time to show the last key, go by what its input box says.
        var position = TypedCount;
        if (frame.InputText is { } shown && KeyHasEchoed(now))
        {
            if (!IsTypedPrefix(shown, target.Text))
            {
                ClearInput(shown, now);
                return;
            }

            position = shown.Length;
            clearAttempts = 0;
        }

        if (position < target.Text.Length)
        {
            TypeKey(position, now);
            return;
        }

        // The whole prompt is typed. Where the input box can be read, wait until it shows the last key.
        if (frame.InputText != null && !KeyHasEchoed(now))
            return;

        if (plugin.Configuration.AutoPressEnter)
        {
            Submit(now);
            return;
        }

        if (enterPressed)
        {
            submittedAt = now;
            plugin.Recorder.Write("TYPIST", "you pressed Enter");
            Show(TypistState.Submitting, "Attacking...");
            return;
        }

        Show(TypistState.WaitingForEnter, $"Press Enter to attack: \"{target.Text}\"");
    }

    private IKeySender Sender => KeySenders.For(plugin.Configuration.InputBackend);

    private string? WhyWait(KtgFrame frame)
    {
        if (frame.FocusedAddon?.StartsWith("ChatLog", StringComparison.Ordinal) == true)
            return "Waiting: the chat box is open";
        if (plugin.ImGuiWantsKeyboard)
            return "Waiting: click the game screen so it has keyboard focus";
        if (plugin.Configuration.InputBackend == InputBackend.SendInputUnicode && !frame.GameWindowFocused)
            return "Waiting: the game window isn't focused";
        return null;
    }

    // Drops the target once its prompt has left the screen: the attack landed, or the enemy is gone.
    private void TrackTarget(KtgFrame frame, DateTime now)
    {
        if (target == null)
            return;

        if (frame.Words.Any(w => w.RowId == target.RowId))
        {
            targetMissingSince = null;
            return;
        }

        // After Enter the prompt is expected to go at once; otherwise allow for a brief flicker.
        targetMissingSince ??= now;
        if (submittedAt == null && now - targetMissingSince.Value < VanishGrace)
            return;

        plugin.Recorder.Write("TYPIST", submittedAt != null
            ? $"attack landed: \"{target.Text}\""
            : $"prompt vanished after {TypedCount}/{target.Text.Length}: \"{target.Text}\"");
        Forget();
    }

    private void BeginPrompt(DateTime now, string why)
    {
        TypedCount = 0;
        submittedAt = null;
        openSentAt = null;
        stuckPosition = -1;
        stuckAttempts = 0;
        clearAttempts = 0;
        timing.StartPrompt(SpeedProfile.For(plugin.Configuration));
        nextKeyAt = now + timing.ReactionDelay();
        plugin.Recorder.Write("TYPIST", $"{why}: \"{target!.Text}\" (row {target.RowId})");
        ShowTyping();
    }

    private void OpenInputBox(DateTime now)
    {
        if (TypedCount >= target!.Text.Length)
        {
            // The box closed after the whole prompt was typed: the player's Enter (or the game) submitted it.
            plugin.Recorder.Write("TYPIST", "input box closed after the prompt was typed");
            submittedAt = now;
            Show(TypistState.Submitting, "Attacking...");
            return;
        }

        if (TypedCount > 0)
        {
            plugin.Recorder.Write("TYPIST", $"input box closed after {TypedCount}/{target.Text.Length}, starting over");
            TypedCount = 0;
        }

        if (openSentAt == null || now - openSentAt.Value > OpenTimeout)
        {
            var sent = Sender.SendEnter();
            plugin.Recorder.Write("KEY", $"{Sender.Name} Enter (open the input box) sent={sent}");
            if (!sent)
            {
                nextKeyAt = now + TimeSpan.FromSeconds(0.5);
                Show(TypistState.Waiting, "Waiting: couldn't send keys to the game");
                return;
            }

            openSentAt = now;
            nextKeyAt = now + timing.OpenDelay();
        }

        ShowTyping();
    }

    private void TypeKey(int position, DateTime now)
    {
        // The game didn't take a key we sent: send it again, then without its accent, then give up on the prompt.
        if (position < TypedCount)
        {
            stuckAttempts = position == stuckPosition ? stuckAttempts + 1 : 1;
            stuckPosition = position;
            if (stuckAttempts >= MaxKeyAttempts)
            {
                SkipTarget(now, $"the game won't take '{target!.Text[position]}' in \"{target.Text}\"");
                return;
            }
        }

        var c = target!.Text[position];
        if (position == stuckPosition && stuckAttempts >= 2)
            c = WithoutAccent(c);

        if (!Sender.SendChar(c))
        {
            plugin.Recorder.Write("KEY", $"{Sender.Name} '{c}' could not be sent");
            nextKeyAt = now + TimeSpan.FromSeconds(0.5);
            Show(TypistState.Waiting, "Waiting: couldn't send keys to the game");
            return;
        }

        plugin.Recorder.Write("KEY", $"{Sender.Name} '{c}'");
        TypedCount = position + 1;
        lastKeySentAt = now;
        framesSinceKey = 0;
        if (TypedCount < target.Text.Length)
            nextKeyAt = now + timing.DelayBetween(c, target.Text[TypedCount]);
        else
            nextKeyAt = now + (plugin.Configuration.AutoPressEnter ? timing.SubmitDelay() : TimeSpan.Zero);
        ShowTyping();
    }

    // Clears text that doesn't belong to the current prompt, e.g. from a prompt another player finished first.
    private void ClearInput(string shown, DateTime now)
    {
        if (++clearAttempts > MaxClearAttempts)
        {
            // Backspace isn't getting through; Enter at least closes the box and empties it.
            plugin.Recorder.Write("TYPIST", $"couldn't clear \"{shown}\", pressing Enter to close the input box");
            Sender.SendEnter();
            clearAttempts = 0;
        }
        else
        {
            var presses = Math.Min(shown.Length + 1, 64);
            plugin.Recorder.Write("TYPIST", $"input box shows \"{shown}\", clearing it ({presses} backspaces)");
            for (var i = 0; i < presses; i++)
                Sender.SendBackspace();
        }

        TypedCount = 0;
        lastKeySentAt = now;
        framesSinceKey = 0;
        nextKeyAt = now + KeyEchoTime;
        ShowTyping();
    }

    private bool KeyHasEchoed(DateTime now) => framesSinceKey >= KeyEchoFrames && now - lastKeySentAt >= KeyEchoTime;

    private void Submit(DateTime now)
    {
        var sent = Sender.SendEnter();
        plugin.Recorder.Write("KEY", $"{Sender.Name} Enter sent={sent}");
        if (!sent)
        {
            nextKeyAt = now + TimeSpan.FromSeconds(0.5);
            Show(TypistState.Waiting, "Waiting: couldn't send keys to the game");
            return;
        }

        submittedAt = now;
        Show(TypistState.Submitting, "Attacking...");
    }

    // After Enter: the prompt normally leaves the screen at once (TrackTarget then moves on). If it doesn't, try again.
    private void WaitForAttack(KtgFrame frame, DateTime now)
    {
        if (now - submittedAt!.Value < RetryAfterSubmit)
        {
            Show(TypistState.Submitting, "Attacking...");
            return;
        }

        if (frame.InputOpen)
        {
            plugin.Recorder.Write("TYPIST", "the input box is still open after Enter");
            submittedAt = null;
            nextKeyAt = now;
            return;
        }

        BeginPrompt(now, "still on screen after Enter, typing it again");
    }

    private void SkipTarget(DateTime now, string why)
    {
        plugin.Recorder.Write("TYPIST", $"{why}; skipping it for now");
        skipUntil[target!.RowId] = now + SkipFor;
        for (var i = 0; i < Math.Min(TypedCount + 1, 64); i++)
            Sender.SendBackspace();
        Forget();
    }

    private bool IsSkipped(uint rowId, DateTime now)
    {
        if (!skipUntil.TryGetValue(rowId, out var until))
            return false;
        if (now < until)
            return true;

        skipUntil.Remove(rowId);
        return false;
    }

    private void Forget()
    {
        target = null;
        TypedCount = 0;
        submittedAt = null;
        openSentAt = null;
        targetMissingSince = null;
        stuckPosition = -1;
        stuckAttempts = 0;
        clearAttempts = 0;
    }

    private void ShowTyping() => Show(TypistState.Typing, $"Typing \"{target!.Text}\" ({TypedCount}/{target.Text.Length})");

    private void Show(TypistState state, string status)
    {
        if (state != State)
            plugin.Recorder.Write("STATE", $"{state}: {status}");
        State = state;
        Status = status;
    }

    // Whether the input box holds the start of the prompt. Like the game, ignores case; also ignores accents.
    private static bool IsTypedPrefix(string shown, string prompt) =>
        CultureInfo.InvariantCulture.CompareInfo.IsPrefix(prompt, shown, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    // "á" becomes "a": the fallback if the game won't take an accented letter as typed.
    private static char WithoutAccent(char c) => c.ToString().Normalize(NormalizationForm.FormD)[0];

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
