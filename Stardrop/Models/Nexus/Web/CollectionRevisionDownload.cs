using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Web
{
    // The revision's downloadLink is not the archive itself. Fetching it returns this list of CDN mirrors,
    // one of which holds the actual .7z
    public class CollectionRevisionDownloadLink
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("short_name")]
        public string? ShortName { get; set; }

        [JsonPropertyName("URI")]
        public string? Uri { get; set; }
    }

    public class CollectionRevisionDownloadResult
    {
        [JsonPropertyName("download_links")]
        public List<CollectionRevisionDownloadLink>? DownloadLinks { get; set; }
    }
}
