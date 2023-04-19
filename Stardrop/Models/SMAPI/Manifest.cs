using Stardrop.Models.SMAPI.Converters;
using System.Text.Json.Serialization;

namespace Stardrop.Models.SMAPI
{
    public class Manifest
    {
        // Based on SMAPI's Manfiest.cs: https://github.com/Pathoschild/SMAPI/blob/c10685b03574e967c1bf48aafc814f60196812ec/src/SMAPI.Toolkit/Serialization/Models/Manifest.cs

        /// <summary>The mod name.</summary>
        public string Name { get; set; }

        /// <summary>A brief description of the mod.</summary>
        public string Description { get; set; }

        /// <summary>The namespaced mod IDs to query for updates (like <c>Nexus:541</c>).</summary>
        [JsonConverter(typeof(ModKeyConverter))]
        public string[] UpdateKeys { get; set; }

        /// <summary>The mod author's name.</summary>
        public string Author { get; set; }

        /// <summary>The mod version.</summary>
        public string Version { get; set; }

        /// <summary>The unique mod ID.</summary>
        public string UniqueID { get; set; }

        /// <summary>The mod which will read this as a content pack. Mutually exclusive with <see cref="Manifest.EntryDll"/>.</summary>
        //[JsonConverter(typeof(ManifestContentPackForConverter))]
        public ManifestContentPackFor ContentPackFor { get; set; }

        /// <summary>The other mods that must be loaded before this mod.</summary>
        //[JsonConverter(typeof(ManifestDependencyArrayConverter))]
        public ManifestDependency[] Dependencies { get; set; }

        // <summary>Custom property for Stardrop.</summary>
        public bool DeleteOldVersion { get; set; }
    }
}
