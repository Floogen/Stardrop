using Avalonia.OpenGL;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stardrop.Models.Nexus.GraphQL
{
    public record QueryResponse<T>([property: JsonPropertyName("data")] T Data);
    public record ModFileData([property: JsonPropertyName("modFiles")] ScannedModFile[] ModFiles);
}
