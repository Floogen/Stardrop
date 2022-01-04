using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stardrop.Models
{
    public class Profile
    {
        public string Name { get; set; }
        public bool IsProtected { get; set; }
        public List<string> EnabledModIds { get; set; }
        public Dictionary<string, JsonDocument> PreservedModConfigs { get; set; }

        public Profile()
        {
            Name = "Unknown";
            IsProtected = false;
            EnabledModIds = new List<string>();
            PreservedModConfigs = new Dictionary<string, JsonDocument>();
        }

        public Profile(string name, bool isProtected = false, List<string>? enabledMods = null, Dictionary<string, JsonDocument>? preservedModConfigs = null)
        {
            Name = name;
            IsProtected = isProtected;
            EnabledModIds = enabledMods is null ? new List<string>() : enabledMods;
            PreservedModConfigs = preservedModConfigs is null ? new Dictionary<string, JsonDocument>() : preservedModConfigs;
        }

        public Profile ShallowCopy()
        {
            return (Profile)this.MemberwiseClone();
        }
    }
}
