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
using System.Text.RegularExpressions;
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
        private static string _nxmPattern = @"nxm:\/\/(?<domain>stardewvalley)\/mods\/(?<mod>[0-9]+)\/files\/(?<file>[0-9]+)\?key=(?<key>.*)&expires=(?<expiry>[0-9]+)&user_id=(?<user>[0-9]+)";

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

        public async static Task<bool> ValidateKey(string? apiKey)
        {
            if (String.IsNullOrEmpty(apiKey))
            {
                return false;
            }

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


        public async static Task<ModDetails?> GetModDetailsViaNXM(string apiKey, NXM nxmData)
        {
            if (nxmData.Link is null)
            {
                return null;
            }

            var match = Regex.Match(Regex.Unescape(nxmData.Link), _nxmPattern);
            if (match.Success is false || match.Groups["domain"].ToString().ToLower() != "stardewvalley" || Int32.TryParse(match.Groups["mod"].ToString(), out int modId) is false)
            {
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
                var response = await client.GetAsync(new Uri(_baseUrl, $"games/stardewvalley/mods/{modId}.json"));
                if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content is not null)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    ModDetails modDetails = JsonSerializer.Deserialize<ModDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (modDetails is null)
                    {
                        Program.helper.Log($"Unable to get mod details for the mod {modId} on Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");

                        return null;
                    }

                    UpdateRequestCounts(response.Headers);

                    return modDetails;
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
                Program.helper.Log($"Unable to get mod details for the mod {modId} on Nexus Mods: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return null;
        }

        public async static Task<ModFile?> GetFileByVersion(string apiKey, int modId, string version, string? modFlag = null)
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
                        ModFile? selectedFile = null;
                        foreach (var file in modFiles.Files.Where(x => String.IsNullOrEmpty(x.Version) is false && SemVersion.TryParse(x.Version.Replace("v", String.Empty), SemVersionStyles.Any, out var modVersion) && modVersion == targetVersion))
                        {
                            if (String.IsNullOrEmpty(modFlag) || ((String.IsNullOrEmpty(file.Name) is false && file.Name.Contains(modFlag, StringComparison.OrdinalIgnoreCase)) || (String.IsNullOrEmpty(file.Description) is false && file.Description.Contains(modFlag, StringComparison.OrdinalIgnoreCase))))
                            {
                                selectedFile = file;
                            }
                        }

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

        public async static Task<string?> GetFileDownloadLink(string apiKey, NXM nxmData, string? serverName = null)
        {
            if (nxmData.Link is null)
            {
                return null;
            }

            var match = Regex.Match(Regex.Unescape(nxmData.Link), _nxmPattern);
            if (match.Success is false || match.Groups["domain"].ToString().ToLower() != "stardewvalley" || Int32.TryParse(match.Groups["mod"].ToString(), out int modId) is false || Int32.TryParse(match.Groups["file"].ToString(), out int fileId) is false)
            {
                return null;
            }

            return await GetFileDownloadLink(apiKey, modId, fileId, match.Groups["key"].ToString(), match.Groups["expiry"].ToString(), serverName);
        }

        public async static Task<string?> GetFileDownloadLink(string apiKey, int modId, int fileId, string? nxmKey = null, string? nxmExpiry = null, string? serverName = null)
        {
            if (String.IsNullOrEmpty(serverName) || Program.settings.NexusDetails.IsPremium is false)
            {
                serverName = "Nexus CDN";
            }

            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("apiKey", apiKey);
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.applicationVersion);
            client.DefaultRequestHeaders.Add("User-Agent", $"Stardrop/{Program.applicationVersion} {Environment.OSVersion}");

            try
            {
                string url = $"games/stardewvalley/mods/{modId}/files/{fileId}/download_link.json";
                if (String.IsNullOrEmpty(nxmKey) is false && String.IsNullOrEmpty(nxmExpiry) is false)
                {
                    url = $"{url}?key={nxmKey}&expires={nxmExpiry}";
                }
                var response = await client.GetAsync(new Uri(_baseUrl, url));

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
                        UpdateRequestCounts(response.Headers);

                        var selectedFile = downloadLinks.FirstOrDefault(x => x.ShortName?.ToLower() == serverName.ToLower());
                        if (selectedFile is not null)
                        {
                            Program.helper.Log($"Requested download link from Nexus Mods using their {serverName} server");
                            return selectedFile.Uri;
                        }
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

                    if (endorsements is null)
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


        public async static Task<EndorsementResponse> SetModEndorsement(string apiKey, int modId, bool isEndorsed)
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
                var response = await client.PostAsync(new Uri(_baseUrlSecured, $"games/stardewvalley/mods/{modId}/{(isEndorsed is true ? "endorse.json" : "abstain.json")}"), requestPackage);
                if (response.Content is not null)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    EndorsementResult endorsementResult = JsonSerializer.Deserialize<EndorsementResult>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (endorsementResult is null)
                    {
                        Program.helper.Log($"Unable to set endorsement for Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");

                        return EndorsementResponse.Unknown;
                    }

                    UpdateRequestCounts(response.Headers);

                    switch (endorsementResult.Status?.ToUpper())
                    {
                        case "ENDORSED":
                            return EndorsementResponse.Endorsed;
                        case "ABSTAINED":
                            return EndorsementResponse.Abstained;
                        case "ERROR":
                            var parsedMessage = endorsementResult.Message?.ToUpper();
                            if (parsedMessage == "IS_OWN_MOD")
                            {
                                return EndorsementResponse.IsOwnMod;
                            }
                            else if (parsedMessage == "TOO_SOON_AFTER_DOWNLOAD")
                            {
                                return EndorsementResponse.TooSoonAfterDownload;
                            }
                            else if (parsedMessage == "NOT_DOWNLOADED_MOD")
                            {
                                return EndorsementResponse.NotDownloadedMod;
                            }
                            Program.helper.Log(parsedMessage);
                            break;
                        default:
                            Program.helper.Log($"Unhandled status for endorsement: {endorsementResult.Status} | {endorsementResult.Message}");
                            break;
                    }
                }
                else
                {
                    Program.helper.Log($"No response from Nexus Mods! Status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to set endorsement for Nexus Mods: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return EndorsementResponse.Unknown;
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
