using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Web
{
    public class WebsocketResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public WebsocketResponseData? Data { get; set; }
    }
}
