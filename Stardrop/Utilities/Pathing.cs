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
        internal static string defaultGamePath = @"E:\SteamLibrary\steamapps\common\Stardew Valley\";
        internal static string defaultModPath = @"E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\";
        internal static string defaultHomePath = @"E:\SteamLibrary\steamapps\common\Stardew Valley\Stardrop\";
        internal static string smapiLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs");

        internal static void EstablishPaths()
        {
            // TODO: Implement this!
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
