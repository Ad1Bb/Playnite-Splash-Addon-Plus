using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SplashAddonPlus
{
    /// <summary>Where the game logo sits on the splash screen.</summary>
    public enum LogoPlacement
    {
        BottomLeft,
        BottomCenter,
        BottomRight,
        Center,
        TopLeft,
        TopRight
    }

    /// <summary>One row of the per-platform display times.</summary>
    public class PlatformDuration : ObservableObject
    {
        private string _platform = string.Empty;
        private int _seconds = 10;

        [JsonProperty("Platform")]
        public string Platform { get => _platform; set => SetValue(ref _platform, value, "Platform"); }

        [JsonProperty("Seconds")]
        public int Seconds { get => _seconds; set => SetValue(ref _seconds, value, "Seconds"); }
    }

    /// <summary>One game in the per-game lists of the settings page.</summary>
    internal sealed class GameEntry
    {
        public string Id { get; set; }

        /// <summary>Name from the library, or null when the game is no longer there.</summary>
        public string Name { get; set; }
    }

    internal static class GameEntries
    {
        /// <summary>Games sorted by name; IDs no longer in the library come last.</summary>
        public static List<GameEntry> Sort(IEnumerable<string> ids, Func<string, string> resolveName)
        {
            return (ids ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => new GameEntry { Id = id, Name = SafeName(id, resolveName) })
                .OrderBy(entry => entry.Name == null)
                .ThenBy(entry => entry.Name ?? entry.Id, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string SafeName(string id, Func<string, string> resolveName)
        {
            try
            {
                string name = resolveName?.Invoke(id);
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch
            {
                return null;
            }
        }
    }
}
