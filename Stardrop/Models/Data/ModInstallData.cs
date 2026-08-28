using System;

namespace Stardrop.Models.Data
{
    /// <summary>
    /// When a specific installed copy of a mod arrived and when it was last replaced. Identity is the same pairing
    /// the rest of the application uses, as a collection can pin a copy of a mod the user also has installed loosely
    /// and the two have their own dates.
    /// </summary>
    public class ModInstallData
    {
        public string UniqueId { get; set; }
        /// <summary>Null for a loose install, which is also how records written before this field existed read</summary>
        public string? SourceId { get; set; }
        public DateTime InstallTimestamp { get; set; }
        /// <summary>Never set for a mod installed by a collection, whose version its collection owns</summary>
        public DateTime? LastUpdateTimestamp { get; set; }

        public ModReference ToReference()
        {
            return new ModReference(UniqueId, SourceId);
        }
    }
}
