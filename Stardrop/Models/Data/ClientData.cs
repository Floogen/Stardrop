using System.Collections.Generic;

namespace Stardrop.Models.Data
{
    public class ClientData
    {
        public List<ModInstallData> ModInstallData { get; set; }
        public Dictionary<string, bool> ColumnActiveStates { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, int> ColumnOrder { get; set; } = new Dictionary<string, int>();
        public LastSessionData LastSessionData { get; set; }

        // Mapping of mod unique id to the ignored suggested version string
        public Dictionary<string, string> IgnoredUpdates { get; set; } = new Dictionary<string, string>();
    }
}
