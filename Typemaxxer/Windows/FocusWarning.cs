using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Typemaxxer.Windows;

/// <summary>
/// A big, pulsing red banner in the middle of the game screen while Typemaxxer is on in a match but the game window
/// isn't in front. The game keeps drawing in the background, so it shows on another monitor too.
/// </summary>
public class FocusWarning : Window, IDisposable
{
    private static readonly Vector4 DeepRed = new(0.45f, 0.04f, 0.04f, 0.92f);
    private static readonly Vector4 BrightRed = new(0.85f, 0.1f, 0.1f, 0.92f);
    private static readonly Vector4 BorderYellow = new(1f, 0.85f, 0.2f, 1f);

    private readonly Plugin plugin;
    private readonly IFontHandle headlineFont;

    public FocusWarning(Plugin plugin)
        : base("Typemaxxer focus warning###TypemaxxerFocusWarning",
               ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize |
               ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
               ImGuiWindowFlags.NoMove)
    {
        IsOpen = true;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        // The banner draws its own pulsing background.
        BgAlpha = 0f;

        this.plugin = plugin;
        headlineFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic34));
    }

    public void Dispose() => headlineFont.Dispose();

    public override bool DrawConditions() => plugin.Typist.FocusLostAt != null;

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + (viewport.Size * new Vector2(0.5f, 0.35f)), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
    }

    public override void Draw()
    {
        // Pulse between a deep and a bright red once a second, so it catches the eye.
        var pulse = (MathF.Sin(Environment.TickCount64 / 1000f * MathF.Tau) + 1) / 2;
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, ToColor(Vector4.Lerp(DeepRed, BrightRed, pulse)), 8f);
        drawList.AddRect(min, max, ToColor(BorderYellow), 8f, ImDrawFlags.None, 3f);

        var typingInBackground = plugin.Configuration.TypeInBackground;
        var seconds = (DateTime.UtcNow - (plugin.Typist.FocusLostAt ?? DateTime.UtcNow)).TotalSeconds;
        ImGuiHelpers.ScaledDummy(2f);
        using (headlineFont.Push())
            ImGui.TextUnformatted(typingInBackground ? "THE GAME ISN'T IN FRONT" : "TYPEMAXXER STOPPED");

        if (typingInBackground)
        {
            ImGui.TextUnformatted("Typemaxxer keeps typing with simulated key presses, which the game counts as idle.");
            ImGui.TextUnformatted($"You will be expelled after about 90 s without focus ({seconds:0} s so far). Click the game to stay in.");
        }
        else
        {
            ImGui.TextUnformatted("The game window isn't in front, so Typemaxxer stopped typing.");
            ImGui.TextUnformatted($"Click the game to carry on ({seconds:0} s so far).");
        }

        ImGuiHelpers.ScaledDummy(2f);
    }

    // ImGui packs colours as ABGR.
    private static uint ToColor(Vector4 color) =>
        ((uint)(color.W * 255) << 24) | ((uint)(color.Z * 255) << 16) | ((uint)(color.Y * 255) << 8) | (uint)(color.X * 255);
}
