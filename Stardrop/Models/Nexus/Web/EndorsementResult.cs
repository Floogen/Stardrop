using System.Text.Json.Serialization;

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
