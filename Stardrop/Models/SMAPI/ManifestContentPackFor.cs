using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models.SMAPI
{
    public class ManifestContentPackFor
    {
        // Based on SMAPI's IManifestContentPackFor.cs: https://github.com/Pathoschild/SMAPI/blob/develop/src/SMAPI.Toolkit.CoreInterfaces/IManifestContentPackFor.cs

        /// <summary>The unique ID of the mod which can read this content pack.</summary>
        public string UniqueID { get; set; }

        /// <summary>The minimum required version (if any).</summary>
        public string MinimumVersion { get; set; }
    }
}
