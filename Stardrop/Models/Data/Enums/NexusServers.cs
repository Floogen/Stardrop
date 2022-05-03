using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
