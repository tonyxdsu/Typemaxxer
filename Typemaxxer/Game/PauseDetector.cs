using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;

namespace Typemaxxer.Game;

/// <summary>A non-player character in the match, with its current cast.</summary>
public sealed record KtgEnemy(
    ulong ObjectId, string Name, uint BaseId, ObjectKind Kind, bool IsCasting, uint CastActionId, float CastTime, float TotalCastTime);

/// <summary>
/// Decides when to go quiet. Typing near the end of an enemy's special attack is punished, so Typemaxxer stays silent
/// from <see cref="Configuration.PauseLeadSeconds"/> before an enemy cast finishes until
/// <see cref="Configuration.PauseTrailSeconds"/> after it.
/// </summary>
public sealed class PauseDetector
{
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

        // Typing during an attack disables input for a few seconds; any key sent before it ends is wasted.
        var reason = frame.InputLockout is { } secondsLeft ? $"inputs disabled ({secondsLeft} s)" : null;
        var enemies = new List<KtgEnemy>();
        foreach (var gameObject in Plugin.ObjectTable)
        {
            if (gameObject is not IBattleChara character || gameObject.ObjectKind == ObjectKind.Pc)
                continue;

            var name = gameObject.Name.TextValue;
            enemies.Add(new KtgEnemy(
                gameObject.GameObjectId, name, gameObject.BaseId, gameObject.ObjectKind,
                character.IsCasting, character.CastActionId, character.CurrentCastTime, character.TotalCastTime));

            var remaining = character.TotalCastTime - character.CurrentCastTime;
            if (configuration.PauseOnEnemyCasts && character.IsCasting && remaining <= configuration.PauseLeadSeconds)
                reason ??= $"{(name.Length > 0 ? name : "an enemy")} is attacking ({Math.Max(0, remaining):0.0} s left)";
        }

        Enemies = enemies;

        if (reason == null && configuration.PauseWhileEnemyCastBarVisible &&
            frame.Addons.Any(a => a.Visible && a.Name == KtgUi.EnemyCastBarAddon))
            reason = "the enemy cast bar is showing";

        if (reason == null)
        {
            var extraAddons = configuration.ExtraPauseAddons.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var visible = frame.Addons.FirstOrDefault(a => a.Visible && extraAddons.Contains(a.Name, StringComparer.OrdinalIgnoreCase));
            if (visible != null)
                reason = $"{visible.Name} is showing";
        }

        var now = DateTime.UtcNow;
        if (reason != null)
        {
            quietUntil = now + TimeSpan.FromSeconds(configuration.PauseTrailSeconds);
            Reason = reason;
        }

        IsPaused = reason != null || now < quietUntil;
        if (!IsPaused)
            Reason = string.Empty;
    }
}
