using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Typemaxxer.Automation;
using Typemaxxer.Diagnostics;
using Typemaxxer.Game;
using Typemaxxer.Input;
using Typemaxxer.Windows;

namespace Typemaxxer;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/tmx";

    // The mini-game's windows can disappear briefly (between waves, during animations),
    // so only count the match as over once they have been gone for this long.
    private static readonly TimeSpan LeaveGrace = TimeSpan.FromSeconds(3);

    // Refresh and update events are left out: the mini-game's windows fire them every frame.
    private static readonly AddonEvent[] RecordedAddonEvents =
    [
        AddonEvent.PostSetup, AddonEvent.PreFinalize, AddonEvent.PostShow, AddonEvent.PostHide,
        AddonEvent.PostReceiveEvent, AddonEvent.PostFocusChanged,
    ];

    public Configuration Configuration { get; init; }
    public WordDictionary Dictionary { get; init; }
    public PauseDetector PauseDetector { get; } = new();
    public Recorder Recorder { get; } = new();
    public Typist Typist { get; init; }

    // Everything read from the game this frame. Replaced (never modified) once per frame.
    public KtgFrame Frame { get; private set; } = KtgFrame.Empty;

    // Whether Dalamud's own UI currently takes keyboard input. When it does, Dalamud keeps keys away from the game.
    public bool ImGuiWantsKeyboard { get; private set; }

    public readonly WindowSystem WindowSystem = new("Typemaxxer");
    private MainWindow MainWindow { get; init; }
    private DebugWindow DebugWindow { get; init; }
    private StatusOverlay StatusOverlay { get; init; }

    private readonly WordReader wordReader;
    private readonly IAddonLifecycle.AddonEventDelegate addonEventHandler;
    private DateTime lastSeenKeyboundBrawler = DateTime.MinValue;
    private bool wasInKeyboundBrawler;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Migrate();
        Dictionary = new WordDictionary();
        wordReader = new WordReader(Dictionary);
        Typist = new Typist(this);

        MainWindow = new MainWindow(this);
        DebugWindow = new DebugWindow(this);
        StatusOverlay = new StatusOverlay(this);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(DebugWindow);
        WindowSystem.AddWindow(StatusOverlay);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Typemaxxer. \"/tmx on\" starts typing, \"/tmx off\" stops it."
        });

        addonEventHandler = Recorder.OnAddonEvent;
        foreach (var addonEvent in RecordedAddonEvents)
            AddonLifecycle.RegisterListener(addonEvent, KtgUi.AddonNames, addonEventHandler);

        Framework.Update += OnFrameworkUpdate;
        ChatGui.ChatMessage += Recorder.OnChatMessage;
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        if (!Configuration.HasSeenIntro)
            MainWindow.IsOpen = true;

        Log.Information($"Loaded {Dictionary.PromptCount} Keybound Brawler prompts ({Dictionary.RowCount} rows).");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ChatGui.ChatMessage -= Recorder.OnChatMessage;
        GameWindowInput.ReleaseHeldKeys(all: true);
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        foreach (var addonEvent in RecordedAddonEvents)
            AddonLifecycle.UnregisterListener(addonEvent, KtgUi.AddonNames, addonEventHandler);

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        DebugWindow.Dispose();
        StatusOverlay.Dispose();

        Recorder.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleDebugUi() => DebugWindow.Toggle();

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            GameWindowInput.ReleaseHeldKeys();

            var now = DateTime.UtcNow;
            var addons = KtgUi.ReadAddons();
            if (KtgUi.IsInKeyboundBrawlerTerritory || addons.Any(a => a.Visible && KtgUi.IsGameplayAddon(a.Name)))
                lastSeenKeyboundBrawler = now;

            var inKeyboundBrawler = now - lastSeenKeyboundBrawler < LeaveGrace;
            var focusedAddon = KtgUi.FocusedAddonName();
            Frame = new KtgFrame(
                inKeyboundBrawler,
                addons,
                wordReader.Read(addons),
                KtgUi.IsTextInputActive(),
                focusedAddon,
                focusedAddon == KtgUi.InputAddon,
                KtgUi.ReadInputText(addons),
                KtgUi.ReadInputLockout(addons),
                KeyState[VirtualKey.RETURN],
                GameWindowInput.IsForeground());

            if (inKeyboundBrawler != wasInKeyboundBrawler)
            {
                wasInKeyboundBrawler = inKeyboundBrawler;
                OnKeyboundBrawlerChanged(inKeyboundBrawler);
            }

            PauseDetector.Update(Configuration, Frame);
            Typist.Update(Frame);
            DebugWindow.Tick(Frame);
            Recorder.Observe(this);
        }
        catch (Exception e)
        {
            Log.Error(e, "Typemaxxer hit an error and stopped typing.");
            if (Typist.Enabled)
                Typist.SetEnabled(false, "Typemaxxer hit an error and stopped to be safe. Details are in /xllog.");
        }
    }

    private void OnKeyboundBrawlerChanged(bool inKeyboundBrawler)
    {
        if (inKeyboundBrawler)
        {
            if (Configuration.RecordSessions)
                Recorder.Start(this);
            Recorder.Write("MATCH", "entered Keybound Brawler");
            return;
        }

        Recorder.Write("MATCH", "left Keybound Brawler");
        Recorder.Stop();
        if (Typist.Enabled)
            Typist.SetEnabled(false, "You left Keybound Brawler, so Typemaxxer switched itself off.");
    }

    private void OnDraw()
    {
        var io = ImGui.GetIO();
        ImGuiWantsKeyboard = io.WantCaptureKeyboard || io.WantTextInput;
        WindowSystem.Draw();
    }

    private void OnCommand(string command, string args)
    {
        var arguments = args.Trim();
        switch (arguments.ToLowerInvariant())
        {
            case "":
                ToggleMainUi();
                break;
            case "on":
            case "start":
                Typist.SetEnabled(true, null);
                break;
            case "off":
            case "stop":
                Typist.SetEnabled(false, null);
                break;
            case "toggle":
                Typist.SetEnabled(!Typist.Enabled, null);
                break;
            case "dev":
            case "debug":
                if (Configuration.DeveloperMode)
                    ToggleDebugUi();
                else
                    ChatGui.Print("Turn on Developer mode under /tmx → Advanced first.", "Typemaxxer");
                break;
            default:
                if (arguments.StartsWith("mark", StringComparison.OrdinalIgnoreCase))
                {
                    Recorder.Mark(this, arguments[4..].Trim());
                    ChatGui.Print(Recorder.IsRecording ? "Marked this moment in the session log." : "Not recording right now.", "Typemaxxer");
                }
                else
                {
                    ChatGui.Print("Usage: /tmx (open), /tmx on, /tmx off", "Typemaxxer");
                }
                break;
        }
    }
}
