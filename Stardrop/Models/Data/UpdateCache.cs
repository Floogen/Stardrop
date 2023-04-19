using System;
using System.Collections.Generic;

namespace Stardrop.Models.Data
{
    public class UpdateCache
    {
        public DateTime LastRuntime { get; set; }
        public List<ModUpdateInfo> Mods { get; set; }

        public UpdateCache(DateTime lastRuntime)
        {
            LastRuntime = lastRuntime;
            Mods = new List<ModUpdateInfo>();
        }
    }
}
