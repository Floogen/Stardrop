using System.Collections.Generic;
using System.Text.Json;

namespace Stardrop.Models
{
    public record ModNote(string UniqueId, string Note);

    public class Profile
    {
        public string Name { get; set; }
        public bool IsProtected { get; set; }
        public List<string> EnabledModIds { get; set; }
        public Dictionary<string, JsonDocument> PreservedModConfigs { get; set; }
        public List<ModNote> Notes { get; set; }

        public Profile()
        {
            Name = "Unknown";
            IsProtected = false;
            EnabledModIds = new List<string>();
            PreservedModConfigs = new Dictionary<string, JsonDocument>();
            Notes = new List<ModNote>();
        }

        public Profile(string name, bool isProtected = false, List<string>? enabledMods = null, Dictionary<string, JsonDocument>? preservedModConfigs = null, List<ModNote> notes = null)
        {
            Name = name;
            IsProtected = isProtected;
            EnabledModIds = enabledMods is null ? new List<string>() : enabledMods;
            PreservedModConfigs = preservedModConfigs is null ? new Dictionary<string, JsonDocument>() : preservedModConfigs;
            Notes = notes is null ? new List<ModNote>() : notes;
        }

        public Profile ShallowCopy()
        {
            return (Profile)this.MemberwiseClone();
        }
    }
}
