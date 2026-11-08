using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SplashAddonPlus
{
    /// <summary>
    /// The game's installation folder, used to recognise the game's own windows.
    /// Folders that are too general (a drive root, Program Files, the user profile, ...) are rejected,
    /// otherwise unrelated programs would be taken for the game.
    /// </summary>
    internal static class GameFolder
    {
        // "C:\something" or "\\server\share\something": at least one folder below the root.
        private static readonly Regex UsablePath = new Regex(@"^([A-Za-z]:\\[^\\]+|\\\\[^\\]+\\[^\\]+\\[^\\]+)", RegexOptions.Compiled);

        // Folders that hold many games (library roots), never a single game.
        private static readonly string[] LibraryFolderEndings =
        {
            @"\steamapps", @"\steamapps\common", @"\WindowsApps", @"\XboxGames", @"\Epic Games", @"\GOG Games",
            @"\Games", @"\Program Files", @"\Program Files (x86)"
        };

        /// <summary>Normalised folder (backslashes, no trailing separator), or null when it cannot be used.</summary>
        public static string Normalize(string folder, IEnumerable<string> forbiddenFolders = null)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }

            string path = folder.Trim().Trim('"');
            if (Path.DirectorySeparatorChar == '\\')
            {
                try
                {
                    path = Path.GetFullPath(path);
                }
                catch
                {
                    return null;
                }
            }

            path = Clean(path);
            if (!UsablePath.IsMatch(path))
            {
                return null;
            }

            if (LibraryFolderEndings.Any(ending => path.EndsWith(ending, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            foreach (string forbidden in forbiddenFolders ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(forbidden) && string.Equals(path, Clean(forbidden), StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return path;
        }

        /// <summary>True when <paramref name="filePath"/> lies inside <paramref name="folder"/> (any depth).</summary>
        public static bool Contains(string folder, string filePath)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            return Clean(filePath).StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>System folders that can never be a single game's folder.</summary>
        public static IEnumerable<string> SystemFolders()
        {
            var folders = new List<string>();
            foreach (Environment.SpecialFolder special in new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments
            })
            {
                try
                {
                    string path = Environment.GetFolderPath(special);
                    if (!string.IsNullOrEmpty(path))
                    {
                        folders.Add(path);
                    }
                }
                catch
                {
                }
            }

            return folders;
        }

        private static string Clean(string path)
        {
            return path.Replace('/', '\\').TrimEnd('\\');
        }
    }
}
