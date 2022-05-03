using Avalonia.Media;
using Semver;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.SMAPI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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
        public Config? _config { get; set; }
        public Config? Config { get { return _config; } set { _config = value; NotifyPropertyChanged("Config"); NotifyPropertyChanged("HasConfig"); } }
        public bool HasConfig { get { return Config is not null; } }
        private List<ManifestDependency> _requirements { get; set; }
        public List<ManifestDependency> Requirements { get { return _requirements; } set { _requirements = value; NotifyPropertyChanged("Requirements"); NotifyPropertyChanged("MissingRequirements"); NotifyPropertyChanged("HardRequirements"); } }
        public List<ManifestDependency> MissingRequirements { get { return _requirements is null ? null : _requirements.Where(r => !String.IsNullOrEmpty(r.Name) && r.IsMissing && r.IsRequired).ToList(); } }
        public List<ManifestDependency> HardRequirements { get { return _requirements is null ? null : _requirements.Where(r => !String.IsNullOrEmpty(r.Name) && !r.IsMissing && r.IsRequired).ToList(); } }
        private string _updateUri { get; set; }
        public string UpdateUri { get { return _updateUri; } set { _updateUri = value; NotifyPropertyChanged("UpdateUri"); } }
        private string _modPageUri { get; set; }
        public string ModPageUri { get { return _modPageUri; } set { _modPageUri = value; NotifyPropertyChanged("ModPageUri"); } }
        public int? NexusModId { get { return GetNexusKey(); } }
        private bool _isEnabled { get; set; }
        public bool IsEnabled { get { return _isEnabled; } set { _isEnabled = value; NotifyPropertyChanged("IsEnabled"); NotifyPropertyChanged("ChangeStateText"); } }
        private bool _isHidden { get; set; }
        public bool IsHidden { get { return _isHidden; } set { _isHidden = value; NotifyPropertyChanged("IsHidden"); } }
        private bool _isEndorsement { get; set; }
        public bool IsEndorsed { get { return _isEndorsement; } set { _isEndorsement = value; NotifyPropertyChanged("IsEndorsed"); } }
        public string ChangeStateText { get { return IsEnabled ? Program.translation.Get("internal.disable") : Program.translation.Get("internal.enable"); } }
        private WikiCompatibilityStatus _status { get; set; }
        public WikiCompatibilityStatus Status { get { return _status; } set { _status = value; NotifyPropertyChanged("Status"); NotifyPropertyChanged("ParsedStatus"); NotifyPropertyChanged("InstallStatus"); } }
        public string ParsedStatus
        {
            get
            {
                if (!String.IsNullOrEmpty(SuggestedVersion) && IsModOutdated(SuggestedVersion))
                {
                    if (_status == WikiCompatibilityStatus.Unofficial)
                    {
                        return String.Format(Program.translation.Get("ui.main_window.hyperlinks.unofficial_update_available"), SuggestedVersion);
                    }
                    return String.Format(Program.translation.Get("ui.main_window.hyperlinks.update_available"), SuggestedVersion);
                }
                else if (_status == WikiCompatibilityStatus.Broken)
                {
                    return Program.translation.Get("ui.main_window.hyperlinks.broken_compatibility_issue");
                }

                return String.Empty;
            }
        }
        private InstallState _installState { get; set; }
        public InstallState InstallState { get { return _installState; } set { _installState = value; NotifyPropertyChanged("InstallState"); NotifyPropertyChanged("InstallStatus"); } }
        public string InstallStatus
        {
            get
            {
                if (!String.IsNullOrEmpty(SuggestedVersion) && IsModOutdated(SuggestedVersion))
                {
                    var nexusModId = GetNexusKey();
                    if (_status == WikiCompatibilityStatus.Unofficial || nexusModId is null)
                    {
                        return String.Empty;
                    }
                    else if (InstallState == InstallState.Unknown)
                    {
                        return Program.translation.Get("ui.main_window.hyperlinks.install_update");
                    }

                    return InstallState == InstallState.Downloading ? Program.translation.Get("ui.main_window.hyperlinks.downloading") : Program.translation.Get("ui.main_window.hyperlinks.installing");
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
            Author = String.IsNullOrEmpty(author) ? Program.translation.Get("internal.unknown") : author;

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

        public bool HasUpdateKeys()
        {
            if (Manifest is not null && Manifest.UpdateKeys is not null && !Manifest.UpdateKeys.Any(k => String.IsNullOrEmpty(k)))
            {
                return true;
            }

            return false;
        }

        public int? GetNexusKey()
        {
            if (HasUpdateKeys() is false)
            {
                return null;
            }

            foreach (string key in Manifest.UpdateKeys)
            {
                string cleanedKey = String.Concat(key.Where(c => !Char.IsWhiteSpace(c)));
                var match = Regex.Match(key, @"Nexus:(?<modId>\d+).*");
                if (match.Success)
                {
                    if (Int32.TryParse(match.Groups["modId"].ToString(), out int modId))
                    {
                        return modId;
                    }
                }
            }

            return null;
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
