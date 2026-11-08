namespace SplashAddonPlus
{
    /// <summary>All user-facing texts of the add-on in one place.</summary>
    internal static class Loc
    {
        // ----- Settings: automatic detection -----

        public const string SectionAutoDetect = "Automatic game detection";

        public const string AutoDetectToggle = "Close the splash screen when the game appears";

        public const string AutoDetectHint = "Instead of a fixed time, the splash screen closes as soon as the game is on screen.";

        public const string WindowedToggle = "Also detect games running in a window";

        public const string WindowedHint = "Works for games with a known installation folder (not for emulators).";

        public const string MinimumDisplay = "Minimum display time (s)";

        public const string MinimumDisplayHint = "Prevents a quick flash when a game starts almost instantly.";

        public const string DetectionDelay = "Delay after the game is detected (s)";

        public const string DetectionDelayHint = "Useful for games that show a black screen before the first image.";

        public const string IgnoredProcesses = "Programs whose windows are never the game (one process name per line)";

        public const string IgnoredProcessesHint =
            "Add a launcher here if it closes the splash screen too early. Its process name is in the log (see Diagnostics).";

        // ----- Settings: closing -----

        public const string SectionClosing = "Closing the splash screen";

        public const string MaximumWait = "Maximum waiting time (s)";

        public const string MaximumWaitHint = "After this time the splash screen closes no matter what.";

        public const string ManualCloseHint = "Esc, a mouse click or the B button on a gamepad closes the splash screen at any time.";

        // ----- Settings: appearance -----

        public const string SectionAppearance = "Appearance";

        public const string LogoPositionLabel = "Logo position";

        public const string LogoBottomLeft = "Bottom left";

        public const string LogoBottomCenter = "Bottom center";

        public const string LogoBottomRight = "Bottom right";

        public const string LogoCenter = "Center";

        public const string LogoTopLeft = "Top left";

        public const string LogoTopRight = "Top right";

        public const string LogoWidth = "Logo size";

        public const string LogoWidthHint = "0% hides the logo.";

        public const string BackgroundDim = "Background dimming";

        public const string VignetteToggle = "Darken the edges (vignette)";

        public const string ZoomToggle = "Slow zoom on the background";

        // ----- Settings: per-game exceptions -----

        public const string SectionExceptions = "Per-game exceptions";

        public const string ExceptionsTip = "Add games by right-clicking them in the library and opening \"Splash Addon Plus\".";

        public const string GameDurations = "Games with a fixed time";

        public const string ExcludedGames = "Games without a splash screen";

        public const string GameColumn = "Game";

        public const string NoGames = "None yet.";

        public static string UnknownGame(string id) => $"Unknown game ({id})";

        // ----- Settings: fixed time (advanced) -----

        public const string SectionFixedTime = "Fixed display time (advanced)";

        public const string FixedTimeHint = "Used when automatic detection is off and for games with a fixed time.";

        public const string WaitForStartedToggle = "Start counting when Playnite reports the game as started";

        public const string DefaultDuration = "Default time (s)";

        public const string PlatformTimersToggle = "Separate times per platform";

        public const string PlatformColumn = "Platform";

        public const string TimeColumn = "Time (s)";

        public const string PriorityHint = "Priority: game time, then platform time, then default time.";

        public const string AddPlatform = "Add platform";

        public const string Remove = "Remove";

        // ----- Settings: diagnostics -----

        public const string SectionDiagnostics = "Diagnostics";

        public const string DiagnosticsToggle = "Write details to the log file";

        public const string DiagnosticsHint =
            "Turn on only when troubleshooting. The log is %AppData%\\Playnite\\extensions.log (portable: in the Playnite folder).";

        // ----- Validation -----

        public const string ErrorDuration = "The default time must be greater than 0.";

        public const string ErrorLogoSize = "The logo size must be between 0 and 90%.";

        public const string ErrorBackgroundDim = "The background dimming must be between 0 and 90%.";

        public const string ErrorMinimumDisplay = "The minimum display time cannot be negative.";

        public const string ErrorDetectionDelay = "The delay after the game is detected cannot be negative.";

        public const string ErrorMaximumWait = "The maximum waiting time must be greater than 0.";

        public const string ErrorMaximumBelowMinimum = "The maximum waiting time cannot be shorter than the minimum display time.";

        public static string ErrorGameDuration(string gameId) => $"The time for game ID \"{gameId}\" must be greater than 0.";

        // ----- Game context menu -----

        public const string MenuDisableOne = "Turn off the splash screen for this game";

        public const string MenuDisableMany = "Turn off the splash screen for the selected games";

        public const string MenuEnable = "Turn the splash screen back on";

        public const string MenuSetTime = "Set a fixed display time…";

        public const string MenuRemoveTime = "Remove the fixed time";

        public const string MenuPreview = "Show splash screen preview";

        public static string AskDuration(string gameName, int gameCount) => gameCount == 1
            ? $"How many seconds should the splash screen last for \"{gameName}\"?"
            : "How many seconds should the splash screen last for the selected games?";

        public const string AskDurationNote = "Automatic detection is not used for games with a fixed time.";

        public const string InvalidNumber = "Enter a whole number greater than zero.";
    }
}
