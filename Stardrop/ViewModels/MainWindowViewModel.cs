using Stardrop.Models;
using Stardrop.Models.SMAPI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Stardrop.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<Mod> Mods { get; set; }

        public MainWindowViewModel(string modsFilePath)
        {
            Mods = new ObservableCollection<Mod>();

            DirectoryInfo modDirectory = new DirectoryInfo(modsFilePath);
            foreach (var fileInfo in modDirectory.GetFiles("manifest.json", SearchOption.AllDirectories))
            {
                if (fileInfo.DirectoryName is null)
                {
                    continue;
                }

                try
                {
                    // By default, disable mods that are hidden from SMAPI by the user
                    bool isEnabled = true;
                    if (fileInfo.DirectoryName.Replace(Program.defaultModPath, String.Empty)[0] == '.')
                    {
                        isEnabled = false;
                    }

                    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(fileInfo.FullName), new JsonSerializerOptions { AllowTrailingCommas = true });
                    if (manifest is null)
                    {
                        Program.helper.Log($"The manifest.json was empty or not deserializable from {fileInfo.DirectoryName}", Utilities.Helper.Status.Alert);
                        continue;
                    }

                    var mod = new Mod(manifest.UniqueID, manifest.Version, manifest.Name, manifest.Description, manifest.Author) { IsEnabled = isEnabled };
                    if (!Mods.Any(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase)))
                    {
                        Mods.Add(mod);
                    }
                    else if (Mods.First(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) && m.Version < mod.Version) is Mod oldMod && oldMod is not null)
                    {
                        // Replace old mod with newer one
                        int oldModIndex = Mods.IndexOf(Mods.First(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) && m.Version < mod.Version));
                        Mods[oldModIndex] = mod;
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load the manifest.json from {fileInfo.DirectoryName}: {ex}", Utilities.Helper.Status.Alert);
                }
            }
        }
    }
}
