using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus;
using Stardrop.Models.Nexus.GraphQL;
using Stardrop.Models.Nexus.Web;
using Stardrop.Utilities.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Stardrop.Utilities.External
{
    public partial class NexusClient
    {
        // Matches the current collection URL format along with the older next.nexusmods.com one
        private static readonly Regex _collectionUrlPattern = new Regex(@"nexusmods\.com\/(?:games\/)?(?<domain>[a-z0-9]+)\/collections\/(?<slug>[a-z0-9]+)(?:\/revisions\/(?<revision>[0-9]+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _nxmCollectionRegex = new Regex(NxmCollectionPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Pulls a collection revision's metadata from Nexus' v2 GraphQL API. Passing a null revision resolves to the
        /// latest published one. The revision's DownloadLink is not the archive itself, feed it to
        /// <see cref="GetCollectionArchiveLink"/> to get a usable URI.
        /// </summary>
        public async Task<CollectionRevision?> GetCollectionRevision(string slug, int? revision = null, string domainName = "stardewvalley")
        {
            try
            {
                var request = new
                {
                    query = @"query GetCollectionRevision($slug: String, $domainName: String, $revision: Int)
                    {
                        collectionRevision(slug: $slug, domainName: $domainName, revision: $revision, viewAdultContent: true)
                        {
                            id
                            revisionNumber
                            downloadLink
                            totalSize
                            modCount
                            collection { id slug name summary user { name } }
                        }
                    }",
                    variables = new { slug, domainName, revision }
                };

                // Override Client.BaseAddress by using full URL
                var response = await _client.PostAsJsonAsync(_graphQLBaseUrl, request);
                var content = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode is false)
                {
                    Program.helper.Log($"Failed to get the collection revision for {slug}: HTTP {response.StatusCode}, {response.ReasonPhrase}", Helper.Status.Alert);
                    return null;
                }

                QueryResponse<CollectionRevisionData>? result;
                try
                {
                    result = JsonSerializer.Deserialize<QueryResponse<CollectionRevisionData>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException ex)
                {
                    Program.helper.Log($"Failed to deserialize the collection revision for {slug}: {ex.Message}", Helper.Status.Alert);
                    Program.helper.Log($"Response from Nexus Mods:\n{content}", Helper.Status.Debug);
                    return null;
                }

                if (result is null || result.Data is null || result.Data.CollectionRevision is null)
                {
                    Program.helper.Log($"Unable to parse the collection revision for {slug}. Response from Nexus Mods:\n{content}", Helper.Status.Alert);
                    return null;
                }

                return result.Data.CollectionRevision;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get the collection revision for {slug}: {ex}", Helper.Status.Alert);
                return null;
            }
        }

        /// <summary>
        /// Resolves a revision's download link into an actual archive URI. The link returns a list of CDN mirrors
        /// rather than the file, so the preferred server is used where it is offered.
        /// </summary>
        public async Task<string?> GetCollectionArchiveLink(string revisionDownloadLink, string? serverName = null)
        {
            try
            {
                var response = await _client.GetAsync(revisionDownloadLink);
                if (response.IsSuccessStatusCode is false || response.Content is null)
                {
                    Program.helper.Log($"Bad status given from Nexus Mods for the collection archive: HTTP {response.StatusCode}, {response.ReasonPhrase}", Helper.Status.Alert);
                    return null;
                }

                var content = (await response.Content.ReadAsStringAsync()).Trim();
                var downloadLinks = JsonSerializer.Deserialize<CollectionRevisionDownloadResult>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (downloadLinks is null || downloadLinks.DownloadLinks is null || downloadLinks.DownloadLinks.Count == 0)
                {
                    Program.helper.Log($"Unable to get the collection archive link. Response from Nexus Mods:\n{content}", Helper.Status.Alert);
                    return null;
                }

                UpdateRequestCounts(response.Headers);

                var preferredLink = downloadLinks.DownloadLinks.FirstOrDefault(l => String.IsNullOrEmpty(serverName) is false && String.IsNullOrEmpty(l.ShortName) is false && l.ShortName.Equals(serverName, StringComparison.OrdinalIgnoreCase));
                if (preferredLink is not null && String.IsNullOrEmpty(preferredLink.Uri) is false)
                {
                    return preferredLink.Uri;
                }

                return downloadLinks.DownloadLinks.First().Uri;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get the archive download link for the Nexus Mods collection: {ex}", Helper.Status.Alert);
                return null;
            }
        }

        /// <summary>
        /// Turns a parsed collection.json into a local install record. Entries that Stardrop cannot fetch itself are
        /// flagged for manual download rather than being dropped and optional entries start out skipped so nothing
        /// installs that the user did not ask for.
        /// </summary>
        public CollectionInstall CreateCollectionInstall(CollectionIndex index, string slug, int revisionNumber, string domainName = "stardewvalley")
        {
            var isPremium = Program.settings.NexusDetails is not null && Program.settings.NexusDetails.IsPremium;
            var install = new CollectionInstall(domainName, slug, revisionNumber)
            {
                Name = index.Info is not null && String.IsNullOrEmpty(index.Info.Name) is false ? index.Info.Name : slug,
                Curator = index.Info is null ? null : index.Info.Author,
                Summary = index.Info is null ? null : index.Info.Description,
                InstallInstructions = index.Info is null ? null : index.Info.InstallInstructions,
                RecommendsNewProfile = index.Config is null || index.Config.RecommendNewProfile
            };
            install.ProfileName = install.Name;

            if (index.Mods is null)
            {
                return install;
            }

            foreach (var collectionMod in index.Mods.OrderBy(m => m.Phase))
            {
                if (collectionMod.Source is null)
                {
                    Program.helper.Log($"Skipping {collectionMod.Name} in the collection {install.Name}, as it has no source", Helper.Status.Warning);
                    continue;
                }

                // Collections list SMAPI as a mod, though it is not one Stardrop can place in the mod folder
                if (collectionMod.Source.ModId == SmapiNexusModId)
                {
                    Program.helper.Log($"Skipping SMAPI in the collection {install.Name}, as it is installed separately");
                    continue;
                }

                var entry = new CollectionModEntry()
                {
                    Name = String.IsNullOrEmpty(collectionMod.Name) ? "Unknown mod" : collectionMod.Name,
                    Version = collectionMod.Version,
                    Author = collectionMod.Author,
                    IsOptional = collectionMod.Optional,
                    Phase = collectionMod.Phase,
                    SourceType = collectionMod.Source.Type,
                    UpdatePolicy = collectionMod.Source.UpdatePolicy,
                    Tag = collectionMod.Source.Tag,
                    NexusModId = collectionMod.Source.ModId,
                    NexusFileId = collectionMod.Source.FileId,
                    ExternalUri = collectionMod.Source.Url,
                    Md5Checksum = collectionMod.Source.MD5Checksum,
                    FileExpression = collectionMod.Source.FileExpression,
                    LogicalFilename = collectionMod.Source.LogicalFilename,
                    SizeBytes = collectionMod.Source.Size
                };

                entry.Status = GetInitialStatus(entry, isPremium);
                install.Mods.Add(entry);
            }

            install.Rules = ResolveModRules(index.ModRules, install.Mods);

            return install;
        }

        /// <summary>
        /// Matches each mod rule's two ends back to the entries they describe. Rules that point at something not in
        /// this collection are dropped, as a rule with an end that cannot be found has nothing to act on.
        /// </summary>
        private static List<CollectionEntryRule> ResolveModRules(List<CollectionModRule>? modRules, List<CollectionModEntry> entries)
        {
            var rules = new List<CollectionEntryRule>();
            if (modRules is null)
            {
                return rules;
            }

            foreach (var modRule in modRules)
            {
                if (modRule.Type is CollectionModRuleType.Unknown)
                {
                    continue;
                }

                var rule = new CollectionEntryRule()
                {
                    Type = modRule.Type,
                    SourceIndex = CollectionReferenceMatcher.FindEntryIndex(entries, modRule.Source),
                    TargetIndex = CollectionReferenceMatcher.FindEntryIndex(entries, modRule.Reference)
                };

                if (rule.IsResolved() is false)
                {
                    Program.helper.Log($"Skipping an unresolvable {modRule.Type} mod rule, as one of its ends is not an entry in this collection");
                    continue;
                }

                rules.Add(rule);
            }

            Program.helper.Log($"Resolved {rules.Count} of {modRules.Count} mod rule(s) from the collection index");

            return rules;
        }

        /// <summary>
        /// Where an entry starts out. An optional entry follows the same route as a required one: with Premium
        /// Stardrop can fetch it along with the rest, and the curator put it in the collection for a reason. Without
        /// Premium it lands as a manual download like everything else, where the window names it as optional so the
        /// user can tell which of the pages they are being sent to are ones they can pass on.
        /// </summary>
        private static CollectionModStatus GetInitialStatus(CollectionModEntry entry, bool isPremium)
        {
            switch (entry.SourceType)
            {
                case CollectionModSourceType.Nexus:
                    return isPremium ? CollectionModStatus.Pending : CollectionModStatus.AwaitingManualDownload;
                case CollectionModSourceType.Bundle:
                    // Bundled files travel inside the collection archive, so they are already on disk
                    return CollectionModStatus.Pending;
                default:
                    return CollectionModStatus.AwaitingManualDownload;
            }
        }

        /// <summary>
        /// Pulls the domain, slug and revision number out of an nxm collection link.
        /// </summary>
        public static bool TryParseCollectionNxmLink(string link, out string domainName, out string slug, out int? revision)
        {
            domainName = String.Empty;
            slug = String.Empty;
            revision = null;

            if (String.IsNullOrEmpty(link))
            {
                return false;
            }

            var match = _nxmCollectionRegex.Match(Regex.Unescape(link));
            if (match.Success is false)
            {
                return false;
            }

            domainName = match.Groups["domain"].Value;
            slug = match.Groups["slug"].Value;

            if (Int32.TryParse(match.Groups["revision"].Value, out var parsedRevision))
            {
                revision = parsedRevision;
            }

            return true;
        }

        /// <summary>
        /// Pulls the domain, slug and (optionally) revision number out of a collection web URL.
        /// </summary>
        public static bool TryParseCollectionUrl(string url, out string domainName, out string slug, out int? revision)
        {
            domainName = String.Empty;
            slug = String.Empty;
            revision = null;

            if (String.IsNullOrEmpty(url))
            {
                return false;
            }

            var match = _collectionUrlPattern.Match(url);
            if (match.Success is false)
            {
                return false;
            }

            domainName = match.Groups["domain"].Value;
            slug = match.Groups["slug"].Value;

            if (match.Groups["revision"].Success && Int32.TryParse(match.Groups["revision"].Value, out var parsedRevision))
            {
                revision = parsedRevision;
            }

            return true;
        }
    }
}
