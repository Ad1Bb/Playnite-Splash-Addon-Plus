using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace SplashAddonPlus
{
    public class SplashAddonPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private const string MenuSection = "Splash Addon Plus";

        // Settings folder of the original Splash Addon by Artzox; imported once so switching keeps the configuration.
        private const string OriginalAddonGuid = "550e8400-e29b-41d4-a716-446655440000";
        private const int PreviewSeconds = 4;

        private readonly SplashAddonSettings _settings;
        private SplashSession _session;

        public override Guid Id { get; } = Guid.Parse("586357a7-11fe-488d-999e-ad9cb2070621");

        public SplashAddonPlugin(IPlayniteAPI api)
            : base(api)
        {
            _settings = LoadPluginSettings<SplashAddonSettings>() ?? ImportOriginalSettings() ?? new SplashAddonSettings();
            _settings.Upgrade();
            _settings.SaveRequested = SaveSettings;
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        /// <summary>
        /// First start of Splash Addon Plus: reuse the configuration of the original Splash Addon
        /// (or of a modified build that still used its ID), if there is one.
        /// </summary>
        private SplashAddonSettings ImportOriginalSettings()
        {
            try
            {
                string path = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, OriginalAddonGuid, "config.json");
                if (!File.Exists(path))
                {
                    return null;
                }

                var imported = JsonConvert.DeserializeObject<SplashAddonSettings>(File.ReadAllText(path));
                if (imported == null)
                {
                    return null;
                }

                SavePluginSettings(imported);
                Logger.Info("Imported settings from the original Splash Addon.");
                return imported;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not import settings from the original Splash Addon.");
                return null;
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return _settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new SplashAddonSettingsView(BuildPlatformList(), ResolveGameName) { DataContext = _settings };
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            SaveSettings();
            base.OnApplicationStopped(args);
        }

        // ---------------------------------------------------------------- game events

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            RunOnUi(() => StartSplash(args.Game, args.SourceAction, preview: false));
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            RunOnUi(() =>
            {
                SplashSession session = _session;
                if (session == null || session.GameId != args.Game.Id || !session.IsWaitingForGameStarted)
                {
                    return;
                }

                int remaining = session.DurationSeconds - (int)session.SecondsSinceShown;
                session.StartCountdown(Math.Max(0, remaining));
            });
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            RunOnUi(() => CloseSplashFor(args.Game, "game stopped"));
        }

        public override void OnGameStartupCancelled(OnGameStartupCancelledEventArgs args)
        {
            RunOnUi(() => CloseSplashFor(args.Game, "startup cancelled"));
        }

        // ---------------------------------------------------------------- context menu

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var items = new List<GameMenuItem>();
            if (args?.Games == null || args.Games.Count == 0)
            {
                return items;
            }

            bool single = args.Games.Count == 1;
            List<string> ids = args.Games.Select(g => g.Id.ToString()).ToList();

            if (ids.Any(id => !_settings.IsExcluded(id)))
            {
                items.Add(new GameMenuItem
                {
                    MenuSection = MenuSection,
                    Description = single ? Loc.MenuDisableOne : Loc.MenuDisableMany,
                    Action = a => UpdateSettings(() => _settings.SetExcluded(IdsOf(a), true))
                });
            }

            if (ids.Any(_settings.IsExcluded))
            {
                items.Add(new GameMenuItem
                {
                    MenuSection = MenuSection,
                    Description = Loc.MenuEnable,
                    Action = a => UpdateSettings(() => _settings.SetExcluded(IdsOf(a), false))
                });
            }

            items.Add(new GameMenuItem
            {
                MenuSection = MenuSection,
                Description = Loc.MenuSetTime,
                Action = a => AskForGameDuration(a.Games)
            });

            if (ids.Any(_settings.HasGameSpecificDuration))
            {
                items.Add(new GameMenuItem
                {
                    MenuSection = MenuSection,
                    Description = Loc.MenuRemoveTime,
                    Action = a => UpdateSettings(() => _settings.SetGameSpecificDuration(IdsOf(a), null))
                });
            }

            if (single)
            {
                items.Add(new GameMenuItem
                {
                    MenuSection = MenuSection,
                    Description = Loc.MenuPreview,
                    Action = a => RunOnUi(() => StartSplash(a.Games[0], null, preview: true))
                });
            }

            return items;
        }

        private void AskForGameDuration(List<Game> games)
        {
            if (games == null || games.Count == 0)
            {
                return;
            }

            string firstId = games[0].Id.ToString();
            int current = _settings.HasGameSpecificDuration(firstId)
                ? _settings.GameSpecificDurations[firstId]
                : _settings.SplashScreenDuration;

            StringSelectionDialogResult result = PlayniteApi.Dialogs.SelectString(
                Loc.AskDuration(games[0].Name, games.Count) + "\n\n" + Loc.AskDurationNote,
                MenuSection,
                current.ToString());

            if (result == null || !result.Result)
            {
                return;
            }

            if (!int.TryParse(result.SelectedString?.Trim(), out int seconds) || seconds <= 0)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(Loc.InvalidNumber, MenuSection);
                return;
            }

            UpdateSettings(() => _settings.SetGameSpecificDuration(games.Select(g => g.Id.ToString()), seconds));
        }

        private static IEnumerable<string> IdsOf(GameMenuItemActionArgs args)
        {
            return args.Games.Select(g => g.Id.ToString()).ToList();
        }

        private void UpdateSettings(Action change)
        {
            change();
            SaveSettings();
        }

        // ---------------------------------------------------------------- splash handling

        private void StartSplash(Game game, GameAction sourceAction, bool preview)
        {
            if (_session != null)
            {
                _session.CloseImmediately();
                _session = null;
            }

            string gameId = game.Id.ToString();
            if (!preview && _settings.IsExcluded(gameId))
            {
                return;
            }

            string platform = game.Platforms?.FirstOrDefault()?.Name ?? string.Empty;
            int duration = _settings.GetDurationForGame(gameId, platform);
            if (duration <= 0)
            {
                duration = _settings.SplashScreenDuration > 0 ? _settings.SplashScreenDuration : 1;
            }

            SplashMode mode;
            if (preview)
            {
                mode = SplashMode.FixedTime;
                duration = PreviewSeconds;
            }
            else if (_settings.AutoDetectFullscreen && !_settings.HasGameSpecificDuration(gameId))
            {
                mode = SplashMode.AutoDetect;
            }
            else if (_settings.UseGameStartedTimer)
            {
                mode = SplashMode.WaitForGameStarted;
            }
            else
            {
                mode = SplashMode.FixedTime;
            }

            var session = new SplashSession(game, new SplashSessionOptions
            {
                Mode = mode,
                DurationSeconds = duration,
                MinimumDisplaySeconds = Math.Max(0, _settings.MinimumDisplaySeconds),
                DetectionDelaySeconds = Math.Max(0, _settings.DetectionDelaySeconds),
                MaximumWaitSeconds = Math.Max(1, _settings.MaximumWaitSeconds),
                IgnoredProcessNames = (_settings.IgnoredProcessNames ?? new List<string>()).ToList(),
                GameFolder = mode == SplashMode.AutoDetect ? ResolveGameFolder(game, sourceAction) : null,
                DetectWindowedGames = _settings.DetectWindowedGames,
                Appearance = new SplashAppearance
                {
                    LogoPosition = _settings.LogoPosition,
                    LogoWidthPercent = _settings.LogoWidthPercent,
                    BackgroundDimPercent = _settings.BackgroundDimPercent,
                    Vignette = _settings.BackgroundVignette,
                    Zoom = _settings.BackgroundZoom
                },
                Images = SplashImageSources.ForGame(game),
                DiagnosticLogging = _settings.EnableDiagnosticLogging
            });
            session.Closed += (s, e) =>
            {
                if (_session == session)
                {
                    _session = null;
                }
            };

            _session = session;
            LogInfo($"Showing splash for \"{game.Name}\" (mode: {mode}, duration: {duration} s).");
            session.Show();
        }

        /// <summary>
        /// Installation folder used to recognise the game's own windows, or null. Emulated games are skipped:
        /// their folder holds the ROM, while the window belongs to the emulator installed elsewhere.
        /// </summary>
        private string ResolveGameFolder(Game game, GameAction sourceAction)
        {
            try
            {
                if (sourceAction?.Type == GameActionType.Emulator || string.IsNullOrWhiteSpace(game.InstallDirectory))
                {
                    return null;
                }

                string folder = PlayniteApi.ExpandGameVariables(game, game.InstallDirectory);
                return GameFolder.Normalize(folder, GameFolder.SystemFolders());
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not resolve the game folder.");
                return null;
            }
        }

        private void CloseSplashFor(Game game, string reason)
        {
            SplashSession session = _session;
            if (session != null && game != null && session.GameId == game.Id)
            {
                LogInfo($"Closing splash for \"{game.Name}\": {reason}.");
                session.FadeOutAndClose();
            }
        }

        // ---------------------------------------------------------------- helpers

        private List<string> BuildPlatformList()
        {
            var names = new HashSet<string>(SplashAddonSettings.AvailablePlatforms, StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Platform platform in PlayniteApi.Database.Platforms)
                {
                    if (!string.IsNullOrWhiteSpace(platform.Name))
                    {
                        names.Add(platform.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not read platforms from the database.");
            }

            // Keep platforms that are already configured selectable even if they are no longer in the library.
            foreach (PlatformDuration row in _settings.PlatformDurations)
            {
                if (!string.IsNullOrEmpty(row?.Platform))
                {
                    names.Add(row.Platform);
                }
            }

            names.Remove(string.Empty);
            var list = names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            list.Insert(0, string.Empty);
            return list;
        }

        private void LogInfo(string message)
        {
            if (_settings.EnableDiagnosticLogging)
            {
                Logger.Info(message);
            }
        }

        private string ResolveGameName(string gameId)
        {
            try
            {
                return Guid.TryParse(gameId, out Guid id) ? PlayniteApi.Database.Games.Get(id)?.Name : null;
            }
            catch
            {
                return null;
            }
        }

        private void SaveSettings()
        {
            try
            {
                SavePluginSettings(_settings);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save Splash Addon settings.");
            }
        }

        private static void RunOnUi(Action action)
        {
            void Guarded()
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Splash Addon error.");
                }
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                Guarded();
            }
            else
            {
                dispatcher.Invoke((Action)Guarded);
            }
        }
    }
}
