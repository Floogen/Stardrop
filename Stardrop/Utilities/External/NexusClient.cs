using Semver;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus;
using Stardrop.Models.Nexus.GraphQL;
using Stardrop.Models.Nexus.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Stardrop.Utilities.External
{
    public static class Nexus
    {
        private static readonly Uri _baseUrl = new Uri("https://api.nexusmods.com/v1/");

        public static NexusClient? Client { get; private set; }

        public delegate void NexusClientChangedHandler(NexusClient? oldClient, NexusClient? newClient);
        public static event NexusClientChangedHandler? ClientChanged = null;

        /// <summary>
        /// If the user has entered their Nexus API key in a previous session, this will attempt to retreive
        /// it.
        /// </summary>
        /// <returns>The key, if it exists. Null otherwise.</returns>
        public static string? GetCachedKey()
        {
            if (Program.settings.NexusDetails?.Key is null || File.Exists(Pathing.GetNotionCachePath()) is false)
            {
                return null;
            }

            var pairedKeys = JsonSerializer.Deserialize<PairedKeys>(File.ReadAllText(Pathing.GetNotionCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            if (pairedKeys?.Vector is null || pairedKeys?.Lock is null)
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

        /// <summary>
        /// Creates a new HttpClient configured with a Nexus API key and validates it against the Nexus API.
        /// If successfully validated, sets <see cref="Client"/>, as well as returning a reference to it.
        /// If called when a <see cref="Client"/> is already set, the Client will be replaced.<br/>
        /// On success, fires <see cref="ClientChanged"/>, with the previous client (if any) and the new client.
        /// </summary>
        /// <param name="apiKey">The API key from Nexus mods that will be included in the 'apiKey' header when making calls.</param>
        /// <returns>The created client, if successful. Null otherwise.</returns>
        public static async Task<NexusClient?> CreateClient(string apiKey)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = _baseUrl;
            client.DefaultRequestHeaders.Add("apiKey", apiKey);
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.ApplicationVersion);
            client.DefaultRequestHeaders.Add("User-Agent", $"Stardrop/{Program.ApplicationVersion} {Environment.OSVersion}");

            var nexusClient = new NexusClient(client);

            bool isKeyValid = await nexusClient.ValidateKey();
            if (isKeyValid is false)
            {
                return null;
            }

            ClientChanged?.Invoke(oldClient: Client, newClient: nexusClient);
            Client = nexusClient;
            return Client;
        }

        /// <summary>
        /// Nulls out the <see cref="Client"/> and fires a <see cref="ClientChanged"/> event
        /// to give consumers a chance to clean up their event handlers.
        /// </summary>
        public static void ClearClient()
        {
            ClientChanged?.Invoke(oldClient: Client, newClient: null);
            Client = null;
        }
    }

    public partial class NexusClient
    {
        private static readonly Uri _graphQLBaseUrl = new Uri("https://api.nexusmods.com/v2/graphql");
        internal const string NxmModPattern = @"nxm:\/\/(?<domain>stardewvalley)\/mods\/(?<mod>[0-9]+)\/files\/(?<file>[0-9]+)\?key=(?<key>.*)&expires=(?<expiry>[0-9]+)&user_id=(?<user>[0-9]+)";
        internal const string NxmCollectionPattern = @"nxm:\/\/(?<domain>[a-z0-9]+)\/collections\/(?<slug>[a-z0-9]+)\/revisions\/(?<revision>[0-9]+)";
        private const string _nxmPattern = NxmModPattern;

        /// <summary>Nexus mod ID for SMAPI, which collections list as a mod but which Stardrop must never install into the mod folder</summary>
        internal const int SmapiNexusModId = 2400;

        private readonly HttpClient _client;
        private NexusUser _settings = null!;

        internal int DailyRequestsLimit { get; private set; }
        internal int DailyRequestsRemaining { get; private set; }
        internal event EventHandler? DailyRequestLimitsChanged = null;
        internal event EventHandler<ModDownloadStartedEventArgs>? DownloadStarted = null;
        internal event EventHandler<ModDownloadProgressEventArgs>? DownloadProgressChanged = null;
        internal event EventHandler<ModDownloadCompletedEventArgs>? DownloadCompleted = null;
        internal event EventHandler<ModDownloadFailedEventArgs>? DownloadFailed = null;

        public NexusClient(HttpClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Calls the /validate endpoint on Nexus Mods' API using the API key stored in this client upon creation.
        /// If the key is successfully validated, its information is cached in <see cref="Program.settings.NexusDetails"/>.<br/>
        /// Returns <see langword="false"/> if validation fails.
        /// </summary>
        public async Task<bool> ValidateKey()
        {
            try
            {
                var response = await _client.GetAsync("users/validate");
                if (!response.IsSuccessStatusCode || response.Content == null)
                {
                    Program.helper.Log($"Call to Nexus Mods failed. HTTP status code: {response.StatusCode}, {response.ReasonPhrase}");
                    if (response.Content == null)
                    {
                        Program.helper.Log($"No response from Nexus Mods!");
                    }
                    else
                    {
                        Program.helper.Log($"Response from Nexus Mods:\n{await response.Content.ReadAsStringAsync()}");
                    }
                    return false;
                }


                string content = await response.Content.ReadAsStringAsync();
                Validate validationModel = JsonSerializer.Deserialize<Validate>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

                if (validationModel is null || String.IsNullOrEmpty(validationModel.Message) is false)
                {
                    Program.helper.Log($"Unable to validate given API key for Nexus Mods");
                    Program.helper.Log($"Response from Nexus Mods:\n{content}");

                    return false;
                }

                if (Program.settings.NexusDetails is null)
                {
                    return false;
                }

                Program.settings.NexusDetails.Username = validationModel.Name;
                Program.settings.NexusDetails.IsPremium = validationModel.IsPremium;
                _settings = Program.settings.NexusDetails;

                UpdateRequestCounts(response.Headers);

                return true;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to validate user's API key for Nexus Mods: {ex}", Helper.Status.Alert);
                return false;
            }
        }

        public async Task<ModDetails?> GetModDetailsViaNXM(NXM nxmData)
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

            try
            {
                var response = await _client.GetAsync($"games/stardewvalley/mods/{modId}.json");
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

            return null;
        }

        /// <summary>
        /// Looks a file up by its own ID rather than by matching on version. A mod can publish several files under
        /// the same version number, so anything holding a file ID (a collection pin, an nxm link) has to ask for
        /// that file directly or it risks being handed one of its siblings.
        /// </summary>
        public async Task<ModFile?> GetFile(int modId, int fileId)
        {
            try
            {
                var response = await _client.GetAsync($"games/stardewvalley/mods/{modId}/files/{fileId}.json");
                if (response.StatusCode != System.Net.HttpStatusCode.OK || response.Content is null)
                {
                    Program.helper.Log($"Bad status given from Nexus Mods for the file {fileId} of mod {modId}: {response.StatusCode}");
                    if (response.Content is not null)
                    {
                        Program.helper.Log($"Response from Nexus Mods:\n{await response.Content.ReadAsStringAsync()}");
                    }

                    return null;
                }

                string content = await response.Content.ReadAsStringAsync();
                ModFile? modFile = JsonSerializer.Deserialize<ModFile>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (modFile is null)
                {
                    Program.helper.Log($"Unable to get the file {fileId} of mod {modId} from Nexus Mods");
                    Program.helper.Log($"Response from Nexus Mods:\n{content}");

                    return null;
                }

                UpdateRequestCounts(response.Headers);

                return modFile;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get the file {fileId} of mod {modId} from Nexus Mods: {ex}", Helper.Status.Alert);
            }

            return null;
        }

        public async Task<ModFile?> GetFileByVersion(int modId, string version, string? modFlag = null, bool ignoreCategory = false)
        {
            if (SemVersion.TryParse(version.Replace("v", String.Empty), SemVersionStyles.Any, out var targetVersion) is false)
            {
                Program.helper.Log($"Unable to parse given target version {version}");
                return null;
            }

            Program.helper.Log($"Requesting version {version} of mod {modId}{(String.IsNullOrEmpty(modFlag) is false ? $" with flag {modFlag}" : String.Empty)}");

            try
            {
                var response = await _client.GetAsync($"games/stardewvalley/mods/{modId}/files.json");
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
                            if (String.IsNullOrEmpty(modFlag) is false && ((String.IsNullOrEmpty(file.Name) is false && file.Name.Contains(modFlag, StringComparison.OrdinalIgnoreCase)) || (String.IsNullOrEmpty(file.Description) is false && file.Description.Contains(modFlag, StringComparison.OrdinalIgnoreCase))))
                            {
                                selectedFile = file;
                            }
                            else if (String.IsNullOrEmpty(modFlag) is true && String.IsNullOrEmpty(file.Category) is false && (ignoreCategory || file.Category.Equals("MAIN", StringComparison.OrdinalIgnoreCase)))
                            {
                                selectedFile = file;
                            }
                        }

                        if (selectedFile is null)
                        {
                            Program.helper.Log($"Unable to get a matching file for the mod {modId} with version {version}{(String.IsNullOrEmpty(modFlag) is false ? $" and with the flag {modFlag}" : String.Empty)} via Nexus Mods: \n{String.Join("\n", modFiles.Files.Select(m => $"{m.Name} | {m.Version}"))}");
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

            return null;
        }

        /// <summary>
        /// Pulls the domain, mod ID and file ID out of an nxm mod link. The key and expiry the link also carries are
        /// left to <see cref="GetFileDownloadLink(NXM, string?)"/>, as they only matter at the point of downloading.
        /// </summary>
        public static bool TryParseModNxmLink(string link, out string domainName, out int modId, out int fileId)
        {
            domainName = String.Empty;
            modId = 0;
            fileId = 0;

            if (String.IsNullOrEmpty(link))
            {
                return false;
            }

            var match = Regex.Match(Regex.Unescape(link), NxmModPattern);
            if (match.Success is false)
            {
                return false;
            }

            domainName = match.Groups["domain"].ToString().ToLower();

            return Int32.TryParse(match.Groups["mod"].ToString(), out modId) && Int32.TryParse(match.Groups["file"].ToString(), out fileId);
        }

        public async Task<string?> GetFileDownloadLink(NXM nxmData, string? serverName = null)
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

            return await GetFileDownloadLink(modId, fileId, match.Groups["key"].ToString(), match.Groups["expiry"].ToString(), serverName);
        }

        public async Task<string?> GetFileDownloadLink(int modId, int fileId, string? nxmKey = null, string? nxmExpiry = null, string? serverName = null)
        {
            if (String.IsNullOrEmpty(serverName) || _settings.IsPremium is false)
            {
                serverName = "Nexus CDN";
            }

            try
            {
                string url = $"games/stardewvalley/mods/{modId}/files/{fileId}/download_link.json";
                if (String.IsNullOrEmpty(nxmKey) is false && String.IsNullOrEmpty(nxmExpiry) is false)
                {
                    url = $"{url}?key={nxmKey}&expires={nxmExpiry}";
                }
                var response = await _client.GetAsync(url);

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

            return null;
        }

        /// <summary>
        /// Downloads a file to the Nexus folder. Passing an external token lets a caller abort the download from
        /// outside, such as a collection install being cancelled, without taking away the download panel's own
        /// per-file cancel button.
        /// </summary>
        public async Task<NexusDownloadResult> DownloadFileAndGetPath(string uri, string fileName, CancellationToken externalCancellationToken = default)
        {
            var requestUri = new Uri(uri);
            var downloadCancellationSource = externalCancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken) : new CancellationTokenSource();
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
            // Every caller here hands over a name that came from Nexus Mods, so the last chance to make it
            // usable as a filename is on the way into the path rather than at any one of them
            var filePath = Path.Combine(Pathing.GetNexusPath(), Pathing.GetSafePathSegment(fileName));

            // FileMode.CreateNew throws on a name that is already taken, which would otherwise send a download that
            // never started down the cleanup path below and delete whatever already held the name
            bool hasCreatedFile = false;
            try
            {

                var response = await _client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, downloadCancellationSource.Token);
                if (response.IsSuccessStatusCode is false)
                {
                    Program.helper.Log($"Failed to download mod file for Nexus Mods: HTTP {response.StatusCode}, {response.ReasonPhrase}", Helper.Status.Alert);
                    return new(DownloadResultKind.Failed, null);
                }

                using var fileStream = new FileStream(filePath, FileMode.CreateNew);
                hasCreatedFile = true;

                using var downloadStream = await response.Content.ReadAsStreamAsync();

                long? contentLength = response.Content.Headers.ContentLength;
                DownloadStarted?.Invoke(this, new ModDownloadStartedEventArgs(requestUri, fileName, contentLength, downloadCancellationSource));

                var buffer = new byte[81920];
                long totalBytesRead = 0;
                int bytesRead;
                while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length, downloadCancellationSource.Token)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, downloadCancellationSource.Token);
                    totalBytesRead += bytesRead;
                    DownloadProgressChanged?.Invoke(this, new ModDownloadProgressEventArgs(requestUri, totalBytesRead));
                }

                DownloadCompleted?.Invoke(this, new ModDownloadCompletedEventArgs(requestUri));
                return new(DownloadResultKind.Success, filePath);
            }
            catch (Exception ex)
            {
                // Delete partially downloaded file, if any
                if (hasCreatedFile)
                {
                    File.Delete(filePath);
                }

                if (ex is TaskCanceledException)
                {
                    Program.helper.Log($"The user canceled the download from Nexus from URL {uri}", Helper.Status.Info);
                    return new(DownloadResultKind.UserCanceled, null);
                }
                else
                {
                    Program.helper.Log($"Failed to download mod file for Nexus Mods: {ex}", Helper.Status.Alert);
                    DownloadFailed?.Invoke(this, new ModDownloadFailedEventArgs(requestUri));
                    return new(DownloadResultKind.Failed, null);
                }
            }
        }

        public async Task<List<Endorsement>> GetEndorsements()
        {
            try
            {
                var response = await _client.GetAsync($"user/endorsements");
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

            return new List<Endorsement>();
        }

        public async Task<EndorsementResponse> SetModEndorsement(int modId, bool isEndorsed)
        {
            try
            {
                var requestPackage = new StringContent("{\"Version\":\"1.0.0\"}", Encoding.UTF8, "application/json");
                var response = await _client.PostAsync($"games/stardewvalley/mods/{modId}/{(isEndorsed is true ? "endorse.json" : "abstain.json")}", requestPackage);
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

            return EndorsementResponse.Unknown;
        }

        public async Task<string?> DownloadThumbnail(int modId)
        {
            try
            {
                var response = await _client.GetAsync($"games/stardewvalley/mods/{modId}.json");
                if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content is not null)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    ModDetails modDetails = JsonSerializer.Deserialize<ModDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (modDetails is null)
                    {
                        Program.helper.Log($"Unable to get mod thumbnail for the mod {modId} on Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");

                        return null;
                    }

                    UpdateRequestCounts(response.Headers);

                    if (string.IsNullOrEmpty(modDetails.ThumbnailUrl))
                    {
                        Program.helper.Log($"The mod {modId} does not have a valid thumbnail image available on Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");
                        return null;
                    }

                    // Download the thumbnail
                    var thumbnailPath = Path.Combine(Pathing.GetThumbnailsPath(), $"{modId}{Path.GetExtension(modDetails.ThumbnailUrl)}");
                    using var fileStream = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true);
                    using var downloadStream = await _client.GetStreamAsync(modDetails.ThumbnailUrl);

                    await downloadStream.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();

                    return thumbnailPath;
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
                Program.helper.Log($"Unable to get mod thumbnail for the mod {modId} on Nexus Mods: {ex}", Helper.Status.Alert);
            }

            return null;
        }

        private void UpdateRequestCounts(HttpResponseHeaders headers)
        {
            if (headers.TryGetValues("x-rl-daily-limit", out var limitValues) && Int32.TryParse(limitValues.First(), out int dailyLimit))
            {
                DailyRequestsLimit = dailyLimit;
            }

            if (headers.TryGetValues("x-rl-daily-remaining", out var remainingValues) && Int32.TryParse(remainingValues.First(), out int dailyRemaining))
            {
                DailyRequestsRemaining = dailyRemaining;
            }

            DailyRequestLimitsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Returns true if file is not VirusScanStatus.QUARANTINED on Nexus, false if it is VirusScanStatus.QUARANTINED and null if unable to verify.
        /// </summary>
        /// <param name="nxmData"></param>
        /// <returns></returns>
        public async Task<bool?> ValidateFileSafety(NXM nxmData)
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

            return await ValidateFileSafety(modId, fileId);
        }

        /// <summary>
        /// Returns true if file is not VirusScanStatus.QUARANTINED on Nexus, false if it is VirusScanStatus.QUARANTINED and null if unable to verify.
        /// </summary>
        /// <param name="modId"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        public async Task<bool?> ValidateFileSafety(int modId, int fileId)
        {
            // TODO: May want to rewrite this to just use a dedicated library for GraphQL handling
            try
            {
                // Override Client.BaseAddress by using full URL
                var request = new
                {
                    query = $@"query GetModFiles
                    {{
                        modFiles(gameId: 1303, modId: {modId})
                        {{
                            fileId, scannedV2
                        }}
                    }}",
                };

                var response = await _client.PostAsJsonAsync(_graphQLBaseUrl, request);
                var result = JsonSerializer.Deserialize<QueryResponse<ModFileData>>(await response.Content.ReadAsStringAsync());

                if (result is null)
                {
                    Program.helper.Log($"Unable to verify file scan results: {(response is null ? "No response" : response.Content)}");
                    return null;
                }

                // Find the matching fileId
                var matchedScannedFile = result.Data.ModFiles.FirstOrDefault(f => f.Id == fileId);
                if (matchedScannedFile is null)
                {
                    Program.helper.Log($"Unable to verify file scan results: No file match with Id {fileId}");
                    return null;
                }

                return matchedScannedFile.VirusScanResults != ScannedModFile.VirusScanStatus.QUARANTINED;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to verify file scan results: {ex}");
                return null;
            }
        }
    }
}
