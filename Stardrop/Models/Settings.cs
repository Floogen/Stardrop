using Semver;
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
        public string LastSelectedProfileName { get; set; }
        public string SMAPIFolderPath { get; set; }
        public GameDetails GameDetails { get; set; }
    }
}
