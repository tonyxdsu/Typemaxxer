using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;

namespace Typemaxxer.Game;

/// <summary>A text node inside one of Keybound Brawler's windows.</summary>
/// <param name="Path">The node id, prefixed by the ids of the components it sits in, e.g. "12/3".</param>
/// <param name="RawHex">The raw text bytes, only kept when they contain formatting macros.</param>
public sealed record KtgTextNode(
    string Path, bool Visible, float X, float Y, ushort Width, ushort Height, string Text, string? RawHex, uint Color);

/// <summary>A non-text node (image, bar, container), only collected for <see cref="KtgUi.DetailAddons"/>.</summary>
public sealed record KtgShapeNode(
    string Path, ushort Type, bool Visible, float X, float Y, ushort Width, ushort Height, float ScaleX, byte Alpha)
{
    /// <summary>The drawn width, which is how bars and gauges show their progress.</summary>
    public float DrawnWidth => Width * ScaleX;
}

/// <summary>One of Keybound Brawler's windows ("addons"), with every text node in it.</summary>
public sealed record KtgAddon(
    string Name, ushort Id, bool Visible, short X, short Y, float Scale,
    IReadOnlyList<KtgTextNode> TextNodes, IReadOnlyList<KtgShapeNode> ShapeNodes);

/// <summary>
/// Reads Keybound Brawler's windows. FFXIVClientStructs knows the mini-game as "KTG", and every one of its windows
/// is named KTG-something.
/// </summary>
public static unsafe class KtgUi
{
    public const string WordAddon = "KTGEWord";
    public const string InputAddon = "KTGInput";
    public const string EnemyCastBarAddon = "KTGEnemyCastBar";

    // The text node in KTGInput that shows what has been typed so far. It is hidden while the box is empty.
    private const string InputTextNodePath = "7/16";

    // The text node in KTGInput that counts down (7, 6, ... 1) while typing is disabled after an enemy attack.
    private const string LockoutCountdownNodePath = "3";

    public static readonly string[] AddonNames =
    [
        "KTGEWord", "KTGIGaugeHp", "KTGEnemyCastBar", "KTGInput", "KTGEnterDialogue", "KTGResult", "KTGHudGaugeHp",
        "KTGHudLog", "KTGHudScore", "KTGHudTodo", "KTGPlayerName", "KTGPlayGuide", "KTGScreenTextFever",
    ];

    // Windows whose bars and images are worth recording too, while working out what signals an enemy attack.
    public static readonly string[] DetailAddons = ["KTGEnemyCastBar", "KTGScreenTextFever"];

    public static bool IsInKeyboundBrawlerTerritory => CurrentTerritoryIntendedUse == (uint)TerritoryIntendedUse.KeyboundBrawler;

    public static uint CurrentTerritoryIntendedUse
    {
        get
        {
            var gameMain = GameMain.Instance();
            return gameMain == null ? 0 : (uint)gameMain->CurrentTerritoryIntendedUseId;
        }
    }

    // Windows that are only on screen while a match is being played.
    public static bool IsGameplayAddon(string name) => name is "KTGEWord" or "KTGInput" or "KTGHudScore";

    public static List<KtgAddon> ReadAddons()
    {
        var addons = new List<KtgAddon>();
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return addons;

        var units = &manager->AllLoadedUnitsList;
        for (var i = 0; i < units->Count; i++)
        {
            var unit = units->Entries[i].Value;
            if (unit == null || !unit->Name.StartsWith("KTG"u8))
                continue;

            // A window can be flagged visible while its root node is hidden, which draws nothing.
            var visible = unit->IsVisible && unit->RootNode != null && unit->RootNode->IsVisible();
            var name = unit->NameString;
            var textNodes = new List<KtgTextNode>();
            var shapeNodes = new List<KtgShapeNode>();
            CollectNodes(&unit->UldManager, string.Empty, textNodes, Array.IndexOf(DetailAddons, name) >= 0 ? shapeNodes : null, 0);
            addons.Add(new KtgAddon(name, unit->Id, visible, unit->X, unit->Y, unit->Scale, textNodes, shapeNodes));
        }

        return addons;
    }

    /// <summary>What the mini-game's input box shows, or null if it can't be found.</summary>
    public static string? ReadInputText(IReadOnlyList<KtgAddon> addons)
    {
        var node = addons.FirstOrDefault(a => a.Name == InputAddon)?.TextNodes.FirstOrDefault(n => n.Path == InputTextNodePath);
        if (node == null)
            return null;
        return node.Visible ? node.Text : string.Empty;
    }

    /// <summary>The countdown while typing is disabled ("Inputs disabled!" after typing during an attack), else null.</summary>
    public static string? ReadInputLockout(IReadOnlyList<KtgAddon> addons)
    {
        var node = addons.FirstOrDefault(a => a.Name == InputAddon)?.TextNodes.FirstOrDefault(n => n.Path == LockoutCountdownNodePath);
        return node is { Visible: true, Text.Length: > 0 } ? node.Text : null;
    }

    /// <summary>Whether any game text field (chat box, search box, ...) is taking typed text.</summary>
    public static bool IsTextInputActive()
    {
        var module = RaptureAtkModule.Instance();
        return module != null && module->IsTextInputActive();
    }

    /// <summary>The name of the game window that holds keyboard focus, if any.</summary>
    public static string? FocusedAddonName()
    {
        var stage = AtkStage.Instance();
        var manager = RaptureAtkUnitManager.Instance();
        if (stage == null || manager == null)
            return null;

        var node = stage->GetFocus();
        if (node == null)
            return null;

        var addon = manager->AtkUnitManager.GetAddonByNode(node);
        return addon == null ? "?" : addon->NameString;
    }

    public static string Decode(ReadOnlySpan<byte> raw) => raw.IsEmpty ? string.Empty : new ReadOnlySeStringSpan(raw).ExtractText();

    // Collects every text node, and every other node too when shapes is given, descending into components.
    private static void CollectNodes(
        AtkUldManager* uldManager, string pathPrefix, List<KtgTextNode> texts, List<KtgShapeNode>? shapes, int depth)
    {
        if (depth > 8 || uldManager->NodeList == null)
            return;

        for (var i = 0; i < uldManager->NodeListCount; i++)
        {
            var node = uldManager->NodeList[i];
            if (node == null)
                continue;

            var path = pathPrefix + node->NodeId;
            if (node->Type == NodeType.Text)
            {
                var textNode = (AtkTextNode*)node;
                var raw = textNode->NodeText.AsSpan();
                texts.Add(new KtgTextNode(
                    path,
                    IsEffectivelyVisible(node),
                    node->ScreenX,
                    node->ScreenY,
                    node->Width,
                    node->Height,
                    Decode(raw),
                    raw.IndexOf((byte)0x02) >= 0 ? Convert.ToHexString(raw) : null,
                    textNode->TextColor.RGBA));
                continue;
            }

            shapes?.Add(new KtgShapeNode(
                path, (ushort)node->Type, IsEffectivelyVisible(node), node->ScreenX, node->ScreenY,
                node->Width, node->Height, node->ScaleX, node->Alpha_2));

            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode != null && componentNode->Component != null)
                CollectNodes(&componentNode->Component->UldManager, $"{path}/", texts, shapes, depth + 1);
        }
    }

    // A node is only drawn if it and all of its parents are visible.
    private static bool IsEffectivelyVisible(AtkResNode* node)
    {
        for (var i = 0; node != null && i < 64; i++, node = node->ParentNode)
        {
            if (!node->IsVisible())
                return false;
        }

        return true;
    }
}
