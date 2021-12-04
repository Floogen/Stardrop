using Stardrop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Stardrop.ViewModels
{
    public class ProfileEditorViewModel : ViewModelBase
    {
        public ObservableCollection<Profile> Profiles { get; set; }
        public List<Profile> OldProfiles { get; set; }

        private readonly string _profileFilePath;

        public ProfileEditorViewModel(string profilesFilePath)
        {
            OldProfiles = new List<Profile>();
            Profiles = new ObservableCollection<Profile>();

            DirectoryInfo profileDirectory = new DirectoryInfo(profilesFilePath);
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

                    OldProfiles.Add(new Profile(profile.Name, profile.EnabledModIds));
                    Profiles.Add(new Profile(profile.Name, profile.EnabledModIds));
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load the profile file {fileInfo.Name} from {fileInfo.DirectoryName}: {ex}", Utilities.Helper.Status.Alert);
                }
            }


            _profileFilePath = profilesFilePath;
        }

        internal void CreateProfile(Profile profile)
        {
            string fileFullName = Path.Combine(_profileFilePath, profile.Name + ".json");
            if (File.Exists(fileFullName))
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
    }
}
