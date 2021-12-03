using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models
{
    public class Mod
    {
        public string UniqueId { get; set; }
        public SemVersion Version { get; set; }
        public string ParsedVersion { get { return Version.ToString(); } }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string Requirements { get; set; }
        public string Status { get; set; }
        public bool IsEnabled { get; set; }

        public Mod(string uniqueId, string version, string? name = null, string? description = null, string? author = null)
        {
            UniqueId = uniqueId;
            Version = SemVersion.Parse(version);
            Name = String.IsNullOrEmpty(name) ? uniqueId : name;
            Description = String.IsNullOrEmpty(description) ? String.Empty : description;
            Author = String.IsNullOrEmpty(author) ? "Unknown" : author;
        }
    }
}
