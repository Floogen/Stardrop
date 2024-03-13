using System;

namespace Stardrop.Models.Data
{
    public class ModInstallData
    {
        public string UniqueId { get; set; }
        public DateTime InstallTimestamp { get; set; }
        public DateTime? LastUpdateTimestamp { get; set; }
    }
}
