# Splash Addon Plus

A splash screen for [Playnite](https://playnite.link/) that covers the screen with the game's background and logo while the game is loading, and gets out of the way on its own once the game is actually on screen.

Splash Addon Plus is a fork of **[Splash Addon](https://github.com/artzox/Playnite-Splash-Addon) by Artzox**. The splash screen itself, its look, the per-platform timers and the per-game settings come from the original add-on. This fork adds automatic detection of the moment the game is ready, plus a few conveniences listed below. All credit for the original idea and code goes to Artzox.

## What is new compared to Splash Addon 1.0.2

### Automatic fullscreen detection

Instead of guessing how many seconds a game needs, the add-on watches for a new window that covers a whole monitor (exclusive fullscreen or a borderless window). Once that window has stayed fullscreen for a moment, the splash screen fades out and the game gets keyboard focus.

- Windows that were already fullscreen before the game started (another app, an overlay) are ignored.
- **Windowed games** are detected too. When Playnite knows the game's installation folder, a window of a program from that folder counts as the game once it covers at least 20% of the screen. A fullscreen window of a program from outside that folder (often a launcher) has to last 3 seconds longer.
- **Programs whose windows are never the game**, such as a launcher's own loading screen, can be listed in the settings. Their windows are never taken for the game, and while one of them fills the screen the splash screen keeps waiting. The list starts with `gamingservicesui` and can be edited freely.
- Timing options: minimum display time, delay after detection and maximum waiting time (after which the splash screen always closes).

Games that need special treatment can be given a fixed time, and the original timer-based behaviour is still available when detection is turned off.

### Appearance

- **Logo:** six positions (corners, bottom center, center) and a size from 0% (no logo) to 90% of the screen width, so it looks the same on 1080p and 4K.
- **Background:** optional dimming, a vignette and a slow zoom.
- **Missing background:** games without a background image use their cover instead, blurred across the screen. When there is no logo either, the cover is also shown in the middle.
- **Loading:** images are loaded in the background and decoded at the screen's resolution, so starting a game never waits for them and large images take less memory.

### Closing the splash screen by hand

Press **Esc**, click the mouse or press **B** on a gamepad (XInput controllers). Input arriving in the first moment after the splash appears is ignored, so the click or button press that launched the game cannot close it by accident. The splash screen also closes by itself when the game exits or its startup is cancelled.

### Game context menu

Right-click one or more games and open **Splash Addon Plus** to:

- turn the splash screen off or back on for those games,
- set or remove a custom display time,
- preview the splash screen.

No more copying game IDs by hand: the settings page lists these games by name, with a button to remove each one and, for fixed-time games, a field for the seconds.

### Other changes

- **Cancel** in the settings dialog now really discards changes, and **Save** stores them immediately.
- The platform list also contains the platforms from your own library.
- Per-platform times are a list you can extend, instead of ten fixed slots. The fixed-time options live in a collapsible **Advanced** section, since automatic detection makes them unnecessary for most people.
- Optional diagnostic logging to `extensions.log` (off by default). This is useful when a game behaves unexpectedly.
- On first start, settings of the original Splash Addon are imported automatically.

## Installation

1. Download the latest `.pext` file from [Releases](../../releases).
2. Double-click it, or drag it onto the Playnite window, and confirm the installation.
3. Restart Playnite.

If the original Splash Addon is installed, uninstall it, otherwise two splash screens will appear. Your settings are imported on the first start of Splash Addon Plus.

## Limitations

- **Pop-ups during startup stay hidden.** The splash screen covers the whole screen and stays on top. If something shows a window while the game is starting, such as an update prompt, a login request, a missing runtime installer (.NET, DirectX, Visual C++) or an error message, it ends up under the splash screen, which keeps waiting. Close the splash screen with **Esc**, a mouse click or **B** on a gamepad, or wait for the maximum waiting time. If this happens every time with one game, turn the splash screen off for it or give it a fixed time.
- **Games without a large window** (a small window, or only a launcher that stays open) are not detected. The splash screen closes after the maximum waiting time or by hand.
- **Programs that fill the screen before the game** can close the splash screen too early. Add their process name to *Programs whose windows are never the game*.
- **Several monitors:** the splash screen opens on the monitor Windows chooses for new windows, usually the primary one.
- **Gamepad:** only XInput controllers. PlayStation controllers work through a tool such as DS4Windows.
- **Logos** come from the Extra Metadata Loader add-on. Games without a downloaded logo are shown without one.

## Troubleshooting

If the splash screen closes too early, too late, or not at all for a particular game:

1. Turn on **Diagnostics → Write details about what the add-on does to the log file** in the add-on settings.
2. Start the game.
3. Open `%AppData%\Playnite\extensions.log` (for a portable installation, the file is in the Playnite folder) and search for `splash`. The log shows which windows appeared while the game was loading and why the splash screen closed.

Turn the option off again afterwards. Feel free to attach these lines to an issue.

## Building

The project is an SDK-style C# project targeting .NET Framework 4.8. Playnite SDK and the reference assemblies come from NuGet, so nothing has to be installed besides the .NET SDK. The settings page is built in code (no XAML), so the project also builds on Linux and macOS.

```
dotnet build SplashAddonPlus.csproj -c Release
```

The output is `bin/Release/SplashAddonPlus.dll`. A `.pext` package is a ZIP archive containing `SplashAddonPlus.dll`, `extension.yaml` and `icon.png`.

The tests cover everything that does not need Windows (settings and their migration, detection rules, image sizes, the gamepad button logic). They run on .NET 8 against the built DLL:

```
dotnet run --project Tests -c Release
```

GitHub Actions builds the add-on, runs the tests and creates the `.pext` package on every push. Pushing a version tag (for example `1.0.0`) publishes a release with the package attached.

## License and credits

- Original add-on: **Splash Addon** by [Artzox](https://github.com/artzox).
- Splash Addon Plus: **Ad1Bb**.

The upstream repository does not include a license file. This fork keeps the original history and clearly credits the original author. If you are the author of the original add-on and would like anything changed, please open an issue.
