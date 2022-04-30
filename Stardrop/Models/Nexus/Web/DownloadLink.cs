using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Stardrop.Models.Nexus.Web
{
    public class DownloadLink
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("short_name")]
        public string? ShortName { get; set; }

        [JsonPropertyName("URI")]
        public string? Uri { get; set; }
    }
}
