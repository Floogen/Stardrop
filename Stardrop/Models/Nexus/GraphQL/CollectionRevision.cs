using Stardrop.Models.Nexus.Converters;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.GraphQL
{
    // Nexus' v2 GraphQL API is still under active development and its fields can change without notice, so every
    // property here is nullable and the collection parser is expected to log and skip anything it cannot read
    public record CollectionRevisionData([property: JsonPropertyName("collectionRevision")] CollectionRevision? CollectionRevision);

    public class CollectionRevision
    {
        [JsonPropertyName("id")]
        [JsonConverter(typeof(FlexibleInt32Converter))]
        public int? Id { get; set; }

        [JsonPropertyName("revisionNumber")]
        [JsonConverter(typeof(FlexibleInt32Converter))]
        public int? RevisionNumber { get; set; }

        /// <summary>Link to the collection archive, which holds collection.json along with any bundled config files</summary>
        [JsonPropertyName("downloadLink")]
        public string? DownloadLink { get; set; }

        [JsonPropertyName("totalSize")]
        [JsonConverter(typeof(FlexibleInt64Converter))]
        public long? TotalSize { get; set; }

        [JsonPropertyName("modCount")]
        [JsonConverter(typeof(FlexibleInt32Converter))]
        public int? ModCount { get; set; }

        [JsonPropertyName("collection")]
        public CollectionSummary? Collection { get; set; }
    }

    public class CollectionSummary
    {
        [JsonPropertyName("id")]
        [JsonConverter(typeof(FlexibleInt32Converter))]
        public int? Id { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("user")]
        public CollectionUser? User { get; set; }
    }

    public class CollectionUser
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

}
