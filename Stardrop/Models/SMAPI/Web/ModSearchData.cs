using System.Collections.Generic;

namespace Stardrop.Models.SMAPI.Web
{
    class ModSearchData
    {
        // Based on SMAPI's ModSearchModel.cs: https://github.com/Pathoschild/SMAPI/blob/develop/src/SMAPI.Toolkit/Framework/Clients/WebApi/ModSearchModel.cs

        /*********
        ** Accessors
        *********/
        /// <summary>The mods for which to find data.</summary>
        public ModSearchEntry[] Mods { get; set; } = null!;

        /// <summary>Whether to include extended metadata for each mod.</summary>
        public bool IncludeExtendedMetadata { get; set; }

        /// <summary>The SMAPI version installed by the player. This is used for version mapping in some cases.</summary>
        public string ApiVersion { get; set; } = null!;

        /// <summary>The Stardew Valley version installed by the player.</summary>
        public string GameVersion { get; set; } = null!;

        /// <summary>The OS on which the player plays.</summary>
        public string Platform { get; set; } = null!;


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an empty instance.</summary>
        public ModSearchData()
        {
            // needed for JSON deserializing
        }

        /// <summary>Construct an instance.</summary>
        /// <param name="mods">The mods to search.</param>
        /// <param name="apiVersion">The SMAPI version installed by the player. If this is null, the API won't provide a recommended update.</param>
        /// <param name="gameVersion">The Stardew Valley version installed by the player.</param>
        /// <param name="platform">The OS on which the player plays.</param>
        /// <param name="includeExtendedMetadata">Whether to include extended metadata for each mod.</param>
        public ModSearchData(List<ModSearchEntry> mods, string apiVersion, string gameVersion, string platform, bool includeExtendedMetadata)
        {
            Mods = [.. mods];
            ApiVersion = apiVersion.ToString();
            GameVersion = gameVersion.ToString();
            Platform = platform;
            IncludeExtendedMetadata = includeExtendedMetadata;
        }
    }
}
