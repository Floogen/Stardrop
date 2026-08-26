using Stardrop.Models.Nexus.Converters;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus
{
    // This is the schema of collection.json, found inside the collection archive. It carries more than the GraphQL
    // revision query does (source types, update policies, checksums, install order), so it is the authority on what
    // a collection actually contains
    public class CollectionInfo
    {
        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("authorUrl")]
        public string? AuthorUrl { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("installInstructions")]
        public string? InstallInstructions { get; set; }

        [JsonPropertyName("domainName")]
        public string? DomainName { get; set; }

        [JsonPropertyName("gameVersions")]
        public List<string>? GameVersions { get; set; }
    }

    public class CollectionConfig
    {
        /// <summary>The curator's own signal that this collection expects its own profile rather than merging into an existing one</summary>
        [JsonPropertyName("recommendNewProfile")]
        public bool RecommendNewProfile { get; set; }
    }

    /// <summary>
    /// Collections carry more rule types than are meaningful here and the set grows over time. Unknown sits first
    /// so that anything unrecognised lands there rather than being mistaken for a rule Stardrop acts on.
    /// </summary>
    [JsonConverter(typeof(FlexibleEnumConverter<CollectionModRuleType>))]
    public enum CollectionModRuleType
    {
        Unknown,
        Before,
        After,
        Conflicts,
        Requires,
        Recommends,
        Provides
    }

    /// <summary>
    /// One end of a mod rule. Both ends carry the same shape in collection.json, so a rule's source and its
    /// reference are read into the same type here and matched by the same tests.
    /// </summary>
    public class CollectionModRuleReference
    {
        [JsonPropertyName("fileExpression")]
        public string? FileExpression { get; set; }

        [JsonPropertyName("fileMD5")]
        public string? FileMD5 { get; set; }

        [JsonPropertyName("versionMatch")]
        public string? VersionMatch { get; set; }

        [JsonPropertyName("idHint")]
        public string? IdHint { get; set; }

        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("logicalFileName")]
        public string? LogicalFileName { get; set; }
    }

    /// <summary>
    /// Load order and conflict rules. These do not decide which files a mod places, only which mod wins where two
    /// of them place the same file: every mod is written whole, in the order the rules impose and the last one to
    /// land takes the collision. A curator's configuration is a mod like any other under that arrangement, ordered
    /// after the mods it configures so that its files sit on top of theirs.
    /// </summary>
    public class CollectionModRule
    {
        [JsonPropertyName("type")]
        public CollectionModRuleType Type { get; set; }

        [JsonPropertyName("source")]
        public CollectionModRuleReference? Source { get; set; }

        [JsonPropertyName("reference")]
        public CollectionModRuleReference? Reference { get; set; }
    }

    public class CollectionModDetails
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CollectionModSourceType
    {
        /// <summary>Downloadable through the API, given a Premium account</summary>
        Nexus,
        /// <summary>A direct file URL somewhere other than Nexus</summary>
        Direct,
        /// <summary>Needs the user to fetch the file through a web browser</summary>
        Browse,
        /// <summary>Ships inside the collection archive itself</summary>
        Bundle
    }

    public class CollectionModSource
    {
        [JsonPropertyName("updatePolicy")]
        public string? UpdatePolicy { get; set; }

        [JsonPropertyName("type")]
        public CollectionModSourceType Type { get; set; }

        [JsonPropertyName("fileSize")]
        [JsonConverter(typeof(FlexibleInt64Converter))]
        public long? Size { get; set; }

        // "bundle" type only
        [JsonPropertyName("adultContent")]
        public bool? AdultContent { get; set; }

        [JsonPropertyName("fileExpression")]
        public string? FileExpression { get; set; }

        // "bundle" or "nexus" type only
        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        // everything except bundle
        [JsonPropertyName("md5")]
        public string? MD5Checksum { get; set; }

        [JsonPropertyName("logicalFilename")]
        public string? LogicalFilename { get; set; }

        // "nexus" type only
        [JsonPropertyName("modId")]
        [JsonConverter(typeof(FlexibleInt32Converter))]
        public int? ModId { get; set; }

        [JsonPropertyName("fileId")]
        [JsonConverter(typeof(FlexibleInt32Converter))]
        public int? FileId { get; set; }

        // "browse" and "direct" types only
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    public class CollectionMod
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("optional")]
        public bool Optional { get; set; }

        [JsonPropertyName("domainName")]
        public string? DomainName { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        /// <summary>Install order grouping. Lower phases install first</summary>
        [JsonPropertyName("phase")]
        public int Phase { get; set; }

        [JsonPropertyName("details")]
        public CollectionModDetails? Details { get; set; }

        [JsonPropertyName("source")]
        public CollectionModSource? Source { get; set; }
    }

    public class CollectionIndex
    {
        [JsonPropertyName("info")]
        public CollectionInfo? Info { get; set; }

        [JsonPropertyName("collectionConfig")]
        public CollectionConfig? Config { get; set; }

        [JsonPropertyName("modRules")]
        public List<CollectionModRule>? ModRules { get; set; }

        [JsonPropertyName("mods")]
        public List<CollectionMod>? Mods { get; set; }
    }
}
