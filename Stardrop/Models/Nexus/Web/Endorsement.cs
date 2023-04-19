using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Web
{
    public class EndorsementResult
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }
}
