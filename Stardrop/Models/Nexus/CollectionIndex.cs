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

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CollectionModRuleType
    {
        Before,
        Conflicts,
        After
    }

    public class CollectionModRuleSource
    {
        [JsonPropertyName("fileExpression")]
        public string? FileExpression { get; set; }

        [JsonPropertyName("fileMD5")]
        public string? FileMD5 { get; set; }

        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("versionMatch")]
        public string? VersionMatch { get; set; }

        [JsonPropertyName("logicalFileName")]
        public string? LogicalFileName { get; set; }
    }

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

        [JsonPropertyName("logicalFileName")]
        public string? LogicalFileName { get; set; }
    }

    /// <summary>
    /// Load order and conflict rules. Stardew has no load order in the sense Vortex means, so only the Conflicts
    /// rules are of much interest here, and then only for warning the user.
    /// </summary>
    public class CollectionModRule
    {
        [JsonPropertyName("type")]
        public CollectionModRuleType Type { get; set; }

        [JsonPropertyName("source")]
        public CollectionModRuleSource? Source { get; set; }

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
        public int? ModId { get; set; }

        [JsonPropertyName("fileId")]
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
