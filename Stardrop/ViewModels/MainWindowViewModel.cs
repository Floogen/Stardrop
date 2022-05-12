using Avalonia.Collections;
using ReactiveUI;
using Stardrop.Models;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.SMAPI;
using Stardrop.Utilities;
using Stardrop.Utilities.External;
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
        private bool _isCheckingForUpdates;
        public bool IsCheckingForUpdates { get { return _isCheckingForUpdates; } set { this.RaiseAndSetIfChanged(ref _isCheckingForUpdates, value); } }
        public ObservableCollection<Mod> Mods { get; set; }
        private int _enabledModCount;
        public int EnabledModCount { get { return _enabledModCount; } set { this.RaiseAndSetIfChanged(ref _enabledModCount, value); } }
        private int _actualModCount;
        public int ActualModCount { get { return _actualModCount; } set { this.RaiseAndSetIfChanged(ref _actualModCount, value); } }
        public DataGridCollectionView DataView { get; set; }

        private DisplayFilter _disabledModFilter;
        public DisplayFilter DisabledModFilter { get { return _disabledModFilter; } set { _disabledModFilter = value; UpdateFilter(); } }

        private bool _showUpdatableMods;
        public bool ShowUpdatableMods { get { return _showUpdatableMods; } set { _showUpdatableMods = value; UpdateFilter(); } }
        private bool _showRequirements;
        public bool ShowRequirements { get { return _showRequirements; } set { this.RaiseAndSetIfChanged(ref _showRequirements, value); } }
        private bool _showEndorsements;
        public bool ShowEndorsements { get { return _showEndorsements; } set { this.RaiseAndSetIfChanged(ref _showEndorsements, value); } }
        private bool _showInstalls;
        public bool ShowInstalls { get { return _showInstalls; } set { this.RaiseAndSetIfChanged(ref _showInstalls, value); } }
        public string RequirementColumnState { get { return ShowRequirements ? Program.translation.Get("ui.main_window.menu_items.context.hide_requirements") : Program.translation.Get("ui.main_window.menu_items.context.show_requirements"); } }
        private string _filterText;
        public string FilterText { get { return _filterText; } set { _filterText = value; UpdateFilter(); } }
        private string _columnFilter;
        public string ColumnFilter { get { return _columnFilter; } set { _columnFilter = value; UpdateFilter(); } }
        private string _updateStatusText = Program.translation.Get("ui.main_window.button.update_status.generic");
        public string UpdateStatusText { get { return _updateStatusText; } set { this.RaiseAndSetIfChanged(ref _updateStatusText, value); } }
        private int _modsWithCachedUpdates;
        public int ModsWithCachedUpdates { get { return _modsWithCachedUpdates; } set { this.RaiseAndSetIfChanged(ref _modsWithCachedUpdates, value); } }
        public string Version { get; set; }

        private string _nexusStatus = String.Concat("Nexus Mods: ", Program.translation.Get("internal.disconnected"));
        public string NexusStatus { get { return _nexusStatus; } set { this.RaiseAndSetIfChanged(ref _nexusStatus, String.Concat("Nexus Mods: ", value)); } }

        private string _nexusLimits;
        public string NexusLimits { get { return _nexusLimits; } set { this.RaiseAndSetIfChanged(ref _nexusLimits, value); } }
        private string _smapiVersion;
        public string SmapiVersion { get { return String.IsNullOrEmpty(_smapiVersion) ? Program.translation.Get("ui.main_window.labels.unknown_SMAPI") : $"v{_smapiVersion}"; } set { this.RaiseAndSetIfChanged(ref _smapiVersion, value); } }

        public MainWindowViewModel(string modsFilePath, string version)
        {
            DiscoverMods(modsFilePath);
            Version = $"v{version}";
            SmapiVersion = Program.settings.GameDetails?.SmapiVersion;

            // Create data view
            var dataGridSortDescription = DataGridSortDescription.FromPath(nameof(Mod.Name), ListSortDirection.Ascending);

            DataView = new DataGridCollectionView(Mods);
            DataView.SortDescriptions.Add(dataGridSortDescription);
            UpdateFilter();

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

            try
            {
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
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to utilize OpenBrowser with the url ({url}): {ex}");
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
                    if (modKeysCache is not null && modKeysCache.Any(m => m.UniqueId.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)))
                    {
                        mod.ModPageUri = modKeysCache.First(m => m.UniqueId.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)).PageUrl;
                    }

                    // Check if any config file exists
                    var configPath = Path.Combine(fileInfo.DirectoryName, "config.json");
                    if (File.Exists(configPath) && new FileInfo(configPath) is FileInfo configInfo && configInfo is not null)
                    {
                        mod.Config = new Config() { UniqueId = mod.UniqueId, FilePath = configInfo.FullName, LastWriteTimeUtc = configInfo.LastWriteTimeUtc, Data = File.ReadAllText(configInfo.FullName) };
                    }

                    // Add or update the mod
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
            DiscoverConfigs(modsFilePath, useArchive: true);
            HideRequiredMods();

            ActualModCount = Mods.Count(m => !m.IsHidden);
        }

        public void HideRequiredMods()
        {
            var requiredModIds = new List<string> { "SMAPI.ConsoleCommands", "SMAPI.ErrorHandler", "SMAPI.SaveBackup" };
            foreach (var mod in Mods.Where(m => requiredModIds.Any(id => id.Equals(m.UniqueId, StringComparison.OrdinalIgnoreCase))))
            {
                mod.IsHidden = true;
                mod.IsEnabled = true;
            }

            // Update the EnabledModCount
            EnabledModCount = Mods.Where(m => m.IsEnabled && !m.IsHidden).Count();

            // Update filter
            UpdateFilter();
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

        public void DiscoverConfigs(string modsFilePath, bool useArchive = false)
        {
            if (modsFilePath is null || !Directory.Exists(modsFilePath))
            {
                return;
            }

            foreach (var fileInfo in GetConfigFiles(new DirectoryInfo(modsFilePath)))
            {
                if (fileInfo.DirectoryName is null || (Program.settings.IgnoreHiddenFolders && ParentFolderContainsPeriod(modsFilePath, fileInfo.Directory)))
                {
                    continue;
                }

                var mod = Mods.FirstOrDefault(m => m.ModFileInfo is not null && m.ModFileInfo.DirectoryName == fileInfo.DirectoryName);
                if (mod is null)
                {
                    continue;
                }
                else if (useArchive && mod.Config is not null)
                {
                    if (fileInfo.LastWriteTimeUtc <= mod.Config.LastWriteTimeUtc)
                    {
                        continue;
                    }

                    mod.Config.Data = File.ReadAllText(fileInfo.FullName);
                    mod.Config.LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
                }
                else
                {
                    mod.Config = new Config() { UniqueId = mod.UniqueId, FilePath = fileInfo.FullName, LastWriteTimeUtc = fileInfo.LastWriteTimeUtc, Data = File.ReadAllText(fileInfo.FullName) };
                }
            }
        }

        internal List<Config> GetPendingConfigUpdates(Profile profile, bool inverseMerge = false, bool excludeMissingConfigs = false)
        {
            // Merge any existing preserved configs
            List<Config> pendingConfigUpdates = new List<Config>();
            foreach (var modId in profile.EnabledModIds.Select(id => id.ToLower()))
            {
                var mod = Mods.FirstOrDefault(m => m.UniqueId.Equals(modId, StringComparison.OrdinalIgnoreCase));
                if (mod is null || mod.ModFileInfo is null)
                {
                    continue;
                }

                try
                {
                    if (profile.PreservedModConfigs.ContainsKey(modId))
                    {
                        // Write the archived config, if the current one doesn't exist
                        if (mod.Config is null)
                        {
                            if (excludeMissingConfigs || String.IsNullOrEmpty(mod.ModFileInfo.DirectoryName))
                            {
                                continue;
                            }

                            mod.Config = new Config() { UniqueId = modId, FilePath = Path.Combine(mod.ModFileInfo.DirectoryName, "config.json"), Data = JsonTools.ParseDocumentToString(profile.PreservedModConfigs[modId]) };
                            pendingConfigUpdates.Add(mod.Config);
                        }
                        else
                        {
                            // Merge the config
                            var originalJson = mod.Config.Data;
                            var archivedJson = JsonTools.ParseDocumentToString(profile.PreservedModConfigs[modId]);

                            if (originalJson != archivedJson)
                            {
                                // JsonTools.Merge will preserve the originalJson values, but will add new properties from archivedJson
                                string mergedJson = inverseMerge ? JsonTools.Merge(archivedJson, originalJson) : JsonTools.Merge(originalJson, archivedJson);
                                mod.Config.Data = mergedJson;

                                // Apply the changes to the config file
                                pendingConfigUpdates.Add(new Config() { UniqueId = modId, FilePath = mod.Config.FilePath, Data = mergedJson });
                            }
                        }
                    }
                    else if (mod.Config is not null)
                    {
                        pendingConfigUpdates.Add(new Config() { UniqueId = modId, FilePath = mod.Config.FilePath, Data = mod.Config.Data });
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to process config.json for mod {modId}: {ex}", Helper.Status.Warning);
                }
            }

            return pendingConfigUpdates;
        }

        internal async void UpdateEndorsements(string? apiKey)
        {
            if (String.IsNullOrEmpty(apiKey))
            {
                return;
            }

            var endorsements = await Nexus.GetEndorsements(apiKey);
            foreach (var mod in Mods.Where(m => m.HasUpdateKeys() && endorsements.Any(e => e.Id == m.NexusModId)))
            {
                mod.IsEndorsed = endorsements.First(e => e.Id == mod.NexusModId).IsEndorsed();
            }
        }

        internal void ReadModConfigs(Profile profile)
        {
            ReadModConfigs(profile, GetPendingConfigUpdates(profile, inverseMerge: true));
        }

        internal void ReadModConfigs(Profile profile, List<Config> pendingConfigUpdates)
        {
            foreach (var configInfo in pendingConfigUpdates)
            {
                try
                {
                    profile.PreservedModConfigs[configInfo.UniqueId] = JsonDocument.Parse(configInfo.Data);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to read config for the mod {configInfo.UniqueId} due to the following error:\n{ex}");
                }
            }
        }

        internal bool WriteModConfigs(Profile profile)
        {
            return WriteModConfigs(profile, GetPendingConfigUpdates(profile));
        }

        internal bool WriteModConfigs(Profile profile, List<Config> pendingConfigUpdates)
        {
            if (pendingConfigUpdates.Count == 0)
            {
                return false;
            }

            // Merge any existing preserved configs
            foreach (var configInfo in pendingConfigUpdates.Where(c => profile.PreservedModConfigs.ContainsKey(c.UniqueId.ToLower())))
            {
                try
                {
                    var fileInfo = new FileInfo(configInfo.FilePath);
                    if (!Directory.Exists(fileInfo.DirectoryName))
                    {
                        continue;
                    }

                    // Apply the changes to the config file
                    File.WriteAllText(configInfo.FilePath, configInfo.Data);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to write config for the mod {configInfo.UniqueId} due to the following error:\n{ex}");
                }
            }

            return true;
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
            HideRequiredMods();

            // Update the EnabledModCount
            EnabledModCount = Mods.Where(m => m.IsEnabled && !m.IsHidden).Count();
        }

        internal void UpdateFilter()
        {
            if (DataView is not null)
            {
                DataView.Filter = null;
                DataView.Filter = ModFilter;
            }
        }

        private bool ModFilter(object item)
        {
            var mod = item as Mod;

            if (mod.IsHidden)
            {
                return false;
            }

            if (_disabledModFilter == DisplayFilter.Show && mod.IsEnabled)
            {
                return false;
            }
            else if (_disabledModFilter == DisplayFilter.Hide && !mod.IsEnabled)
            {
                return false;
            }

            if (_showUpdatableMods && String.IsNullOrEmpty(mod.ParsedStatus))
            {
                return false;
            }
            if (!String.IsNullOrEmpty(_filterText) && !String.IsNullOrEmpty(_columnFilter))
            {
                if (_columnFilter == Program.translation.Get("ui.main_window.combobox.mod_name") && !mod.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else if (_columnFilter == Program.translation.Get("ui.main_window.combobox.author") && !mod.Author.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else if (_columnFilter == Program.translation.Get("ui.main_window.combobox.requirements") && !mod.HardRequirements.Any(r => r.Name is null || r.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase)) && !mod.MissingRequirements.Any(r => r.Name is null || r.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
