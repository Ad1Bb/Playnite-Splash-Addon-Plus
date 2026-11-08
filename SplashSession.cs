using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace SplashAddonPlus
{
    internal enum SplashMode
    {
        /// <summary>Close after a fixed number of seconds from the moment the splash appears.</summary>
        FixedTime,

        /// <summary>Wait for Playnite's "game started" event, then count down the rest of the time.</summary>
        WaitForGameStarted,

        /// <summary>Close once a game window covers the whole screen.</summary>
        AutoDetect
    }

    internal sealed class SplashSessionOptions
    {
        public SplashMode Mode { get; set; }
        public int DurationSeconds { get; set; }
        public int MinimumDisplaySeconds { get; set; }
        public int DetectionDelaySeconds { get; set; }
        public int MaximumWaitSeconds { get; set; }
        public IList<string> IgnoredProcessNames { get; set; }

        /// <summary>Normalised installation folder of the game, or null (unknown, too general, or an emulated game).</summary>
        public string GameFolder { get; set; }

        /// <summary>Accept a large window from the game's folder even when it is not fullscreen.</summary>
        public bool DetectWindowedGames { get; set; }

        public SplashAppearance Appearance { get; set; }
        public SplashImageSources Images { get; set; }

        /// <summary>Write details about detection to extensions.log (off by default).</summary>
        public bool DiagnosticLogging { get; set; }

    }

    /// <summary>
    /// One splash screen shown for one game launch, together with everything that decides when it closes.
    /// All members must be used on the UI thread.
    /// </summary>
    internal sealed class SplashSession
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(1.0);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan GamepadPollInterval = TimeSpan.FromMilliseconds(100);
        private const double InputGraceSeconds = 0.75;

        // A fullscreen window from outside a known game folder is often a launcher, so it has to last a bit longer.
        private const int OutsideFolderExtraSeconds = 3;

        private readonly SplashSessionOptions _options;
        private readonly SplashScene _scene;
        private readonly Window _window;
        private MonitorArea _monitor;
        private readonly List<DispatcherTimer> _timers = new List<DispatcherTimer>();

        private DateTime _shownAt;
        private bool _closing;
        private bool _countdownStarted;
        private bool _mousePressedOnSplash;

        private GamepadWatcher _gamepad;
        private DispatcherTimer _gamepadTimer;

        private GameWindowDetector _detector;
        private IntPtr _candidateWindow = IntPtr.Zero;
        private DateTime _candidateSince;
        private IntPtr _detectedGameWindow = IntPtr.Zero;
        private bool _ignoredProgramFillsScreen;
        private DateTime _ignoredProgramLastSeen = DateTime.MinValue;

        public SplashSession(Game game, SplashSessionOptions options)
        {
            GameId = game.Id;
            GameName = game.Name;
            _options = options;
            _scene = new SplashScene(options.Appearance);
            _window = CreateCoverWindow(_scene.Root);
            _window.Title = "SplashAddonPlusSplashScreen";
        }

        public event EventHandler Closed;

        public Guid GameId { get; }

        public string GameName { get; }

        public SplashMode Mode => _options.Mode;

        public int DurationSeconds => _options.DurationSeconds;

        public bool IsWaitingForGameStarted => _options.Mode == SplashMode.WaitForGameStarted && !_countdownStarted && !_closing;

        public void Show()
        {
            if (_options.Mode == SplashMode.AutoDetect)
            {
                _detector = new GameWindowDetector(_options.IgnoredProcessNames, _options.DiagnosticLogging,
                    _options.GameFolder, _options.DetectWindowedGames);
                _detector.TakeSnapshot();
            }


            _window.Loaded += (s, e) =>
            {
                if (!_closing)
                {
                    _window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, FadeDuration));
                }
            };

            // Manual escape hatch for launchers asking for input, windowed games, etc.
            // Input arriving right after the splash appears is ignored, so the tail of the
            // double-click or key press that launched the game cannot dismiss it.
            _window.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && !e.IsRepeat && SecondsSinceShown >= InputGraceSeconds)
                {
                    Log(() => $"Splash for \"{GameName}\" closed with Esc.");
                    FadeOutAndClose();
                }
            };
            _window.MouseDown += (s, e) =>
            {
                _mousePressedOnSplash = SecondsSinceShown >= InputGraceSeconds;
            };
            _window.MouseUp += (s, e) =>
            {
                if (_mousePressedOnSplash)
                {
                    Log(() => $"Splash for \"{GameName}\" closed by mouse click.");
                    FadeOutAndClose();
                }
            };
            _window.Closed += (s, e) =>
            {
                StopTimers();
                StopGamepad();
                Closed?.Invoke(this, EventArgs.Empty);
            };

            _shownAt = DateTime.Now;

            try
            {
                _window.Show();
                _window.Activate();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to show splash screen.");
                return;
            }

            // Only read: the size of the screen the splash opened on, so images are decoded at that resolution.
            _monitor = MonitorHelper.FromWindow(new WindowInteropHelper(_window).Handle) ?? MonitorHelper.Primary();
            Log(() => $"Splash for \"{GameName}\" on a {_monitor?.ToString() ?? "unknown"} screen; game folder: {_options.GameFolder ?? "not used"}.");
            _scene.LoadImagesAsync(_options.Images, _monitor);

            StartGamepad();

            switch (_options.Mode)
            {
                case SplashMode.FixedTime:
                    StartCountdown(_options.DurationSeconds);
                    break;

                case SplashMode.WaitForGameStarted:
                    StartSafetyTimer();
                    break;

                case SplashMode.AutoDetect:
                    StartSafetyTimer();
                    StartTimer(PollInterval, PollForGameWindow, repeat: true);
                    break;
            }
        }

        /// <summary>Starts the regular countdown; 0 or less closes the splash right away.</summary>
        public void StartCountdown(int seconds)
        {
            if (_closing || _countdownStarted)
            {
                return;
            }

            _countdownStarted = true;
            StopTimers();

            if (seconds <= 0)
            {
                FadeOutAndClose();
                return;
            }

            StartTimer(TimeSpan.FromSeconds(seconds), FadeOutAndClose);
        }

        public double SecondsSinceShown => (DateTime.Now - _shownAt).TotalSeconds;

        public void FadeOutAndClose()
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            StopTimers();
            StopGamepad();

            var fadeOut = new DoubleAnimation { To = 0.0, Duration = FadeDuration };
            fadeOut.Completed += (s, e) => CloseWindow();
            _window.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        public void CloseImmediately()
        {
            _closing = true;
            StopTimers();
            StopGamepad();
            CloseWindow();
        }

        private void CloseWindow()
        {
            try
            {
                _window.Close();
            }
            catch
            {
            }

            if (_detectedGameWindow != IntPtr.Zero)
            {
                GameWindowDetector.FocusIfPlayniteHasForeground(_detectedGameWindow);
            }
        }

        private void StartSafetyTimer()
        {
            int maxWait = Math.Max(1, _options.MaximumWaitSeconds);
            StartTimer(TimeSpan.FromSeconds(maxWait), () =>
            {
                Log(() => $"Splash for \"{GameName}\" reached the maximum wait time ({maxWait} s), closing.");
                FadeOutAndClose();
            });
        }

        private void PollForGameWindow()
        {
            if (_closing || _detector == null)
            {
                return;
            }

            DateTime now = DateTime.Now;
            WindowScanResult scan = _detector.Scan();

            foreach (string window in scan.NewWindows)
            {
                Log(() => $"New window while loading \"{GameName}\" at {SecondsSinceShown:0.0} s: {window}");
            }

            // A program from the ignore list covering the screen (typically a launcher's loading screen) is never
            // the game, and the splash waits until it is gone.
            if (scan.IgnoredProgramFillsScreen)
            {
                if (!_ignoredProgramFillsScreen)
                {
                    Log(() => $"Ignored program fills the screen while loading \"{GameName}\" at {SecondsSinceShown:0.0} s, waiting: {scan.IgnoredProgramWindow}");
                }

                _ignoredProgramFillsScreen = true;
                _ignoredProgramLastSeen = now;
                GameWindowDetector.KeepOnTop(new WindowInteropHelper(_window).Handle);
            }
            else if (_ignoredProgramFillsScreen)
            {
                _ignoredProgramFillsScreen = false;
                Log(() => $"Ignored program no longer fills the screen at {SecondsSinceShown:0.0} s.");
            }

            IntPtr hWnd = scan.GameWindow;
            if (hWnd == IntPtr.Zero)
            {
                _candidateWindow = IntPtr.Zero;
                return;
            }

            bool outsideKnownFolder = _detector.KnowsGameFolder && !scan.GameWindowFromFolder;
            if (hWnd != _candidateWindow)
            {
                _candidateWindow = hWnd;
                _candidateSince = now;
                string kind = scan.GameWindowFromFolder
                    ? (scan.GameWindowIsFullscreen ? "Game window found (fullscreen)" : "Game window found (windowed)")
                    : "Fullscreen window found";
                string note = outsideKnownFolder ? $"; not from the game folder, waiting {OutsideFolderExtraSeconds} s longer" : string.Empty;
                Log(() => $"{kind} for \"{GameName}\" after {SecondsSinceShown:0.0} s: {_detector.Describe(hWnd)}{note}");
            }

            if (scan.IgnoredProgramFillsScreen)
            {
                return;
            }

            // The delay counts from the later of: the game window appearing, the ignored program leaving the screen.
            DateTime readySince = _candidateSince > _ignoredProgramLastSeen ? _candidateSince : _ignoredProgramLastSeen;
            int requiredSeconds = _options.DetectionDelaySeconds + (outsideKnownFolder ? OutsideFolderExtraSeconds : 0);
            bool stableLongEnough = (now - readySince).TotalSeconds >= requiredSeconds;
            bool shownLongEnough = (now - _shownAt).TotalSeconds >= _options.MinimumDisplaySeconds;
            if (stableLongEnough && shownLongEnough)
            {
                _detectedGameWindow = hWnd;
                Log(() => $"Closing splash for \"{GameName}\", the game window is ready.");
                FadeOutAndClose();
            }
        }

        // Kept apart from _timers: starting the regular countdown must not switch the pad off.
        private void StartGamepad()
        {
            try
            {
                _gamepad = new GamepadWatcher();
                _gamepadTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = GamepadPollInterval };
                _gamepadTimer.Tick += (s, e) =>
                {
                    if (_closing || _gamepad == null)
                    {
                        return;
                    }

                    int controller = _gamepad.PollForBPress(acceptPress: SecondsSinceShown >= InputGraceSeconds);
                    if (controller >= 0)
                    {
                        Log(() => $"Splash for \"{GameName}\" closed with gamepad button B (controller {controller + 1}).");
                        FadeOutAndClose();
                    }
                };
                _gamepadTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Gamepad support could not be started.");
                StopGamepad();
            }
        }

        private void StopGamepad()
        {
            _gamepadTimer?.Stop();
            _gamepadTimer = null;
            _gamepad = null;
        }

        /// <summary>Diagnostic entry; the message is not even built unless logging is enabled.</summary>
        private void Log(Func<string> message)
        {
            if (_options.DiagnosticLogging)
            {
                Logger.Info(message());
            }
        }

        private void StartTimer(TimeSpan interval, Action onTick, bool repeat = false)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = interval };
            timer.Tick += (s, e) =>
            {
                if (!repeat)
                {
                    timer.Stop();
                }

                onTick();
            };
            _timers.Add(timer);
            timer.Start();
        }

        private void StopTimers()
        {
            foreach (DispatcherTimer timer in _timers)
            {
                timer.Stop();
            }

            _timers.Clear();
        }

        /// <summary>Borderless, maximized, topmost black window.</summary>
        private static Window CreateCoverWindow(UIElement content)
        {
            return new Window
            {
                WindowStyle = WindowStyle.None,
                WindowState = WindowState.Maximized,
                Topmost = true,
                Background = Brushes.Black,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Opacity = 0.0,
                Content = content
            };
        }
    }
}
