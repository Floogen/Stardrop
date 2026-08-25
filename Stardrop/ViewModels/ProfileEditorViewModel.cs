using Stardrop.Models;
using Stardrop.Utilities.Internal;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Stardrop.ViewModels
{
    public class ProfileEditorViewModel : ViewModelBase
    {
        public ObservableCollection<Profile> Profiles { get; set; }
        public List<Profile> OldProfiles { get; set; }
        public string ToolTip_Save { get; set; }
        public string ToolTip_Cancel { get; set; }

        private readonly string _profileFilePath;
        private List<Mod> _mods;

        // Deletion is deferred until the editor is applied, so the choice made at click time is held here until then
        private readonly Dictionary<string, bool> _pendingCollectionRemovals = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Set when a collection was removed, so the caller knows the mod list needs rebuilding</summary>
        public bool HasRemovedCollections { get; private set; }

        public ProfileEditorViewModel(string profilesFilePath, List<Mod> mods)
        {
            OldProfiles = new List<Profile>();
            Profiles = new ObservableCollection<Profile>();

            _profileFilePath = profilesFilePath;
            _mods = mods;

            DirectoryInfo profileDirectory = new DirectoryInfo(_profileFilePath);
            foreach (var fileInfo in profileDirectory.GetFiles("*.json", SearchOption.AllDirectories))
            {
                if (fileInfo.DirectoryName is null)
                {
                    continue;
                }

                try
                {
                    var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(fileInfo.FullName), new JsonSerializerOptions { AllowTrailingCommas = true });
                    if (profile is null)
                    {
                        Program.helper.Log($"The profile file {fileInfo.Name} was empty or not deserializable from {fileInfo.DirectoryName}", Utilities.Helper.Status.Alert);
                        continue;
                    }

                    Profiles.Add(profile);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load the profile file {fileInfo.Name} from {fileInfo.DirectoryName}: {ex}", Utilities.Helper.Status.Alert);
                }
            }

            if (!Profiles.Any(p => p.Name == Program.defaultProfileName))
            {
                var defaultProfile = new Profile(Program.defaultProfileName) { IsProtected = true };
                Profiles.Insert(0, defaultProfile);
                CreateProfile(defaultProfile);
            }
            else if (Profiles.IndexOf(Profiles.First(p => p.Name == Program.defaultProfileName)) != 0)
            {
                // Move the default profile to the top
                Profiles.Move(Profiles.IndexOf(Profiles.First(p => p.Name == Program.defaultProfileName)), 0);
            }

            OldProfiles = Profiles.ToList();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ToolTip_Save = Program.translation.Get("ui.settings_window.tooltips.save_changes");
                ToolTip_Cancel = Program.translation.Get("ui.settings_window.tooltips.cancel_changes");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // TEMPORARY FIX: Due to bug with Avalonia on Linux platforms, tooltips currently cause crashes when they disappear
                // To work around this, tooltips are purposely not displayed
            }
        }

        /// <summary>
        /// Adds a profile to the live list and writes it to disk. CreateProfile only does the latter, so anything
        /// calling it alone stays invisible until Stardrop restarts and re-reads the profile folder.
        /// </summary>
        internal void AddProfile(Profile profile, bool force = false)
        {
            if (Profiles.Any(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)) is false)
            {
                Profiles.Add(profile);
            }

            // Kept in step so the profile editor does not later treat this as an unsaved addition
            if (OldProfiles.Any(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)) is false)
            {
                OldProfiles.Add(profile);
            }

            CreateProfile(profile, force);
        }

        internal void CreateProfile(Profile profile, bool force = false)
        {
            string fileFullName = Path.Combine(_profileFilePath, profile.Name + ".json");
            if (File.Exists(fileFullName) && !force)
            {
                Program.helper.Log($"Attempted to create an already existing profile file ({profile.Name}) at the path {fileFullName}", Utilities.Helper.Status.Warning);
                return;
            }

            File.WriteAllText(fileFullName, JsonSerializer.Serialize(profile, new JsonSerializerOptions() { WriteIndented = true }));
        }

        /// <summary>
        /// Discards every unapplied change, restoring the list to the last applied state. Additions, deletions,
        /// renames and copies are all held in memory until the editor is applied, so nothing on disk needs undoing.
        /// </summary>
        internal void RevertChanges()
        {
            Profiles.Clear();
            foreach (var profile in OldProfiles)
            {
                Profiles.Add(profile);
            }

            // A confirmed collection removal that was never applied has to go too
            _pendingCollectionRemovals.Clear();
        }

        /// <summary>
        /// Records what should happen to a collection's downloaded mods when its profile is deleted. Called when the
        /// user confirms, and acted on later when the editor is applied.
        /// </summary>
        internal void MarkCollectionForRemoval(string sourceId, bool deleteInstalledMods)
        {
            _pendingCollectionRemovals[sourceId] = deleteInstalledMods;
        }

        internal void DeleteProfile(Profile profile)
        {
            string fileFullName = Path.Combine(_profileFilePath, profile.Name + ".json");
            if (File.Exists(fileFullName) is false)
            {
                Program.helper.Log($"Attempted to delete a non-existent profile file ({profile.Name}) at the path {fileFullName}", Utilities.Helper.Status.Warning);
            }
            else
            {
                File.Delete(fileFullName);
            }

            RemoveCollectionForProfile(profile);
        }

        /// <summary>
        /// Removes the collection record behind a profile, and its downloaded mods where the user asked for that.
        /// Mods the collection reused from elsewhere are never touched, as those belong to the user rather than to
        /// the collection and live outside its folder.
        /// </summary>
        private void RemoveCollectionForProfile(Profile profile)
        {
            if (profile.IsFromCollection is false || String.IsNullOrEmpty(profile.SourceId))
            {
                return;
            }

            var deleteInstalledMods = _pendingCollectionRemovals.ContainsKey(profile.SourceId) && _pendingCollectionRemovals[profile.SourceId];
            Program.helper.Log($"Removing the collection {profile.SourceId}{(deleteInstalledMods ? " along with its downloaded mods" : ", keeping its downloaded mods")}");

            CollectionCache.Delete(profile.SourceId, deleteInstalledMods);
            _pendingCollectionRemovals.Remove(profile.SourceId);

            HasRemovedCollections = true;
        }

        /// <summary>
        /// Clears the removal flag once the caller has rebuilt its mod list.
        /// </summary>
        public void ClearRemovedCollectionsFlag()
        {
            HasRemovedCollections = false;
        }

        internal void UpdateProfile(Profile profile, ObservableCollection<Mod> mods)
        {
            int profileIndex = Profiles.IndexOf(profile);
            if (profileIndex == -1)
            {
                return;
            }

            Profiles[profileIndex].EnabledModIds = mods.Where(m => m.IsEnabled).Select(m => m.ToReference()).ToList();
            Profiles[profileIndex].Notes = mods.Where(m => string.IsNullOrEmpty(m.Note) is false).Select(m => new ModNote(m.UniqueId, m.Note)).ToList();
            CreateProfile(profile, true);
        }

        internal List<Mod> GetMods()
        {
            return _mods;
        }
    }
}
