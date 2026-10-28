# Typemaxxer

Typemaxxer is a Dalamud plugin for Final Fantasy XIV. It types the prompts in **Keybound Brawler**, the Gold Saucer's typing mini-game, for you, at the speed you choose.

It reads the prompt above each enemy, types it into the mini-game's input box with natural, uneven timing, and presses Enter to attack (or leaves that to you, if you prefer).

## Install

1. In game, type `/xlsettings`, open **Experimental**, and under **Custom Plugin Repositories** paste this URL, click **+**, then **Save**:

   ```
   https://tonyxdsu.github.io/Typemaxxer/repo.json
   ```

2. Type `/xlplugins`, search for **Typemaxxer** and click **Install**.
3. Type `/tmx` to open it.

Updates arrive through the plugin installer like any other plugin.

## How to use it

1. Join a Keybound Brawler match at the Gold Saucer.
2. Press **Start Typing!** (or type `/tmx on`), then click anywhere on the game screen.
3. Typemaxxer types the prompt above an enemy and presses Enter to attack. To press Enter yourself, turn off **Press Enter for me**.

It switches itself off when you leave the match. `/tmx off` stops it at any time.

**Keep the game window in front.** Keybound Brawler expels players who don't type with the keyboard for about a minute. Typemaxxer types with real key presses, which only reach the game while its window is in front. If you switch to another window, Typemaxxer stops and shows a warning until you click the game again.

## What it does

- **Reads the prompts from the game itself.** Prompts come from the mini-game's own windows and the game's prompt list, with no screen capture or image recognition.
- **Types like a person.** The pace drifts from prompt to prompt, slows a little after spaces and hesitates now and then. It checks the input box shows what it typed before submitting.
- **Goes quiet for attacks that punish typing.** Cursed Gaze and Dynamic Sensory Jammer lock your input for 7 seconds if you type right after they land. Typemaxxer stops shortly before they land and waits until their effect wears off.
- **Keeps typing through other attacks**, so you can defeat the enemy before its attack goes off. It types barrier phrases and prompts of enemies casting an attack first.
- **Copes with obscured phrases.** When prompts blink in and out, it keeps typing the one it started.
- **Never types into the chat box**, and only ever acts inside Keybound Brawler.

## Settings

| Speed | Typing speed | Pause between prompts |
|---|---|---|
| Grandpa | 5 characters/s (~60 WPM) | 0.5 s |
| Gamer (default) | 10 characters/s (~120 WPM) | 0.3 s |
| Ludicrous Speed | 15 characters/s (~180 WPM) | 0.2 s |

- **Press Enter for me:** on by default. Turn it off to press Enter yourself after each prompt.
- **Show status during matches:** a small line at the top of the screen that says what Typemaxxer is doing.
- **Advanced:**
  - Fine-tune typing speed (up to 20 characters/s) and the pause between prompts.
  - How long to stay quiet before and after an attack that punishes typing.
  - **Send simulated keypresses upon focus loss**: keeps typing while the game is in the background. The game counts that as idle and expels you after about 90 s, so it's only for short alt-tabs.

## FAQ

**Can other players tell?**
They see your score. Grandpa and Gamer type within the range of a fast human typist; Ludicrous Speed is faster than almost anyone.

**What does it store or send?**
Only your settings, on your PC. Typemaxxer makes no network connections. Developer mode can record diagnostic logs of a match; recording is off unless you turn it on, and the logs stay on your PC.

**I was expelled for inactivity.**
The game window wasn't in front for too long; see "Keep the game window in front" above.

**Does it work with the Japanese client?**
Not yet: Japanese prompts are typed as kana, which Typemaxxer doesn't do.

**How do I check which version I have?**
It's in the title of the Typemaxxer window, e.g. "Typemaxxer v1.0".

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and XIVLauncher with Dalamud (run the game with Dalamud once).

```
dotnet build Typemaxxer.slnx
```

Then add the full path of `Typemaxxer/bin/x64/Debug/Typemaxxer.dll` under `/xlsettings` → **Experimental** → **Dev Plugin Locations**, and enable it under `/xlplugins` → **Dev Tools**. On Linux or WSL, point the `DALAMUD_HOME` environment variable at XIVLauncher's `addon/Hooks/dev` folder.

Releases are built by GitHub Actions when a `v*` tag is pushed. The workflow publishes `latest.zip` on the release and updates `repo.json`.

## License

[GNU Affero General Public License v3.0](LICENSE).
