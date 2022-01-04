using Avalonia.Collections;
using ReactiveUI;
using Stardrop.Models;
using Stardrop.Models.Data;
using Stardrop.Models.SMAPI;
using Stardrop.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Stardrop.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private string ChromeHint { get; set; } = "NoChrome";
        private bool HasSystemDecorations { get; set; } = true;
        private bool ShowTitle { get; set; } = true;
        private bool ShowMainMenu { get; set; } = true;
        private bool ShowWindowMenu { get; set; } = true;


        private string _dragOverColor = "#ff9f2a";
        public string DragOverColor { get { return _dragOverColor; } set { this.RaiseAndSetIfChanged(ref _dragOverColor, value); } }
        private bool _isLocked;
        public bool IsLocked { get { return _isLocked; } set { this.RaiseAndSetIfChanged(ref _isLocked, value); } }
        public ObservableCollection<Mod> Mods { get; set; }
        private int _enabledModCount;
        public int EnabledModCount { get { return _enabledModCount; } set { this.RaiseAndSetIfChanged(ref _enabledModCount, value); } }
        public DataGridCollectionView DataView { get; set; }

        private bool _hideDisabledMods;
        public bool HideDisabledMods { get { return _hideDisabledMods; } set { _hideDisabledMods = value; UpdateFilter(); } }

        private bool _showUpdatableMods;
        public bool ShowUpdatableMods { get { return _showUpdatableMods; } set { _showUpdatableMods = value; UpdateFilter(); } }
        private string _filterText;
        public string FilterText { get { return _filterText; } set { _filterText = value; UpdateFilter(); } }
        private string _columnFilter;
        public string ColumnFilter { get { return _columnFilter; } set { _columnFilter = value; UpdateFilter(); } }
        private string _updateStatusText = "Mods Ready to Update: Click to Refresh";
        public string UpdateStatusText { get { return _updateStatusText; } set { this.RaiseAndSetIfChanged(ref _updateStatusText, value); } }
        public int ModsWithCachedUpdates { get; set; }
        public string Version { get; set; }

        public MainWindowViewModel(string modsFilePath, string version)
        {
            DiscoverMods(modsFilePath);
            Version = $"v{version}";

            // Create data view
            var dataGridSortDescription = DataGridSortDescription.FromPath(nameof(Mod.Name), ListSortDirection.Ascending);

            DataView = new DataGridCollectionView(Mods);
            DataView.SortDescriptions.Add(dataGridSortDescription);

            // Do OS specific setup
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ChromeHint = "Default";
                ShowMainMenu = false;
                ShowWindowMenu = false;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                ChromeHint = "Default";
                ShowWindowMenu = false;
                ShowTitle = false;
            }
        }

        public void OpenBrowser(string url)
        {
            if (String.IsNullOrEmpty(url))
            {
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // If no associated application/json MimeType is found xdg-open opens retrun error
                // but it tries to open it anyway using the console editor (nano, vim, other..)
                ShellExec($"xdg-open {url}", waitForExit: false);
            }
            else
            {
                using Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? url : "open",
                    Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? $"{url}" : "",
                    CreateNoWindow = true,
                    UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                });
            }
        }

        private static void ShellExec(string cmd, bool waitForExit = true)
        {
            var escapedArgs = Regex.Replace(cmd, "(?=[`~!#&*()|;'<>])", "\\")
                .Replace("\"", "\\\\\\\"");

            using (var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"{escapedArgs}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            ))
            {
                if (waitForExit)
                {
                    process.WaitForExit();
                }
            }
        }

        public bool ParentFolderContainsPeriod(string oldestAncestorPath, DirectoryInfo? directoryInfo)
        {
            if (directoryInfo is null)
            {
                return false;
            }
            else if (directoryInfo.Name[0] == '.')
            {
                return true;
            }

            var ancestorFolder = directoryInfo.Parent;
            while (ancestorFolder is not null && !ancestorFolder.FullName.Equals(oldestAncestorPath, StringComparison.OrdinalIgnoreCase))
            {
                if (ancestorFolder.Name[0] == '.')
                {
                    return true;
                }

                ancestorFolder = ancestorFolder.Parent;
            }

            return false;
        }

        public List<FileInfo> GetManifestFiles(DirectoryInfo modDirectory)
        {
            List<FileInfo> manifests = new List<FileInfo>();
            foreach (var directory in modDirectory.EnumerateDirectories())
            {
                var localManifest = directory.EnumerateFiles("manifest.json");
                if (localManifest.Count() == 0)
                {
                    manifests.AddRange(GetManifestFiles(directory));
                }
                else
                {
                    manifests.Add(localManifest.First());
                }
            }

            return manifests;
        }

        public List<FileInfo> GetConfigFiles(DirectoryInfo modDirectory)
        {
            List<FileInfo> configs = new List<FileInfo>();
            foreach (var directory in modDirectory.EnumerateDirectories())
            {
                var localConfigs = directory.EnumerateFiles("config.json");
                if (localConfigs.Count() == 0)
                {
                    configs.AddRange(GetConfigFiles(directory));
                    continue;
                }

                var localConfig = localConfigs.First();
                if (localConfig.Directory is not null && localConfig.Directory.EnumerateFiles("manifest.json", SearchOption.TopDirectoryOnly).Count() == 1)
                {
                    configs.Add(localConfig);
                }
            }

            return configs;
        }

        public void DiscoverMods(string modsFilePath)
        {
            if (Mods is null)
            {
                Mods = new ObservableCollection<Mod>();
            }
            Mods.Clear();

            if (modsFilePath is null || !Directory.Exists(modsFilePath))
            {
                return;
            }

            // Get cached key data
            List<ModKeyInfo> modKeysCache = new List<ModKeyInfo>();
            if (File.Exists(Pathing.GetKeyCachePath()))
            {
                modKeysCache = JsonSerializer.Deserialize<List<ModKeyInfo>>(File.ReadAllText(Pathing.GetKeyCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            }

            foreach (var fileInfo in GetManifestFiles(new DirectoryInfo(modsFilePath)))
            {
                if (fileInfo.DirectoryName is null || (Program.settings.IgnoreHiddenFolders && ParentFolderContainsPeriod(modsFilePath, fileInfo.Directory)))
                {
                    continue;
                }

                try
                {
                    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(fileInfo.FullName), new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true });
                    if (manifest is null)
                    {
                        Program.helper.Log($"The manifest.json was empty or not deserializable from {fileInfo.DirectoryName}", Helper.Status.Alert);
                        continue;
                    }

                    var mod = new Mod(manifest, fileInfo, manifest.UniqueID, manifest.Version, manifest.Name, manifest.Description, manifest.Author);
                    if (manifest.ContentPackFor is not null && modKeysCache is not null)
                    {
                        var dependencyKey = modKeysCache.FirstOrDefault(m => m.UniqueId.Equals(manifest.ContentPackFor.UniqueID, StringComparison.OrdinalIgnoreCase));
                        mod.Requirements.Add(new ManifestDependency(manifest.ContentPackFor.UniqueID, manifest.ContentPackFor.MinimumVersion, true) { Name = dependencyKey is null ? manifest.ContentPackFor.UniqueID : dependencyKey.Name });
                    }
                    if (manifest.Dependencies is not null && modKeysCache is not null)
                    {
                        foreach (var dependency in manifest.Dependencies)
                        {
                            if (mod.Requirements.Any(r => r.UniqueID.Equals(dependency.UniqueID, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            var dependencyKey = modKeysCache.FirstOrDefault(m => m.UniqueId.Equals(dependency.UniqueID, StringComparison.OrdinalIgnoreCase));
                            mod.Requirements.Add(new ManifestDependency(dependency.UniqueID, dependency.MinimumVersion, dependency.IsRequired) { Name = dependencyKey is null ? dependency.UniqueID : dependencyKey.Name });
                        }
                    }

                    if (!Mods.Any(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase)))
                    {
                        Mods.Add(mod);
                    }
                    else if (Mods.FirstOrDefault(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) && m.Version < mod.Version) is Mod oldMod && oldMod is not null)
                    {
                        // Replace old mod with newer one
                        int oldModIndex = Mods.IndexOf(Mods.First(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) && m.Version < mod.Version));
                        Mods[oldModIndex] = mod;
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load the manifest.json from {fileInfo.DirectoryName}: {ex}", Helper.Status.Alert);
                }
            }

            EvaluateRequirements();
        }

        public void EvaluateRequirements()
        {
            // Get cached key data
            List<ModKeyInfo> modKeysCache = new List<ModKeyInfo>();
            if (File.Exists(Pathing.GetKeyCachePath()))
            {
                modKeysCache = JsonSerializer.Deserialize<List<ModKeyInfo>>(File.ReadAllText(Pathing.GetKeyCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            }

            // Flag any missing requirements
            foreach (var mod in Mods)
            {
                try
                {
                    foreach (var requirement in mod.Requirements.Where(r => r.IsRequired))
                    {
                        if (!Mods.Any(m => m.UniqueId.Equals(requirement.UniqueID, StringComparison.OrdinalIgnoreCase)) || Mods.First(m => m.UniqueId.Equals(requirement.UniqueID, StringComparison.OrdinalIgnoreCase)) is Mod matchedMod && matchedMod.IsModOutdated(requirement.MinimumVersion))
                        {
                            requirement.IsMissing = true;

                            if (modKeysCache is not null)
                            {
                                var dependencyKey = modKeysCache.FirstOrDefault(m => m.UniqueId.Equals(requirement.UniqueID, StringComparison.OrdinalIgnoreCase));
                                requirement.Name = dependencyKey is null ? requirement.UniqueID : dependencyKey.Name;
                            }
                        }
                    }

                    mod.NotifyPropertyChanged("Requirements");
                    mod.NotifyPropertyChanged("MissingRequirements");
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to check requirements for {mod.Name} due to the following error: {ex}");
                }
            }
        }

        public void EnableModsByProfile(Profile profile)
        {
            foreach (var mod in Mods)
            {
                mod.IsEnabled = false;
                if (profile.EnabledModIds.Any(id => id.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)))
                {
                    mod.IsEnabled = true;
                }
            }

            // Update the EnabledModCount
            EnabledModCount = Mods.Where(m => m.IsEnabled).Count();
        }

        internal List<ConfigInfo> GetPendingConfigUpdates(Profile profile, bool inverseMerge = false)
        {
            // Get the current config files
            var configFiles = GetConfigFiles(new DirectoryInfo(Pathing.defaultModPath));

            // Load the current configs for the enabled mods
            var idToConfigFiles = new Dictionary<string, FileInfo>();
            var enabledModConfigs = new Dictionary<string, JsonDocument>();
            foreach (var modId in profile.EnabledModIds.Select(id => id.ToLower()))
            {
                var mod = Mods.FirstOrDefault(m => m.UniqueId.Equals(modId, StringComparison.OrdinalIgnoreCase));
                if (mod is null || mod.ModFileInfo is null)
                {
                    continue;
                }

                var configInfo = configFiles.FirstOrDefault(c => c.DirectoryName == mod.ModFileInfo.DirectoryName);
                if (configInfo is not null)
                {
                    idToConfigFiles[modId] = configInfo;
                    enabledModConfigs[modId] = JsonDocument.Parse(File.ReadAllText(configInfo.FullName));
                }
            }

            // Merge any existing preserved configs
            List<ConfigInfo> pendingConfigUpdates = new List<ConfigInfo>();
            foreach (var modConfigId in enabledModConfigs.Keys)
            {
                if (profile.PreservedModConfigs.ContainsKey(modConfigId))
                {
                    // Merge the config
                    var originalJson = JsonTools.ParseDocumentToString(enabledModConfigs[modConfigId]);
                    var archivedJson = JsonTools.ParseDocumentToString(profile.PreservedModConfigs[modConfigId]);

                    if (originalJson != archivedJson)
                    {
                        // JsonTools.Merge will preserve the originalJson values, but will add new properties from archivedJson
                        string mergedJson = inverseMerge ? JsonTools.Merge(archivedJson, originalJson) : JsonTools.Merge(originalJson, archivedJson);
                        enabledModConfigs[modConfigId] = JsonDocument.Parse(mergedJson);

                        // Apply the changes to the config file
                        if (idToConfigFiles.ContainsKey(modConfigId))
                        {
                            pendingConfigUpdates.Add(new ConfigInfo() { UniqueId = modConfigId, FilePath = idToConfigFiles[modConfigId].FullName, Data = mergedJson });
                        }
                    }
                }
                else
                {
                    pendingConfigUpdates.Add(new ConfigInfo() { UniqueId = modConfigId, FilePath = idToConfigFiles[modConfigId].FullName, Data = JsonTools.ParseDocumentToString(enabledModConfigs[modConfigId]) });
                }
            }

            return pendingConfigUpdates;
        }

        internal void ReadModConfigs(Profile profile)
        {
            ReadModConfigs(profile, GetPendingConfigUpdates(profile, inverseMerge: true));
        }

        internal void ReadModConfigs(Profile profile, List<ConfigInfo> pendingConfigUpdates)
        {
            foreach (var configInfo in pendingConfigUpdates)
            {
                profile.PreservedModConfigs[configInfo.UniqueId] = JsonDocument.Parse(configInfo.Data);
            }
        }

        internal bool WriteModConfigs(Profile profile)
        {
            return WriteModConfigs(profile, GetPendingConfigUpdates(profile));
        }

        internal bool WriteModConfigs(Profile profile, List<ConfigInfo> pendingConfigUpdates)
        {
            if (pendingConfigUpdates.Count == 0)
            {
                return false;
            }

            // Merge any existing preserved configs
            foreach (var configInfo in pendingConfigUpdates)
            {
                // Apply the changes to the config file
                File.WriteAllText(configInfo.FilePath, configInfo.Data);
            }

            return true;
        }

        internal void UpdateFilter()
        {
            DataView.Filter = null;
            DataView.Filter = ModFilter;
        }

        private bool ModFilter(object item)
        {
            var mod = item as Mod;

            if (_hideDisabledMods && !mod.IsEnabled)
            {
                return false;
            }
            if (_showUpdatableMods && String.IsNullOrEmpty(mod.ParsedStatus))
            {
                return false;
            }
            if (!String.IsNullOrEmpty(_filterText) && !String.IsNullOrEmpty(_columnFilter))
            {
                if (_columnFilter == "Mod Name" && !mod.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else if (_columnFilter == "Author" && !mod.Author.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else if (_columnFilter == "Requirements" && !mod.Requirements.Any(r => r.UniqueID.Equals(_filterText, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
