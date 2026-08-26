using ReactiveUI;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Utilities.Internal;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Stardrop.ViewModels
{
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
        public bool IsOutstanding { get; }

        public CollectionEntryView(CollectionInstall collection, CollectionModEntry entry)
        {
            Name = String.IsNullOrEmpty(entry.Name) ? Program.translation.Get("internal.unknown") : entry.Name;
            Version = String.IsNullOrEmpty(entry.Version) ? String.Empty : entry.Version;
            Status = DescribeStatus(entry);
            PageUri = collection.GetEntryPageUri(entry);

            // Skipped entries are optional ones the user turned down, so they are accounted for rather than pending
            IsOutstanding = entry.IsSatisfied() is false && entry.Status is not CollectionModStatus.Skipped;
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
        public string Name { get; }
        public string Curator { get; }
        public string Revision { get; }
        public string Profile { get; }
        public string Progress { get; }
        public string PageUri { get; }
        public bool HasCurator { get; }
        public List<CollectionEntryView> Entries { get; }

        public CollectionView(CollectionInstall collection)
        {
            Name = String.IsNullOrEmpty(collection.Name) ? collection.Slug : collection.Name;
            HasCurator = String.IsNullOrEmpty(collection.Curator) is false;
            Curator = HasCurator ? String.Format(Program.translation.Get("ui.collections_window.labels.curator"), collection.Curator) : String.Empty;
            Revision = String.Format(Program.translation.Get("ui.collections_window.labels.revision"), collection.RevisionNumber, collection.InstallTimestamp.ToShortDateString());
            Profile = String.Format(Program.translation.Get("ui.collections_window.labels.profile"), collection.ProfileName);
            Progress = String.Format(Program.translation.Get("ui.collections_window.labels.installed"), collection.GetInstalledCount(), collection.GetModCount());
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

        private bool _showOutstandingOnly;
        public bool ShowOutstandingOnly
        {
            get { return _showOutstandingOnly; }
            set
            {
                this.RaiseAndSetIfChanged(ref _showOutstandingOnly, value);
                RefreshEntries();
            }
        }

        public bool HasCollections { get { return Collections.Count > 0; } }
        public bool HasSelection { get { return _selectedCollection is not null; } }

        /// <summary>
        /// Reads every cached collection record. Newest first, as the one a user has just installed is the one they
        /// are most likely here about.
        /// </summary>
        public void Load()
        {
            Collections.Clear();

            foreach (var collection in CollectionCache.LoadAll().OrderByDescending(c => c.InstallTimestamp))
            {
                Collections.Add(new CollectionView(collection));
            }

            this.RaisePropertyChanged(nameof(HasCollections));

            SelectedCollection = Collections.FirstOrDefault();
        }

        private void RefreshEntries()
        {
            Entries.Clear();

            if (_selectedCollection is null)
            {
                return;
            }

            foreach (var entry in _selectedCollection.Entries.Where(e => _showOutstandingOnly is false || e.IsOutstanding))
            {
                Entries.Add(entry);
            }
        }
    }
}
