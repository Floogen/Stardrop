using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Web
{
    public class ModDetails
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("picture_url")]
        public string? ThumbnailUrl { get; set; }
    }
}
