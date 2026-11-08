using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SplashAddonPlus
{
    /// <summary>Outcome of one pass over all top-level windows.</summary>
    internal sealed class WindowScanResult
    {
        /// <summary>
        /// Best guess for the game window, or IntPtr.Zero: a large window of a program from the game's folder
        /// if there is one, otherwise the topmost window of another process that covers a whole monitor.
        /// </summary>
        public IntPtr GameWindow { get; set; } = IntPtr.Zero;

        /// <summary>The game window belongs to a program from the game's installation folder.</summary>
        public bool GameWindowFromFolder { get; set; }

        /// <summary>The game window covers its whole monitor (false for a windowed game).</summary>
        public bool GameWindowIsFullscreen { get; set; }

        /// <summary>
        /// A program from the ignore list fills a whole screen (e.g. a launcher's own loading screen);
        /// the splash waits while this is the case.
        /// </summary>
        public bool IgnoredProgramFillsScreen { get; set; }

        /// <summary>Description of that window, for the log.</summary>
        public string IgnoredProgramWindow { get; set; }

        /// <summary>Descriptions of windows that showed up since the previous scan (for the log).</summary>
        public List<string> NewWindows { get; } = new List<string>();
    }

    /// <summary>
    /// Watches top-level windows of other processes for one splash session and finds the game window:
    /// a window covering a whole monitor (exclusive fullscreen or borderless), or, when the game's folder is known,
    /// a large window of a program from that folder (also a windowed game).
    /// </summary>
    internal sealed class GameWindowDetector
    {
        // A few pixels of slack: some borderless games size their window 1 px
        // smaller than the screen, and DPI virtualization can round coordinates.
        private const int EdgeTolerance = 2;

        private const int MaxLoggedNewWindows = 30;

        /// <summary>A window from the game's folder counts as the game when it covers at least this part of its monitor.</summary>
        public const double WindowedMinCoverage = 0.20;

        private static readonly HashSet<string> IgnoredClassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "NotifyIconOverflowWindow",
            "Windows.UI.Core.CoreWindow",
            "XamlExplorerHostIslandWindow",
            "ForegroundStaging",
            "MultitaskingViewFrame",
            "TaskListThumbnailWnd",
            "CEF-OSC-WIDGET" // NVIDIA overlay
        };

        private static readonly int OwnProcessId = Process.GetCurrentProcess().Id;

        private readonly HashSet<string> _ignoredProcesses;
        private readonly HashSet<IntPtr> _preExistingFullscreen = new HashSet<IntPtr>();
        private readonly HashSet<IntPtr> _preExistingWindows = new HashSet<IntPtr>();
        private readonly HashSet<IntPtr> _knownWindows = new HashSet<IntPtr>();
        private readonly Dictionary<uint, string> _processNames = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> _processPaths = new Dictionary<uint, string>();
        private readonly bool _collectNewWindows;
        private readonly string _gameFolder;
        private readonly bool _detectWindowed;
        private int _loggedNewWindows;

        /// <param name="gameFolder">Normalised installation folder (see <see cref="GameFolder"/>), or null when unknown.</param>
        /// <param name="detectWindowed">Accept a large, not fullscreen window from the game's folder as the game.</param>
        public GameWindowDetector(IEnumerable<string> extraIgnoredProcesses, bool collectNewWindows, string gameFolder, bool detectWindowed)
        {
            _collectNewWindows = collectNewWindows;
            _gameFolder = gameFolder;
            _detectWindowed = detectWindowed;
            _ignoredProcesses = new HashSet<string>(
                (extraIgnoredProcesses ?? Enumerable.Empty<string>())
                    .Select(NormalizeProcessName)
                    .Where(name => name.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        public bool KnowsGameFolder => _gameFolder != null;

        public static string NormalizeProcessName(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(0, trimmed.Length - 4).Trim()
                : trimmed;
        }

        /// <summary>
        /// Remembers what is on screen before the game starts, so that windows which were already
        /// there (another app in fullscreen, an overlay) are not taken for the game.
        /// </summary>
        public void TakeSnapshot()
        {
            try
            {
                NativeMethods.EnumWindows((hWnd, lParam) =>
                {
                    if (TryGetVisibleWindow(hWnd, out WindowInfo info))
                    {
                        _knownWindows.Add(hWnd);
                        _preExistingWindows.Add(hWnd);
                        if (info.CoversMonitor)
                        {
                            _preExistingFullscreen.Add(hWnd);
                        }
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                // Detection is best effort; an empty snapshot only means fewer exclusions.
            }
        }

        public WindowScanResult Scan()
        {
            var result = new WindowScanResult();
            bool folderWindowFound = false;
            try
            {
                NativeMethods.EnumWindows((hWnd, lParam) =>
                {
                    if (!TryGetVisibleWindow(hWnd, out WindowInfo info))
                    {
                        return true;
                    }

                    if (_collectNewWindows && _knownWindows.Add(hWnd) && _loggedNewWindows < MaxLoggedNewWindows)
                    {
                        _loggedNewWindows++;
                        result.NewWindows.Add(Describe(info));
                    }

                    if (!result.IgnoredProgramFillsScreen && info.CoversMonitor && !_preExistingFullscreen.Contains(hWnd)
                        && _ignoredProcesses.Contains(GetProcessName(info.ProcessId)))
                    {
                        result.IgnoredProgramFillsScreen = true;
                        result.IgnoredProgramWindow = Describe(info);
                    }

                    if (!folderWindowFound && IsFolderCandidate(info))
                    {
                        // A window of the game's own program wins over any fullscreen window found so far.
                        folderWindowFound = true;
                        result.GameWindow = hWnd;
                        result.GameWindowFromFolder = true;
                        result.GameWindowIsFullscreen = info.CoversMonitor;
                    }
                    else if (result.GameWindow == IntPtr.Zero && IsGameCandidate(info))
                    {
                        result.GameWindow = hWnd;
                        result.GameWindowIsFullscreen = true;
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }

            return result;
        }

        public string Describe(IntPtr hWnd)
        {
            return TryGetVisibleWindow(hWnd, out WindowInfo info) ? Describe(info) : "window no longer visible";
        }

        private string Describe(WindowInfo info)
        {
            string where = _gameFolder != null && IsFromGameFolder(info.ProcessId) ? ", from the game folder" : string.Empty;
            return $"{GetProcessName(info.ProcessId)} (PID {info.ProcessId}, class \"{info.ClassName}\", " +
                   $"{info.Rect.Right - info.Rect.Left}x{info.Rect.Bottom - info.Rect.Top}" +
                   $"{(info.CoversMonitor ? ", fullscreen" : $", {info.Coverage:P0} of the screen")}{where})";
        }

        /// <summary>Puts the splash back on top of the topmost band without taking focus.</summary>
        public static void KeepOnTop(IntPtr splashHandle)
        {
            try
            {
                if (splashHandle != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(splashHandle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Hands keyboard focus to the game window, but only when Playnite itself
        /// holds the foreground (e.g. because the splash screen was active).
        /// </summary>
        public static void FocusIfPlayniteHasForeground(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
                {
                    return;
                }

                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == hWnd)
                {
                    return;
                }

                if (foreground != IntPtr.Zero)
                {
                    NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundPid);
                    if (foregroundPid != OwnProcessId)
                    {
                        return;
                    }
                }

                NativeMethods.SetForegroundWindow(hWnd);
            }
            catch
            {
            }
        }

        private bool IsGameCandidate(WindowInfo info)
        {
            if (!info.CoversMonitor || _preExistingFullscreen.Contains(info.Handle))
            {
                return false;
            }

            return LooksLikeAppWindow(info);
        }

        /// <summary>A big window, not there before the game started, of a program from the game's installation folder.</summary>
        private bool IsFolderCandidate(WindowInfo info)
        {
            if (_gameFolder == null || _preExistingWindows.Contains(info.Handle))
            {
                return false;
            }

            if (!info.CoversMonitor && (!_detectWindowed || info.Coverage < WindowedMinCoverage))
            {
                return false;
            }

            return IsFromGameFolder(info.ProcessId) && LooksLikeAppWindow(info);
        }

        private bool LooksLikeAppWindow(WindowInfo info)
        {
            // Overlays and helper windows: click-through, tool windows, non-activatable.
            if ((info.ExStyle & (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE)) != 0)
            {
                return false;
            }

            if (IgnoredClassNames.Contains(info.ClassName))
            {
                return false;
            }

            return !_ignoredProcesses.Contains(GetProcessName(info.ProcessId));
        }

        private bool IsFromGameFolder(uint pid)
        {
            return _gameFolder != null && GameFolder.Contains(_gameFolder, GetProcessPath(pid));
        }

        private string GetProcessPath(uint pid)
        {
            if (_processPaths.TryGetValue(pid, out string cached))
            {
                return cached;
            }

            string path = string.Empty;
            IntPtr process = IntPtr.Zero;
            try
            {
                process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (process != IntPtr.Zero)
                {
                    var buffer = new StringBuilder(1024);
                    int size = buffer.Capacity;
                    if (NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size))
                    {
                        path = buffer.ToString();
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (process != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(process);
                }
            }

            _processPaths[pid] = path;
            return path;
        }

        private string GetProcessName(uint pid)
        {
            if (_processNames.TryGetValue(pid, out string cached))
            {
                return cached;
            }

            string name = "?";
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                {
                    name = process.ProcessName;
                }
            }
            catch
            {
            }

            _processNames[pid] = name;
            return name;
        }

        private struct WindowInfo
        {
            public IntPtr Handle;
            public uint ProcessId;
            public int ExStyle;
            public string ClassName;
            public NativeMethods.RECT Rect;
            public bool CoversMonitor;

            /// <summary>Part of the monitor (0..1) covered by the window.</summary>
            public double Coverage;
        }

        /// <summary>Visible, not minimized, not cloaked top-level window that belongs to another process.</summary>
        private static bool TryGetVisibleWindow(IntPtr hWnd, out WindowInfo info)
        {
            info = default(WindowInfo);
            if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
            {
                return false;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0 || pid == OwnProcessId)
            {
                return false;
            }

            int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
            if ((style & NativeMethods.WS_CHILD) != 0 || IsCloaked(hWnd))
            {
                return false;
            }

            if (!NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            {
                return false;
            }

            MeasureAgainstMonitor(hWnd, rect, out bool coversMonitor, out double coverage);
            info = new WindowInfo
            {
                Handle = hWnd,
                ProcessId = pid,
                ExStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE),
                ClassName = GetClassName(hWnd),
                Rect = rect,
                CoversMonitor = coversMonitor,
                Coverage = coverage
            };
            return true;
        }

        private static void MeasureAgainstMonitor(IntPtr hWnd, NativeMethods.RECT windowRect, out bool coversMonitor, out double coverage)
        {
            coversMonitor = false;
            coverage = 0;

            IntPtr monitor = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            {
                return;
            }

            NativeMethods.RECT screen = monitorInfo.rcMonitor;
            coversMonitor = windowRect.Left <= screen.Left + EdgeTolerance
                && windowRect.Top <= screen.Top + EdgeTolerance
                && windowRect.Right >= screen.Right - EdgeTolerance
                && windowRect.Bottom >= screen.Bottom - EdgeTolerance;

            coverage = CoverageOf(
                windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom,
                screen.Left, screen.Top, screen.Right, screen.Bottom);
        }

        /// <summary>Part (0..1) of the screen rectangle covered by the window rectangle.</summary>
        internal static double CoverageOf(int wl, int wt, int wr, int wb, int sl, int st, int sr, int sb)
        {
            double screenArea = (double)(sr - sl) * (sb - st);
            if (screenArea <= 0)
            {
                return 0;
            }

            double width = Math.Max(0, Math.Min(wr, sr) - Math.Max(wl, sl));
            double height = Math.Max(0, Math.Min(wb, sb) - Math.Max(wt, st));
            return Math.Min(1.0, width * height / screenArea);
        }

        private static bool IsCloaked(IntPtr hWnd)
        {
            try
            {
                return NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                    && cloaked != 0;
            }
            catch
            {
                // dwmapi missing or attribute unsupported (old Windows): treat as visible.
                return false;
            }
        }

        private static string GetClassName(IntPtr hWnd)
        {
            var buffer = new StringBuilder(256);
            return NativeMethods.GetClassName(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
        }

        private static class NativeMethods
        {
            public const int GWL_STYLE = -16;
            public const int GWL_EXSTYLE = -20;
            public const int WS_CHILD = 0x40000000;
            public const int WS_EX_TOOLWINDOW = 0x00000080;
            public const int WS_EX_TRANSPARENT = 0x00000020;
            public const int WS_EX_NOACTIVATE = 0x08000000;
            public const uint MONITOR_DEFAULTTONEAREST = 2;
            public const int DWMWA_CLOAKED = 14;
            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOACTIVATE = 0x0010;

            public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MONITORINFO
            {
                public int cbSize;
                public RECT rcMonitor;
                public RECT rcWork;
                public uint dwFlags;
            }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindowVisible(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsIconic(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

            [DllImport("user32.dll")]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

            [DllImport("user32.dll")]
            public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

            [DllImport("dwmapi.dll")]
            public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

            public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);
        }
    }
}
