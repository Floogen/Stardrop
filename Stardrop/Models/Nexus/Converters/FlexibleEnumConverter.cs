using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Converters
{
    /// <summary>
    /// Reads an enum from its name, falling back to the enum's first member rather than throwing when the name is
    /// not one we know. Rule and source types are added over time and collection.json is parsed as a whole, so one
    /// unrecognised value would otherwise take down the install of an entire collection.
    /// </summary>
    public class FlexibleEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.String && Enum.TryParse<TEnum>(reader.GetString(), true, out var namedValue))
            {
                return namedValue;
            }

            if (reader.TokenType is JsonTokenType.Number && reader.TryGetInt32(out var numericValue) && Enum.IsDefined(typeof(TEnum), numericValue))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
            }

            return default;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
