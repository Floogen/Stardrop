using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
