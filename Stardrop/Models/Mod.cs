using Avalonia.Media;
using Semver;
using Stardrop.Models.SMAPI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        public string GetDescriptionToolTip
        {
            get
            {
                // TEMPORARY FIX: Due to bug with Avalonia on Linux platforms, tooltips currently cause crashes when they disappear
                // To work around this, tooltips are purposely not displayed
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return null;
                }

                return Description;
            }
        }
        public string Author { get; set; }
        private List<ManifestDependency> _requirements { get; set; }
        public List<ManifestDependency> Requirements { get { return _requirements; } set { _requirements = value; NotifyPropertyChanged("Requirements"); NotifyPropertyChanged("MissingRequirements"); } }
        public List<ManifestDependency> MissingRequirements { get { return _requirements.Where(r => r.IsMissing && r.IsRequired).ToList(); } }
        private string _uri { get; set; }
        public string Uri { get { return _uri; } set { _uri = value; NotifyPropertyChanged("Uri"); } }
        private bool _isEnabled { get; set; }
        public bool IsEnabled { get { return _isEnabled; } set { _isEnabled = value; NotifyPropertyChanged("IsEnabled"); NotifyPropertyChanged("ChangeStateText"); } }
        public string ChangeStateText { get { return IsEnabled ? "Disable" : "Enable"; } }
        private WikiCompatibilityStatus _status { get; set; }
        public WikiCompatibilityStatus Status { get { return _status; } set { _status = value; NotifyPropertyChanged("Status"); NotifyPropertyChanged("ParsedStatus"); } }
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
            Version = SemVersion.TryParse(version, out var parsedVersion) ? parsedVersion : new SemVersion(0, 0, 0, "bad-version");
            Name = String.IsNullOrEmpty(name) ? uniqueId : name;
            Description = String.IsNullOrEmpty(description) ? String.Empty : description;
            Author = String.IsNullOrEmpty(author) ? "Unknown" : author;

            Requirements = new List<ManifestDependency>();
        }

        public bool IsModOutdated(string version)
        {
            if (String.IsNullOrEmpty(version) || !HasValidVersion())
            {
                return false;
            }

            return SemVersion.Parse(version) > Version;
        }

        public bool HasValidVersion()
        {
            if (Version.Prerelease.Equals("bad-version", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        internal void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
