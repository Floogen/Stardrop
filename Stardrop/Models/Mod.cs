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

namespace Stardrop.Models
{
    public class Mod : INotifyPropertyChanged
    {
        internal readonly FileInfo ModFileInfo;
        internal readonly Manifest Manifest;

        public string UniqueId { get; set; }
        public SemVersion Version { get; set; }
        public string ParsedVersion { get { return Version.ToString(); } }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string Requirements { get; set; }
        private string _uri { get; set; }
        public string Uri { get { return _uri; } set { _uri = value; NotifyPropertyChanged("Uri"); } }
        private string _status { get; set; }
        public string Status { get { return _status; } set { _status = value; NotifyPropertyChanged("Status"); } }
        private bool _isEnabled { get; set; }
        public bool IsEnabled { get { return _isEnabled; } set { _isEnabled = value; NotifyPropertyChanged("IsEnabled"); } }

        public Mod(Manifest manifest, FileInfo modFileInfo, string uniqueId, string version, string? name = null, string? description = null, string? author = null)
        {
            Manifest = manifest;
            ModFileInfo = modFileInfo;

            UniqueId = uniqueId;
            Version = SemVersion.Parse(version);
            Name = String.IsNullOrEmpty(name) ? uniqueId : name;
            Description = String.IsNullOrEmpty(description) ? String.Empty : description;
            Author = String.IsNullOrEmpty(author) ? "Unknown" : author;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
