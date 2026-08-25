using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Stardrop.Utilities
{
    public static class Pathing
    {
        internal static string defaultGamePath;
        internal static string defaultModPath;
        internal static string defaultHomePath;

        internal static void SetHomePath(string homePath)
        {
            defaultHomePath = Path.Combine(homePath, "Stardrop", "Data");
        }

        internal static void SetSmapiPath(string smapiPath, bool useDefaultModPath = false)
        {
            if (smapiPath is not null)
            {
                defaultGamePath = smapiPath;

                if (useDefaultModPath)
                {
                    defaultModPath = Path.Combine(smapiPath, "Mods");
                }
            }
        }

        internal static void SetModPath(string modPath)
        {
            if (modPath is not null)
            {
                defaultModPath = modPath;
            }
        }

        internal static string GetLogFolderPath()
        {
            return Path.Combine(defaultHomePath, "Logs");
        }

        internal static string GetSettingsPath()
        {
            return Path.Combine(defaultHomePath, "Settings.json");
        }

        public static string GetProfilesFolderPath()
        {
            return Path.Combine(defaultHomePath, "Profiles");
        }

        public static string GetSelectedModsFolderPath()
        {
            return Path.Combine(defaultHomePath, "Selected Mods");
        }

        public static string GetSmapiPath()
        {
            return Path.Combine(defaultGamePath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "StardewModdingAPI.exe" : "StardewModdingAPI.dll");
        }

        internal static string GetSmapiLogFolderPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "StardewValley", "ErrorLogs");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs");
        }

        public static string GetCacheFolderPath()
        {
            return Path.Combine(defaultHomePath, "Cache");
        }

        public static string GetVersionCachePath()
        {
            return Path.Combine(GetCacheFolderPath(), "Versions.json");
        }

        internal static string GetKeyCachePath()
        {
            return Path.Combine(GetCacheFolderPath(), "Keys.json");
        }

        internal static string GetDataCachePath()
        {
            return Path.Combine(GetCacheFolderPath(), "Data.json");
        }

        public static string GetNotionCachePath()
        {
            return Path.Combine(GetCacheFolderPath(), "Notion.json");
        }

        public static string GetLinksCachePath()
        {
            return Path.Combine(GetCacheFolderPath(), "Links.json");
        }

        public static string GetNexusPath()
        {
            return Path.Combine(defaultHomePath, "Nexus");
        }

        public static string GetThumbnailsPath()
        {
            return Path.Combine(defaultHomePath, "Thumbnails", "Nexus");
        }

        public static string GetSmapiUpgradeFolderPath()
        {
            return Path.Combine(defaultHomePath, "SMAPI");
        }

        /// <summary>
        /// Root folder for collection installs. This sits inside the scanned mod folder on purpose, so that
        /// collection mods are picked up by the existing discovery pass with no extra plumbing.
        /// </summary>
        public static string GetCollectionsFolderPath()
        {
            return Path.Combine(defaultModPath, "Stardrop Collections");
        }

        public static string GetCollectionInstallPath(string sourceId)
        {
            return Path.Combine(GetCollectionsFolderPath(), sourceId);
        }

        public static string GetCollectionsCacheFolderPath()
        {
            return Path.Combine(defaultHomePath, "Collections");
        }

        public static string GetCollectionCachePath(string sourceId)
        {
            return Path.Combine(GetCollectionsCacheFolderPath(), $"{sourceId}.json");
        }

        /// <summary>
        /// Returns the collection SourceId owning the given mod folder, or null when the mod is a loose install.
        /// </summary>
        public static string? GetCollectionSourceId(string? modDirectoryPath)
        {
            if (String.IsNullOrEmpty(modDirectoryPath) || String.IsNullOrEmpty(defaultModPath))
            {
                return null;
            }

            var collectionsRoot = GetCollectionsFolderPath();
            if (modDirectoryPath.StartsWith(collectionsRoot, StringComparison.OrdinalIgnoreCase) is false)
            {
                return null;
            }

            var relativePath = modDirectoryPath.Substring(collectionsRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (String.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            var separatorIndex = relativePath.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            return separatorIndex == -1 ? relativePath : relativePath.Substring(0, separatorIndex);
        }
    }
}