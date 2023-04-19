using System.Text.Json.Serialization;

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
