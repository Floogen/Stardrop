using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus;
using Stardrop.Models.SMAPI;

namespace Stardrop.Models
{
    public class Settings
    {
        public string Theme { get; set; } = "Stardrop";
        public string Language { get; set; }
        public ModGrouping ModGroupingMethod { get; set; } = ModGrouping.None;
        public string Version { get; set; }
        public string LastSelectedProfileName { get; set; }
        public string SMAPIFolderPath { get; set; }
        public string ModFolderPath { get; set; }
        public string ModInstallPath { get; set; }
        public bool IgnoreHiddenFolders { get; set; } = true;
        public bool EnableProfileSpecificModConfigs { get; set; }
        public bool ShouldWriteToModConfigs { get; set; }
        public bool EnableModsOnAdd { get; set; }
        /// <summary>
        /// Whether to always ask before deleting a previous version of a mod when updating the mod.
        /// </summary>
        public bool AlwaysAskToDelete { get; set; } = true;
        public bool ShouldAutomaticallySaveProfileChanges { get; set; } = true;
        public bool ShowModThumbnails { get; set; }
        public NexusServers PreferredNexusServer { get; set; } = NexusServers.NexusCDN;
        public bool IsAskingBeforeAcceptingNXM { get; set; } = true;
        /// <summary>Whether a collection entry's link asks Nexus Mods for the mod manager download rather than the plain file page</summary>
        public bool UseNXMLinks { get; set; } = true;
        public GameDetails GameDetails { get; set; }
        public NexusUser NexusDetails { get; set; }

        public Settings ShallowCopy()
        {
            return (Settings)this.MemberwiseClone();
        }
    }
}
