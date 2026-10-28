using System;

namespace Typemaxxer.Automation;

/// <summary>The speed presets users choose from. "Reasonably fast" and "Ludicrous" will join Human-like later.</summary>
public enum SpeedMode
{
    HumanLike = 0,
}

/// <summary>
/// How a preset types: an average speed and pause between prompts, plus the irregularities that make it look like a
/// person. The gaps inside a prompt are multiples of the time per key, so they shrink as the speed goes up.
/// </summary>
public sealed record SpeedProfile(
    string Name,
    string Description,
    float CharsPerSecond,
    float PromptSpeedVariation,
    float KeyJitter,
    float BreakGapMin,
    float BreakGapMax,
    float HesitationChance,
    float HesitationMin,
    float HesitationMax,
    float PromptPauseSeconds,
    float PromptPauseVariation)
{
    public static readonly SpeedProfile HumanLike = new(
        Name: "Human-like",
        Description: "Natural, uneven timing: the pace drifts from prompt to prompt, slows a little after spaces and hesitates now and then.",
        CharsPerSecond: Configuration.DefaultTypingSpeed,
        PromptSpeedVariation: 0.15f,
        KeyJitter: 0.30f,
        BreakGapMin: 0.25f,
        BreakGapMax: 1.0f,
        HesitationChance: 0.04f,
        HesitationMin: 1.25f,
        HesitationMax: 3.0f,
        PromptPauseSeconds: Configuration.DefaultPromptPauseSeconds,
        PromptPauseVariation: 0.33f);

    /// <summary>The preset the player picked, with their speed and pause sliders applied (kept within the sliders' range).</summary>
    public static SpeedProfile For(Configuration configuration) => configuration.SpeedMode switch
    {
        _ => HumanLike with
        {
            CharsPerSecond = Math.Clamp(configuration.TypingSpeed, Configuration.MinTypingSpeed, Configuration.MaxTypingSpeed),
            PromptPauseSeconds = Math.Clamp(configuration.PromptPauseSeconds, 0f, Configuration.MaxPromptPauseSeconds),
        },
    };
}

/// <summary>Produces the waits between keys and between prompts.</summary>
public sealed class KeyTiming
{
    // How the pause between prompts is split: a short beat before pressing Enter, the rest before the next prompt.
    private const float SubmitShare = 0.3f;

    private readonly Random random = new();
    private SpeedProfile profile = SpeedProfile.HumanLike;
    private float keyInterval = 1f / SpeedProfile.HumanLike.CharsPerSecond;

    /// <summary>Starts a new prompt. Each prompt gets a slightly different pace, like a person's would.</summary>
    public void StartPrompt(SpeedProfile speedProfile)
    {
        profile = speedProfile;
        keyInterval = 1f / (Math.Max(0.5f, profile.CharsPerSecond) * (1 + Spread(profile.PromptSpeedVariation)));
    }

    /// <summary>The pause before starting a prompt, or before resuming after an interruption.</summary>
    public TimeSpan ReactionDelay() => PromptPause(1 - SubmitShare);

    /// <summary>The beat between finishing a prompt and pressing Enter (when Typemaxxer presses it).</summary>
    public TimeSpan SubmitDelay() => PromptPause(SubmitShare);

    /// <summary>The short wait between opening the game's input box and the first key.</summary>
    public TimeSpan OpenDelay() => Seconds(keyInterval * 0.4f * (1 + Spread(profile.KeyJitter)));

    public TimeSpan DelayBetween(char previous, char next)
    {
        var seconds = keyInterval * (1 + Spread(profile.KeyJitter));

        // People slow down a little after a word or a punctuation mark...
        if (char.IsWhiteSpace(previous) || char.IsPunctuation(previous))
            seconds += keyInterval * Between(profile.BreakGapMin, profile.BreakGapMax);

        // ...and now and then hesitate.
        if (random.NextSingle() < profile.HesitationChance)
            seconds += keyInterval * Between(profile.HesitationMin, profile.HesitationMax);

        return Seconds(seconds);
    }

    private TimeSpan PromptPause(float share) =>
        Seconds(profile.PromptPauseSeconds * share * (1 + Spread(profile.PromptPauseVariation)));

    private float Spread(float amount) => ((random.NextSingle() * 2) - 1) * amount;

    private float Between(float min, float max) => min + (random.NextSingle() * (max - min));

    private static TimeSpan Seconds(float seconds) => TimeSpan.FromSeconds(Math.Max(0f, seconds));
}
