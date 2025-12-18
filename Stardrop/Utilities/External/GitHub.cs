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
            KeyValuePair<string, string>? versionToUri = null;

            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Stardrop - SDV Mod Manager");

            try
            {
                var response = await client.GetAsync("https://api.github.com/repos/Pathoschild/SMAPI/releases/latest");

                if (response.Content is not null)
                {
                    JsonDocument parsedContent = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    string tagName = parsedContent.RootElement.GetProperty("tag_name").ToString();
                    string downloadUri = parsedContent.RootElement.GetProperty("html_url").ToString();
                    downloadUri = String.Concat(downloadUri, "/", $"SMAPI-{tagName}-installer.zip").Replace("releases/tag/", "releases/download/");

                    versionToUri = new KeyValuePair<string, string>(tagName, downloadUri);
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get latest the version of SMAPI: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return versionToUri;
        }

        public async static Task<string> DownloadLatestSMAPIRelease(string uri)
        {
            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Stardrop - SDV Mod Manager");

            string downloadedArchivePath = String.Empty;
            try
            {
                var response = await client.GetAsync(uri);
                using (var archive = ArchiveFactory.Open(await response.Content.ReadAsStreamAsync()))
                {
                    downloadedArchivePath = Path.Combine(Pathing.GetSmapiUpgradeFolderPath(), Path.GetDirectoryName(archive.Entries.First().Key));
                    foreach (var entry in archive.Entries)
                    {
                        entry.WriteToDirectory(Pathing.GetSmapiUpgradeFolderPath(), new SharpCompress.Common.ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
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
            KeyValuePair<string, string>? versionToUri = null;

            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Stardrop - SDV Mod Manager");

            try
            {
                var response = await client.GetAsync("https://api.github.com/repos/Floogen/Stardrop/releases/latest");

                if (response.Content is not null)
                {
                    JsonDocument parsedContent = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    string tagName = parsedContent.RootElement.GetProperty("tag_name").ToString();
                    string downloadUri = parsedContent.RootElement.GetProperty("html_url").ToString();
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        downloadUri = String.Concat(downloadUri, "/", "Stardrop-osx-x64.zip");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        downloadUri = String.Concat(downloadUri, "/", "Stardrop-linux-x64.zip");
                    }
                    else
                    {
                        downloadUri = String.Concat(downloadUri, "/", "Stardrop-win-x64.zip");
                    }
                    downloadUri = downloadUri.Replace("releases/tag/", "releases/download/");

                    versionToUri = new KeyValuePair<string, string>(tagName, downloadUri);
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get latest the version of Stardrop: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return versionToUri;
        }

        public async static Task<string> DownloadLatestStardropRelease(string uri)
        {
            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Stardrop - SDV Mod Manager");

            string downloadedArchivePath = String.Empty;
            try
            {
                var response = await client.GetAsync(uri);
                using (var archive = ArchiveFactory.Open(await response.Content.ReadAsStreamAsync()))
                {
                    foreach (var entry in archive.Entries)
                    {
                        entry.WriteToDirectory(Directory.GetCurrentDirectory(), new SharpCompress.Common.ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
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
