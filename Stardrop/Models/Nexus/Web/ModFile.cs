using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Stardrop.Models.Nexus.Web
{
    public class ModFile
    {
        [JsonPropertyName("file_id")]
        public int Id { get; set; }

        [JsonPropertyName("file_name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("category_name")]
        public string? Category { get; set; }
    }
}
