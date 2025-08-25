using System;
using System.Text.RegularExpressions;
using Stardrop.Utilities.External;

namespace Stardrop.Models.Nexus.Web
{
    public class NXM
    {
        public enum NXMPurpose
        {
            Mod,
            Collection,
            Unknown
        }

        public static NXMPurpose CalculatePurpose(NXM nxm)
        {
            if (nxm.Link is null) return NXMPurpose.Unknown;

            var modMatch = Regex.Match(Regex.Unescape(nxm.Link), NexusClient._nxmModPattern);
            var collectionMatch = Regex.Match(Regex.Unescape(nxm.Link), NexusClient._nxmCollectionPattern);

            if (modMatch.Success) return NXMPurpose.Mod;
            if (collectionMatch.Success) return NXMPurpose.Collection;

            return NXMPurpose.Unknown;
        }


        public string? Link { get; set; }
        public DateTime Timestamp { get; set; }

        public NXMPurpose? Purpose { get; set;  }
    }
}
