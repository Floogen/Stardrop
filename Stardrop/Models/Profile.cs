using Stardrop.Models.Data;
using System.Collections.Generic;
using System.Text.Json;

namespace Stardrop.Models
{
    public record ModNote(string UniqueId, string Note);

    public class Profile
    {
        public string Name { get; set; }
        public bool IsProtected { get; set; }
        /// <summary>
        /// When set, this profile was generated from the collection with the matching SourceId and should be treated
        /// as read-only. Users wanting to tweak a collection clone it into a plain profile instead.
        /// </summary>
        public string? SourceId { get; set; }
        public List<ModReference> EnabledModIds { get; set; }
        public Dictionary<string, JsonDocument> PreservedModConfigs { get; set; }
        public List<ModNote> Notes { get; set; }

        public bool IsFromCollection => string.IsNullOrEmpty(SourceId) is false;

        public Profile()
        {
            Name = "Unknown";
            IsProtected = false;
            EnabledModIds = new List<ModReference>();
            PreservedModConfigs = new Dictionary<string, JsonDocument>();
            Notes = new List<ModNote>();
        }

        public Profile(string name, bool isProtected = false, List<ModReference>? enabledMods = null, Dictionary<string, JsonDocument>? preservedModConfigs = null, List<ModNote> notes = null, string? sourceId = null)
        {
            Name = name;
            IsProtected = isProtected;
            SourceId = sourceId;
            EnabledModIds = enabledMods is null ? new List<ModReference>() : enabledMods;
            PreservedModConfigs = preservedModConfigs is null ? new Dictionary<string, JsonDocument>() : preservedModConfigs;
            Notes = notes is null ? new List<ModNote>() : notes;
        }

        public Profile ShallowCopy()
        {
            return (Profile)this.MemberwiseClone();
        }
    }
}
