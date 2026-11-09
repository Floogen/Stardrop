using Semver;
using Stardrop.Models;
using Stardrop.Models.SMAPI;
using Stardrop.Models.SMAPI.Web;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Stardrop.Utilities.External
{
    static class SMAPI
    {
        internal static bool IsRunning = false;
        internal static Process Process;
        internal static readonly Regex responseHeadersPattern = new Regex(@"^HEADERS\r?\n=+", RegexOptions.Multiline);

        public static ProcessStartInfo GetPrepareProcess(bool hideConsole)
        {
            var smapiInfo = new FileInfo(Pathing.GetSmapiPath());

            var fileName = smapiInfo.FullName;
            var arguments = string.Empty;
            var parsedModPath = string.Empty;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) is true)
            {
                fileName = "/usr/bin/env";
                arguments = $"bash -c \"SMAPI_MODS_PATH='{Pathing.GetSelectedModsFolderPath()}' '{Pathing.GetSmapiPath().Replace("StardewModdingAPI.dll", "StardewValley")}'\"";
                parsedModPath = $"'{Pathing.GetSelectedModsFolderPath()}'";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) is true)
            {
                fileName = "/usr/bin/open";
                arguments = $"-a \"Terminal\" \"{Pathing.GetSmapiPath().Replace("StardewModdingAPI.dll", "StardewModdingAPI")}\" --args --mods-path \"{Pathing.GetSelectedModsFolderPath()}\"";
                parsedModPath = $"{Pathing.GetSelectedModsFolderPath()}";

                /* Alternative route (using AppleScript) of activating Terminal + SMAPI
                fileName = "/usr/bin/env";
                arguments = $@"osascript -e ""tell application \""Terminal\""
                set smapi_path to \""{Pathing.GetSmapiPath().Replace("StardewModdingAPI.dll", "StardewModdingAPI")}\""
                set mods_path to \""{Pathing.GetSelectedModsFolderPath()}\""
                activate
                do script \""\"" & quoted form of the POSIX path of smapi_path & \"" --mods-path \"" & quoted form of the POSIX path of mods_path
                end tell""";
                */
            }
            else
            {
                parsedModPath = Pathing.GetSelectedModsFolderPath();
            }

            Program.helper.Log($"Starting SMAPI with the following arguments: {arguments}");
            var processInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = smapiInfo.DirectoryName,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = hideConsole,
                UseShellExecute = false
            };

            // Set SMAPI_MODS_PATH EnvironmentVariable if required
            if (string.IsNullOrEmpty(parsedModPath) is false)
            {
                Program.helper.Log($"Setting SMAPI_MODS_PATH to: {parsedModPath}");
                processInfo.EnvironmentVariables["SMAPI_MODS_PATH"] = parsedModPath;

                Program.helper.Log($"Process SMAPI_MODS_PATH: {processInfo.EnvironmentVariables["SMAPI_MODS_PATH"]}");
                Program.helper.Log($"System SMAPI_MODS_PATH: {Environment.GetEnvironmentVariable("SMAPI_MODS_PATH")}");
            }

            return processInfo;
        }

        public static string GetProcessName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "StardewModdingA";
            }

            return "StardewModdingAPI";
        }

        /// <summary>Requests update data from smapi.io for the given mods and their requirements</summary>
        /// <returns>The update data, or null if smapi.io could not provide it (so a failed check is not mistaken for one that found nothing)</returns>
        public async static Task<List<ModEntry>?> GetModUpdateData(GameDetails gameDetails, List<Mod> mods)
        {
            List<ModSearchEntry> searchEntries = new List<ModSearchEntry>();

            // Mods installed by a collection are pinned to whatever version their collection asks for, so they are
            // never sent to smapi.io. Their requirements below are still sent, as those feed the mod key name cache
            foreach (var mod in mods.Where(m => m.IsFromCollection is false && m.HasValidVersion() && m.HasUpdateKeys()))
            {
                searchEntries.Add(new ModSearchEntry(mod.UniqueId, mod.Version, mod.Manifest.UpdateKeys));
            }
            foreach (var requirementKey in mods.SelectMany(m => m.Requirements))
            {
                if (!searchEntries.Any(e => e.Id.Equals(requirementKey.UniqueID, StringComparison.OrdinalIgnoreCase)))
                {
                    searchEntries.Add(new ModSearchEntry() { Id = requirementKey.UniqueID });
                }
            }

            // Create the body to be sent via the POST request
            ModSearchData searchData = new ModSearchData(searchEntries, gameDetails.SmapiVersion, gameDetails.GameVersion, gameDetails.System.ToString(), true);

            // Create a throwaway client
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.ApplicationVersion);
            client.DefaultRequestHeaders.Add("User-Agent", $"Stardrop/{Program.ApplicationVersion} {Environment.OSVersion}");

            var parsedRequest = JsonSerializer.Serialize(searchData, new JsonSerializerOptions() { WriteIndented = true, IgnoreNullValues = true });
            var requestPackage = new StringContent(parsedRequest, Encoding.UTF8, "application/json");

            // Network failures and timeouts are reported as no data, so the caller treats them the same as an error status from smapi.io
            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync("https://smapi.io/api/v3.0/mods", requestPackage);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                Program.helper.Log($"Unable to reach smapi.io: {ex.Message}");
                return null;
            }

            List<ModEntry>? modUpdateData = null;
            if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content is not null)
            {
                // In the name of the Nine Divines, why is JsonSerializer.Deserialize case sensitive by default???
                string content = await response.Content.ReadAsStringAsync();
                try
                {
                    modUpdateData = JsonSerializer.Deserialize<List<ModEntry>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException ex)
                {
                    Program.helper.Log($"Unable to parse mod update data from smapi.io: {ex.Message}");
                }

                if (modUpdateData is null || modUpdateData.Count == 0)
                {
                    Program.helper.Log($"Mod update data was not parsable from smapi.io");
                    Program.helper.Log($"Response from smapi.io:\n{content}");
                    Program.helper.Log($"Our request to smapi.io:\n{parsedRequest}");
                }
            }
            else
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    Program.helper.Log($"Bad status given from smapi.io: {response.StatusCode}");
                    if (response.Content is not null)
                    {
                        Program.helper.Log($"Response from smapi.io:\n{GetLoggableResponse(await response.Content.ReadAsStringAsync())}");
                    }
                }
                else if (response.Content is null)
                {
                    Program.helper.Log($"No response from smapi.io!");
                }
                else
                {
                    Program.helper.Log($"Error getting mod update data from smapi.io!");
                }

                Program.helper.Log($"Our request to smapi.io:\n{parsedRequest}");
            }

            return modUpdateData;
        }

        /// <summary>Drops the request headers smapi.io echoes back on its error page</summary>
        private static string GetLoggableResponse(string content)
        {
            var headersMatch = responseHeadersPattern.Match(content);
            if (headersMatch.Success is false)
            {
                return content;
            }

            return $"{content.Substring(0, headersMatch.Index).TrimEnd()}\n[Request headers omitted]";
        }

        internal static SemVersion? GetVersion()
        {
            AssemblyName smapiAssembly = AssemblyName.GetAssemblyName(Path.Combine(Pathing.defaultGamePath, "StardewModdingAPI.dll"));

            if (smapiAssembly is null || smapiAssembly.Version is null)
            {
                return null;
            }

            return SemVersion.Parse($"{smapiAssembly.Version.Major}.{smapiAssembly.Version.Minor}.{smapiAssembly.Version.Build}", SemVersionStyles.Any);
        }
    }
}
