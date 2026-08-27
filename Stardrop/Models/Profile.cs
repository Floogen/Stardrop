using Stardrop.Models.Data;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stardrop.Models
{
    public record ModNote(string UniqueId, string Note);

    public class Profile : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public bool IsProtected { get; set; }
        /// <summary>
        /// When set, this profile was generated from the collection with the matching SourceId and should be treated
        /// as read-only. Users wanting to tweak a collection clone it into a plain profile instead.
        /// </summary>
        public string? SourceId { get; set; }
        public List<ModReference> EnabledModIds { get; set; }
        public Dictionary<string, JsonDocument> PreservedModConfigs { get; set; }
        public List<ModNote> Notes { get; set; }

        [JsonIgnore]
        public bool IsFromCollection => string.IsNullOrEmpty(SourceId) is false;

        private bool _isFirstCollectionProfile;
        /// <summary>
        /// Whether this is the first collection profile in the profile list, which is what draws the divider
        /// separating the plain profiles from the collection ones in the profile dropdown. Maintained by
        /// <see cref="ViewModels.ProfileEditorViewModel"/> as the list changes and never written to the profile
        /// file, as it describes a position in a list rather than anything about the profile itself.
        /// </summary>
        [JsonIgnore]
        public bool IsFirstCollectionProfile { get { return _isFirstCollectionProfile; } set { _isFirstCollectionProfile = value; NotifyPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;

        public Profile()
        {
            Name = "Unknown";
            IsProtected = false;
            EnabledModIds = new List<ModReference>();
            PreservedModConfigs = new Dictionary<string, JsonDocument>();
            Notes = new List<ModNote>();
        }

        public Profile(string name, bool isProtected = false, List<ModReference>? enabledMods = null, Dictionary<string, JsonDocument>? preservedModConfigs = null, List<ModNote> notes = null, string? sourceId = null)
        {
            Name = name;
            IsProtected = isProtected;
            SourceId = sourceId;
            EnabledModIds = enabledMods is null ? new List<ModReference>() : enabledMods;
            PreservedModConfigs = preservedModConfigs is null ? new Dictionary<string, JsonDocument>() : preservedModConfigs;
            Notes = notes is null ? new List<ModNote>() : notes;
        }

        /// <summary>
        /// A plain, editable copy of this profile under a new name. The copy is never tied to a collection, even
        /// when the original was: a collection's profile is generated from its cache record, so a second profile
        /// claiming the same SourceId would be treated as that collection and deleting it would take the
        /// collection's record and downloaded mods with it.
        ///
        /// The SourceId carried by each entry in EnabledModIds is a different thing and is kept. That one points a
        /// mod at the collection folder it lives in, which is where the copy still has to find its mods.
        /// </summary>
        public Profile CopyAsPlainProfile(string name)
        {
            var copy = (Profile)this.MemberwiseClone();

            copy.Name = name;
            copy.IsProtected = false;
            copy.SourceId = null;

            // Rebuilt rather than shared. MemberwiseClone hands over the same instances, so an in-place write such
            // as the one in ReadModConfigs would go straight through into the profile that was copied
            copy.EnabledModIds = new List<ModReference>(EnabledModIds);
            copy.PreservedModConfigs = new Dictionary<string, JsonDocument>(PreservedModConfigs);
            copy.Notes = new List<ModNote>(Notes);

            // MemberwiseClone hands over the subscriber list along with everything else, which would have the copy
            // raising its changes at the lists bound to the profile it was copied from
            copy.PropertyChanged = null;
            copy.IsFirstCollectionProfile = false;

            return copy;
        }

        /// <summary>
        /// Turns a collection's generated profile into an ordinary one, keeping its name and its enabled mods. Used
        /// when a collection is removed but its downloaded mods are kept, since the profile is then the only record
        /// of which of those mods the curator had enabled.
        /// </summary>
        public void DetachFromCollection()
        {
            IsProtected = false;
            SourceId = null;

            // The profile moves out of the collection group in the list and is no longer marked as being from one,
            // neither of which the list can see without being told
            NotifyPropertyChanged(nameof(IsFromCollection));
        }

        internal void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged is not null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
