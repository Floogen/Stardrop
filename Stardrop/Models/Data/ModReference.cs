using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Data
{
    /// <summary>
    /// Identifies a specific installed copy of a mod. A mod's unique ID is not enough on its own, as a collection
    /// can pin a different version of a mod that the user also has installed loosely. SourceId is null for loose
    /// installs and is the collection's source ID for anything installed under a collection folder.
    /// </summary>
    [JsonConverter(typeof(ModReferenceConverter))]
    public record ModReference(string UniqueId, string? SourceId = null)
    {
        public bool IsFromCollection => String.IsNullOrEmpty(SourceId) is false;

        public bool Matches(Mod mod)
        {
            if (mod is null)
            {
                return false;
            }

            return Matches(mod.UniqueId, mod.SourceId);
        }

        public bool Matches(string uniqueId, string? sourceId)
        {
            if (UniqueId.Equals(uniqueId, StringComparison.OrdinalIgnoreCase) is false)
            {
                return false;
            }

            return String.Equals(SourceId, sourceId, StringComparison.OrdinalIgnoreCase);
        }

        // Manifests are inconsistent about casing, so equality has to ignore it or profiles will silently lose mods
        public virtual bool Equals(ModReference? other)
        {
            if (other is null)
            {
                return false;
            }

            return Matches(other.UniqueId, other.SourceId);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(UniqueId.ToLowerInvariant(), SourceId is null ? String.Empty : SourceId.ToLowerInvariant());
        }

        public override string ToString()
        {
            return IsFromCollection ? $"{UniqueId} ({SourceId})" : UniqueId;
        }
    }

    /// <summary>
    /// Reads both the legacy format (a bare string of the mod's unique ID) and the current object format, so existing
    /// profile files keep loading. Writes the legacy format whenever SourceId is null to avoid churning those files.
    /// </summary>
    public class ModReferenceConverter : JsonConverter<ModReference>
    {
        public override ModReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType is JsonTokenType.String)
            {
                var legacyId = reader.GetString();
                return String.IsNullOrEmpty(legacyId) ? null : new ModReference(legacyId);
            }

            if (reader.TokenType is not JsonTokenType.StartObject)
            {
                throw new JsonException($"Unexpected token {reader.TokenType} when reading a {nameof(ModReference)}");
            }

            string? uniqueId = null;
            string? sourceId = null;
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType is not JsonTokenType.PropertyName)
                {
                    continue;
                }

                var propertyName = reader.GetString();
                reader.Read();

                if (String.Equals(propertyName, nameof(ModReference.UniqueId), StringComparison.OrdinalIgnoreCase))
                {
                    uniqueId = reader.GetString();
                }
                else if (String.Equals(propertyName, nameof(ModReference.SourceId), StringComparison.OrdinalIgnoreCase))
                {
                    sourceId = reader.TokenType is JsonTokenType.Null ? null : reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }

            return String.IsNullOrEmpty(uniqueId) ? null : new ModReference(uniqueId, sourceId);
        }

        public override void Write(Utf8JsonWriter writer, ModReference value, JsonSerializerOptions options)
        {
            if (value.IsFromCollection is false)
            {
                writer.WriteStringValue(value.UniqueId);
                return;
            }

            writer.WriteStartObject();
            writer.WriteString(nameof(ModReference.UniqueId), value.UniqueId);
            writer.WriteString(nameof(ModReference.SourceId), value.SourceId);
            writer.WriteEndObject();
        }
    }
}
