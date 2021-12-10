using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
