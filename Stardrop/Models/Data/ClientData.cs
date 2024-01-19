using System;
using System.Collections.Generic;

namespace Stardrop.Models.Data
{
    public class ClientData
    {
        public List<ModInstallData> ModInstallData { get; set; }
        public Dictionary<string, bool> ColumnActiveStates { get; set; } = new Dictionary<string, bool>();
    }
}
