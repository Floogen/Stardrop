using Avalonia.Threading;
using Stardrop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

        /// <summary>Set while RefreshProfileOrdering is moving profiles about, so that its own moves are not taken as another change to react to</summary>
        private bool _isReorderingProfiles;
        /// <summary>Set between a change arriving and the reorder it posted running, so that a run of changes such as the one in RevertChanges only posts once</summary>
        private bool _hasReorderPending;

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

            // Subscribed after the load rather than during it, so that the profiles being read off disk do not each
            // post an ordering pass of their own
            RefreshProfileOrdering();
            Profiles.CollectionChanged += Profiles_CollectionChanged;

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

        private void Profiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isReorderingProfiles || _hasReorderPending)
            {
                return;
            }

            // Posted rather than run here. The dropdown and the editor's list are part way through handling this
            // same change, and moving profiles underneath them at that point leaves their own indexes pointing at
            // the wrong rows
            _hasReorderPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _hasReorderPending = false;
                RefreshProfileOrdering();
            });
        }

        /// <summary>
        /// Moves the collection profiles to the bottom of the list and marks the first of them, which is what draws
        /// the divider in the profile dropdown. The order within each group is left alone, so the default profile
        /// keeps the top position the constructor gave it and a rename never shuffles anything.
        /// </summary>
        internal void RefreshProfileOrdering()
        {
            _isReorderingProfiles = true;

            try
            {
                // Everything from this index down is a collection profile once the moves below are done
                int nextPlainIndex = 0;
                foreach (var profile in Profiles.Where(p => p.IsFromCollection is false).ToList())
                {
                    int currentIndex = Profiles.IndexOf(profile);
                    if (currentIndex != nextPlainIndex)
                    {
                        Profiles.Move(currentIndex, nextPlainIndex);
                    }

                    nextPlainIndex += 1;
                }

                for (int i = 0; i < Profiles.Count; i++)
                {
                    bool isFirstCollectionProfile = i == nextPlainIndex;
                    if (Profiles[i].IsFirstCollectionProfile != isFirstCollectionProfile)
                    {
                        Profiles[i].IsFirstCollectionProfile = isFirstCollectionProfile;
                    }
                }
            }
            finally
            {
                _isReorderingProfiles = false;
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
        }

        /// <summary>
        /// Deletes a profile there and then, rather than on the editor being applied. For callers outside the editor
        /// such as the collections window, which have no apply step to defer the work to.
        /// </summary>
        internal void RemoveProfileNow(Profile profile)
        {
            DeleteProfile(profile);

            Profiles.Remove(profile);
            OldProfiles.RemoveAll(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Turns a collection's generated profile into an ordinary one and writes it back. Used when a collection is
        /// removed but its mods are kept, so that the profile outlives the record it was generated from instead of
        /// becoming a protected profile with no collection behind it.
        /// </summary>
        internal void DetachCollectionProfile(Profile profile)
        {
            profile.DetachFromCollection();

            // The profile has left the collection group without the list itself changing, so nothing has posted the
            // pass that would move it back up among the plain profiles
            RefreshProfileOrdering();

            CreateProfile(profile, force: true);
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
