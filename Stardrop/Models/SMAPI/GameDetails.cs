using Semver;
using System;

namespace Stardrop.Models.SMAPI
{
    public class GameDetails
    {
        public enum OS
        {
            Unknown,
            Linux,
            Mac,
            Windows
        }

        /// <summary>Stardew Valley's game version.</summary>
        public string GameVersion { get; set; }

        /// <summary>SMAPI's version.</summary>
        public string SmapiVersion { get; set; }

        /// <summary>The operating system.</summary>
        public OS System { get; set; }

        public GameDetails()
        {

        }

        public GameDetails(string gameVersion, string smapiVersion, string system)
        {
            GameVersion = gameVersion;
            if (GameVersion.Contains(' '))
            {
                GameVersion = GameVersion.Split(' ')[0];
            }
            SmapiVersion = smapiVersion;

            if (system.Contains("macOS", StringComparison.OrdinalIgnoreCase))
            {
                System = OS.Mac;
            }
            else if (system.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            {
                System = OS.Windows;
            }
            else
            {
                System = OS.Linux;
            }
        }

        public bool HasSMAPIUpdated(string version)
        {
            if (String.IsNullOrEmpty(version))
            {
                return false;
            }

            return HasSMAPIUpdated(SemVersion.Parse(version));
        }

        public bool HasSMAPIUpdated(SemVersion version)
        {
            if (version is null)
            {
                return false;
            }

            return version != SemVersion.Parse(SmapiVersion);
        }

        public bool HasBadGameVersion()
        {
            if (GameVersion.Contains(' '))
            {
                return true;
            }

            return false;
        }
    }
}
