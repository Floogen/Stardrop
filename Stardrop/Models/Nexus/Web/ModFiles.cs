using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Web
{
    public class ModFiles
    {
        [JsonPropertyName("files")]
        public List<ModFile> Files { get; set; }
    }
}
