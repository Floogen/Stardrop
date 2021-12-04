using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models
{
    public class Profile
    {
        public string Name { get; set; }
        public List<string> EnabledModIds { get; set; }

        public Profile()
        {
            Name = "Unknown";
            EnabledModIds = new List<string>();
        }

        public Profile(string name, List<string>? enabledMods = null)
        {
            Name = name;
            EnabledModIds = enabledMods is null ? new List<string>() : enabledMods;
        }
    }
}
