using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Typemaxxer.Game;

/// <summary>
/// The game's inactivity timers, for diagnostics. Keybound Brawler doesn't go by these: it expels players who type no
/// letters with the keyboard for about a minute and a half, which is why Typemaxxer types with real key presses.
/// </summary>
public static unsafe class GameActivity
{
    /// <summary>All of the game's inactivity timers, for the session log.</summary>
    public static string Describe()
    {
        var timers = InputTimerModule.Instance();
        if (timers == null)
            return "(input timers unavailable)";

        // Private in FFXIVClientStructs ("used in a couple different ways"), read by its offset.
        var unknown1C = *(float*)((byte*)timers + 0x1C);
        return $"afk={timers->AfkTimer:0.0} content={timers->ContentInputTimer:0.0} input={timers->InputTimer:0.0} " +
               $"unk1C={unknown1C:0.0} status={timers->Status} contentLimit={timers->InstanceContentAfkTimeLimit:0} " +
               $"afkLimit={timers->AfkTimeLimit:0} autoAfkLimit={timers->AutoAfkTimeLimit:0}";
    }

    /// <summary>Seconds since the game last saw input in a duty, and how long it allows before expelling you.</summary>
    public static (float Seconds, float Limit)? DutyInactivity
    {
        get
        {
            var timers = InputTimerModule.Instance();
            return timers == null ? null : (timers->ContentInputTimer, timers->InstanceContentAfkTimeLimit);
        }
    }
}
