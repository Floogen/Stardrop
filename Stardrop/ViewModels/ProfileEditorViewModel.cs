using Stardrop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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

        public ProfileEditorViewModel(string profilesFilePath)
        {
            OldProfiles = new List<Profile>();
            Profiles = new ObservableCollection<Profile>();

            _profileFilePath = profilesFilePath;
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

                    Profiles.Add(new Profile(profile.Name, profile.IsProtected, profile.EnabledModIds));
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
                ToolTip_Save = "Save Changes";
                ToolTip_Cancel = "Cancel";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // TEMPORARY FIX: Due to bug with Avalonia on Linux platforms, tooltips currently cause crashes when they disappear
                // To work around this, tooltips are purposely not displayed
            }
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

        internal void DeleteProfile(Profile profile)
        {
            string fileFullName = Path.Combine(_profileFilePath, profile.Name + ".json");
            if (!File.Exists(fileFullName))
            {
                Program.helper.Log($"Attempted to delete a non-existent profile file ({profile.Name}) at the path {fileFullName}", Utilities.Helper.Status.Warning);
                return;
            }

            File.Delete(fileFullName);
        }

        internal void UpdateProfile(Profile profile, List<string> enabledModIds)
        {
            int profileIndex = Profiles.IndexOf(profile);
            if (profileIndex == -1)
            {
                return;
            }

            Profiles[profileIndex].EnabledModIds = enabledModIds;
            CreateProfile(profile, true);
        }
    }
}
