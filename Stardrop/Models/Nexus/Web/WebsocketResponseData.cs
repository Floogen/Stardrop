using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Web
{
    public class WebsocketResponseData
    {
        [JsonPropertyName("connection_token")]
        public string? ConnectionToken { get; set; }

        [JsonPropertyName("api_key")]
        public string? ApiKey { get; set; }
    }
}
