using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.GraphQL
{
    public class ScannedModFile
    {
        [JsonConverter(typeof(JsonStringEnumConverter<VirusScanStatus>))]
        public enum VirusScanStatus
        {
            NOT_SCANNED,
            QUEUED,
            WAITING_REPORT,
            VERIFIED,
            INTERNALLY_VERIFIED,
            QUARANTINED,
            MANUALLY_VERIFIED,
            MOD_DOES_NOT_EXIST,
            FILE_NOT_FOUND,
            REPORT_ERROR,
            TOO_LARGE
        }

        [JsonPropertyName("fileId")]
        public int Id { get; set; }

        [JsonPropertyName("scannedV2")]
        public VirusScanStatus VirusScanResults { get; set; }
    }
}
