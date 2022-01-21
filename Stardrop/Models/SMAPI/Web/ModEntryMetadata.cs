using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Stardrop.Models.SMAPI.Web
{
    public class ModEntryMetadata
    {
        // Based on SMAPI's WikiCompatibilityStatus.cs: https://github.com/Pathoschild/SMAPI/blob/develop/src/SMAPI.Toolkit/Framework/Clients/Wiki/WikiCompatibilityStatus.cs
        /// <summary>The compatibility status for a mod.</summary>
        public enum WikiCompatibilityStatus
        {
            /// <summary>The status is unknown.</summary>
            Unknown,

            /// <summary>The mod is compatible.</summary>
            Ok,

            /// <summary>The mod is compatible if you use an optional official download.</summary>
            Optional,

            /// <summary>The mod is compatible if you use an unofficial update.</summary>
            Unofficial,

            /// <summary>The mod isn't compatible, but the player can fix it or there's a good alternative.</summary>
            Workaround,

            /// <summary>The mod isn't compatible.</summary>
            Broken,

            /// <summary>The mod is no longer maintained by the author, and an unofficial update or continuation is unlikely.</summary>
            Abandoned,

            /// <summary>The mod is no longer needed and should be removed.</summary>
            Obsolete
        }

        // Based on SMAPI's ModExtendedMetadataModel.cs: https://github.com/Pathoschild/SMAPI/blob/develop/src/SMAPI.Toolkit/Framework/Clients/WebApi/ModExtendedMetadataModel.cs
        /// <summary>The mod's display name.</summary>
        public string Name { get; set; }

        /// <summary>The main version.</summary>
        public ModEntryVersion Main { get; set; }

        /// <summary>The latest unofficial version, if newer than <see cref="Main"/> and <see cref="Optional"/>.</summary>
        public ModEntryVersion Unofficial { get; set; }
        public string CustomUrl { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WikiCompatibilityStatus CompatibilityStatus { get; set; }

        /// <summary>The human-readable summary of the compatibility status or workaround, without HTML formatting.</summary>
        public string CompatibilitySummary { get; set; }
    }
}
