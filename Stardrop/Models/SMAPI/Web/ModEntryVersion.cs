using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models.SMAPI.Web
{
    public class ModEntryVersion
    {
        // Based on SMAPI's ModEntryVersionModel.cs: https://github.com/Pathoschild/SMAPI/blob/develop/src/SMAPI.Toolkit/Framework/Clients/WebApi/ModEntryVersionModel.cs

        /*********
        ** Accessors
        *********/
        /// <summary>The version number.</summary>
        public string Version { get; set; }

        /// <summary>The mod page URL.</summary>
        public string Url { get; set; }


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        public ModEntryVersion() { }

        /// <summary>Construct an instance.</summary>
        /// <param name="version">The version number.</param>
        /// <param name="url">The mod page URL.</param>
        public ModEntryVersion(string version, string url)
        {
            this.Version = version;
            this.Url = url;
        }
    }
}
