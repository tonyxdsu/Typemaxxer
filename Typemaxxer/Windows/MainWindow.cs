using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Typemaxxer.Automation;

namespace Typemaxxer.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base($"Typemaxxer {Plugin.VersionLabel}###TypemaxxerMain")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 260),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(420, 540);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawStatus();
        ImGuiHelpers.ScaledDummy(6f);
        DrawHowItWorks();
        DrawSettings();
        DrawAdvanced();
    }

    private void DrawStatus()
    {
        var typist = plugin.Typist;
        ImGui.TextColored(UiHelpers.StatusColor(typist.State), typist.Status);

        var enabled = typist.Enabled;
        var color = enabled ? ImGuiColors.DalamudRed : ImGuiColors.HealerGreen;
        ImGui.PushStyleColor(ImGuiCol.Button, color * new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * new Vector4(0.75f, 0.75f, 0.75f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, color);
        if (ImGui.Button(enabled ? "Stop typing" : "Start typing", new Vector2(ImGui.GetContentRegionAvail().X, 40 * ImGuiHelpers.GlobalScale)))
            typist.SetEnabled(!enabled, null);
        ImGui.PopStyleColor(3);

        ImGui.TextDisabled(enabled
            ? "Click anywhere on the game screen so the game has keyboard focus."
            : "Typemaxxer only ever types inside Keybound Brawler.");
    }

    private void DrawHowItWorks()
    {
        var configuration = plugin.Configuration;
        if (!ImGui.CollapsingHeader("How it works", configuration.HasSeenIntro ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextWrapped("1. Join a Keybound Brawler match at the Gold Saucer.");
        ImGui.TextWrapped("2. Press Start typing (or type /tmx on), then click anywhere on the game screen.");
        ImGui.TextWrapped("3. Typemaxxer types the prompt above an enemy at a human pace. You press Enter to attack.");
        ImGuiHelpers.ScaledDummy(2f);
        ImGui.TextWrapped("It goes quiet during enemy attacks, never types into the chat box, and switches itself off " +
                          "when you leave the match. It makes no network connections, so nothing leaves your PC.");

        if (!configuration.HasSeenIntro && ImGui.Button("Got it"))
        {
            configuration.HasSeenIntro = true;
            configuration.Save();
        }

        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawSettings()
    {
        var configuration = plugin.Configuration;
        if (!ImGui.CollapsingHeader("Settings", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Text("Speed");
        if (ImGui.RadioButton(SpeedProfile.HumanLike.Name, configuration.SpeedMode == SpeedMode.HumanLike))
        {
            configuration.SpeedMode = SpeedMode.HumanLike;
            configuration.Save();
        }

        ImGui.Indent();
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(SpeedProfile.HumanLike.Description);
        ImGui.PopTextWrapPos();

        var typingSpeed = configuration.TypingSpeed;
        if (ImGui.SliderFloat($"Typing speed (~{typingSpeed * 12:0} WPM)###typingSpeed", ref typingSpeed,
                              Configuration.MinTypingSpeed, Configuration.MaxTypingSpeed, "%.1f characters/s"))
            configuration.TypingSpeed = typingSpeed;
        if (ImGui.IsItemDeactivatedAfterEdit())
            configuration.Save();

        var promptPause = configuration.PromptPauseSeconds;
        if (ImGui.SliderFloat("Pause between prompts###promptPause", ref promptPause, 0f, Configuration.MaxPromptPauseSeconds, "%.2f s"))
            configuration.PromptPauseSeconds = promptPause;
        if (ImGui.IsItemDeactivatedAfterEdit())
            configuration.Save();
        UiHelpers.HelpMarker("How long Typemaxxer waits between finishing one prompt and starting the next.");

        if (ImGui.Button("Reset speed"))
        {
            configuration.TypingSpeed = Configuration.DefaultTypingSpeed;
            configuration.PromptPauseSeconds = Configuration.DefaultPromptPauseSeconds;
            configuration.Save();
        }

        ImGui.TextDisabled("Faster presets are on the way.");
        ImGui.Unindent();
        ImGuiHelpers.ScaledDummy(4f);

        var autoPressEnter = configuration.AutoPressEnter;
        if (ImGui.Checkbox("Press Enter for me", ref autoPressEnter))
        {
            configuration.AutoPressEnter = autoPressEnter;
            configuration.Save();
        }

        UiHelpers.HelpMarker("Off: Typemaxxer types each prompt, and you press Enter to attack.\nOn: Typemaxxer presses Enter for you as well.\n\n" +
                             "Either way, while the game window is in front Typemaxxer types with real key presses, so the game counts you as active. " +
                             "If you switch to another window, it stops typing until you click the game again (see Advanced).");

        var showOverlay = configuration.ShowStatusOverlay;
        if (ImGui.Checkbox("Show status during matches", ref showOverlay))
        {
            configuration.ShowStatusOverlay = showOverlay;
            configuration.Save();
        }

        UiHelpers.HelpMarker("A small line at the top of the screen that says what Typemaxxer is doing.");
        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawAdvanced()
    {
        var configuration = plugin.Configuration;
        if (!ImGui.CollapsingHeader("Advanced"))
            return;

        ImGui.TextWrapped("Some enemy attacks, like Cursed Gaze, punish typing just after they land. Typemaxxer goes quiet " +
                          "shortly before they land and stays quiet until their effect wears off. Through other attacks it " +
                          "keeps typing, so you can defeat the enemy first. You shouldn't need to change these.");

        var lead = configuration.PauseLeadSeconds;
        if (ImGui.SliderFloat("Quiet before an attack", ref lead, 0.5f, 5f, "%.1f s"))
            configuration.PauseLeadSeconds = lead;
        if (ImGui.IsItemDeactivatedAfterEdit())
            configuration.Save();
        UiHelpers.HelpMarker("How long before the attack lands Typemaxxer stops typing.");

        var trail = configuration.PauseTrailSeconds;
        if (ImGui.SliderFloat("Quiet after an attack", ref trail, 0f, Configuration.MaxPauseTrailSeconds, "%.1f s"))
            configuration.PauseTrailSeconds = trail;
        if (ImGui.IsItemDeactivatedAfterEdit())
            configuration.Save();
        UiHelpers.HelpMarker("Covers the moment between the attack landing and its effect showing up on you (about 0.7 s). " +
                             "After that, Typemaxxer stays quiet for as long as the effect lasts, however this is set.");

        if (ImGui.Button("Reset to defaults"))
        {
            configuration.PauseLeadSeconds = Configuration.DefaultPauseLeadSeconds;
            configuration.PauseTrailSeconds = Configuration.DefaultPauseTrailSeconds;
            configuration.Save();
        }

        ImGuiHelpers.ScaledDummy(6f);
        var typeInBackground = configuration.TypeInBackground;
        if (ImGui.Checkbox("Send simulated keypresses upon focus loss.", ref typeInBackground))
        {
            configuration.TypeInBackground = typeInBackground;
            configuration.Save();
        }

        ImGui.PushTextWrapPos();
        ImGui.TextColored(ImGuiColors.DalamudOrange,
                          "WARNING: You will EXIT the instance automatically after 90s of continuous loss of focus of the client. " +
                          "This option is meant for short alt-tabbing only.");
        ImGui.PopTextWrapPos();

        ImGuiHelpers.ScaledDummy(6f);
        var developerMode = configuration.DeveloperMode;
        if (ImGui.Checkbox("Developer mode", ref developerMode))
        {
            configuration.DeveloperMode = developerMode;
            configuration.Save();
        }

        UiHelpers.HelpMarker("Shows the diagnostic tools used while testing Typemaxxer. Not needed for normal play.");

        if (configuration.DeveloperMode)
        {
            ImGui.SameLine();
            if (ImGui.Button("Open developer window"))
                plugin.ToggleDebugUi();
        }
    }
}
