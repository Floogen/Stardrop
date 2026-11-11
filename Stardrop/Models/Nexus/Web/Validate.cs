using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Web
{
    public class Validate
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("is_premium")]
        public bool IsPremium { get; set; }

        [JsonPropertyName("profile_url")]
        public string ProfileUrl { get; set; } = null!;

        [JsonPropertyName("message")]
        public string Message { get; set; } = null!;
    }
}