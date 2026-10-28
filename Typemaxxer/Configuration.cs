using System;
using Dalamud.Configuration;
using Typemaxxer.Automation;
using Typemaxxer.Input;

namespace Typemaxxer;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 2;
    public const float DefaultTypingSpeed = 10f;
    public const float MinTypingSpeed = 3f;
    public const float MaxTypingSpeed = 16f;
    public const float DefaultPromptPauseSeconds = 0.2f;
    public const float MaxPromptPauseSeconds = 1f;
    public const float DefaultPauseLeadSeconds = 2.5f;
    public const float DefaultPauseTrailSeconds = 1.5f;

    public int Version { get; set; } = CurrentVersion;

    // Everyday settings.
    public SpeedMode SpeedMode { get; set; } = SpeedMode.HumanLike;
    public float TypingSpeed { get; set; } = DefaultTypingSpeed;
    public float PromptPauseSeconds { get; set; } = DefaultPromptPauseSeconds;
    public bool AutoPressEnter { get; set; } = false;
    public bool ShowStatusOverlay { get; set; } = true;
    public bool HasSeenIntro { get; set; } = false;

    // Advanced: how long before an enemy attack lands to go quiet, and how long to stay quiet afterwards.
    public float PauseLeadSeconds { get; set; } = DefaultPauseLeadSeconds;
    public float PauseTrailSeconds { get; set; } = DefaultPauseTrailSeconds;

    // Developer mode.
    public bool DeveloperMode { get; set; } = false;
    public InputBackend InputBackend { get; set; } = InputBackend.WindowMessageChar;
    public bool PauseOnEnemyCasts { get; set; } = true;
    public bool PauseWhileEnemyCastBarVisible { get; set; } = false;
    public string ExtraPauseAddons { get; set; } = string.Empty;
    public bool RecordSessions { get; set; } = false;

    /// <summary>Brings settings saved by an older version up to date.</summary>
    public void Migrate()
    {
        if (Version >= CurrentVersion)
            return;

        // Version 0 went quiet whenever the enemy cast bar window was up, which turned out to be the whole match.
        if (Version < 1)
            PauseWhileEnemyCastBarVisible = false;

        // Version 1 resumed 0.5 s after an attack, which is too soon: a key 0.67 s after the cast ended got inputs disabled.
        if (Version < 2 && PauseTrailSeconds == 0.5f)
            PauseTrailSeconds = DefaultPauseTrailSeconds;

        Version = CurrentVersion;
        Save();
    }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
