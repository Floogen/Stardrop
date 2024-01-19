using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models.Data
{
    public class ModInstallData
    {
        public string UniqueId { get; set; }
        public DateTime InstallTimestamp { get; set; }
        public DateTime? LastUpdateTimestamp { get; set; }
    }
}
