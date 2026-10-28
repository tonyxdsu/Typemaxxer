using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;

namespace Typemaxxer.Windows;

/// <summary>A small, click-through line near the top of the screen saying what Typemaxxer is doing during a match.</summary>
public class StatusOverlay : Window, IDisposable
{
    private readonly Plugin plugin;

    public StatusOverlay(Plugin plugin)
        : base("Typemaxxer status###TypemaxxerStatus",
               ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize |
               ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
               ImGuiWindowFlags.NoMove)
    {
        IsOpen = true;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        BgAlpha = 0.7f;

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override bool DrawConditions() => plugin.Configuration.ShowStatusOverlay && plugin.Frame.InKeyboundBrawler;

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        var topCenter = new Vector2(viewport.Pos.X + (viewport.Size.X / 2), viewport.Pos.Y + (viewport.Size.Y * 0.08f));
        ImGui.SetNextWindowPos(topCenter, ImGuiCond.Always, new Vector2(0.5f, 0f));
    }

    public override void Draw()
    {
        var typist = plugin.Typist;
        ImGui.TextColored(UiHelpers.StatusColor(typist.State), $"Typemaxxer: {typist.Status}");
        if (!typist.Enabled)
            ImGui.TextDisabled("Type /tmx on to start");

        if (plugin.Configuration.DeveloperMode)
        {
            if (plugin.Recorder.IsRecording)
                ImGui.TextColored(ImGuiColors.DalamudRed, $"Recording ({Plugin.VersionLabel})");
            else
                ImGui.TextDisabled($"Not recording ({Plugin.VersionLabel})");
        }
    }
}
