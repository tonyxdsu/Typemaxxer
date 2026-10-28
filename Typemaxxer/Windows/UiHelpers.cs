using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Typemaxxer.Automation;

namespace Typemaxxer.Windows;

internal static class UiHelpers
{
    /// <summary>A greyed-out "(?)" after the previous item that explains it on hover.</summary>
    public static void HelpMarker(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    public static Vector4 StatusColor(TypistState state) => state switch
    {
        TypistState.Typing or TypistState.Submitting => ImGuiColors.HealerGreen,
        TypistState.WaitingForEnter => ImGuiColors.DalamudYellow,
        TypistState.Paused or TypistState.Waiting => ImGuiColors.DalamudOrange,
        _ => ImGuiColors.DalamudGrey,
    };
}
