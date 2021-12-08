using Stardrop.Models;
using Stardrop.Models.SMAPI;
using Stardrop.Models.SMAPI.Web;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stardrop.Utilities.SMAPI
{
    static class SMAPI
    {
        public static ProcessStartInfo GetPrepareProcess(bool hideConsole)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(Path.Combine(Program.defaultGamePath, "StardewModdingAPI.exe"));
            processInfo.CreateNoWindow = hideConsole;

            return processInfo;
        }

        public async static Task<List<ModEntry>> GetModUpdateData(GameDetails gameDetails, List<Mod> mods)
        {
            List<ModSearchEntry> searchEntries = new List<ModSearchEntry>();
            foreach (var mod in mods)
            {
                searchEntries.Add(new ModSearchEntry(mod.UniqueId, mod.Version, mod.Manifest.UpdateKeys));
            }

            // Create the body to be sent via the POST request
            ModSearchData searchData = new ModSearchData(searchEntries, gameDetails.SmapiVersion, gameDetails.GameVersion, gameDetails.System.ToString(), true);

            // Create a throwaway client
            HttpClient client = new HttpClient();
            var requestPackage = new StringContent(JsonSerializer.Serialize(searchData), Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://smapi.io/api/v3.0/mods", requestPackage);

            List<ModEntry> modUpdateData = new List<ModEntry>();
            if (response.Content is not null)
            {
                // In the name of the Nine Divines, why is JsonSerializer.Deserialize case sensitive by default???

                modUpdateData = JsonSerializer.Deserialize<List<ModEntry>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            client.Dispose();

            return modUpdateData;
        }
    }
}
