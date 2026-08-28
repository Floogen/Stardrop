using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.Converters
{
    // Nexus' GraphQL API hands back some numeric fields as strings.
    // These read either form and return null rather than throwing when a value cannot be parsed at all, so one
    // unexpected field does not take down the whole response
    public class FlexibleInt64Converter : JsonConverter<long?>
    {
        public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Number)
            {
                return reader.TryGetInt64(out var value) ? value : null;
            }

            if (reader.TokenType is JsonTokenType.String)
            {
                return FlexibleNumberReader.ParseInt64(reader.GetString());
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteNumberValue(value.Value);
        }
    }

    public class FlexibleInt32Converter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Number)
            {
                return reader.TryGetInt32(out var value) ? value : null;
            }

            if (reader.TokenType is JsonTokenType.String)
            {
                var parsed = FlexibleNumberReader.ParseInt64(reader.GetString());
                if (parsed is null || parsed.Value > Int32.MaxValue || parsed.Value < Int32.MinValue)
                {
                    return null;
                }

                return (int)parsed.Value;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteNumberValue(value.Value);
        }
    }

    internal static class FlexibleNumberReader
    {
        public static long? ParseInt64(string? raw)
        {
            if (String.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (Int64.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            // Byte sizes occasionally arrive with a decimal component
            if (Double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return (long)doubleValue;
            }

            return null;
        }
    }
}
