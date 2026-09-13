using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Stardrop.Utilities.Internal
{
    /// <summary>
    /// Loads theme files from the Themes folder, backfilling any Stardrop brush the theme does not define
    /// from Defaults.xaml. Without this a theme that omits a key leaves the binding unresolved,
    /// which fails silently rather than falling back to anything.
    /// </summary>
    public static class ThemeManager
    {
        public const string THEMES_FOLDER_NAME = "Themes";
        public const string DEFAULTS_FILE_NAME = "Defaults";
        public const string THEME_FILE_EXTENSION = ".xaml";
        public const string THEME_FILE_SEARCH_PATTERN = "*" + THEME_FILE_EXTENSION;

        // The active theme always occupies the first slot of Application.Current.Styles
        public const int THEME_STYLE_INDEX = 0;

        private static string? _defaultsFileText;
        private static bool _hasCheckedForDefaultsFile;

        public static string GetThemesFolderPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, THEMES_FOLDER_NAME);
        }

        /// <summary>
        /// Returns every selectable theme file in folder order, excluding the shared defaults file
        /// </summary>
        public static IEnumerable<string> GetThemeFilePaths()
        {
            var themesFolderPath = GetThemesFolderPath();
            if (Directory.Exists(themesFolderPath) is false)
            {
                return Enumerable.Empty<string>();
            }

            return Directory.EnumerateFiles(themesFolderPath, THEME_FILE_SEARCH_PATTERN, SearchOption.AllDirectories).Where(fileFullName => IsDefaultsFile(fileFullName) is false);
        }

        public static bool IsDefaultsFile(string fileFullName)
        {
            return Path.GetFileNameWithoutExtension(fileFullName).Equals(DEFAULTS_FILE_NAME, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses a theme file and backfills anything it does not define from the defaults file
        /// </summary>
        public static Styles Load(string fileFullName)
        {
            var theme = AvaloniaRuntimeXamlLoader.Parse<Styles>(File.ReadAllText(fileFullName));
            ApplyDefaults(theme);

            return theme;
        }

        /// <summary>
        /// Copies any default resource the given theme does not already provide into that theme
        /// </summary>
        public static void ApplyDefaults(Styles theme)
        {
            var defaults = ParseDefaults();
            if (defaults is null)
            {
                return;
            }

            foreach (var key in defaults.Resources.Keys.ToList())
            {
                // A theme may declare resources at the Styles level or inside a child Style, so ask the theme as a whole
                if (((IResourceProvider)theme).TryGetResource(key, out _))
                {
                    continue;
                }

                theme.Resources[key] = defaults.Resources[key];
            }
        }

        /// <summary>
        /// Parses a fresh copy of the defaults, so brush instances are never shared between themes
        /// </summary>
        private static Styles? ParseDefaults()
        {
            var defaultsFileText = GetDefaultsFileText();
            if (String.IsNullOrEmpty(defaultsFileText))
            {
                return null;
            }

            try
            {
                return AvaloniaRuntimeXamlLoader.Parse<Styles>(defaultsFileText);
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to parse the theme defaults: {ex}", Helper.Status.Warning);
                _defaultsFileText = null;

                return null;
            }
        }

        private static string? GetDefaultsFileText()
        {
            if (_hasCheckedForDefaultsFile)
            {
                return _defaultsFileText;
            }
            _hasCheckedForDefaultsFile = true;

            var defaultsFilePath = Path.Combine(GetThemesFolderPath(), $"{DEFAULTS_FILE_NAME}{THEME_FILE_EXTENSION}");
            if (File.Exists(defaultsFilePath) is false)
            {
                Program.helper.Log($"No theme defaults found at {defaultsFilePath}, themes will fall back to nothing for any brush they omit", Helper.Status.Warning);

                return null;
            }

            _defaultsFileText = File.ReadAllText(defaultsFilePath);

            return _defaultsFileText;
        }
    }
}