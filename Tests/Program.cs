// Tests for the parts of Splash Addon Plus that do not need Windows: settings, migration of old settings,
// detection rules, image sizes and the gamepad button logic.
// Build the add-on first (dotnet build SplashAddonPlus.csproj -c Release), then: dotnet run --project Tests -c Release
// The exit code is the number of failed checks.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SplashAddonPlus;

int failures = 0;
void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }

// 1) Settings of the original Splash Addon 1.0.2 load; new fields get defaults.
string oldJson = @"{""ExcludedGameIds"":[""aaa""],""SplashScreenDuration"":12,""UsePlatformSpecificTimers"":true,
 ""GameSpecificDurations"":{""bbb"":30},""UseGameStartedTimer"":true,""LogoSize"":250,
 ""Platform1"":""PC (Windows)"",""Duration1"":7,""Platform2"":""Nintendo Switch"",""Duration2"":20,""Platform3"":""PC (Windows)"",""Duration3"":99}";
var s = JsonConvert.DeserializeObject<SplashAddonSettings>(oldJson);
s.Upgrade(); // the plugin does this right after loading
Check(s.SplashScreenDuration == 12 && s.LogoSize == 250 && s.UseGameStartedTimer, "old values kept");
Check(s.AutoDetectFullscreen && s.MinimumDisplaySeconds == 2 && s.DetectionDelaySeconds == 1 && s.MaximumWaitSeconds == 60, "new fields default");
Check(s.IsExcluded("aaa") && s.IsExcluded("AAA") && !s.IsExcluded("bbb"), "IsExcluded");

// 2) Duration priority + duplicate platform no longer throws.
Check(s.GetDurationForGame("bbb", "PC (Windows)") == 30, "per-game wins");
Check(s.GetDurationForGame("x", "PC (Windows)") == 7, "platform exact (first duplicate wins)");
Check(s.GetDurationForGame("x", "switch") == 20, "platform partial match");
Check(s.GetDurationForGame("x", "Sega Saturn") == 12, "fallback default");

// 3) Menu helpers.
s.SetExcluded(new[] { "ccc", "aaa" }, true);
Check(s.ExcludedGameIds.Count == 2 && s.IsExcluded("ccc"), "exclude adds without duplicates");
s.SetExcluded(new[] { "aaa" }, false);
Check(!s.IsExcluded("aaa") && s.IsExcluded("ccc"), "include removes");
var changed = new List<string>();
s.PropertyChanged += (o, e) => changed.Add(e.PropertyName);
s.SetGameSpecificDuration(new[] { "ddd" }, 5);
Check(s.HasGameSpecificDuration("ddd") && changed.Contains("GameSpecificDurationsText"), "set duration + text refresh");
s.SetGameSpecificDuration(new[] { "ddd" }, null);
Check(!s.HasGameSpecificDuration("ddd"), "remove duration");

// 4) Cancel restores, End keeps and requests save.
int saves = 0;
var t = new SplashAddonSettings();
typeof(SplashAddonSettings).GetProperty("SaveRequested", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
    .SetValue(t, (Action)(() => saves++));
t.BeginEdit();
t.AutoDetectFullscreen = false; t.MaximumWaitSeconds = 5; t.ExcludedGameIdsText = "x1\r\ny2"; t.GameSpecificDurationsText = "g:3";
t.CancelEdit();
Check(t.AutoDetectFullscreen && t.MaximumWaitSeconds == 60 && t.ExcludedGameIds.Count == 0 && t.GameSpecificDurations.Count == 0, "cancel restores");
t.BeginEdit(); t.MinimumDisplaySeconds = 4; t.EndEdit();
Check(t.MinimumDisplaySeconds == 4 && saves == 1, "end edit keeps + saves");

// 5) Serialization: text helpers not written, new fields written.
string json = JsonConvert.SerializeObject(t);
Check(!json.Contains("Text") && json.Contains("AutoDetectFullscreen") && !json.Contains("SaveRequested"), "json shape");

// 6) Validation.
t.MaximumWaitSeconds = 1; t.MinimumDisplaySeconds = 3;
Check(!t.VerifySettings(out var errs) && errs.Count == 1, "max < min rejected: " + string.Join(" | ", errs));
t.MaximumWaitSeconds = 60;
Check(t.VerifySettings(out _), "valid settings accepted");

// 7) Text format of per-game times, as in the original.
t.GameSpecificDurationsText = "a:5\nbad\nb: 0\nc:x\n d : 9 ";
Check(t.GameSpecificDurations.Count == 2 && t.GameSpecificDurations["d"] == 9, "duration text parsing");


// 8) Ignored processes.
var ip = new SplashAddonSettings();
Check(ip.IgnoredProcessNames.Count == 0, "ignored list empty before Upgrade");
ip.IgnoredProcessNamesText = " EADesktop.exe \r\nUbisoftConnect; eadesktop ,  \n\n";
Check(ip.IgnoredProcessNames.Count == 2 && ip.IgnoredProcessNames[0] == "EADesktop" && ip.IgnoredProcessNames[1] == "UbisoftConnect",
      "ignored text parsing: " + string.Join("|", ip.IgnoredProcessNames));
string ipJson = JsonConvert.SerializeObject(ip);
var reloaded = JsonConvert.DeserializeObject<SplashAddonSettings>(ipJson);
reloaded = JsonConvert.DeserializeObject<SplashAddonSettings>(JsonConvert.SerializeObject(reloaded));
Check(reloaded.IgnoredProcessNames.Count == 2, "reload does not duplicate list");
var fromOld = JsonConvert.DeserializeObject<SplashAddonSettings>(oldJson);
fromOld.Upgrade();
Check(fromOld.IgnoredProcessNames.Count == 1 && fromOld.IgnoredProcessNames[0] == "gamingservicesui", "original config gets the default ignore entry");
ip.BeginEdit(); ip.IgnoredProcessNamesText = "x"; ip.CancelEdit();
Check(ip.IgnoredProcessNames.Count == 2, "cancel restores ignore list");

// 9) Diagnostic logging: off by default, survives save/load, imported configs stay off.
Check(!new SplashAddonSettings().EnableDiagnosticLogging, "diagnostic logging off by default");
Check(!JsonConvert.DeserializeObject<SplashAddonSettings>(oldJson).EnableDiagnosticLogging, "old config keeps logging off");
var dl = new SplashAddonSettings { EnableDiagnosticLogging = true };
Check(JsonConvert.DeserializeObject<SplashAddonSettings>(JsonConvert.SerializeObject(dl)).EnableDiagnosticLogging, "logging flag round-trips");
dl.BeginEdit(); dl.EnableDiagnosticLogging = false; dl.CancelEdit();
Check(dl.EnableDiagnosticLogging, "cancel restores logging flag");

// 10) Gamepad B button.
{
    var pads = new ushort?[4];           // null = slot empty
    var calls = new int[4];
    var now = new DateTime(2026, 1, 1, 12, 0, 0);
    const ushort B = 0x2000, A = 0x1000;
    pads[0] = B;                          // B already held when the splash appears
    var w = new GamepadWatcher(i => { calls[i]++; return pads[i]; }, () => now);

    now = now.AddMilliseconds(100);
    Check(w.PollForBPress(true) == -1, "held B at start does not close");
    pads[0] = 0; now = now.AddMilliseconds(100); w.PollForBPress(true);
    pads[0] = B; now = now.AddMilliseconds(100);
    Check(w.PollForBPress(true) == 0, "fresh B press closes (controller 1)");
    now = now.AddMilliseconds(100);
    Check(w.PollForBPress(true) == -1, "holding B does not fire again");

    pads[0] = 0; now = now.AddMilliseconds(100); w.PollForBPress(false);
    pads[0] = B; now = now.AddMilliseconds(100);
    Check(w.PollForBPress(false) == -1, "press during grace period ignored");
    now = now.AddMilliseconds(100);
    Check(w.PollForBPress(true) == -1, "B held through grace period still ignored");

    pads[0] = A; now = now.AddMilliseconds(100);
    Check(w.PollForBPress(true) == -1, "other buttons ignored");

    // Slot 2 empty: probed only once per second.
    int before = calls[1];
    for (int i = 0; i < 9; i++) { now = now.AddMilliseconds(100); w.PollForBPress(true); }
    Check(calls[1] - before <= 1, "empty slot probed at most once per second (" + (calls[1] - before) + " calls / 0.9 s)");

    // Controller connected while holding B: not a press.
    pads[1] = B; now = now.AddSeconds(1.1);
    Check(w.PollForBPress(true) == -1, "pad connected with B held does not close");
    pads[1] = 0; now = now.AddMilliseconds(100); w.PollForBPress(true);
    pads[1] = B; now = now.AddMilliseconds(100);
    Check(w.PollForBPress(true) == 1, "second controller press reported as controller 2");

    // Unplugged controller resets cleanly.
    pads[1] = null; now = now.AddMilliseconds(100);
    Check(w.PollForBPress(true) == -1, "unplugged controller ignored");
}

// 11) English-only texts.
Check(Loc.MenuPreview == "Show splash screen preview", "menu text in English");
var bad = new SplashAddonSettings { MaximumWaitSeconds = 0 };
bad.VerifySettings(out var enErrors);
Check(enErrors.Count == 1 && enErrors[0] == "The maximum waiting time must be greater than 0.", "validation error in English: " + enErrors[0]);
Check(Loc.AskDuration("Forza", 1).Contains("\"Forza\"") && Loc.AskDuration("x", 3).Contains("selected games"), "duration prompt singular/plural");

// 12) Game folder.
Check(GameFolder.Normalize(@"D:\SteamLibrary\steamapps\common\BALLxPIT\") == @"D:\SteamLibrary\steamapps\common\BALLxPIT", "steam game folder kept, trailing slash removed");
Check(GameFolder.Normalize(@"C:\Program Files\WindowsApps\Microsoft.ForteBaseGame_3.483.970.0_x64__8wekyb3d8bbwe") != null, "Game Pass folder kept");
Check(GameFolder.Normalize("D:/Witcher3") == @"D:\Witcher3", "forward slashes, one level deep");
Check(GameFolder.Normalize(@"\\nas\games\Doom") == @"\\nas\games\Doom", "network folder kept");
foreach (string badFolder in new[] { @"C:\", "C:", @"D:\Games", @"D:\SteamLibrary\steamapps\common", @"E:\Steam\steamapps", @"C:\Program Files",
                                @"C:\Program Files (x86)", @"C:\Program Files\WindowsApps", @"C:\XboxGames", @"\\nas\games", "relative\\folder", "", null })
{
    Check(GameFolder.Normalize(badFolder) == null, "too general or invalid folder rejected: " + (badFolder ?? "null"));
}
Check(GameFolder.Normalize(@"C:\Users\Me", new[] { @"C:\Users\Me\" }) == null, "system folder from the list rejected");
string bj = GameFolder.Normalize(@"D:\SteamLibrary\steamapps\common\BALLxPIT");
Check(GameFolder.Contains(bj, @"d:\steamlibrary\steamapps\common\ballxpit\Balls.exe"), "exe in folder (case-insensitive)");
Check(GameFolder.Contains(bj, @"D:\SteamLibrary\steamapps\common\BALLxPIT\bin\x64\Game.exe"), "exe in a subfolder");
Check(!GameFolder.Contains(bj, @"D:\SteamLibrary\steamapps\common\BALLxPIT2\Balls.exe"), "sibling folder with the same prefix is not inside");
Check(!GameFolder.Contains(bj, @"C:\Windows\explorer.exe") && !GameFolder.Contains(null, @"C:\a.exe") && !GameFolder.Contains(bj, ""), "outside / empty");

// 13) Window coverage.
Check(Math.Abs(GameWindowDetector.CoverageOf(0, 0, 1920, 1080, 0, 0, 3840, 2160) - 0.25) < 1e-9, "1080p window on a 4K screen = 25%");
Check(Math.Abs(GameWindowDetector.CoverageOf(-8, -8, 3848, 2168, 0, 0, 3840, 2160) - 1.0) < 1e-9, "window larger than screen = 100%");
Check(GameWindowDetector.CoverageOf(0, 0, 622, 302, 0, 0, 3840, 2160) < GameWindowDetector.WindowedMinCoverage, "Forza's small launch window stays below the threshold");
Check(Math.Abs(GameWindowDetector.CoverageOf(3000, 0, 4840, 1000, 0, 0, 3840, 2160) - (840.0 * 1000 / (3840.0 * 2160))) < 1e-9, "only the part on this screen counts");
Check(GameWindowDetector.CoverageOf(0, 0, 100, 100, 0, 0, 0, 0) == 0, "empty screen rectangle");

// 14) Image decode size.
Check(ImageLoader.ComputeDecodeWidth(3840, 2160, 1920, 1080, true) == 1920, "4K background for a 1080p screen decoded at 1920");
Check(ImageLoader.ComputeDecodeWidth(5120, 1440, 1920, 1080, true) == 3840, "ultrawide image keeps enough height to fill the screen");
Check(ImageLoader.ComputeDecodeWidth(1280, 720, 1920, 1080, true) == 0, "small image is never upscaled at decode time");
Check(ImageLoader.ComputeDecodeWidth(2000, 500, 307, 864, false) == 307, "logo fits the target width");
Check(ImageLoader.ComputeDecodeWidth(1200, 1800, 360, 0, false) == 360, "width-only target (blurred cover)");
Check(ImageLoader.ComputeDecodeWidth(600, 900, 0, 1296, false) == 0, "height-only target larger than image");
Check(ImageLoader.ComputeDecodeWidth(0, 0, 1920, 1080, true) == 0 && ImageLoader.ComputeDecodeWidth(100, 100, 0, 0, true) == 0, "unknown sizes");

// 15) Platform list and appearance settings.
var up = JsonConvert.DeserializeObject<SplashAddonSettings>(oldJson);
up.Upgrade();
Check(up.PlatformDurations.Count == 3 && up.PlatformDurations[0].Platform == "PC (Windows)" && up.PlatformDurations[1].Seconds == 20, "10 fixed slots moved into the list");
up.Upgrade();
Check(up.PlatformDurations.Count == 3, "upgrade can run twice");
string upJson = JsonConvert.SerializeObject(up);
Check(!upJson.Contains("\"Platform1\"") && !upJson.Contains("\"Duration1\"") && upJson.Contains("\"PlatformDurations\""), "old slots are no longer written");
var again = JsonConvert.DeserializeObject<SplashAddonSettings>(upJson);
again.Upgrade();
Check(again.PlatformDurations.Count == 3 && again.GetDurationForGame("x", "PC (Windows)") == 7, "list survives save/load");
up.BeginEdit(); up.PlatformDurations.Add(new PlatformDuration { Platform = "Sega Saturn", Seconds = 3 }); up.PlatformDurations.RemoveAt(0); up.CancelEdit();
Check(up.PlatformDurations.Count == 3 && up.PlatformDurations[0].Platform == "PC (Windows)", "cancel restores the platform list");
up.PlatformDurations.Clear(); up.BeginEdit(); up.CancelEdit(); up.Upgrade();
Check(up.PlatformDurations.Count == 0, "removed rows are not brought back from the old slots");

Check(new SplashAddonSettings().LogoWidthPercent == 16, "default logo width 16%");
Check(JsonConvert.DeserializeObject<SplashAddonSettings>(oldJson).LogoWidthPercent == 13, "old 250 px logo becomes 13%");
var lw = new SplashAddonSettings { LogoWidthPercent = 22 };
Check(JsonConvert.DeserializeObject<SplashAddonSettings>(JsonConvert.SerializeObject(lw)).LogoWidthPercent == 22, "logo width round-trips");

var ap = new SplashAddonSettings { LogoPosition = LogoPlacement.TopRight, BackgroundDimPercent = 30, BackgroundZoom = true };
string apJson = JsonConvert.SerializeObject(ap);
var ap2 = JsonConvert.DeserializeObject<SplashAddonSettings>(apJson);
Check(apJson.Contains("\"TopRight\"") && ap2.LogoPosition == LogoPlacement.TopRight && ap2.BackgroundDimPercent == 30 && ap2.BackgroundZoom,
      "appearance saved, logo position as a readable name");
var defs = JsonConvert.DeserializeObject<SplashAddonSettings>(oldJson);
Check(defs.DetectWindowedGames && defs.LogoPosition == LogoPlacement.BottomLeft
      && defs.BackgroundDimPercent == 0 && !defs.BackgroundVignette && !defs.BackgroundZoom, "old configs get the new defaults (look unchanged)");

Check(new SplashAddonSettings { LogoWidthPercent = 0 }.VerifySettings(out _), "logo size 0% (no logo) accepted");
Check(!new SplashAddonSettings { LogoWidthPercent = 91 }.VerifySettings(out _), "logo size above 90% rejected");
Check(JsonConvert.DeserializeObject<SplashAddonSettings>("{\"LogoSize\":0}").LogoWidthPercent == 0, "original 0 px logo (off) stays off");
Check(JsonConvert.DeserializeObject<SplashAddonSettings>("{\"LogoSize\":5000}").LogoWidthPercent == 90, "huge original logo capped at 90%");

// 16) Default ignore entry: added once, removable.
var dflt = (SplashAddonSettings)new SplashAddonSettings().GetDefaults();
Check(dflt.IgnoredProcessNames.Count == 1 && dflt.IgnoredProcessNames[0] == "gamingservicesui" && dflt.DefaultsVersion == 1, "fresh settings start with gamingservicesui");
var mine = JsonConvert.DeserializeObject<SplashAddonSettings>("{\"IgnoredProcessNames\":[\"EADesktop\"]}");
mine.Upgrade();
Check(mine.IgnoredProcessNames.Count == 2 && mine.IgnoredProcessNames[0] == "gamingservicesui" && mine.IgnoredProcessNames[1] == "EADesktop", "added in front of the user's own entries");
mine.Upgrade();
Check(mine.IgnoredProcessNames.Count == 2, "added only once");
mine.IgnoredProcessNamesText = "EADesktop";
var reread = JsonConvert.DeserializeObject<SplashAddonSettings>(JsonConvert.SerializeObject(mine));
reread.Upgrade();
Check(reread.IgnoredProcessNames.Count == 1 && reread.IgnoredProcessNames[0] == "EADesktop", "removing it sticks after save, load and upgrade");
var dup = JsonConvert.DeserializeObject<SplashAddonSettings>("{\"IgnoredProcessNames\":[\"GamingServicesUI\"]}");
dup.Upgrade();
Check(dup.IgnoredProcessNames.Count == 1, "no duplicate when the user already had it (any letter case)");
var badDim = new SplashAddonSettings { BackgroundDimPercent = 95 };
Check(!badDim.VerifySettings(out _), "dimming above 90% rejected");

// 17) Readable game lists in the settings.
var names = new Dictionary<string, string> { ["b"] = "witcher 3", ["a"] = "Alan Wake", ["c"] = "Celeste" };
var sorted = GameEntries.Sort(new[] { "b", "zzz-gone", "a", "A", "c", " ", null }, id => names.TryGetValue(id, out var n) ? n : null);
Check(string.Join(",", sorted.Select(g => g.Name ?? "?" + g.Id)) == "Alan Wake,Celeste,witcher 3,?zzz-gone",
      "sorted by name, case-insensitive, unknown last, duplicates and blanks dropped: " + string.Join(",", sorted.Select(g => g.Name ?? "?" + g.Id)));
var failing = GameEntries.Sort(new[] { "x" }, id => throw new InvalidOperationException());
Check(failing.Count == 1 && failing[0].Name == null, "a failing name lookup shows the game as unknown");
Check(GameEntries.Sort(null, null).Count == 0, "no games");
Check(Loc.UnknownGame("abc") == "Unknown game (abc)", "unknown game label");
// Editing a time in place keeps the value through Save, and Cancel brings the old one back.
var ed = new SplashAddonSettings();
ed.SetGameSpecificDuration(new[] { "g1" }, 15);
ed.BeginEdit(); ed.GameSpecificDurations["g1"] = 40; ed.CancelEdit();
Check(ed.GameSpecificDurations["g1"] == 15, "cancel restores an edited game time");
ed.BeginEdit(); ed.GameSpecificDurations["g1"] = 0;
Check(!ed.VerifySettings(out _), "a 0 s game time is reported on Save");

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures;
