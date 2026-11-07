using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stardrop.Utilities.External
{
    static class GitHub
    {
        public async static Task<KeyValuePair<string, string>?> GetLatestSMAPIRelease()
        {

            // Create a throwaway client
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "Stardrop - SDV Mod Manager");

            try
            {
                using HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/Pathoschild/SMAPI/releases/latest");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Program.helper.Log($"Failed to get latest version of SMAPI. HTTP Status: {(int)response.StatusCode} {response.StatusCode}. Response: {errorContent}", Helper.Status.Warning);
                    return null;
                }

                using JsonDocument parsedContent = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                var root = parsedContent.RootElement;

                if(root.TryGetProperty("tag_name", out JsonElement tagNameElement) && root.TryGetProperty("html_url", out JsonElement htmlUrlElement))
                {
                    string tagName = tagNameElement.ToString();
                    string downloadUri = htmlUrlElement.ToString();
                    return new KeyValuePair<string, string>(tagName, downloadUri);
                }

                Program.helper.Log($"GitHub API response is missing required fields.", Helper.Status.Warning);
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get latest the version of SMAPI: {ex}", Helper.Status.Alert);
            }
            return null;
        }

        public async static Task<string> DownloadLatestSMAPIRelease(string uri)
        {
            // Create a throwaway client
            HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "Stardrop - SDV Mod Manager");

            string downloadedArchivePath = string.Empty;
            try
            {
                var response = await client.GetAsync(uri);
                // TODO refactor this to use async
                using (var archive = ArchiveFactory.OpenArchive(await response.Content.ReadAsStreamAsync()))
                {
                    downloadedArchivePath = Path.Combine(Pathing.GetSmapiUpgradeFolderPath(), Path.GetDirectoryName(archive.Entries.First().Key)!);
                    foreach (var entry in archive.Entries)
                    {
                        await entry.WriteToDirectoryAsync(Pathing.GetSmapiUpgradeFolderPath(), new SharpCompress.Common.ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
                    }
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to download latest the version of SMAPI: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return downloadedArchivePath;
        }

        public async static Task<KeyValuePair<string, string>?> GetLatestStardropRelease()
        {
            // Create a throwaway client
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "Stardrop - SDV Mod Manager");

            try
            {
                using HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/Floogen/Stardrop/releases/latest");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Program.helper.Log($"Failed to get latest version of Stardrop. HTTP Status: {(int)response.StatusCode} {response.StatusCode}. Response: {errorContent}", Helper.Status.Warning);
                    return null;
                }
                using JsonDocument parsedContent = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                var root = parsedContent.RootElement;

                if (root.TryGetProperty("tag_name", out var tagNameElement) && root.TryGetProperty("html_url", out var htmlUrlElement))
                {
                    
                    string tagName = tagNameElement.ToString();
                    string downloadUri = htmlUrlElement.ToString();
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        downloadUri = string.Concat(downloadUri, "/", "Stardrop-osx-x64.zip");
                        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                        {
                            downloadUri = string.Concat(downloadUri, "/", "Stardrop-osx-arm64.zip");
                        }
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        downloadUri = string.Concat(downloadUri, "/", "Stardrop-linux-x64.zip");
                        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                        {
                            downloadUri = string.Concat(downloadUri, "/", "Stardrop-linux-arm64.zip");
                        }
                    }
                    else
                    {
                        downloadUri = string.Concat(downloadUri, "/", "Stardrop-win-x64.zip");
                        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                        {
                            downloadUri = string.Concat(downloadUri, "/", "Stardrop-win-arm64.zip");
                        }
                    }
                    downloadUri = downloadUri.Replace("releases/tag/", "releases/download/");

                    return new KeyValuePair<string, string>(tagName, downloadUri);
                }
                Program.helper.Log($"GitHub API response is missing required fields.", Helper.Status.Warning);
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get latest the version of Stardrop: {ex}", Helper.Status.Alert);
            }
            return null;
        }

        public async static Task<string> DownloadLatestStardropRelease(string uri)
        {
            // Create a throwaway client
            HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "Stardrop - SDV Mod Manager");

            string downloadedArchivePath = string.Empty;
            try
            {
                var response = await client.GetAsync(uri);
                // TODO refactor this to use async
                using (var archive = ArchiveFactory.OpenArchive(await response.Content.ReadAsStreamAsync()))
                {
                    foreach (var entry in archive.Entries)
                    {
                        await entry.WriteToDirectoryAsync(Directory.GetCurrentDirectory(), new SharpCompress.Common.ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
                    }
                }

                var extractFolderName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Stardrop.app" : "Stardrop";
                var adjustedExtractFolderName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "~Stardrop.app" : "~Stardrop";
                if (Directory.Exists(extractFolderName))
                {
                    if (Directory.Exists(adjustedExtractFolderName))
                    {
                        Directory.Delete(adjustedExtractFolderName, true);
                    }
                    Directory.Move(extractFolderName, adjustedExtractFolderName);
                }
                downloadedArchivePath = Path.Combine(Directory.GetCurrentDirectory(), adjustedExtractFolderName);
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to download latest the version of Stardrop: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return downloadedArchivePath;
        }
    }
}
