using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Typemaxxer.Automation;
using Typemaxxer.Diagnostics;
using Typemaxxer.Game;
using Typemaxxer.Input;

namespace Typemaxxer.Windows;

/// <summary>Developer tools: what Typemaxxer reads from the game, keyboard input tests and the session recorder.</summary>
public class DebugWindow : Window, IDisposable
{
    private static readonly TimeSpan TestCountdown = TimeSpan.FromSeconds(3);

    private readonly Plugin plugin;
    private readonly KeyTiming testTiming = new();
    private InputBackend testBackend;
    private bool testPressEnter;
    private DateTime? testStartsAt;
    private InputTest? test;

    public DebugWindow(Plugin plugin) : base("Typemaxxer - Developer###TypemaxxerDeveloper")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(760, 680);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override bool DrawConditions() => plugin.Configuration.DeveloperMode;

    /// <summary>Runs a pending input test, one key at a time. Called from the framework update.</summary>
    public void Tick(KtgFrame frame)
    {
        var now = DateTime.UtcNow;
        if (testStartsAt is { } startsAt && now >= startsAt)
        {
            testStartsAt = null;
            var text = frame.Words.FirstOrDefault()?.Text ?? "test";
            test = new InputTest(KeySenders.For(testBackend), text, testPressEnter && frame.InKeyboundBrawler) { NextKeyAt = now };
            testTiming.StartPrompt(SpeedProfile.For(plugin.Configuration));
            if (plugin.Typist.Enabled)
                plugin.Typist.SetEnabled(false, "Typemaxxer switched itself off for the input test.");
            plugin.Recorder.Write("TEST", $"start {test.Sender.Name}: \"{text}\" pressEnter={test.PressEnter}");

            // The mini-game only takes typing while its input box is open, and Enter opens it.
            if (frame.InKeyboundBrawler && !frame.InputOpen)
            {
                plugin.Recorder.Write("TEST", $"{test.Sender.Name} Enter (open the input box) sent={test.Sender.SendEnter()}");
                test.NextKeyAt = now + TimeSpan.FromSeconds(0.3);
            }
        }

        if (test == null || test.Done || now < test.NextKeyAt)
            return;

        if (test.Typed < test.Text.Length)
        {
            var c = test.Text[test.Typed];
            var sent = test.Sender.SendChar(c);
            plugin.Recorder.Write("TEST", $"{test.Sender.Name} '{c}' sent={sent}");
            test.Typed++;
            test.NextKeyAt = now + (test.Typed < test.Text.Length
                                        ? testTiming.DelayBetween(c, test.Text[test.Typed])
                                        : testTiming.SubmitDelay());
            return;
        }

        if (test.PressEnter)
            plugin.Recorder.Write("TEST", $"{test.Sender.Name} Enter sent={test.Sender.SendEnter()}");
        plugin.Recorder.Write("TEST", "done");
        test.Done = true;
    }

    public override void Draw()
    {
        DrawStatus();
        DrawInputTests();
        DrawRecording();
        DrawPrompts();
        DrawWindows();
        DrawEnemies();
    }

    private void DrawStatus()
    {
        if (!ImGui.CollapsingHeader("Status", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var recorder = plugin.Recorder;
        ImGui.BeginDisabled(!recorder.IsRecording);
        if (ImGui.Button("Mark this moment", new Vector2(200, 32) * ImGuiHelpers.GlobalScale))
            recorder.Mark(plugin, "marked from the developer window");
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (recorder.IsRecording)
            ImGui.TextColored(ImGuiColors.DalamudRed, "Recording this match");
        else
            ImGui.TextColored(ImGuiColors.DalamudGrey, plugin.Configuration.RecordSessions
                                                           ? "Recording starts when a match does"
                                                           : "Not recording: tick \"Record Keybound Brawler matches\" below");
        ImGuiHelpers.ScaledDummy(4f);

        var frame = plugin.Frame;
        var dictionary = plugin.Dictionary;
        ImGui.Text($"In Keybound Brawler: {frame.InKeyboundBrawler} (territory use {KtgUi.CurrentTerritoryIntendedUse}; the mini-game is 65)");
        ImGui.Text($"Prompts known: {dictionary.PromptCount} ({dictionary.RowCount} rows)");
        if (dictionary.LoadError != null)
            ImGui.TextColored(ImGuiColors.DalamudRed, $"Prompt list failed to load: {dictionary.LoadError}");
        ImGui.Text($"Typist: {plugin.Typist.State} - {plugin.Typist.Status}");
        ImGui.Text($"Paused: {(plugin.PauseDetector.IsPaused ? plugin.PauseDetector.Reason : "no")}");
        ImGui.Text($"Game text input active: {frame.TextInputActive}   Focused game window: {frame.FocusedAddon ?? "-"}");
        ImGui.Text($"Input box open: {frame.InputOpen}   Shows: {(frame.InputText == null ? "(can't read it)" : $"\"{frame.InputText}\"")}");
        ImGui.Text($"Inputs disabled: {frame.InputLockout ?? "no"}");
        if (GameActivity.DutyInactivity is var (seconds, limit))
            ImGui.Text($"Duty inactivity: {seconds:0} s since the game last saw input (it expels you at {limit:0} s)");
        ImGui.Text($"Windows idle: {GameWindowInput.SystemIdleTime().TotalSeconds:0} s since the last real keyboard or mouse input");
        ImGui.Text($"Dalamud UI has keyboard: {plugin.ImGuiWantsKeyboard}   Game window focused: {frame.GameWindowFocused}   Handle: 0x{GameWindowInput.Handle:X}");
        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawInputTests()
    {
        if (!ImGui.CollapsingHeader("Input tests", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextWrapped("Each test types the first prompt on screen (or \"test\") at Human-like speed, 3 seconds after " +
                          "you click. During the countdown, click the game screen so it has keyboard focus, then watch " +
                          "whether the letters register.");
        ImGui.Checkbox("Press Enter afterwards (only inside Keybound Brawler)", ref testPressEnter);

        var busy = testStartsAt != null || test is { Done: false };
        foreach (var backend in Enum.GetValues<InputBackend>())
        {
            var sender = KeySenders.For(backend);
            ImGui.BeginDisabled(busy);
            if (ImGui.Button($"Test {sender.Name}###test{backend}"))
            {
                testBackend = backend;
                testStartsAt = DateTime.UtcNow + TestCountdown;
                test = null;
            }

            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled(sender.Description);
        }

        if (testStartsAt is { } startsAt)
            ImGui.TextColored(ImGuiColors.DalamudYellow,
                              $"{KeySenders.For(testBackend).Name} test starts in {Math.Max(0, (startsAt - DateTime.UtcNow).TotalSeconds):0.0} s - click the game screen now.");
        else if (test != null)
            ImGui.Text(test.Done
                           ? $"Finished typing \"{test.Text}\" with {test.Sender.Name}."
                           : $"Typing \"{test.Text}\" with {test.Sender.Name}: {test.Typed}/{test.Text.Length}");

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.Text("Method the typist uses:");
        foreach (var backend in Enum.GetValues<InputBackend>())
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{KeySenders.For(backend).Name}###use{backend}", plugin.Configuration.InputBackend == backend))
            {
                plugin.Configuration.InputBackend = backend;
                plugin.Configuration.Save();
            }
        }

        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawRecording()
    {
        if (!ImGui.CollapsingHeader("Recording and pauses", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var configuration = plugin.Configuration;
        var recorder = plugin.Recorder;

        var record = configuration.RecordSessions;
        if (ImGui.Checkbox("Record Keybound Brawler matches", ref record))
        {
            configuration.RecordSessions = record;
            configuration.Save();
            if (record && plugin.Frame.InKeyboundBrawler)
                recorder.Start(plugin);
            if (!record)
                recorder.Stop();
        }

        UiHelpers.HelpMarker("Logs what the mini-game's windows show, enemy casts and every key Typemaxxer sends. " +
                             "One file per match, kept on this PC.");

        ImGui.TextDisabled(recorder.IsRecording ? $"Recording to {recorder.CurrentFile}" : $"Logs go to {Recorder.Folder}");
        if (ImGui.Button("Copy folder path"))
            ImGui.SetClipboardText(Recorder.Folder);

        ImGuiHelpers.ScaledDummy(4f);
        var enemyCastPause = configuration.PauseOnEnemyCasts;
        if (ImGui.Checkbox("Go quiet near the end of enemy casts", ref enemyCastPause))
        {
            configuration.PauseOnEnemyCasts = enemyCastPause;
            configuration.Save();
        }

        UiHelpers.HelpMarker("Pauses from \"Quiet before an attack\" seconds before any enemy's cast finishes " +
                             "(set under Advanced in the main window). The status line names the enemy.");

        var castBarPause = configuration.PauseWhileEnemyCastBarVisible;
        if (ImGui.Checkbox("Go quiet while the enemy cast bar window is up", ref castBarPause))
        {
            configuration.PauseWhileEnemyCastBarVisible = castBarPause;
            configuration.Save();
        }

        UiHelpers.HelpMarker("Off by default: in the first test this window was up for the whole match, so typing never started.");

        var extraPauseAddons = configuration.ExtraPauseAddons;
        if (ImGui.InputText("Also go quiet while these windows show", ref extraPauseAddons, 256))
            configuration.ExtraPauseAddons = extraPauseAddons;
        if (ImGui.IsItemDeactivatedAfterEdit())
            configuration.Save();

        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawPrompts()
    {
        if (!ImGui.CollapsingHeader("Prompts on screen", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var words = plugin.Frame.Words;
        if (words.Count == 0)
        {
            ImGui.TextDisabled($"No known prompts in {KtgUi.WordAddon} right now.");
            return;
        }

        if (!ImGui.BeginTable("prompts", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            return;

        ImGui.TableSetupColumn("Prompt");
        ImGui.TableSetupColumn("Row");
        ImGui.TableSetupColumn("On screen for");
        ImGui.TableSetupColumn("Position");
        ImGui.TableHeadersRow();

        var now = DateTime.UtcNow;
        foreach (var word in words)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(plugin.Typist.Target?.RowId == word.RowId ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, word.Text);
            ImGui.TableNextColumn();
            ImGui.Text($"{word.RowId}");
            ImGui.TableNextColumn();
            ImGui.Text($"{(now - word.FirstSeen).TotalSeconds:0.0} s");
            ImGui.TableNextColumn();
            ImGui.Text($"{word.X:0}, {word.Y:0}");
        }

        ImGui.EndTable();
    }

    private void DrawWindows()
    {
        if (!ImGui.CollapsingHeader("Keybound Brawler windows"))
            return;

        var addons = plugin.Frame.Addons;
        if (addons.Count == 0)
        {
            ImGui.TextDisabled("No KTG windows are loaded.");
            return;
        }

        foreach (var addon in addons)
        {
            var label = $"{addon.Name} #{addon.Id}: {(addon.Visible ? "visible" : "hidden")}, {addon.TextNodes.Count} text nodes###addon{addon.Id}";
            if (!ImGui.TreeNode(label))
                continue;

            if (ImGui.BeginTable($"nodes{addon.Id}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Node");
                ImGui.TableSetupColumn("Shown");
                ImGui.TableSetupColumn("Text");
                ImGui.TableSetupColumn("Position");
                ImGui.TableSetupColumn("Colour / raw");
                ImGui.TableHeadersRow();

                foreach (var node in addon.TextNodes)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(node.Path);
                    ImGui.TableNextColumn();
                    ImGui.Text(node.Visible ? "yes" : "no");
                    ImGui.TableNextColumn();
                    var isPrompt = plugin.Dictionary.TryGetRowId(node.Text, out _);
                    ImGui.TextColored(isPrompt ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, node.Text);
                    ImGui.TableNextColumn();
                    ImGui.Text($"{node.X:0}, {node.Y:0}");
                    ImGui.TableNextColumn();
                    ImGui.Text(node.RawHex == null ? $"{node.Color:X8}" : $"{node.Color:X8} {node.RawHex}");
                }

                ImGui.EndTable();
            }

            if (addon.ShapeNodes.Count > 0 &&
                ImGui.BeginTable($"shapes{addon.Id}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Node");
                ImGui.TableSetupColumn("Type");
                ImGui.TableSetupColumn("Shown");
                ImGui.TableSetupColumn("Drawn size");
                ImGui.TableSetupColumn("Position / alpha");
                ImGui.TableHeadersRow();

                foreach (var shape in addon.ShapeNodes)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(shape.Path);
                    ImGui.TableNextColumn();
                    ImGui.Text($"{shape.Type}");
                    ImGui.TableNextColumn();
                    ImGui.Text(shape.Visible ? "yes" : "no");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{shape.DrawnWidth:0} x {shape.Height}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{shape.X:0}, {shape.Y:0} / {shape.Alpha}");
                }

                ImGui.EndTable();
            }

            ImGui.TreePop();
        }
    }

    private void DrawEnemies()
    {
        if (!ImGui.CollapsingHeader("Enemies"))
            return;

        var enemies = plugin.PauseDetector.Enemies;
        if (enemies.Count == 0)
        {
            ImGui.TextDisabled("None. Enemies are only read during a match.");
            return;
        }

        if (!ImGui.BeginTable("enemies", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            return;

        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("Kind / base id");
        ImGui.TableSetupColumn("Cast");
        ImGui.TableSetupColumn("Cast time");
        ImGui.TableHeadersRow();

        foreach (var enemy in enemies)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(enemy.Name);
            ImGui.TableNextColumn();
            ImGui.Text($"{enemy.Kind} {enemy.BaseId}");
            ImGui.TableNextColumn();
            ImGui.Text(enemy.IsCasting ? $"action {enemy.CastActionId}" : "-");
            ImGui.TableNextColumn();
            ImGui.Text(enemy.IsCasting ? $"{enemy.CastTime:0.00} / {enemy.TotalCastTime:0.00} s" : "-");
        }

        ImGui.EndTable();
    }

    private sealed class InputTest(IKeySender sender, string text, bool pressEnter)
    {
        public IKeySender Sender { get; } = sender;
        public string Text { get; } = text;
        public bool PressEnter { get; } = pressEnter;
        public int Typed { get; set; }
        public DateTime NextKeyAt { get; set; }
        public bool Done { get; set; }
    }
}
