using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models.SMAPI
{
    public class ManifestDependency
    {
        // Based on SMAPI's IManifestDependency.cs: https://github.com/Pathoschild/SMAPI/blob/develop/src/SMAPI.Toolkit.CoreInterfaces/IManifestDependency.cs

        /// <summary>The unique mod ID to require.</summary>
        public string UniqueID { get; set; }

        /// <summary>The minimum required version (if any).</summary>
        public string MinimumVersion { get; set; }

        /// <summary>Whether the dependency must be installed to use the mod.</summary>
        public bool IsRequired { get; set; }

        // <summary>Custom properties for Stardrop.</summary>
        public string Name { get; set; }
        public bool IsMissing { get; set; }
        public string GenericLink { get { return $"https://smapi.io/mods#{Name.Replace(" ", "_")}"; } }

        public ManifestDependency(string uniqueId, string minimumVersion, bool isRequired = false)
        {
            UniqueID = uniqueId;
            MinimumVersion = minimumVersion;
            IsRequired = isRequired;
        }
    }
}
