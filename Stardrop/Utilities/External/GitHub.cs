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

namespace Stardrop.Utilities.External
{
    static class GitHub
    {
        public async static Task<KeyValuePair<string, string>?> GetLatestRelease()
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
                    versionToUri = new KeyValuePair<string, string>(parsedContent.RootElement.GetProperty("tag_name").ToString(), parsedContent.RootElement.GetProperty("html_url").ToString());
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get latest the version of Stardrop: {ex}", Helper.Status.Alert);
            }
            client.Dispose();

            return versionToUri;
        }
    }
}
