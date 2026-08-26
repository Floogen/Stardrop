using ReactiveUI;
using Semver;
using Stardrop.Models;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Utilities.Internal;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Stardrop.ViewModels
{
    public enum CollectionSortColumn
    {
        Name,
        Version,
        Status
    }

    /// <summary>
    /// One of a collection's entries as the window shows it. Nothing on <see cref="CollectionModEntry"/> is ready to
    /// display, since the status is an enum and the page address has to be built from the Nexus IDs, so the shaping
    /// happens once here rather than in a converter per column.
    /// </summary>
    public class CollectionEntryView
    {
        public string Name { get; }
        public string Version { get; }
        public string Status { get; }
        public string? PageUri { get; }
        /// <summary>Whether the user still has to do something about this entry, which is what the filter reads</summary>
        public bool IsMissing { get; }
        /// <summary>Parsed for sorting, so that 10.0 lands above 9.0 rather than below it. Null when unparseable</summary>
        public SemVersion? SortableVersion { get; }

        public CollectionEntryView(CollectionInstall collection, CollectionModEntry entry)
        {
            Name = String.IsNullOrEmpty(entry.Name) ? Program.translation.Get("internal.unknown") : entry.Name;
            Version = String.IsNullOrEmpty(entry.Version) ? String.Empty : entry.Version;
            Status = DescribeStatus(entry);
            PageUri = collection.GetEntryPageUri(entry);
            SortableVersion = SemVersion.TryParse(Version, SemVersionStyles.Any, out var parsedVersion) ? parsedVersion : null;

            // Skipped entries are optional ones the user turned down, so they are accounted for rather than missing
            IsMissing = entry.IsSatisfied() is false && entry.Status is not CollectionModStatus.Skipped;
        }

        private static string DescribeStatus(CollectionModEntry entry)
        {
            var status = entry.Status switch
            {
                CollectionModStatus.Installed => Program.translation.Get("ui.collections_window.status.installed"),
                CollectionModStatus.AppliedAsOverlay => Program.translation.Get("ui.collections_window.status.overlay"),
                CollectionModStatus.AwaitingManualDownload => Program.translation.Get("ui.collections_window.status.awaiting_manual"),
                CollectionModStatus.Downloading => Program.translation.Get("ui.collections_window.status.downloading"),
                CollectionModStatus.Skipped => Program.translation.Get("ui.collections_window.status.skipped"),
                CollectionModStatus.Failed => Program.translation.Get("ui.collections_window.status.failed"),
                _ => Program.translation.Get("ui.collections_window.status.pending")
            };

            // The reason is the useful half of a failure, so it rides along rather than hiding in a tooltip
            if (entry.Status is CollectionModStatus.Failed && String.IsNullOrEmpty(entry.FailureReason) is false)
            {
                return $"{status} ({entry.FailureReason})";
            }

            return status;
        }
    }

    /// <summary>
    /// An installed collection as the window shows it, built once from its cached record.
    /// </summary>
    public class CollectionView
    {
        public string SourceId { get; }
        public string ProfileName { get; }
        public string Name { get; }
        public string Curator { get; }
        public string Revision { get; }
        public string Profile { get; }
        public string Progress { get; }
        public string PageUri { get; }
        public bool HasCurator { get; }
        public int InstalledModCount { get; }
        public List<CollectionEntryView> Entries { get; }

        public CollectionView(CollectionInstall collection)
        {
            SourceId = collection.SourceId;
            ProfileName = collection.ProfileName;
            Name = String.IsNullOrEmpty(collection.Name) ? collection.Slug : collection.Name;
            HasCurator = String.IsNullOrEmpty(collection.Curator) is false;
            Curator = HasCurator ? String.Format(Program.translation.Get("ui.collections_window.labels.curator"), collection.Curator) : String.Empty;
            Revision = String.Format(Program.translation.Get("ui.collections_window.labels.revision"), collection.RevisionNumber, collection.InstallTimestamp.ToShortDateString());
            Profile = String.Format(Program.translation.Get("ui.collections_window.labels.profile"), collection.ProfileName);
            InstalledModCount = collection.GetInstalledCount();
            Progress = String.Format(Program.translation.Get("ui.collections_window.labels.installed"), InstalledModCount, collection.GetModCount());
            PageUri = collection.GetPageUri();

            Entries = collection.Mods.Select(m => new CollectionEntryView(collection, m)).ToList();
        }
    }

    public class CollectionsWindowViewModel : ViewModelBase
    {
        public ObservableCollection<CollectionView> Collections { get; } = new ObservableCollection<CollectionView>();
        public ObservableCollection<CollectionEntryView> Entries { get; } = new ObservableCollection<CollectionEntryView>();

        private CollectionView? _selectedCollection;
        public CollectionView? SelectedCollection
        {
            get { return _selectedCollection; }
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedCollection, value);
                this.RaisePropertyChanged(nameof(HasSelection));
                RefreshEntries();
            }
        }

        private bool _showMissingOnly;
        public bool ShowMissingOnly
        {
            get { return _showMissingOnly; }
            set
            {
                this.RaiseAndSetIfChanged(ref _showMissingOnly, value);
                RefreshEntries();
            }
        }

        // Built on CompareSortOrderTo, so a prerelease sorts below the release it leads up to
        private static readonly IComparer<SemVersion?> _versionComparer = Comparer<SemVersion?>.Create((left, right) => left is null || right is null ? 0 : left.CompareSortOrderTo(right));

        // Status ascending puts the missing entries on top, which is what someone opening this window is here for
        private CollectionSortColumn _sortColumn = CollectionSortColumn.Status;
        private bool _sortDescending;

        public bool HasCollections { get { return Collections.Count > 0; } }
        public bool HasSelection { get { return _selectedCollection is not null; } }

        public string NameHeader { get { return BuildHeader("ui.collections_window.headers.mod_name", CollectionSortColumn.Name); } }
        public string VersionHeader { get { return BuildHeader("ui.collections_window.headers.version", CollectionSortColumn.Version); } }
        public string StatusHeader { get { return BuildHeader("ui.collections_window.headers.status", CollectionSortColumn.Status); } }

        /// <summary>
        /// Reads every cached collection record. Newest first, as the one a user has just installed is the one they
        /// are most likely here about.
        /// </summary>
        public void Load()
        {
            var previousSourceId = _selectedCollection?.SourceId;

            Collections.Clear();

            foreach (var collection in CollectionCache.LoadAll().OrderByDescending(c => c.InstallTimestamp))
            {
                Collections.Add(new CollectionView(collection));
            }

            this.RaisePropertyChanged(nameof(HasCollections));

            // Held across a reload where the collection is still there, so a refresh does not move the user
            SelectedCollection = Collections.FirstOrDefault(c => c.SourceId.Equals(previousSourceId, StringComparison.OrdinalIgnoreCase)) ?? Collections.FirstOrDefault();
        }

        /// <summary>
        /// The profiles whose enabled mods live in the given collection's folder, leaving out the collection's own
        /// generated profile. Removing the collection's mods breaks every one of them, so the count is what the
        /// confirmation is built around.
        /// </summary>
        public static List<Profile> GetDependentProfiles(IEnumerable<Profile> profiles, CollectionView collection)
        {
            return profiles.Where(p => p.Name.Equals(collection.ProfileName, StringComparison.OrdinalIgnoreCase) is false && p.EnabledModIds.Any(m => collection.SourceId.Equals(m.SourceId, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        /// <summary>
        /// Sorts by the given column, reversing the direction when it is already the one being sorted on. Clicking
        /// Status twice therefore returns the list to how it opened, so the default view is never out of reach.
        /// </summary>
        public void SortBy(CollectionSortColumn column)
        {
            if (_sortColumn == column)
            {
                _sortDescending = _sortDescending is false;
            }
            else
            {
                _sortColumn = column;
                _sortDescending = false;
            }

            this.RaisePropertyChanged(nameof(NameHeader));
            this.RaisePropertyChanged(nameof(VersionHeader));
            this.RaisePropertyChanged(nameof(StatusHeader));

            RefreshEntries();
        }

        private string BuildHeader(string key, CollectionSortColumn column)
        {
            var header = Program.translation.Get(key);
            if (_sortColumn != column)
            {
                return header;
            }

            return _sortDescending ? $"{header} \u25BC" : $"{header} \u25B2";
        }

        private void RefreshEntries()
        {
            Entries.Clear();

            if (_selectedCollection is null)
            {
                return;
            }

            foreach (var entry in SortEntries(_selectedCollection.Entries.Where(e => _showMissingOnly is false || e.IsMissing)))
            {
                Entries.Add(entry);
            }
        }

        /// <summary>
        /// Applies the current sort. Name is the tie-break on every column, so entries sharing a version or a status
        /// hold a readable order rather than shuffling about between sorts.
        /// </summary>
        private IEnumerable<CollectionEntryView> SortEntries(IEnumerable<CollectionEntryView> entries)
        {
            switch (_sortColumn)
            {
                case CollectionSortColumn.Name:
                    return _sortDescending ? entries.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase) : entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

                case CollectionSortColumn.Version:
                    // An unparseable version sorts last either way, as it carries no position of its own
                    var byVersion = _sortDescending
                        ? entries.OrderBy(e => e.SortableVersion is null).ThenByDescending(e => e.SortableVersion, _versionComparer)
                        : entries.OrderBy(e => e.SortableVersion is null).ThenBy(e => e.SortableVersion, _versionComparer);

                    return byVersion.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

                default:
                    var byStatus = _sortDescending
                        ? entries.OrderBy(e => e.IsMissing).ThenByDescending(e => e.Status, StringComparer.OrdinalIgnoreCase)
                        : entries.OrderByDescending(e => e.IsMissing).ThenBy(e => e.Status, StringComparer.OrdinalIgnoreCase);

                    return byStatus.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
