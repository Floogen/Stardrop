using Stardrop.Models.Data.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Stardrop.Models.Nexus.Web
{
    public class Endorsement
    {
        [JsonPropertyName("mod_id")]
        public int Id { get; set; }

        [JsonPropertyName("domain_name")]
        public string? DomainName { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        public bool IsEndorsed()
        {
            if (Status?.ToUpper() == "ENDORSED")
            {
                return true;
            }

            return false;
        }
    }
}
