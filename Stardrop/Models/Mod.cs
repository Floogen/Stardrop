using Avalonia.Media;
using Semver;
using Stardrop.Models.SMAPI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static Stardrop.Models.SMAPI.Web.ModEntryMetadata;

namespace Stardrop.Models
{
    public class Mod : INotifyPropertyChanged
    {
        internal readonly FileInfo ModFileInfo;
        internal readonly Manifest Manifest;

        public string UniqueId { get; set; }
        public SemVersion Version { get; set; }
        public string ParsedVersion { get { return Version.ToString(); } }
        public string SuggestedVersion { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        private List<ManifestDependency> _requirements { get; set; }
        public List<ManifestDependency> Requirements { get { return _requirements; } set { _requirements = value; NotifyPropertyChanged("Requirements"); } }
        private string _uri { get; set; }
        public string Uri { get { return _uri; } set { _uri = value; NotifyPropertyChanged("Uri"); } }
        private bool _isEnabled { get; set; }
        public bool IsEnabled { get { return _isEnabled; } set { _isEnabled = value; NotifyPropertyChanged("IsEnabled"); } }
        private WikiCompatibilityStatus _status { get; set; }
        public WikiCompatibilityStatus Status { get { return _status; } set { _status = value; UpdateMessageBrush(); NotifyPropertyChanged("Status"); NotifyPropertyChanged("ParsedStatus"); } }
        public Brush StatusBrush { get; set; }
        public string ParsedStatus
        {
            get
            {
                if (!String.IsNullOrEmpty(SuggestedVersion) && IsModOutdated(SuggestedVersion))
                {
                    if (_status == WikiCompatibilityStatus.Unofficial)
                    {
                        return $"Unofficial Update Available ({SuggestedVersion})";
                    }
                    return $"Update Available ({SuggestedVersion})";
                }
                else if (_status == WikiCompatibilityStatus.Broken)
                {
                    return $"[Broken] Compatibility Issue";
                }

                return String.Empty;
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        public Mod(Manifest manifest, FileInfo modFileInfo, string uniqueId, string version, string? name = null, string? description = null, string? author = null)
        {
            Manifest = manifest;
            ModFileInfo = modFileInfo;

            UniqueId = uniqueId;
            Version = SemVersion.Parse(version);
            Name = String.IsNullOrEmpty(name) ? uniqueId : name;
            Description = String.IsNullOrEmpty(description) ? String.Empty : description;
            Author = String.IsNullOrEmpty(author) ? "Unknown" : author;

            Requirements = new List<ManifestDependency>();
        }

        public bool IsModOutdated(string version)
        {
            if (String.IsNullOrEmpty(version))
            {
                return false;
            }

            return SemVersion.Parse(version) > Version;
        }

        private void UpdateMessageBrush()
        {
            var converter = new BrushConverter();

            switch (Status)
            {
                case WikiCompatibilityStatus.Broken:
                    StatusBrush = (Brush)converter.ConvertFrom("#f74040");
                    break;
                case WikiCompatibilityStatus.Unofficial:
                    StatusBrush = (Brush)converter.ConvertFrom("#fdfd2e");
                    break;
                default:
                    StatusBrush = (Brush)converter.ConvertFrom("#1cff96");
                    break;
            }

            NotifyPropertyChanged("StatusBrush");
        }

        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
