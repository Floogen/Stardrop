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
        public int? NexusModId { get; set; }
        public int? NexusFileId { get; set; }
        /// <summary>Set for Browse and Direct entries, which Stardrop cannot fetch on the user's behalf</summary>
        public string? ExternalUri { get; set; }
        public string? Md5Checksum { get; set; }
        /// <summary>Used to locate a Bundle entry's file inside the extracted collection archive</summary>
        public string? FileExpression { get; set; }
        public string? LogicalFilename { get; set; }

        /// <summary>The archive this entry was installed from, recorded so a repair can skip re-downloading</summary>
        public string? SourceArchivePath { get; set; }
        /// <summary>
        /// Set when the entry was satisfied by a mod the user already had. Holds that mod's reference so the profile
        /// can point at it, and so a later validation pass can tell whether it has since drifted off the pin.
        /// </summary>
        public ModReference? SatisfiedBy { get; set; }
        /// <summary>The version that was present when the entry was satisfied externally, for drift detection</summary>
        public string? SatisfiedByVersion { get; set; }
        /// <summary>Everything this entry's archive placed on disk, taken from AddMods rather than matched by name</summary>
        public List<InstalledModRecord> InstalledMods { get; set; } = new List<InstalledModRecord>();
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
            return Status is CollectionModStatus.Installed or CollectionModStatus.SatisfiedExternally;
        }

        public bool IsFromNexus()
        {
            return SourceType is CollectionModSourceType.Nexus && NexusModId is not null && NexusFileId is not null;
        }
    }

    /// <summary>
    /// A collection revision the user has installed. Owns the generated profile rather than being one, and is
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
        public DateTime? LastRefreshTimestamp { get; set; }
        /// <summary>Name of the profile generated for this collection, so the two can be re-linked after a rename</summary>
        public string ProfileName { get; set; } = String.Empty;
        public List<CollectionModEntry> Mods { get; set; } = new List<CollectionModEntry>();

        public CollectionInstall()
        {

        }

        public CollectionInstall(string domainName, string slug, int revisionNumber)
        {
            DomainName = domainName;
            Slug = slug;
            RevisionNumber = revisionNumber;
            SourceId = CreateSourceId(domainName, slug, revisionNumber);
        }

        /// <summary>
        /// Builds a filesystem-safe source ID. Periods are stripped because a folder containing one is skipped
        /// during discovery whenever Settings.IgnoreHiddenFolders is enabled.
        /// </summary>
        public static string CreateSourceId(string domainName, string slug, int revisionNumber)
        {
            var invalidCharacters = System.IO.Path.GetInvalidFileNameChars().Concat(new[] { '.', ' ' }).ToArray();
            var safeSlug = String.Join("-", slug.Split(invalidCharacters, StringSplitOptions.RemoveEmptyEntries));
            var safeDomain = String.Join("-", domainName.Split(invalidCharacters, StringSplitOptions.RemoveEmptyEntries));

            return $"{safeDomain}-{safeSlug}-r{revisionNumber}";
        }

        public bool IsFullyInstalled()
        {
            return GetPendingCount() == 0;
        }

        public int GetPendingCount()
        {
            return Mods.Count(m => m.Status is CollectionModStatus.Pending or CollectionModStatus.AwaitingManualDownload or CollectionModStatus.Downloading or CollectionModStatus.Failed);
        }

        public int GetReusedCount()
        {
            return Mods.Count(m => m.Status is CollectionModStatus.SatisfiedExternally);
        }

        /// <summary>
        /// Builds the profile's enabled list. Entries Stardrop installed resolve to the collection's own copy, while
        /// reused entries point at whatever the user already had, so both end up junctioned at launch.
        /// </summary>
        public List<ModReference> GetEnabledModReferences()
        {
            var references = new List<ModReference>();
            foreach (var entry in Mods.Where(m => m.IsSatisfied()))
            {
                if (entry.Status is CollectionModStatus.SatisfiedExternally)
                {
                    if (entry.SatisfiedBy is not null)
                    {
                        references.Add(entry.SatisfiedBy);
                    }

                    continue;
                }

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
    }
}
