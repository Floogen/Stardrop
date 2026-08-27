using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Stardrop.Models.Data
{
    /// <summary>
    /// A single mod that a collection entry's archive placed on disk. An archive can hold several mods, so an entry
    /// maps to zero or more of these.
    /// </summary>
    public record InstalledModRecord(string UniqueId, string? FolderName = null);

    /// <summary>
    /// A single mod pinned by a collection revision. The pin data lives here rather than on the generated profile,
    /// as a profile only tracks which mods are enabled and has nowhere to record a mod / file ID pair.
    /// </summary>
    public class CollectionModEntry
    {
        public string Name { get; set; } = String.Empty;
        public string? Version { get; set; }
        public string? Author { get; set; }
        public bool IsOptional { get; set; }
        /// <summary>Install order grouping from collection.json. Lower phases install first</summary>
        public int Phase { get; set; }
        public CollectionModSourceType SourceType { get; set; } = CollectionModSourceType.Nexus;
        /// <summary>Nexus' update policy for this pin (exact, prefer, latest). Entries pinned to exact should not raise update prompts</summary>
        public string? UpdatePolicy { get; set; }
        /// <summary>The curator's own identifier for this entry, which is what a mod rule points at when it has one</summary>
        public string? Tag { get; set; }
        public int? NexusModId { get; set; }
        public int? NexusFileId { get; set; }
        /// <summary>Set for Browse and Direct entries, which Stardrop cannot fetch on the user's behalf</summary>
        public string? ExternalUri { get; set; }
        public string? Md5Checksum { get; set; }
        /// <summary>Used to locate a Bundle entry's file inside the extracted collection archive</summary>
        public string? FileExpression { get; set; }
        public string? LogicalFilename { get; set; }
        /// <summary>Download size from collection.json, used to drive the aggregate progress bar</summary>
        public long? SizeBytes { get; set; }

        /// <summary>Name of the archive this entry was installed from. The archive itself is removed after installing, so this is a record rather than something to reuse</summary>
        public string? SourceArchivePath { get; set; }
        /// <summary>Everything this entry's archive placed on disk, taken from AddMods rather than matched by name</summary>
        public List<InstalledModRecord> InstalledMods { get; set; } = new List<InstalledModRecord>();
        /// <summary>Names of the mods this entry's files were copied over, when it was applied as an overlay rather than installed</summary>
        public List<string> OverlayTargets { get; set; } = new List<string>();
        public CollectionModStatus Status { get; set; } = CollectionModStatus.Pending;
        public string? FailureReason { get; set; }

        public bool IsPinnedExactly()
        {
            return String.Equals(UpdatePolicy, "exact", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether this entry is accounted for, whether by Stardrop installing it or by the user already having it.
        /// </summary>
        public bool IsSatisfied()
        {
            return Status is CollectionModStatus.Installed or CollectionModStatus.AppliedAsOverlay;
        }

        public bool IsFromNexus()
        {
            return SourceType is CollectionModSourceType.Nexus && NexusModId is not null && NexusFileId is not null;
        }
    }

    /// <summary>
    /// A rule from collection.json's modRules, resolved to the entries it points at. Indices into
    /// <see cref="CollectionInstall.Mods"/> are stored rather than the reference fields, as the matching only has
    /// to be worked out once, at the point the collection is read.
    /// </summary>
    public class CollectionEntryRule
    {
        public CollectionModRuleType Type { get; set; }
        /// <summary>The entry the rule belongs to, or -1 when it could not be matched to one</summary>
        public int SourceIndex { get; set; } = -1;
        /// <summary>The entry the rule points at, or -1 when it could not be matched to one</summary>
        public int TargetIndex { get; set; } = -1;

        public bool IsResolved()
        {
            return SourceIndex >= 0 && TargetIndex >= 0 && SourceIndex != TargetIndex;
        }
    }

    /// <summary>
    /// A collection revision the user has installed. Owns the generated profile rather than being one and is
    /// cached under <see cref="Utilities.Pathing.GetCollectionsCacheFolderPath"/> as a single JSON file per collection.
    /// </summary>
    public class CollectionInstall
    {
        /// <summary>Stable identifier used as both the folder name under the collections root and the SourceId on every mod it installs</summary>
        public string SourceId { get; set; } = String.Empty;
        public string Slug { get; set; } = String.Empty;
        public string DomainName { get; set; } = "stardewvalley";
        public int RevisionNumber { get; set; }
        public string Name { get; set; } = String.Empty;
        public string? Curator { get; set; }
        public string? Summary { get; set; }
        /// <summary>Free text from the curator that is worth surfacing once the install finishes</summary>
        public string? InstallInstructions { get; set; }
        /// <summary>The curator's own signal, taken from collectionConfig.recommendNewProfile</summary>
        public bool RecommendsNewProfile { get; set; } = true;
        public DateTime InstallTimestamp { get; set; } = DateTime.Now;
        /// <summary>When the revision check last ran, so a window opening can say how current its answer is</summary>
        public DateTime? LastRefreshTimestamp { get; set; }
        /// <summary>Latest revision Nexus reported, or null where the check has not run or could not reach them</summary>
        public int? LatestRevisionNumber { get; set; }
        /// <summary>
        /// The revision currently being applied, or null when nothing is part way through. RevisionNumber stays on
        /// the last revision fully on disk while this is set, so an update that stalls on a manual download never
        /// reads as though it completed.
        /// </summary>
        public int? PendingRevisionNumber { get; set; }
        /// <summary>Name of the profile generated for this collection, so the two can be re-linked after a rename</summary>
        public string ProfileName { get; set; } = String.Empty;
        public List<CollectionModEntry> Mods { get; set; } = new List<CollectionModEntry>();
        /// <summary>The curator's modRules, resolved against <see cref="Mods"/></summary>
        public List<CollectionEntryRule> Rules { get; set; } = new List<CollectionEntryRule>();

        public CollectionInstall()
        {

        }

        public CollectionInstall(string domainName, string slug, int revisionNumber)
        {
            DomainName = domainName;
            Slug = slug;
            RevisionNumber = revisionNumber;
            SourceId = CreateSourceId(domainName, slug);
        }

        /// <summary>
        /// Builds a filesystem-safe source ID. Periods are stripped because a folder containing one is skipped
        /// during discovery whenever Settings.IgnoreHiddenFolders is enabled.
        ///
        /// The revision is deliberately absent, even though <see cref="RevisionNumber"/> sits alongside it. A
        /// collection keeps one identity for its whole life so that a new revision updates the install it already
        /// has, rather than appearing as an unrelated second collection with its own folder and profile.
        /// </summary>
        public static string CreateSourceId(string domainName, string slug)
        {
            var invalidCharacters = System.IO.Path.GetInvalidFileNameChars().Concat(new[] { '.', ' ' }).ToArray();
            var safeSlug = String.Join("-", slug.Split(invalidCharacters, StringSplitOptions.RemoveEmptyEntries));
            var safeDomain = String.Join("-", domainName.Split(invalidCharacters, StringSplitOptions.RemoveEmptyEntries));

            return $"{safeDomain}-{safeSlug}";
        }

        public bool IsFullyInstalled()
        {
            return GetPendingCount() == 0;
        }

        /// <summary>
        /// Whether the curator has published a revision newer than the installed one. A collection revision is the
        /// only unit of update Stardrop recognises, as its mods take their versions from the collection rather than
        /// from their own update keys.
        /// </summary>
        public bool HasUpdate()
        {
            return LatestRevisionNumber is not null && LatestRevisionNumber.Value > RevisionNumber;
        }

        /// <summary>Whether a revision is part way through being applied, with entries still to land</summary>
        public bool IsUpdateInProgress()
        {
            return PendingRevisionNumber is not null;
        }

        /// <summary>
        /// Moves the record onto the revision being applied, once every entry of it has landed. Called both at the
        /// end of an update and by the paths that satisfy a single entry afterwards, since the last manual download
        /// a user drops in is what completes the update.
        /// </summary>
        public bool TryCompleteUpdate()
        {
            if (PendingRevisionNumber is null || IsFullyInstalled() is false)
            {
                return false;
            }

            RevisionNumber = PendingRevisionNumber.Value;
            PendingRevisionNumber = null;

            return true;
        }

        public int GetPendingCount()
        {
            return Mods.Count(m => m.Status is CollectionModStatus.Pending or CollectionModStatus.AwaitingManualDownload or CollectionModStatus.Downloading or CollectionModStatus.Failed);
        }

        /// <summary>
        /// Entries that ended up on disk. Overlays are excluded, as they are the curator's configuration rather than
        /// mods of their own and counting them would report more mods than the collection actually pins.
        /// </summary>
        public int GetInstalledCount()
        {
            return Mods.Count(m => m.Status is CollectionModStatus.Installed);
        }

        /// <summary>The number of mods the collection pins, leaving out the configuration entries among them</summary>
        public int GetModCount()
        {
            return Mods.Count - Mods.Count(m => m.Status is CollectionModStatus.AppliedAsOverlay);
        }

        /// <summary>The collection's own page on Nexus Mods</summary>
        public string GetPageUri()
        {
            var domainName = String.IsNullOrEmpty(DomainName) ? "stardewvalley" : DomainName;

            return $"https://next.nexusmods.com/{domainName}/collections/{Slug}";
        }

        /// <summary>
        /// The page for one specific revision, which is where the curator's notes for it live. The collection's own
        /// page opens on whatever revision is latest without saying which, so an update links here instead.
        /// </summary>
        public string GetRevisionPageUri(int revisionNumber)
        {
            return $"{GetPageUri()}/revisions/{revisionNumber}";
        }

        /// <summary>
        /// The page to send the user to for an entry they have to handle themselves, preferring the curator's own
        /// link over one built from the Nexus IDs. Returns null when neither is available, which leaves the entry
        /// as plain text wherever it is shown.
        /// </summary>
        /// <param name="useModManagerLink">Whether a pinned file asks Nexus Mods for the mod manager download</param>
        public string? GetEntryPageUri(CollectionModEntry entry, bool useModManagerLink = true)
        {
            if (String.IsNullOrEmpty(entry.ExternalUri) is false)
            {
                return entry.ExternalUri;
            }

            if (entry.NexusModId is null)
            {
                return null;
            }

            var domainName = String.IsNullOrEmpty(DomainName) ? "stardewvalley" : DomainName;
            if (entry.NexusFileId is null)
            {
                return $"https://www.nexusmods.com/{domainName}/mods/{entry.NexusModId}";
            }

            // The collection pins a specific file, so the files tab saves the user hunting for it themselves
            var filePage = $"https://www.nexusmods.com/{domainName}/mods/{entry.NexusModId}?tab=files&file_id={entry.NexusFileId}";

            // An entry still waiting on the user gets the mod manager form of that link, which has Nexus hand the
            // file back as an nxm link rather than leaving them to find the button on the page. An entry already
            // accounted for keeps the plain page, since a download for one has no pending entry to match and would
            // land as a loose second copy of a mod the collection already holds
            if (useModManagerLink is false || entry.IsSatisfied())
            {
                return filePage;
            }

            return $"{filePage}&nmm=1";
        }

        /// <summary>
        /// Total download size of the entries Stardrop will fetch. Returns zero when no entry reports a size, which
        /// the caller should treat as a signal to fall back to counting mods.
        /// </summary>
        public long GetPendingDownloadSize()
        {
            return Mods.Where(m => m.Status is CollectionModStatus.Pending && m.SizeBytes is not null).Sum(m => m.SizeBytes!.Value);
        }

        /// <summary>
        /// Builds the profile's enabled list. Every entry resolves to the collection's own copy, as every entry is
        /// installed into the collection's folder rather than being satisfied by something already on disk.
        /// </summary>
        public List<ModReference> GetEnabledModReferences()
        {
            var references = new List<ModReference>();
            foreach (var entry in Mods.Where(m => m.IsSatisfied()))
            {
                foreach (var installedMod in entry.InstalledMods)
                {
                    references.Add(new ModReference(installedMod.UniqueId, SourceId));
                }
            }

            return references.Distinct().ToList();
        }

        /// <summary>
        /// Entries the user has to fetch themselves, either because the source is not Nexus or because they are
        /// not a Premium member. Collected up so the UI can present one list rather than a dialog per mod.
        /// </summary>
        public List<CollectionModEntry> GetManualDownloads()
        {
            return Mods.Where(m => m.Status is CollectionModStatus.AwaitingManualDownload).ToList();
        }

        public List<CollectionModEntry> GetFailures()
        {
            return Mods.Where(m => m.Status is CollectionModStatus.Failed).ToList();
        }

        /// <summary>
        /// Entries in the order their files should be written, so that where two of them place the same path the one
        /// the rules put last wins. A topological sort over the before and after rules, with everything the rules say
        /// nothing about left in the order the collection declares it.
        /// </summary>
        public List<CollectionModEntry> GetInstallOrder()
        {
            return BuildInstallOrder(out _);
        }

        /// <summary>Whether the ordering rules form a cycle, which leaves the entries caught in it unordered</summary>
        public bool HasRuleCycle()
        {
            BuildInstallOrder(out var hasCycle);

            return hasCycle;
        }

        private List<CollectionModEntry> BuildInstallOrder(out bool hasCycle)
        {
            var dependencyCounts = new int[Mods.Count];
            var dependents = new List<int>[Mods.Count];
            for (int index = 0; index < Mods.Count; index++)
            {
                dependents[index] = new List<int>();
            }

            foreach (var rule in Rules.Where(r => r.IsResolved() && r.Type is CollectionModRuleType.After or CollectionModRuleType.Before))
            {
                if (rule.SourceIndex >= Mods.Count || rule.TargetIndex >= Mods.Count)
                {
                    continue;
                }

                // After means the source is written once the target has been and before is that same edge reversed
                var from = rule.Type is CollectionModRuleType.After ? rule.TargetIndex : rule.SourceIndex;
                var to = rule.Type is CollectionModRuleType.After ? rule.SourceIndex : rule.TargetIndex;

                dependents[from].Add(to);
                dependencyCounts[to] += 1;
            }

            var ordered = new List<CollectionModEntry>();
            var placed = new bool[Mods.Count];
            var ready = new Queue<int>(Enumerable.Range(0, Mods.Count).Where(i => dependencyCounts[i] == 0));

            while (ready.Count > 0)
            {
                var index = ready.Dequeue();
                ordered.Add(Mods[index]);
                placed[index] = true;

                foreach (var dependent in dependents[index])
                {
                    dependencyCounts[dependent] -= 1;
                    if (dependencyCounts[dependent] == 0)
                    {
                        ready.Enqueue(dependent);
                    }
                }
            }

            hasCycle = ordered.Count < Mods.Count;

            // A cycle leaves no valid order at all. Refusing to install over that is the worse outcome of the two,
            // so the entries caught in one go last, in the order the collection declares them
            for (int index = 0; index < Mods.Count; index++)
            {
                if (placed[index] is false)
                {
                    ordered.Add(Mods[index]);
                }
            }

            return ordered;
        }

        /// <summary>
        /// The entries this one is ordered after, which for configuration are the mods its files are meant to land
        /// on top of. Used for reporting rather than for placement, as placement follows the install order.
        /// </summary>
        public List<CollectionModEntry> GetOverlayTargets(CollectionModEntry entry)
        {
            var targets = new List<CollectionModEntry>();

            var sourceIndex = Mods.IndexOf(entry);
            if (sourceIndex < 0)
            {
                return targets;
            }

            foreach (var rule in Rules.Where(r => r.Type is CollectionModRuleType.After && r.SourceIndex == sourceIndex && r.IsResolved()))
            {
                if (rule.TargetIndex >= Mods.Count)
                {
                    continue;
                }

                targets.Add(Mods[rule.TargetIndex]);
            }

            return targets.Distinct().ToList();
        }

        public List<CollectionModEntry> GetOverlays()
        {
            return Mods.Where(m => m.Status is CollectionModStatus.AppliedAsOverlay).ToList();
        }

        /// <summary>
        /// Conflicts the curator declared between two entries. Only pairs that both ended up accounted for are
        /// returned, as a conflict with something that was never installed is not something the user can act on.
        /// </summary>
        public List<(CollectionModEntry Source, CollectionModEntry Target)> GetConflicts()
        {
            var conflicts = new List<(CollectionModEntry Source, CollectionModEntry Target)>();
            foreach (var rule in Rules.Where(r => r.Type is CollectionModRuleType.Conflicts && r.IsResolved()))
            {
                if (rule.SourceIndex >= Mods.Count || rule.TargetIndex >= Mods.Count)
                {
                    continue;
                }

                var source = Mods[rule.SourceIndex];
                var target = Mods[rule.TargetIndex];
                if (source.IsSatisfied() is false || target.IsSatisfied() is false)
                {
                    continue;
                }

                conflicts.Add((source, target));
            }

            return conflicts;
        }
    }
}
