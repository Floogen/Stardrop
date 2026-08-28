using Stardrop.Utilities.External;
using System;
using System.Text.RegularExpressions;

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

        public string? Link { get; set; }
        public DateTime Timestamp { get; set; }
        public NXMPurpose? Purpose { get; set; }

        /// <summary>
        /// Works out whether the link points at a single mod file or at a collection revision.
        /// </summary>
        public static NXMPurpose CalculatePurpose(NXM nxm)
        {
            if (String.IsNullOrEmpty(nxm.Link))
            {
                return NXMPurpose.Unknown;
            }

            var unescapedLink = Regex.Unescape(nxm.Link);
            if (Regex.IsMatch(unescapedLink, NexusClient.NxmModPattern))
            {
                return NXMPurpose.Mod;
            }

            if (Regex.IsMatch(unescapedLink, NexusClient.NxmCollectionPattern))
            {
                return NXMPurpose.Collection;
            }

            return NXMPurpose.Unknown;
        }

        /// <summary>
        /// Sets Purpose from the link, if it has not already been resolved.
        /// </summary>
        public NXMPurpose ResolvePurpose()
        {
            if (Purpose is null)
            {
                Purpose = CalculatePurpose(this);
            }

            return Purpose.Value;
        }
    }
}
