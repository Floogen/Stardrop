using System.ComponentModel;

namespace Stardrop.Models.Data.Enums
{
    public enum NexusServers
    {
        [Description("Nexus CDN")]
        NexusCDN,
        Chicago,
        Paris,
        Amsterdam,
        Prague,
        [Description("Los Angeles")]
        LosAngeles,
        Miami,
        Singapore
    }
}
