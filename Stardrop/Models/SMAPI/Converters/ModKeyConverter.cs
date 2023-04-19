using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stardrop.Models.SMAPI.Converters
{
    internal class ModKeyConverter : JsonConverter<string[]>
    {
        public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException();
            }

            var modKeys = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return modKeys.ToArray();
                }

                if (reader.TokenType == JsonTokenType.Number)
                {
                    modKeys.Add($"Nexus: {reader.GetInt32()}");
                }
                else
                {
                    modKeys.Add(reader.GetString());
                }
            }

            // Should not reach here, due to reader.TokenType == JsonTokenType.EndArray
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
        {
            throw new InvalidOperationException("This converter should not be used to write, it is read only.");
        }
    }
}
