using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models.SMAPI.Web
{
    public class ModEntry
    {
        // Based on SMAPI's ModEntryModel.cs: https://github.com/Pathoschild/SMAPI/blob/develop/src/SMAPI.Toolkit/Framework/Clients/WebApi/ModEntryModel.cs

        /// <summary>The mod's unique ID.</summary>
        public string Id { get; set; }

        /// <summary>The update version recommended by the web API based on its version update and mapping rules.</summary>
        public ModEntryVersion SuggestedUpdate { get; set; }

        /// <summary>Optional extended data which isn't needed for update checks.</summary>
        public ModEntryMetadata Metadata { get; set; }

        /// <summary>The errors that occurred while fetching update data.</summary>
        public string[] Errors { get; set; } = new string[0];
    }
}
