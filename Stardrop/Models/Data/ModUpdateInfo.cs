using static Stardrop.Models.SMAPI.Web.ModEntryMetadata;

namespace Stardrop.Models.Data
{
    public class ModUpdateInfo
    {
        public string UniqueId { get; set; } = null!;
        public string SuggestedVersion { get; set; } = null!;
        public WikiCompatibilityStatus Status { get; set; }
        public string Link { get; set; } = null!;


        public ModUpdateInfo()
        {

        }

        public ModUpdateInfo(string uniqueId, string recommendedVersion, WikiCompatibilityStatus status, string link)
        {
            UniqueId = uniqueId;
            SuggestedVersion = recommendedVersion;
            Status = status;
            Link = link;
        }
    }
}
