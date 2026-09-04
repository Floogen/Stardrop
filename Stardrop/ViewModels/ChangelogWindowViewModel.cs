using ReactiveUI;
using Semver;
using Stardrop.Models.Nexus.Web;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;

namespace Stardrop.ViewModels
{
    public class ChangelogWindowViewModel : ViewModelBase
    {
        public ObservableCollection<ChangelogVersion> Versions { get; set; }

        private string _modName = String.Empty;
        public string ModName { get { return _modName; } set { this.RaiseAndSetIfChanged(ref _modName, value); } }

        private bool _hasChanges;
        public bool HasChanges { get { return _hasChanges; } set { this.RaiseAndSetIfChanged(ref _hasChanges, value); } }

        private bool _isLoading;
        public bool IsLoading { get { return _isLoading; } set { this.RaiseAndSetIfChanged(ref _isLoading, value); } }

        private bool _showEmptyMessage;
        public bool ShowEmptyMessage { get { return _showEmptyMessage; } set { this.RaiseAndSetIfChanged(ref _showEmptyMessage, value); } }

        private string _modPageUri = String.Empty;
        public string ModPageUri { get { return _modPageUri; } set { this.RaiseAndSetIfChanged(ref _modPageUri, value); } }

        private bool _hasModPage;
        public bool HasModPage { get { return _hasModPage; } set { this.RaiseAndSetIfChanged(ref _hasModPage, value); } }

        private string _emptyMessage = String.Empty;
        public string EmptyMessage { get { return _emptyMessage; } set { this.RaiseAndSetIfChanged(ref _emptyMessage, value); } }

        public ChangelogWindowViewModel()
        {
            Versions = new ObservableCollection<ChangelogVersion>();
        }

        /// <summary>
        /// Orders changelogs newest first. The keys can't be relied on for ordering - they arrive in
        /// dictionary order and sort incorrectly as strings ("2.8.9" would land above "2.8.35").
        /// Entries are HTML-decoded as Nexus stores them encoded.
        /// </summary>
        public static List<ChangelogVersion> SortNewestFirst(Dictionary<string, List<string>> changelogs)
        {
            var parsed = new List<(SemVersion Version, ChangelogVersion Entry)>();
            var unparsed = new List<ChangelogVersion>();

            foreach (var changelog in changelogs)
            {
                var entry = new ChangelogVersion(changelog.Key, changelog.Value.ConvertAll(c => WebUtility.HtmlDecode(c) ?? c));

                if (SemVersion.TryParse(StripVersionPrefix(changelog.Key), SemVersionStyles.Any, out var version))
                {
                    parsed.Add((version, entry));
                }
                else
                {
                    unparsed.Add(entry);
                }
            }

            parsed.Sort((left, right) => right.Version.CompareSortOrderTo(left.Version));

            // Non-semver keys (such as four-part versions) trail the sorted entries rather than being dropped.
            var ordered = new List<ChangelogVersion>();
            ordered.AddRange(parsed.ConvertAll(p => p.Entry));
            ordered.AddRange(unparsed);

            return ordered;
        }

        // Only a leading "v" - a blanket Replace would corrupt versions such as "1.0.0-preview".
        private static string StripVersionPrefix(string version)
        {
            return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version.Substring(1) : version;
        }
    }
}
