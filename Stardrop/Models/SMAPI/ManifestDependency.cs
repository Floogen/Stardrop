using Stardrop.Models.SMAPI.Converters;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Stardrop.Models.SMAPI
{
    public class ManifestDependency : INotifyPropertyChanged
    {
        // Based on SMAPI's IManifestDependency.cs: https://github.com/Pathoschild/SMAPI/blob/develop/src/SMAPI.Toolkit.CoreInterfaces/IManifestDependency.cs

        /// <summary>The unique mod ID to require.</summary>
        public string UniqueID { get; set; }

        /// <summary>The minimum required version (if any).</summary>
        public string MinimumVersion { get; set; }

        /// <summary>Whether the dependency must be installed to use the mod.</summary>
        [JsonConverter(typeof(BooleanConverterAssumeTrue))]
        public bool IsRequired { get; set; }

        // <summary>Custom properties for Stardrop.</summary>
        private string _name { get; set; }
        public string Name { get { return _name; } set { _name = value; NotifyPropertyChanged("Name"); NotifyPropertyChanged("GenericLink"); } }
        public bool IsMissing { get; set; }
        public string GenericLink { get { return $"https://smapi.io/mods#{Name.Replace(" ", "_")}"; } }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ManifestDependency(string uniqueId, string minimumVersion, bool isRequired = true)
        {
            UniqueID = uniqueId;
            MinimumVersion = minimumVersion;
            IsRequired = isRequired;
        }

        private void NotifyPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
