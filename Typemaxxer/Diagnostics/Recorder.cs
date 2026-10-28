using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Typemaxxer.Game;
using Typemaxxer.Input;

namespace Typemaxxer.Diagnostics;

/// <summary>
/// Opt-in session log used to learn how Keybound Brawler behaves: what its windows show, when enemies cast, and which
/// keys Typemaxxer sent. It writes one file per match into the plugin's config folder and never leaves this PC.
/// </summary>
public sealed unsafe class Recorder : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BarSampleInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan TimersInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, string> lastUiState = [];
    private readonly Dictionary<string, string> lastBars = [];
    private readonly Dictionary<ulong, string> lastEnemyState = [];
    private StreamWriter? writer;
    private DateTime startedAt;
    private DateTime lastFlush;
    private DateTime lastBarSample;
    private DateTime lastTimers;
    private string lastFocus = string.Empty;
    private string lastWords = string.Empty;
    private bool lastEnterDown;
    private bool lastPaused;

    public static string Folder => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "sessions");

    public bool IsRecording => writer != null;
    public string? CurrentFile { get; private set; }

    public void Start(Plugin plugin)
    {
        if (writer != null)
            return;

        try
        {
            Directory.CreateDirectory(Folder);
            CurrentFile = Path.Combine(Folder, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            writer = new StreamWriter(CurrentFile, false, new UTF8Encoding(false));
            startedAt = lastFlush = DateTime.UtcNow;

            var configuration = plugin.Configuration;
            Write("SESSION", $"Typemaxxer {Plugin.PluginInterface.Manifest.AssemblyVersion}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Write("SESSION", $"language={Plugin.ClientState.ClientLanguage} territory={Plugin.ClientState.TerritoryType} " +
                             $"intendedUse={KtgUi.CurrentTerritoryIntendedUse} prompts={plugin.Dictionary.PromptCount}");
            Write("SESSION", $"input={configuration.InputBackend} speed={configuration.SpeedMode} autoEnter={configuration.AutoPressEnter} " +
                             $"pauseLead={configuration.PauseLeadSeconds} pauseTrail={configuration.PauseTrailSeconds} " +
                             $"enemyCastPause={configuration.PauseOnEnemyCasts} castBarPause={configuration.PauseWhileEnemyCastBarVisible} " +
                             $"extraPause='{configuration.ExtraPauseAddons}'");
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "Could not start the session log.");
            writer?.Dispose();
            writer = null;
        }
    }

    public void Stop()
    {
        if (writer == null)
            return;

        Write("SESSION", "ended");
        writer.Dispose();
        writer = null;

        lastUiState.Clear();
        lastBars.Clear();
        lastEnemyState.Clear();
        lastFocus = string.Empty;
        lastWords = string.Empty;
        lastEnterDown = false;
        lastPaused = false;
    }

    public void Dispose() => Stop();

    public void Write(string kind, string message)
    {
        writer?.WriteLine($"{(DateTime.UtcNow - startedAt).TotalSeconds,9:0.000} {kind,-7} {message}");
    }

    /// <summary>Marks a moment the player flagged, with a snapshot of every enemy cast and everything on screen.</summary>
    public void Mark(Plugin plugin, string note)
    {
        if (writer == null)
            return;

        Write("MARK", note.Length > 0 ? note : "(marked)");
        Write("MARK", $"  typist {plugin.Typist.State}: {plugin.Typist.Status}; paused={plugin.PauseDetector.IsPaused} {plugin.PauseDetector.Reason}");
        foreach (var enemy in plugin.PauseDetector.Enemies)
        {
            Write("MARK", $"  enemy {enemy.Name} [{enemy.Kind} base {enemy.BaseId} id {enemy.ObjectId:X}] " +
                          (enemy.IsCasting ? $"casting action {enemy.CastActionId} {enemy.CastTime:0.00}/{enemy.TotalCastTime:0.00} s" : "idle"));
        }

        foreach (var addon in plugin.Frame.Addons.Where(a => a.Visible))
        {
            var texts = string.Join(" | ", addon.TextNodes.Where(n => n.Visible && n.Text.Length > 0).Select(n => $"n{n.Path} \"{n.Text}\""));
            var bars = BarSummary(addon);
            Write("MARK", $"  window {addon.Name}#{addon.Id}: {texts}{(bars.Length > 0 ? $" bars: {bars}" : string.Empty)}");
        }

        writer.Flush();
    }

    /// <summary>Logs whatever changed since the last frame.</summary>
    public void Observe(Plugin plugin)
    {
        if (writer == null)
            return;

        var frame = plugin.Frame;
        ObserveUi(frame);
        ObserveEnemies(plugin.PauseDetector);

        var words = string.Join(", ", frame.Words.Select(w => $"\"{w.Text}\"#{w.RowId}"));
        if (words != lastWords)
        {
            lastWords = words;
            Write("WORDS", words.Length > 0 ? words : "(none)");
        }

        var focus = $"textInput={frame.TextInputActive} focused={frame.FocusedAddon ?? "-"} " +
                    $"dalamudUiKeyboard={plugin.ImGuiWantsKeyboard} gameWindowFocused={frame.GameWindowFocused}";
        if (focus != lastFocus)
        {
            lastFocus = focus;
            Write("FOCUS", focus);
        }

        if (frame.EnterDown != lastEnterDown)
        {
            lastEnterDown = frame.EnterDown;
            if (frame.EnterDown)
                Write("ENTER", "Enter key down");
        }

        if (plugin.PauseDetector.IsPaused != lastPaused)
        {
            lastPaused = plugin.PauseDetector.IsPaused;
            Write("PAUSE", lastPaused ? $"start: {plugin.PauseDetector.Reason}" : "end");
        }

        var now = DateTime.UtcNow;

        // Which of the game's inactivity checks is closing in (it expels players it thinks are idle).
        if (now - lastTimers >= TimersInterval)
        {
            lastTimers = now;
            Write("TIMERS", $"{GameActivity.Describe()} windowsIdle={GameWindowInput.SystemIdleTime().TotalSeconds:0.0}");
        }

        if (now - lastFlush > FlushInterval)
        {
            lastFlush = now;
            writer.Flush();
        }
    }

    // System messages only (other players' chat is never recorded), plus anything about inactivity.
    public void OnChatMessage(IHandleableChatMessage message)
    {
        if (writer == null)
            return;

        var text = message.Message.TextValue;
        if (message.LogKind is XivChatType.SystemMessage or XivChatType.ErrorMessage or XivChatType.Notice ||
            text.Contains("inactiv", StringComparison.OrdinalIgnoreCase))
            Write("CHAT", $"{message.LogKind}: {text}");
    }

    public void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        if (writer == null)
            return;

        var details = string.Empty;
        if (args is AddonReceiveEventArgs receive)
        {
            details = $" event={receive.AtkEventType} param={receive.EventParam}";
            if (receive.AtkEventData != 0)
                details += $" data={Convert.ToHexString(new ReadOnlySpan<byte>((void*)receive.AtkEventData, 32))}";
        }

        Write("ADDON", $"{type} {args.AddonName}{details}");
    }

    private void ObserveUi(KtgFrame frame)
    {
        var seen = new HashSet<string>();
        foreach (var addon in frame.Addons)
        {
            var addonKey = $"{addon.Name}#{addon.Id}";
            Diff(seen, addonKey, addon.Visible ? "visible" : "hidden",
                 $"at ({addon.X},{addon.Y}) scale {addon.Scale:0.##}, {addon.TextNodes.Count} text nodes");

            foreach (var node in addon.TextNodes)
            {
                var state = node.Visible
                    ? $"\"{node.Text}\" color={node.Color:X8}{(node.RawHex != null ? $" raw={node.RawHex}" : string.Empty)}"
                    : "hidden";
                Diff(seen, $"{addonKey} n{node.Path}", state,
                     node.Visible ? $"at ({node.X:0},{node.Y:0}) size {node.Width}x{node.Height}" : string.Empty);
            }

            foreach (var shape in addon.ShapeNodes)
            {
                Diff(seen, $"{addonKey} s{shape.Path}", shape.Visible ? $"type {shape.Type} shown" : "hidden",
                     shape.Visible ? $"at ({shape.X:0},{shape.Y:0}) size {shape.DrawnWidth:0}x{shape.Height} alpha {shape.Alpha}" : string.Empty);
            }
        }

        foreach (var key in lastUiState.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            lastUiState.Remove(key);
            Write("UI", $"{key} gone");
        }

        // Bars fill gradually, so sample their widths a couple of times a second instead of logging every frame.
        var now = DateTime.UtcNow;
        if (now - lastBarSample < BarSampleInterval)
            return;

        lastBarSample = now;
        foreach (var addon in frame.Addons.Where(a => a.Visible && a.ShapeNodes.Count > 0))
        {
            var key = $"{addon.Name}#{addon.Id}";
            var bars = BarSummary(addon);
            if (lastBars.TryGetValue(key, out var previous) && previous == bars)
                continue;

            lastBars[key] = bars;
            Write("BAR", $"{key} {bars}");
        }
    }

    private static string BarSummary(KtgAddon addon) =>
        string.Join(" ", addon.ShapeNodes.Where(s => s.Visible).Select(s => $"s{s.Path}={s.DrawnWidth:0}"));

    // Positions are logged alongside a change but don't count as one, or every moving enemy would flood the log.
    private void Diff(HashSet<string> seen, string key, string state, string details)
    {
        seen.Add(key);
        if (lastUiState.TryGetValue(key, out var previous) && previous == state)
            return;

        lastUiState[key] = state;
        Write("UI", $"{key} {state} {details}".TrimEnd());
    }

    private void ObserveEnemies(PauseDetector pause)
    {
        var seen = new HashSet<ulong>();
        foreach (var enemy in pause.Enemies)
        {
            seen.Add(enemy.ObjectId);
            var state = enemy.IsCasting ? $"casting action {enemy.CastActionId} ({enemy.TotalCastTime:0.00}s)" : "idle";
            var known = lastEnemyState.TryGetValue(enemy.ObjectId, out var previous);
            if (known && previous == state)
                continue;

            lastEnemyState[enemy.ObjectId] = state;
            Write(known ? "CAST" : "ENEMY", $"{enemy.Name} [{enemy.Kind} base {enemy.BaseId} id {enemy.ObjectId:X}] {state}");
        }

        foreach (var id in lastEnemyState.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            lastEnemyState.Remove(id);
            Write("ENEMY", $"id {id:X} gone");
        }
    }
}
