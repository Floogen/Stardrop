using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Utilities
{
    public static class Pathing
    {
        internal const string relativeSettingsPath = @"Settings\";
        internal const string relativeLogPath = @"Logs\";

        internal static string defaultGamePath;
        internal static string defaultModPath;
        internal static string defaultHomePath;
        internal static string smapiLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs");

        internal static void EstablishPaths(string homePath, string smapiPath)
        {
            defaultHomePath = homePath;
            SetModPath(smapiPath);
        }

        internal static void SetModPath(string smapiPath)
        {
            if (smapiPath is not null)
            {
                defaultGamePath = smapiPath;
                defaultModPath = Path.Combine(smapiPath, "Mods");
            }
        }

        internal static string GetSettingsPath()
        {
            return Path.Combine(relativeSettingsPath, "settings.json");
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
            return Path.Combine(defaultGamePath, "StardewModdingAPI.exe");
        }

        public static string GetCacheFolderPath()
        {
            return Path.Combine(defaultHomePath, "Cache");
        }

        public static string GetVersionCachePath()
        {
            return Path.Combine(defaultHomePath, "Cache", "Versions.json");
        }
    }
}
