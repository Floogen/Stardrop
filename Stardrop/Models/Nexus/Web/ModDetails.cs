using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Stardrop.Models.Nexus.Web
{
    public class ModDetails
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
