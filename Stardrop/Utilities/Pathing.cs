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
        internal const string relativeDataPath = @"Data\";
        internal const string relativeLogPath = @"Logs\";

        internal static string defaultGamePath;
        internal static string defaultModPath;
        internal static string defaultHomePath;
        internal static string smapiLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs");

        internal static void EstablishPaths(string homePath, string smapiPath)
        {
            SetHomePath(homePath);
            SetModPath(smapiPath);
        }

        internal static void SetHomePath(string homePath)
        {
            defaultHomePath = Path.Combine(homePath, relativeDataPath);
        }

        internal static void SetModPath(string smapiPath)
        {
            if (smapiPath is not null)
            {
                defaultGamePath = smapiPath;
                defaultModPath = Path.Combine(smapiPath, "Mods");
            }
        }

        internal static string GetLogFolderPath()
        {
            return Path.Combine(defaultHomePath, relativeLogPath);
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
            Program.helper.Log(Path.Combine(defaultGamePath, "StardewModdingAPI.exe"));
            return Path.Combine(defaultGamePath, "StardewModdingAPI.exe");
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
    }
}
