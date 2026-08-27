using System;
using System.Collections.Generic;
using System.Linq;

namespace Stardrop.Models.Data
{
    /// <summary>
    /// An entry whose pin has moved between revisions, holding both sides so the folders the old file left behind
    /// can be cleared once the new one has landed.
    /// </summary>
    public record CollectionEntryReplacement(CollectionModEntry Previous, CollectionModEntry Updated);

    /// <summary>
    /// What applying a revision changes against the one already installed. Produced before any downloading starts,
    /// so the install pass, the profile amendment and the summary all read the same answer rather than each working
    /// it out again.
    /// </summary>
    public class CollectionUpdatePlan
    {
        /// <summary>The record being replaced, kept for the profile amendment and for the summary</summary>
        public CollectionInstall Previous { get; }

        /// <summary>Entries this revision introduces, which install like any other new entry</summary>
        public List<CollectionModEntry> Added { get; } = new List<CollectionModEntry>();
        /// <summary>Entries whose pin has not moved, carried over with their install state intact</summary>
        public List<CollectionModEntry> Unchanged { get; } = new List<CollectionModEntry>();
        /// <summary>Entries the curator has pinned to a different file, which are downloaded again</summary>
        public List<CollectionEntryReplacement> Replaced { get; } = new List<CollectionEntryReplacement>();
        /// <summary>Entries this revision no longer lists. Their files stay on disk, though the profile drops them</summary>
        public List<CollectionModEntry> Removed { get; } = new List<CollectionModEntry>();

        public CollectionUpdatePlan(CollectionInstall previous)
        {
            Previous = previous;
        }

        /// <summary>Whether anything actually differs, so a revision bump with an identical mod list can say so</summary>
        public bool HasChanges()
        {
            return Added.Count > 0 || Replaced.Count > 0 || Removed.Count > 0;
        }

        /// <summary>
        /// Every unique ID the previous revision had on disk. Anything in here that the profile does not list was
        /// turned off by the user, since the install pass enables everything it installs.
        /// </summary>
        public List<string> GetPreviouslyInstalledIds()
        {
            var uniqueIds = new List<string>();

            foreach (var entry in Previous.Mods.Where(m => m.IsSatisfied()))
            {
                foreach (var installedMod in entry.InstalledMods)
                {
                    uniqueIds.Add(installedMod.UniqueId);
                }
            }

            return uniqueIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Folders the replaced entries occupied that their replacements did not write into. A mod renaming its
        /// folder between versions would otherwise leave the old copy behind, enabled by nothing and reported by
        /// the grid as part of this collection.
        /// </summary>
        public List<string> GetStaleFolderNames()
        {
            var staleFolders = new List<string>();

            foreach (var replacement in Replaced)
            {
                // A replacement that never landed leaves the previous version as the only copy on disk, so nothing
                // of it is stale. Clearing these would take a working mod away over a download that failed
                if (replacement.Updated.IsSatisfied() is false)
                {
                    continue;
                }

                var writtenFolders = replacement.Updated.InstalledMods.Select(m => m.FolderName).Where(f => String.IsNullOrEmpty(f) is false).ToList();

                foreach (var installedMod in replacement.Previous.InstalledMods)
                {
                    if (String.IsNullOrEmpty(installedMod.FolderName))
                    {
                        continue;
                    }

                    if (writtenFolders.Contains(installedMod.FolderName, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    staleFolders.Add(installedMod.FolderName);
                }
            }

            return staleFolders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Folder names belonging to entries that are being replaced, which is where a user's own configuration sits
        /// for any of them the curator supplies no configuration for.
        /// </summary>
        public List<string> GetReplacedFolderNames()
        {
            var folderNames = new List<string>();

            foreach (var replacement in Replaced)
            {
                foreach (var installedMod in replacement.Previous.InstalledMods)
                {
                    if (String.IsNullOrEmpty(installedMod.FolderName))
                    {
                        continue;
                    }

                    folderNames.Add(installedMod.FolderName);
                }
            }

            return folderNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
