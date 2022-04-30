using Stardrop.Models.Data.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
