using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ActionSheet = Lumina.Excel.Sheets.Action;

namespace Typemaxxer.Game;

/// <summary>A non-player character in the match, with its current cast.</summary>
/// <param name="CastTarget">Who the cast is aimed at: "you", "itself", "none" or "other".</param>
/// <param name="PunishesTyping">Whether Typemaxxer goes quiet for this cast.</param>
public sealed record KtgEnemy(
    ulong ObjectId, string Name, uint BaseId, ObjectKind Kind, bool IsCasting, uint CastActionId, string CastName,
    float CastTime, float TotalCastTime, bool Interruptible, string CastTarget, bool PunishesTyping);

/// <summary>
/// Decides when to go quiet. Most enemy casts are attacks you stop by defeating the enemy first, so Typemaxxer keeps
/// typing through them. A few punish typing: about 0.7 s after their cast ends they put a 2-second status effect on
/// you, and typing while it's on gets you "Letterlocked" (input disabled for 7 seconds). Typemaxxer goes quiet
/// <see cref="Configuration.PauseLeadSeconds"/> before such a cast ends, stays quiet for
/// <see cref="Configuration.PauseTrailSeconds"/> after it (until the status shows up), and then for as long as the
/// status is on you.
/// </summary>
public sealed class PauseDetector
{
    // Attacks that punish typing, keyed by action id, which (unlike the name) is the same in every game language.
    private static readonly Dictionary<uint, string> PunishingCasts = new()
    {
        [49529] = "Cursed Gaze",
        [49702] = "Dynamic Sensory Jammer",
    };

    // The status effect each of those attacks leaves on you while typing is dangerous.
    private static readonly Dictionary<uint, string> DangerStatuses = new()
    {
        [5346] = "Cursed Gaze",
        [5437] = "Dynamic Sensory Jammer",
    };

    // "Letterlocked": input disabled after typing during one of those attacks.
    private const uint LetterlockedStatus = 5347;

    // Stay quiet this long after a danger status or a lockout wears off, to be safe.
    private static readonly TimeSpan AfterStatus = TimeSpan.FromSeconds(0.3);

    // The game object id meaning "no target".
    private const ulong NoTarget = 0xE0000000;

    private readonly Dictionary<uint, string> actionNames = [];
    private DateTime quietUntil = DateTime.MinValue;

    public bool IsPaused { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public IReadOnlyList<KtgEnemy> Enemies { get; private set; } = [];

    public void Update(Configuration configuration, KtgFrame frame)
    {
        if (!frame.InKeyboundBrawler)
        {
            Enemies = [];
            IsPaused = false;
            Reason = string.Empty;
            quietUntil = DateTime.MinValue;
            return;
        }

        var now = DateTime.UtcNow;
        var afterCast = TimeSpan.FromSeconds(configuration.PauseTrailSeconds);

        // Typing during an attack disables input for a few seconds; any key sent before it ends is wasted.
        var reason = frame.InputLockout is { } secondsLeft ? $"inputs disabled ({secondsLeft} s)" : null;
        var statusQuiet = reason != null;
        if (Plugin.ObjectTable.LocalPlayer is { } player)
        {
            foreach (var status in player.StatusList)
            {
                if (status.StatusId == LetterlockedStatus)
                    reason ??= $"inputs disabled ({status.RemainingTime:0} s)";
                else if (configuration.PauseOnEnemyCasts && DangerStatuses.TryGetValue(status.StatusId, out var attack))
                    reason ??= $"{attack} ({status.RemainingTime:0.0} s left)";
                else
                    continue;

                statusQuiet = true;
            }
        }

        var castQuiet = false;
        var extraPunishing = configuration.ExtraQuietCasts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var playerId = Plugin.ObjectTable.LocalPlayer?.GameObjectId;
        var enemies = new List<KtgEnemy>();
        foreach (var gameObject in Plugin.ObjectTable)
        {
            if (gameObject is not IBattleChara character || gameObject.ObjectKind == ObjectKind.Pc)
                continue;

            var castName = character.IsCasting ? ActionName(character.CastActionId) : string.Empty;
            var punishes = character.IsCasting &&
                           (configuration.PauseOnAllEnemyCasts || PunishingCasts.ContainsKey(character.CastActionId) ||
                            extraPunishing.Contains(castName, StringComparer.OrdinalIgnoreCase) ||
                            extraPunishing.Contains(character.CastActionId.ToString()));
            var target = character.CastTargetObjectId switch
            {
                NoTarget or 0 => "none",
                var id when id == playerId => "you",
                var id when id == gameObject.GameObjectId => "itself",
                _ => "other",
            };
            enemies.Add(new KtgEnemy(
                gameObject.GameObjectId, gameObject.Name.TextValue, gameObject.BaseId, gameObject.ObjectKind,
                character.IsCasting, character.CastActionId, castName, character.CurrentCastTime, character.TotalCastTime,
                character.IsCastInterruptible, target, punishes));

            var remaining = character.TotalCastTime - character.CurrentCastTime;
            if (configuration.PauseOnEnemyCasts && punishes && remaining <= configuration.PauseLeadSeconds)
            {
                reason ??= $"{castName} ({Math.Max(0, remaining):0.0} s left)";
                castQuiet = true;
            }
        }

        Enemies = enemies;

        if (reason == null && configuration.PauseWhileEnemyCastBarVisible &&
            frame.Addons.Any(a => a.Visible && a.Name == KtgUi.EnemyCastBarAddon))
        {
            reason = "the enemy cast bar is showing";
            castQuiet = true;
        }

        if (reason == null)
        {
            var extraAddons = configuration.ExtraPauseAddons.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var visible = frame.Addons.FirstOrDefault(a => a.Visible && extraAddons.Contains(a.Name, StringComparer.OrdinalIgnoreCase));
            if (visible != null)
            {
                reason = $"{visible.Name} is showing";
                castQuiet = true;
            }
        }

        // After a cast, stay quiet long enough for its status to show up; after a status, only a moment more.
        if (castQuiet && now + afterCast > quietUntil)
            quietUntil = now + afterCast;
        if (statusQuiet && now + AfterStatus > quietUntil)
            quietUntil = now + AfterStatus;
        if (reason != null)
            Reason = reason;

        IsPaused = reason != null || now < quietUntil;
        if (!IsPaused)
            Reason = string.Empty;
    }

    // The attack's name from the game's own data, in the client's language.
    private string ActionName(uint actionId)
    {
        if (actionNames.TryGetValue(actionId, out var name))
            return name;

        name = Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var row) ? row.Name.ExtractText() : string.Empty;
        if (name.Length == 0)
            name = PunishingCasts.GetValueOrDefault(actionId, $"action {actionId}");
        actionNames[actionId] = name;
        return name;
    }
}
