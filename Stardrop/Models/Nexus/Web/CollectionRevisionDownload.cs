using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Web
{
    public class CollectionRevisionDownloadLink
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("short_name")]
        public string ShortName { get; set; }

        [JsonPropertyName("URI")]
        public string Uri { get; set; }
    }
    
    public class CollectionRevisionDownloadResult
    {
        [JsonPropertyName("download_links")]
        public List<CollectionRevisionDownloadLink> DownloadLinks { get; set; }
    }
}
