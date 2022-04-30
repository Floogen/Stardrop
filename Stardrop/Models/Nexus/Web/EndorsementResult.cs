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

        public EndorsementState GetEndorsementState()
        {
            if (Enum.TryParse(typeof(EndorsementState), Status, out var state) && state is not null)
            {
                return (EndorsementState)state;
            }

            return EndorsementState.Undetermined;
        }
    }
}
