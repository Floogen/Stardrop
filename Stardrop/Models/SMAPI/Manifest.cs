using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models.SMAPI
{
    class Manifest
    {
        // Based on SMAPI's Manfiest.cs: https://github.com/Pathoschild/SMAPI/blob/c10685b03574e967c1bf48aafc814f60196812ec/src/SMAPI.Toolkit/Serialization/Models/Manifest.cs

        /// <summary>The mod name.</summary>
        public string Name { get; set; }

        /// <summary>A brief description of the mod.</summary>
        public string Description { get; set; }

        /// <summary>The mod author's name.</summary>
        public string Author { get; set; }

        /// <summary>The mod version.</summary>
        public string Version { get; set; }

        /// <summary>The unique mod ID.</summary>
        public string UniqueID { get; set; }

        // <summary>Custom property for Stardrop.</summary>
        public bool DeleteOldVersion { get; set; }
    }
}
