using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Web
{
    using System.Text.Json.Serialization;

    public class CollectionResult
    {
        [JsonPropertyName("collection")]
        public Collection Collection { get; set; }
    }

    public class Collection
    {
        [JsonPropertyName("gameId")]
        public int GameId { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        [JsonPropertyName("latestPublishedRevision")]
        public LatestPublishedRevision LatestPublishedRevision { get; set; }
    }

    public class LatestPublishedRevision
    {
        [JsonPropertyName("downloadLink")]
        public string DownloadLink { get; set; }
    }
}
