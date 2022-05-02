using Semver;
using SharpCompress.Archives;
using Stardrop.Models;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus;
using Stardrop.Models.Nexus.Web;
using Stardrop.Models.SMAPI;
using Stardrop.Models.SMAPI.Web;
using Stardrop.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stardrop.Utilities.External
{
    static class Nexus
    {
        internal static int dailyRequestsRemaining;
        internal static int dailyRequestsLimit;

        private static MainWindowViewModel _displayModel;
        private static Uri _baseUrl = new Uri("http://api.nexusmods.com/v1/");
        private static Uri _baseUrlSecured = new Uri("https://api.nexusmods.com/v1/");

        // Regex for extracting required components for Nexus file downloading: 
        // nxm:\/\/(?<domain>stardewvalley)\/mods\/(?<mod>[0-9]+)\/files\/(?<file>[0-9]+)\?key=(?<key>[0-9]+)&expires=(?<expiry>[0-9]+)&user_id=(?<user>[0-9]+)
        //https://app.swaggerhub.com/apis-docs/NexusMods/nexus-mods_public_api_params_in_form_data/1.0#/Mod%20Files/get_v1_games_game_domain_mods_mod_id_files_id_download_link.json

        public static void SetDisplayWindow(MainWindowViewModel viewModel)
        {
            _displayModel = viewModel;
        }

        public static string? GetKey()
        {
            if (Program.settings.NexusDetails is null || Program.settings.NexusDetails.Key is null || File.Exists(Pathing.GetNotionCachePath()) is false)
            {
                return null;
            }

            var pairedKeys = JsonSerializer.Deserialize<PairedKeys>(File.ReadAllText(Pathing.GetNotionCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            if (pairedKeys is null || pairedKeys.Vector is null || pairedKeys.Vector is null)
            {
                return null;
            }

            try
            {
                return SimpleObscure.Decrypt(Program.settings.NexusDetails.Key, pairedKeys.Lock, pairedKeys.Vector);
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to parse API key when requested: {ex}");
            }

            return null;
        }

        public async static Task<bool> ValidateKey(string apiKey)
        {
            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("apiKey", apiKey);
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.applicationVersion);
            client.DefaultRequestHeaders.Add("User-Agent", $"Stardrop/{Program.applicationVersion} {Environment.OSVersion}");

            bool wasValidated = true;
            try
            {
                var response = await client.GetAsync(new Uri(_baseUrl, "users/validate"));
                if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content is not null)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    Validate validationModel = JsonSerializer.Deserialize<Validate>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (validationModel is null || String.IsNullOrEmpty(validationModel.Message) is false)
                    {
                        Program.helper.Log($"Unable to validate given API key for Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");

                        wasValidated = false;
                    }
                    else if (Program.settings.NexusDetails is not null)
                    {
                        Program.settings.NexusDetails.Username = validationModel.Name;
                        Program.settings.NexusDetails.IsPremium = validationModel.IsPremium;

                        UpdateRequestCounts(response.Headers);
                    }
                }
                else
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        Program.helper.Log($"Bad status given from Nexus Mods: {response.StatusCode}");
                        if (response.Content is not null)
                        {
                            Program.helper.Log($"Response from Nexus Mods:\n{await response.Content.ReadAsStringAsync()}");
                        }
                    }
                    else if (response.Content is null)
                    {
                        Program.helper.Log($"No response from Nexus Mods!");
                    }

                    wasValidated = false;
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to validate user's API key for Nexus Mods: {ex}", Helper.Status.Alert);
                wasValidated = false;
            }
            client.Dispose();

            return wasValidated;
        }

        public async static Task<ModFile?> GetFileByVersion(string apiKey, int modId, string version)
        {
            if (SemVersion.TryParse(version.Replace("v", String.Empty), SemVersionStyles.Any, out var targetVersion) is false)
            {
                Program.helper.Log($"Unable to parse given target version {version}");
                return null;
            }

            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("apiKey", apiKey);
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.applicationVersion);
            client.DefaultRequestHeaders.Add("User-Agent", $"Stardrop/{Program.applicationVersion} {Environment.OSVersion}");

            try
            {
                var response = await client.GetAsync(new Uri(_baseUrl, $"games/stardewvalley/mods/{modId}/files.json"));
                if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content is not null)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    ModFiles modFiles = JsonSerializer.Deserialize<ModFiles>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (modFiles is null || modFiles.Files is null || modFiles.Files.Count == 0)
                    {
                        Program.helper.Log($"Unable to get the mod file for Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");
                    }
                    else
                    {
                        var selectedFile = modFiles.Files.FirstOrDefault(x => String.IsNullOrEmpty(x.Version) is false && SemVersion.TryParse(x.Version.Replace("v", String.Empty), SemVersionStyles.Any, out var modVersion) && modVersion == targetVersion);
                        if (selectedFile is null)
                        {
                            Program.helper.Log($"Unable to get a matching file for the mod {modId} with version {version} via Nexus Mods: \n{String.Join("\n", modFiles.Files.Select(m => $"{m.Name} | {m.Version}"))}");
                        }

                        UpdateRequestCounts(response.Headers);

                        return selectedFile;
                    }
                }
                else
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        Program.helper.Log($"Bad status given from Nexus Mods: {response.StatusCode}");
                        if (response.Content is not null)
                        {
                            Program.helper.Log($"Response from Nexus Mods:\n{await response.Content.ReadAsStringAsync()}");
                        }
                    }
                    else if (response.Content is null)
                    {
                        Program.helper.Log($"No response from Nexus Mods!");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get the mod file for Nexus Mods: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return null;
        }

        public async static Task<string?> GetFileDownloadLink(string apiKey, int modId, int fileId, string serverName = "Nexus CDN")
        {
            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("apiKey", apiKey);
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.applicationVersion);
            client.DefaultRequestHeaders.Add("User-Agent", $"Stardrop/{Program.applicationVersion} {Environment.OSVersion}");

            try
            {
                var response = await client.GetAsync(new Uri(_baseUrl, $"games/stardewvalley/mods/{modId}/files/{fileId}/download_link.json"));
                if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content is not null)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    List<DownloadLink> downloadLinks = JsonSerializer.Deserialize<List<DownloadLink>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (downloadLinks is null || downloadLinks.Count == 0)
                    {
                        Program.helper.Log($"Unable to get the download link for Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");
                    }
                    else
                    {
                        var selectedFile = downloadLinks.FirstOrDefault(x => x.ShortName?.ToLower() == serverName.ToLower());
                        if (selectedFile is not null)
                        {
                            return selectedFile.Uri;
                        }

                        UpdateRequestCounts(response.Headers);
                    }
                }
                else
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        Program.helper.Log($"Bad status given from Nexus Mods: {response.StatusCode}");
                        if (response.Content is not null)
                        {
                            Program.helper.Log($"Response from Nexus Mods:\n{await response.Content.ReadAsStringAsync()}");
                        }
                    }
                    else if (response.Content is null)
                    {
                        Program.helper.Log($"No response from Nexus Mods!");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get the download link for Nexus Mods: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return null;
        }

        public async static Task<string?> DownloadFileAndGetPath(string uri, string fileName)
        {
            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.applicationVersion);
            client.DefaultRequestHeaders.Add("User-Agent", $"Stardrop/{Program.applicationVersion} {Environment.OSVersion}");

            try
            {
                var stream = await client.GetStreamAsync(new Uri(uri));
                using (var fileStream = new FileStream(Path.Combine(Pathing.GetNexusPath(), fileName), FileMode.CreateNew))
                {
                    await stream.CopyToAsync(fileStream);
                }

                return Path.Combine(Pathing.GetNexusPath(), fileName);
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to download mod file for Nexus Mods: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return null;
        }

        public async static Task<List<Endorsement>> GetEndorsements(string apiKey)
        {
            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("apiKey", apiKey);
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.applicationVersion);
            client.DefaultRequestHeaders.Add("User-Agent", $"Stardrop/{Program.applicationVersion} {Environment.OSVersion}");

            try
            {
                var response = await client.GetAsync(new Uri(_baseUrl, $"user/endorsements"));
                if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content is not null)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    List<Endorsement> endorsements = JsonSerializer.Deserialize<List<Endorsement>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (endorsements is null || endorsements.Count == 0)
                    {
                        Program.helper.Log($"Unable to get endorsements for Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");
                    }
                    else
                    {
                        endorsements = endorsements.Where(e => e.DomainName?.ToLower() == "stardewvalley").ToList();

                        UpdateRequestCounts(response.Headers);

                        return endorsements;
                    }
                }
                else
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        Program.helper.Log($"Bad status given from Nexus Mods: {response.StatusCode}");
                        if (response.Content is not null)
                        {
                            Program.helper.Log($"Response from Nexus Mods:\n{await response.Content.ReadAsStringAsync()}");
                        }
                    }
                    else if (response.Content is null)
                    {
                        Program.helper.Log($"No response from Nexus Mods!");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get endorsements for Nexus Mods: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return new List<Endorsement>();
        }


        public async static Task<bool> SetModEndorsement(string apiKey, int modId, EndorsementState state)
        {
            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("apiKey", apiKey);
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.applicationVersion);
            client.DefaultRequestHeaders.Add("User-Agent", $"Stardrop/{Program.applicationVersion} {Environment.OSVersion}");

            try
            {
                var requestPackage = new StringContent("{\"Version\":\"1.0.0\"}", Encoding.UTF8, "application/json");
                var response = await client.PostAsync(new Uri(_baseUrlSecured, $"games/stardewvalley/mods/{modId}/{(state == EndorsementState.Endorsed ? "endorse.json" : "abstain.json")}"), requestPackage);
                if ((response.StatusCode == System.Net.HttpStatusCode.OK || response.StatusCode == System.Net.HttpStatusCode.Created) && response.Content is not null)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    EndorsementResult endorsementResult = JsonSerializer.Deserialize<EndorsementResult>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (endorsementResult is null)
                    {
                        Program.helper.Log($"Unable to set endorsement for Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");
                    }
                    else
                    {
                        UpdateRequestCounts(response.Headers);

                        return true;
                    }
                }
                else
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.OK && response.StatusCode != System.Net.HttpStatusCode.Created)
                    {
                        Program.helper.Log($"Bad status given from Nexus Mods: {response.StatusCode}");
                        if (response.Content is not null)
                        {
                            Program.helper.Log($"Response from Nexus Mods:\n{await response.Content.ReadAsStringAsync()}");
                        }
                    }
                    else if (response.Content is null)
                    {
                        Program.helper.Log($"No response from Nexus Mods!");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to set endorsement for Nexus Mods: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return false;
        }

        private static void UpdateRequestCounts(HttpResponseHeaders headers)
        {
            if (headers.TryGetValues("x-rl-daily-limit", out var limitValues) && Int32.TryParse(limitValues.First(), out int dailyLimit))
            {
                dailyRequestsLimit = dailyLimit;
            }

            if (headers.TryGetValues("x-rl-daily-remaining", out var remainingValues) && Int32.TryParse(remainingValues.First(), out int dailyRemaining))
            {
                dailyRequestsRemaining = dailyRemaining;
            }

            if (_displayModel is null)
            {
                return;
            }

            _displayModel.NexusLimits = $"(Remaining Daily Requests: {dailyRequestsRemaining}) ";
        }
    }
}
