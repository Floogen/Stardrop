using Semver;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus;
using Stardrop.Models.SMAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models
{
    public class Settings
    {
        public string Theme { get; set; } = "Stardrop";
        public string Language { get; set; }
        public string LastSelectedProfileName { get; set; }
        public string SMAPIFolderPath { get; set; }
        public string ModFolderPath { get; set; }
        public string ModInstallPath { get; set; }
        public bool IgnoreHiddenFolders { get; set; } = true;
        public bool EnableProfileSpecificModConfigs { get; set; }
        public NexusServers PreferredNexusServer { get; set; } = NexusServers.NexusCDN;
        public bool IsAskingBeforeAcceptingNXM { get; set; } = true;
        public GameDetails GameDetails { get; set; }
        public NexusUser NexusDetails { get; set; }

        public Settings ShallowCopy()
        {
            return (Settings)this.MemberwiseClone();
        }
    }
}
