using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus;
using Stardrop.Models.SMAPI;

namespace Stardrop.Models
{
    public class Settings
    {
        public string Theme { get; set; } = "Stardrop";
        public string Language { get; set; }
        public string Version { get; set; }
        public string LastSelectedProfileName { get; set; }
        public string SMAPIFolderPath { get; set; }
        public string ModFolderPath { get; set; }
        public string ModInstallPath { get; set; }
        public string FolderForModsToAddPath { get; set; }
        public bool IgnoreHiddenFolders { get; set; } = true;
        public bool EnableProfileSpecificModConfigs { get; set; }
        public bool ShouldWriteToModConfigs { get; set; }
        public bool EnableModsOnAdd { get; set; }
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
