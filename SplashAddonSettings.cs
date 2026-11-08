using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Playnite.SDK;

namespace SplashAddonPlus
{
    public class SplashAddonSettings : ObservableObject, ISettings, IEditableObject
    {
        private List<string> _excludedGameIds = new List<string>();
        private int _splashScreenDuration = 10;
        private bool _usePlatformSpecificTimers;
        private Dictionary<string, int> _gameSpecificDurations = new Dictionary<string, int>();
        private bool _useGameStartedTimer;
        private int _logoSize = 300;

        private bool _autoDetectFullscreen = true;
        private int _minimumDisplaySeconds = 2;
        private int _detectionDelaySeconds = 1;
        private int _maximumWaitSeconds = 60;
        private List<string> _ignoredProcessNames = new List<string>();
        private bool _enableDiagnosticLogging;
        private int _defaultsVersion; // which built-in defaults have already been added to older settings

        private bool _detectWindowedGames = true;
        private LogoPlacement _logoPosition = LogoPlacement.BottomLeft;
        private int _logoWidthPercent = -1; // -1: not set yet, derived from the old pixel LogoSize
        private int _backgroundDimPercent;
        private bool _backgroundVignette;
        private bool _backgroundZoom;
        private ObservableCollection<PlatformDuration> _platformDurations = new ObservableCollection<PlatformDuration>();

        private string _editBackup;

        // ----- Automatic fullscreen detection -----

        [JsonProperty("AutoDetectFullscreen")]
        public bool AutoDetectFullscreen { get => _autoDetectFullscreen; set => SetValue(ref _autoDetectFullscreen, value, "AutoDetectFullscreen"); }

        [JsonProperty("MinimumDisplaySeconds")]
        public int MinimumDisplaySeconds { get => _minimumDisplaySeconds; set => SetValue(ref _minimumDisplaySeconds, value, "MinimumDisplaySeconds"); }

        [JsonProperty("DetectionDelaySeconds")]
        public int DetectionDelaySeconds { get => _detectionDelaySeconds; set => SetValue(ref _detectionDelaySeconds, value, "DetectionDelaySeconds"); }

        [JsonProperty("MaximumWaitSeconds")]
        public int MaximumWaitSeconds { get => _maximumWaitSeconds; set => SetValue(ref _maximumWaitSeconds, value, "MaximumWaitSeconds"); }

        /// <summary>Extra process names (without .exe) whose windows are never taken for the game.</summary>
        [JsonProperty("IgnoredProcessNames", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> IgnoredProcessNames
        {
            get => _ignoredProcessNames;
            set
            {
                SetValue(ref _ignoredProcessNames, value, "IgnoredProcessNames");
                OnPropertyChanged("IgnoredProcessNamesText");
            }
        }

        [JsonIgnore]
        public string IgnoredProcessNamesText
        {
            get => string.Join(Environment.NewLine, IgnoredProcessNames ?? new List<string>());
            set
            {
                IgnoredProcessNames = (value ?? string.Empty)
                    .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(GameWindowDetector.NormalizeProcessName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        /// <summary>Bookkeeping for <see cref="Upgrade"/>; not shown in the settings.</summary>
        [JsonProperty("DefaultsVersion")]
        public int DefaultsVersion { get => _defaultsVersion; set => SetValue(ref _defaultsVersion, value, "DefaultsVersion"); }

        /// <summary>Detailed entries in extensions.log; errors are logged regardless.</summary>
        [JsonProperty("EnableDiagnosticLogging")]
        public bool EnableDiagnosticLogging { get => _enableDiagnosticLogging; set => SetValue(ref _enableDiagnosticLogging, value, "EnableDiagnosticLogging"); }

        // ----- Settings from 1.0.x (names unchanged, so existing configuration keeps working) -----

        [JsonProperty("ExcludedGameIds")]
        public List<string> ExcludedGameIds
        {
            get => _excludedGameIds;
            set
            {
                SetValue(ref _excludedGameIds, value, "ExcludedGameIds");
                OnPropertyChanged("ExcludedGameIdsText");
            }
        }

        [JsonProperty("SplashScreenDuration")]
        public int SplashScreenDuration { get => _splashScreenDuration; set => SetValue(ref _splashScreenDuration, value, "SplashScreenDuration"); }

        [JsonProperty("UsePlatformSpecificTimers")]
        public bool UsePlatformSpecificTimers { get => _usePlatformSpecificTimers; set => SetValue(ref _usePlatformSpecificTimers, value, "UsePlatformSpecificTimers"); }

        [JsonProperty("GameSpecificDurations")]
        public Dictionary<string, int> GameSpecificDurations
        {
            get => _gameSpecificDurations;
            set
            {
                SetValue(ref _gameSpecificDurations, value, "GameSpecificDurations");
                OnPropertyChanged("GameSpecificDurationsText");
            }
        }

        [JsonProperty("UseGameStartedTimer")]
        public bool UseGameStartedTimer { get => _useGameStartedTimer; set => SetValue(ref _useGameStartedTimer, value, "UseGameStartedTimer"); }

        /// <summary>Logo width in pixels, as saved by the original Splash Addon. Kept only to derive <see cref="LogoWidthPercent"/> for imported settings.</summary>
        [JsonProperty("LogoSize")]
        public int LogoSize { get => _logoSize; set => SetValue(ref _logoSize, value, "LogoSize"); }

        // ----- Detection and appearance -----

        [JsonProperty("DetectWindowedGames")]
        public bool DetectWindowedGames { get => _detectWindowedGames; set => SetValue(ref _detectWindowedGames, value, "DetectWindowedGames"); }

        [JsonProperty("LogoPosition"), JsonConverter(typeof(StringEnumConverter))]
        public LogoPlacement LogoPosition { get => _logoPosition; set => SetValue(ref _logoPosition, value, "LogoPosition"); }

        /// <summary>Logo width as a percentage of the screen width.</summary>
        [JsonProperty("LogoWidthPercent")]
        public int LogoWidthPercent
        {
            get => _logoWidthPercent >= 0 ? _logoWidthPercent : PercentFromPixels(LogoSize);
            set => SetValue(ref _logoWidthPercent, value, "LogoWidthPercent");
        }

        [JsonProperty("BackgroundDimPercent")]
        public int BackgroundDimPercent { get => _backgroundDimPercent; set => SetValue(ref _backgroundDimPercent, value, "BackgroundDimPercent"); }

        [JsonProperty("BackgroundVignette")]
        public bool BackgroundVignette { get => _backgroundVignette; set => SetValue(ref _backgroundVignette, value, "BackgroundVignette"); }

        [JsonProperty("BackgroundZoom")]
        public bool BackgroundZoom { get => _backgroundZoom; set => SetValue(ref _backgroundZoom, value, "BackgroundZoom"); }

        [JsonProperty("PlatformDurations", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<PlatformDuration> PlatformDurations
        {
            get => _platformDurations;
            set => SetValue(ref _platformDurations, value ?? new ObservableCollection<PlatformDuration>(), "PlatformDurations");
        }

        public const int MaxPercent = 90;

        /// <summary>
        /// The original pixel width as a share of a 1920 px wide screen: the 300 px default becomes 16%,
        /// and 0 px (logo turned off) stays 0%.
        /// </summary>
        internal static int PercentFromPixels(int pixels)
        {
            if (pixels < 0)
            {
                return 16;
            }

            return Math.Min(MaxPercent, (int)Math.Round(pixels / 19.2));
        }

        // ----- Per-platform slots of the original Splash Addon: read from imported settings, moved to PlatformDurations by Upgrade(), never written -----
        [JsonProperty("Platform1")]
        public string Platform1 { get; set; }

        [JsonProperty("Duration1")]
        public int Duration1 { get; set; }

        [JsonProperty("Platform2")]
        public string Platform2 { get; set; }

        [JsonProperty("Duration2")]
        public int Duration2 { get; set; }

        [JsonProperty("Platform3")]
        public string Platform3 { get; set; }

        [JsonProperty("Duration3")]
        public int Duration3 { get; set; }

        [JsonProperty("Platform4")]
        public string Platform4 { get; set; }

        [JsonProperty("Duration4")]
        public int Duration4 { get; set; }

        [JsonProperty("Platform5")]
        public string Platform5 { get; set; }

        [JsonProperty("Duration5")]
        public int Duration5 { get; set; }

        [JsonProperty("Platform6")]
        public string Platform6 { get; set; }

        [JsonProperty("Duration6")]
        public int Duration6 { get; set; }

        [JsonProperty("Platform7")]
        public string Platform7 { get; set; }

        [JsonProperty("Duration7")]
        public int Duration7 { get; set; }

        [JsonProperty("Platform8")]
        public string Platform8 { get; set; }

        [JsonProperty("Duration8")]
        public int Duration8 { get; set; }

        [JsonProperty("Platform9")]
        public string Platform9 { get; set; }

        [JsonProperty("Duration9")]
        public int Duration9 { get; set; }

        [JsonProperty("Platform10")]
        public string Platform10 { get; set; }

        [JsonProperty("Duration10")]
        public int Duration10 { get; set; }

        public bool ShouldSerializePlatform1() => false;
        public bool ShouldSerializePlatform2() => false;
        public bool ShouldSerializePlatform3() => false;
        public bool ShouldSerializePlatform4() => false;
        public bool ShouldSerializePlatform5() => false;
        public bool ShouldSerializePlatform6() => false;
        public bool ShouldSerializePlatform7() => false;
        public bool ShouldSerializePlatform8() => false;
        public bool ShouldSerializePlatform9() => false;
        public bool ShouldSerializePlatform10() => false;
        public bool ShouldSerializeDuration1() => false;
        public bool ShouldSerializeDuration2() => false;
        public bool ShouldSerializeDuration3() => false;
        public bool ShouldSerializeDuration4() => false;
        public bool ShouldSerializeDuration5() => false;
        public bool ShouldSerializeDuration6() => false;
        public bool ShouldSerializeDuration7() => false;
        public bool ShouldSerializeDuration8() => false;
        public bool ShouldSerializeDuration9() => false;
        public bool ShouldSerializeDuration10() => false;

        [JsonIgnore]
        public string ExcludedGameIdsText
        {
            get => string.Join(Environment.NewLine, ExcludedGameIds ?? new List<string>());
            set
            {
                ExcludedGameIds = (value ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();
            }
        }

        [JsonIgnore]
        public string GameSpecificDurationsText
        {
            get
            {
                if (GameSpecificDurations == null || !GameSpecificDurations.Any())
                {
                    return string.Empty;
                }

                return string.Join(Environment.NewLine, GameSpecificDurations.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
            }
            set => ParseGameSpecificDurations(value);
        }

        [JsonIgnore]
        public static List<string> AvailablePlatforms => new List<string>
        {
            "", "3DO Interactive Multiplayer", "Amstrad CPC", "Apple II", "Atari 2600", "Atari 5200", "Atari 7800", "Atari 8-bit", "Atari Jaguar", "Atari Lynx",
            "Atari ST/STE", "Bandai WonderSwan", "Bandai WonderSwan Color", "Coleco ColecoVision", "Commodore Amiga", "Commodore Amiga CD32", "Commodore Amiga CDTV", "Commodore Plus/4", "Commodore VIC20", "GCE Vectrex",
            "Macintosh", "Magnavox Odyssey 2", "Mattel Intellivision", "Microsoft MSX", "Microsoft MSX2", "Microsoft Xbox", "Microsoft Xbox 360", "Microsoft Xbox One", "Microsoft Xbox Series", "NEC PC-98",
            "NEC PC-FX", "NEC SuperGrafx", "NEC TurboGrafx 16", "NEC TurboGrafx-CD", "Nintendo 3DS", "Nintendo 64", "Nintendo DS", "Nintendo DSi", "Nintendo Entertainment System", "Nintendo Family Computer Disk System",
            "Nintendo Game Boy", "Nintendo Game Boy Advance", "Nintendo Game Boy Color", "Nintendo GameCube", "Nintendo SNES", "Nintendo Switch", "Nintendo Switch 2", "Nintendo Virtual Boy", "Nintendo Wii", "Nintendo Wii U",
            "PC", "PC (DOS)", "PC (Linux)", "PC (Windows)", "Philips CD-i", "PlayStation", "PlayStation 2", "PlayStation 3", "PlayStation 4", "PlayStation 5",
            "PlayStation Portable", "PlayStation Vita", "Sega 32X", "Sega CD", "Sega Dreamcast", "Sega Game Gear", "Sega Genesis", "Sega Master System", "Sega Saturn", "SNK Neo Geo",
            "SNK Neo Geo CD", "SNK Neo Geo Pocket", "SNK Neo Geo Pocket Color", "Sony PSP"
        };

        /// <summary>Called after the user confirms the settings dialog.</summary>
        [JsonIgnore]
        internal Action SaveRequested { get; set; }

        public SplashAddonSettings()
        {
            if (GameSpecificDurations == null)
            {
                GameSpecificDurations = new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Brings settings saved by an older version up to date: the ten fixed platform slots become
        /// rows of <see cref="PlatformDurations"/>. Safe to call more than once.
        /// </summary>
        public void Upgrade()
        {
            if (PlatformDurations == null)
            {
                PlatformDurations = new ObservableCollection<PlatformDuration>();
            }

            if (DefaultsVersion < 1)
            {
                // The Xbox / Game Pass loading screen. Added once as an ordinary entry, so it can be removed
                // if it is ever no longer needed.
                const string xboxLoadingScreen = "gamingservicesui";
                var ignored = new List<string>(IgnoredProcessNames ?? new List<string>());
                if (!ignored.Any(name => string.Equals(name, xboxLoadingScreen, StringComparison.OrdinalIgnoreCase)))
                {
                    ignored.Insert(0, xboxLoadingScreen);
                    IgnoredProcessNames = ignored;
                }

                DefaultsVersion = 1;
            }

            var slots = new[]
            {
                new KeyValuePair<string, int>(Platform1, Duration1), new KeyValuePair<string, int>(Platform2, Duration2),
                new KeyValuePair<string, int>(Platform3, Duration3), new KeyValuePair<string, int>(Platform4, Duration4),
                new KeyValuePair<string, int>(Platform5, Duration5), new KeyValuePair<string, int>(Platform6, Duration6),
                new KeyValuePair<string, int>(Platform7, Duration7), new KeyValuePair<string, int>(Platform8, Duration8),
                new KeyValuePair<string, int>(Platform9, Duration9), new KeyValuePair<string, int>(Platform10, Duration10)
            };

            if (PlatformDurations.Count == 0)
            {
                foreach (KeyValuePair<string, int> slot in slots)
                {
                    if (!string.IsNullOrWhiteSpace(slot.Key))
                    {
                        PlatformDurations.Add(new PlatformDuration { Platform = slot.Key, Seconds = slot.Value });
                    }
                }
            }

            Platform1 = null;
            Duration1 = 0;
            Platform2 = null;
            Duration2 = 0;
            Platform3 = null;
            Duration3 = 0;
            Platform4 = null;
            Duration4 = 0;
            Platform5 = null;
            Duration5 = 0;
            Platform6 = null;
            Duration6 = 0;
            Platform7 = null;
            Duration7 = 0;
            Platform8 = null;
            Duration8 = 0;
            Platform9 = null;
            Duration9 = 0;
            Platform10 = null;
            Duration10 = 0;
        }

        public void ParseGameSpecificDurations(string rawText)
        {
            var durations = new Dictionary<string, int>();
            if (!string.IsNullOrEmpty(rawText))
            {
                foreach (string line in rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = line.Trim().Split(':');
                    if (parts.Length == 2
                        && !string.IsNullOrWhiteSpace(parts[0])
                        && int.TryParse(parts[1].Trim(), out int seconds)
                        && seconds > 0)
                    {
                        durations[parts[0].Trim()] = seconds;
                    }
                }
            }

            GameSpecificDurations = durations;
        }

        // ----- Helpers used by the game context menu -----

        public bool IsExcluded(string gameId)
        {
            return ExcludedGameIds != null && ExcludedGameIds.Any(id => string.Equals(id.Trim(), gameId, StringComparison.OrdinalIgnoreCase));
        }

        public void SetExcluded(IEnumerable<string> gameIds, bool excluded)
        {
            var ids = new List<string>(ExcludedGameIds ?? new List<string>());
            foreach (string gameId in gameIds)
            {
                ids.RemoveAll(id => string.Equals(id.Trim(), gameId, StringComparison.OrdinalIgnoreCase));
                if (excluded)
                {
                    ids.Add(gameId);
                }
            }

            ExcludedGameIds = ids;
        }

        public bool HasGameSpecificDuration(string gameId)
        {
            return !string.IsNullOrEmpty(gameId) && GameSpecificDurations != null && GameSpecificDurations.ContainsKey(gameId);
        }

        public void SetGameSpecificDuration(IEnumerable<string> gameIds, int? seconds)
        {
            var durations = new Dictionary<string, int>(GameSpecificDurations ?? new Dictionary<string, int>());
            foreach (string gameId in gameIds)
            {
                if (seconds.HasValue && seconds.Value > 0)
                {
                    durations[gameId] = seconds.Value;
                }
                else
                {
                    durations.Remove(gameId);
                }
            }

            GameSpecificDurations = durations;
        }

        // ----- ISettings / IEditableObject -----

        public void BeginEdit()
        {
            _editBackup = JsonConvert.SerializeObject(this);
        }

        public void CancelEdit()
        {
            if (_editBackup == null)
            {
                return;
            }

            JsonConvert.PopulateObject(_editBackup, this, new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            });
            _editBackup = null;
        }

        public void EndEdit()
        {
            _editBackup = null;
            SaveRequested?.Invoke();
        }

        public ISettings GetDefaults()
        {
            var defaults = new SplashAddonSettings();
            defaults.Upgrade();
            return defaults;
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (SplashScreenDuration <= 0)
            {
                errors.Add(Loc.ErrorDuration);
            }

            if (LogoWidthPercent < 0 || LogoWidthPercent > MaxPercent)
            {
                errors.Add(Loc.ErrorLogoSize);
            }

            if (BackgroundDimPercent < 0 || BackgroundDimPercent > MaxPercent)
            {
                errors.Add(Loc.ErrorBackgroundDim);
            }

            if (MinimumDisplaySeconds < 0)
            {
                errors.Add(Loc.ErrorMinimumDisplay);
            }

            if (DetectionDelaySeconds < 0)
            {
                errors.Add(Loc.ErrorDetectionDelay);
            }

            if (MaximumWaitSeconds <= 0)
            {
                errors.Add(Loc.ErrorMaximumWait);
            }
            else if (MaximumWaitSeconds < MinimumDisplaySeconds)
            {
                errors.Add(Loc.ErrorMaximumBelowMinimum);
            }

            if (GameSpecificDurations != null)
            {
                foreach (KeyValuePair<string, int> entry in GameSpecificDurations)
                {
                    if (entry.Value <= 0)
                    {
                        errors.Add(Loc.ErrorGameDuration(entry.Key));
                    }
                }
            }

            return errors.Count == 0;
        }

        public int GetDurationForGame(string gameId, string platformName)
        {
            if (HasGameSpecificDuration(gameId))
            {
                return GameSpecificDurations[gameId];
            }

            if (UsePlatformSpecificTimers && !string.IsNullOrEmpty(platformName))
            {
                var byPlatform = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (PlatformDuration row in PlatformDurations ?? new ObservableCollection<PlatformDuration>())
                {
                    // Duplicate platforms: the first row wins.
                    if (row != null && !string.IsNullOrEmpty(row.Platform) && !byPlatform.ContainsKey(row.Platform))
                    {
                        byPlatform.Add(row.Platform, row.Seconds);
                    }
                }

                if (byPlatform.TryGetValue(platformName, out int exact) && exact > 0)
                {
                    return exact;
                }

                string wanted = platformName.ToLowerInvariant();
                foreach (KeyValuePair<string, int> entry in byPlatform)
                {
                    string candidate = entry.Key.ToLowerInvariant();
                    if ((candidate.Contains(wanted) || wanted.Contains(candidate)) && entry.Value > 0)
                    {
                        return entry.Value;
                    }
                }
            }

            return SplashScreenDuration;
        }
    }
}
