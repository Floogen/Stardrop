using System.Collections.Generic;
using System.Dynamic;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus
{
    public class CollectionInfo
    {
        [JsonPropertyName("author")]
        public string Author { get; set; }
        [JsonPropertyName("authorUrl")]
        public string AuthorUrl { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("installInstructions")]
        public string InstallInstructions { get; set; }
        [JsonPropertyName("domainName")]
        public string DomainName { get; set; }
        [JsonPropertyName("gameVersions")]
        public List<string> GameVersions { get; set; }
    }

    public class CollectionConfig
    {
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
        public string FileExpression { get; set; }
        [JsonPropertyName("fileMD5")]
        public string? FileMD5 { get; set; }
        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("versionMatch")]
        public string VersionMatch { get; set; }

        [JsonPropertyName("logicalFileName")]
        public string LogicalFileName { get; set; }
    }

    public class CollectionModRuleReference
    {
        [JsonPropertyName("fileExpression")]
        public string? FileExpression { get; set; }
        [JsonPropertyName("fileMD5")]
        public string? FileMD5 { get; set; }

        [JsonPropertyName("versionMatch")]
        public string VersionMatch { get; set; }

        [JsonPropertyName("idHint")]
        public string? IdHint { get; set; }
        [JsonPropertyName("logicalFileName")]
        public string? LogicalFileName { get; set; }
    }

    public class CollectionModRule
    {
        [JsonPropertyName("type")]
        public CollectionModRuleType Type { get; set; }

        [JsonPropertyName("source")]
        public CollectionModRuleSource Source { get; set; }

        [JsonPropertyName("reference")]
        public CollectionModRuleReference Reference { get; set; }
    }

    public class CollectionModDetails
    {
        [JsonPropertyName("category")]
        public string Category { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CollectionModSourceType
    {
        Nexus,
        Direct,
        Browse, // manual download via web browser, picky suggested ignoring this one.
        Bundle
    }

    public class CollectionModSource
    {
        [JsonPropertyName("updatePolicy")]
        public string UpdatePolicy { get; set; }
        [JsonPropertyName("type")]
        public CollectionModSourceType Type { get; set; }
        [JsonPropertyName("fileSize")]
        public int Size { get; set; }

        // "bundle" type only
        [JsonPropertyName("adultContent")]
        public bool? AdultContent { get; set; }
        [JsonPropertyName("fileExpression")]
        public string? FileExpression { get; set; }

        // "bundle" or "nexus" type only
        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        // everything except bundle only
        [JsonPropertyName("md5")]
        public string? MD5Checksum { get; set; }
        [JsonPropertyName("logicalFilename")]
        public string? LogicalFilename { get; set; }
        

        // "nexus" type only
        [JsonPropertyName("modId")]
        public int? ModId { get; set; }
        [JsonPropertyName("fileId")]
        public int? FileId { get; set; }

        // "browse" and "direct" type only
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    public class CollectionMods
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("version")]
        public string Version { get; set; }
        [JsonPropertyName("optional")]
        public bool Optional { get; set; }
        [JsonPropertyName("domainName")]
        public string DomainName { get; set; }
        [JsonPropertyName("author")]
        public string Author { get; set; }
        [JsonPropertyName("phase")]
        public int Phase { get; set; }
        [JsonPropertyName("details")]
        public CollectionModDetails Details { get; set; }
        [JsonPropertyName("source")]
        public CollectionModSource Source { get; set; }

    }

    public class CollectionIndex
    {
        [JsonPropertyName("info")]
        public CollectionInfo Info { get; set; }
        [JsonPropertyName("collectionConfig")]
        public CollectionConfig Config { get; set; }
        [JsonPropertyName("modRules")]
        public List<CollectionModRule> ModRules { get; set; }
        [JsonPropertyName("mods")]
        public List<CollectionMods> Mods { get; set; }
    }
}