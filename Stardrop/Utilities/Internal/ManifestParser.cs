using SharpCompress.Archives;
using Stardrop.Models.SMAPI;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stardrop.Utilities.Internal
{
    internal static class ManifestParser
    {
        public static async Task<Manifest?> GetDataAsync(IArchiveEntry manifestFile)
        {
            using (Stream stream = manifestFile.OpenEntryStream())
            {
                using (var reader = new StreamReader(stream))
                {
                    return GetData(await reader.ReadToEndAsync());
                }
            }
        }

        public static Manifest? GetData(string manifestText)
        {
            try
            {
                return JsonSerializer.Deserialize<Manifest>(manifestText, new JsonSerializerOptions() { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                // Attempt to parse out illegal JSON characters, as System.Text.Json does not have any native handling (unlike Newtonsoft.Json)
                manifestText = manifestText.Replace("\r", String.Empty).Replace("\n", String.Empty);
                return JsonSerializer.Deserialize<Manifest>(manifestText, new JsonSerializerOptions() { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true });
            }
        }
    }
}
