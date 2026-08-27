using Avalonia.Collections;
using Avalonia.Controls;
using Json.More;
using ReactiveUI;
using SharpCompress.Archives;
using SharpCompress.Common;
using Stardrop.Models;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.SMAPI;
using Stardrop.Utilities;
using Stardrop.Utilities.Extension;
using Stardrop.Utilities.External;
using Stardrop.Utilities.Internal;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Stardrop.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private string ChromeHint { get; set; } = "NoChrome";
        private bool HasSystemDecorations { get; set; } = true;
        private bool ShowTitle { get; set; } = true;
        private bool ShowMainMenu { get; set; } = true;
        private bool ShowWindowMenu { get; set; } = true;

        private DataGridPathGroupDescription _modPathGrouping = new DataGridPathGroupDescription(nameof(Mod.Path));
        private DataGridPathGroupDescription _rootPathGrouping = new DataGridPathGroupDescription(nameof(Mod.RootPath));
        private DataGridPathGroupDescription _frameworkGrouping = new DataGridPathGroupDescription(nameof(Mod.FrameworkID));

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
        private ModSourceFilter _modSourceFilter = ModSourceFilter.ActiveProfile;
        public ModSourceFilter ModSourceFilter { get { return _modSourceFilter; } set { _modSourceFilter = value; UpdateFilter(); RefreshModCounts(); } }
        private bool _hasCollectionMods;
        /// <summary>Whether any installed mod belongs to a collection. Drives the visibility of the source filter, which is noise for users with no collections</summary>
        public bool HasCollectionMods { get { return _hasCollectionMods; } set { this.RaiseAndSetIfChanged(ref _hasCollectionMods, value); } }
        private Profile? _activeProfile;
        private HashSet<ModReference> _activeProfileReferences = new HashSet<ModReference>();
        private bool _showEndorsements;
        public bool ShowEndorsements { get { return _showEndorsements; } set { this.RaiseAndSetIfChanged(ref _showEndorsements, value); } }
        private bool _showInstalls;
        public bool ShowInstalls { get { return _showInstalls; } set { this.RaiseAndSetIfChanged(ref _showInstalls, value); } }
        private string _filterText;
        public string FilterText { get { return _filterText; } set { _filterText = value; UpdateFilter(); } }
        private List<string> _columnFilter;
        public List<string> ColumnFilter { get { return _columnFilter; } set { _columnFilter = value; UpdateFilter(); } }
        private string _updateStatusText = Program.translation.Get("ui.main_window.button.update_status.generic");
        public string UpdateStatusText { get { return _updateStatusText; } set { this.RaiseAndSetIfChanged(ref _updateStatusText, value); } }
        private string _downloadsButtonText;
        public string DownloadsButtonText { get => _downloadsButtonText; set => this.RaiseAndSetIfChanged(ref _downloadsButtonText, value); }
        private int _modsWithCachedUpdates;
        public int ModsWithCachedUpdates { get { return _modsWithCachedUpdates; } set { this.RaiseAndSetIfChanged(ref _modsWithCachedUpdates, value); } }
        private int _collectionsWithUpdates;
        /// <summary>
        /// Collections sitting behind a newer revision. Shown beside the mod update count as a pointer towards the
        /// collections window, which is where the update itself is described and acted on.
        /// </summary>
        public int CollectionsWithUpdates
        {
            get { return _collectionsWithUpdates; }
            set
            {
                this.RaiseAndSetIfChanged(ref _collectionsWithUpdates, value);
                this.RaisePropertyChanged(nameof(HasCollectionUpdates));
            }
        }
        /// <summary>Hides the collection update count outright, rather than showing a zero to the many users who have no collections</summary>
        public bool HasCollectionUpdates { get { return _collectionsWithUpdates > 0; } }
        public string Version { get; set; }

        private string _nexusStatus = String.Concat("Nexus Mods: ", Program.translation.Get("internal.disconnected"));
        public string NexusStatus { get { return _nexusStatus; } set { this.RaiseAndSetIfChanged(ref _nexusStatus, String.Concat("Nexus Mods: ", value)); } }

        private string _nexusLimits;
        public string NexusLimits { get { return _nexusLimits; } set { this.RaiseAndSetIfChanged(ref _nexusLimits, value); } }
        private string _smapiVersion;
        public string SmapiVersion { get { return String.IsNullOrEmpty(_smapiVersion) ? Program.translation.Get("ui.main_window.labels.unknown_SMAPI") : $"v{_smapiVersion}"; } set { this.RaiseAndSetIfChanged(ref _smapiVersion, value); } }

        public bool ShowSaveProfileChanges { get { return _showSaveProfileChanges; } set { this.RaiseAndSetIfChanged(ref _showSaveProfileChanges, value); } }
        private bool _showSaveProfileChanges;
        public bool AreModGroupsEnabled { get { return _areModGroupsEnabled; } set { this.RaiseAndSetIfChanged(ref _areModGroupsEnabled, value); } }
        private bool _areModGroupsEnabled = Program.settings.ModGroupingMethod != ModGrouping.None;
        public bool ShowModThumbnails { get { return _showModThumbnails; } set { this.RaiseAndSetIfChanged(ref _showModThumbnails, value); } }
        private bool _showModThumbnails = Program.settings.ShowModThumbnails;
        public string ModGroupsStateButtonText { get { return _modGroupsStateButtonText; } set { this.RaiseAndSetIfChanged(ref _modGroupsStateButtonText, value); } }
        private string _modGroupsStateButtonText = Program.settings.ModGroupingMethod != ModGrouping.None ? Program.translation.Get("ui.main_window.buttons.mod_groups_state.collapse") : Program.translation.Get("ui.main_window.buttons.mod_groups_state.expand");

        public MainWindowViewModel(string modsFilePath, string version)
        {
            DiscoverMods(modsFilePath);
            Version = $"v{version}";
            SmapiVersion = Program.settings.GameDetails?.SmapiVersion;

            // Create data view
            DataView = new DataGridCollectionView(Mods, isDataSorted: false, isDataInGroupOrder: false);
            DataView.SortDescriptions.CollectionChanged += DataViewSortDescription_CollectionChanged;

            UpdateFilter();

            DataView.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(Mod.Name), ListSortDirection.Ascending));

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
            Toolkit.OpenBrowser(url);
        }

        public void ChangeColumnVisibility(MenuItem column)
        {
            if (column is null)
            {
                return;
            }

            var modGrid = column.FindControl<DataGrid>("modGrid");
            if (modGrid is null)
            {
                return;
            }

            if (column.Classes.Contains("ColumnInactive"))
            {
                SetColumnVisibility(column, modGrid, true);
            }
            else
            {
                SetColumnVisibility(column, modGrid, false);
            }
        }

        public void SetColumnVisibility(MenuItem column, DataGrid modGrid, bool isActive)
        {
            // Get the local data
            ClientData localDataCache = new ClientData();
            if (File.Exists(Pathing.GetDataCachePath()))
            {
                localDataCache = JsonSerializer.Deserialize<ClientData>(File.ReadAllText(Pathing.GetDataCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            }

            if (isActive)
            {
                if (modGrid.Columns.Any(c => c.Header is string text && text == (string)column.Header))
                {
                    column.Classes.Remove("ColumnInactive");
                    column.Classes.Add("ColumnActive");

                    modGrid.Columns.First(c => c.Header is string text && text == (string)column.Header).IsVisible = true;
                    localDataCache.ColumnActiveStates[(string)column.Header] = true;
                }
            }
            else
            {
                if (modGrid.Columns.Any(c => c.Header is string text && text == (string)column.Header))
                {
                    column.Classes.Remove("ColumnActive");
                    column.Classes.Add("ColumnInactive");

                    modGrid.Columns.First(c => c.Header is string text && text == (string)column.Header).IsVisible = false;
                    localDataCache.ColumnActiveStates[(string)column.Header] = false;
                }
            }

            // Cache the local data
            File.WriteAllText(Pathing.GetDataCachePath(), JsonSerializer.Serialize(localDataCache, new JsonSerializerOptions() { WriteIndented = true }));
        }

        public void SetColumnOrder(DataGrid modGrid)
        {
            // Get the local data
            ClientData localDataCache = new ClientData();
            if (File.Exists(Pathing.GetDataCachePath()))
            {
                localDataCache = JsonSerializer.Deserialize<ClientData>(File.ReadAllText(Pathing.GetDataCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            }

            if (localDataCache is not null && modGrid is not null && modGrid.Columns is not null)
            {
                localDataCache.ColumnOrder.Clear();
                foreach (var column in modGrid.Columns)
                {
                    var columnKey = ColumnExtensions.GetKey(column);
                    if (string.IsNullOrEmpty(columnKey))
                    {
                        Program.helper.Log($"Failed to reorder column {column.Header.ToString()}: it lacks an ext:ColumnExtensions.Key value in the XAML.");
                        continue;
                    }
                    localDataCache.ColumnOrder[columnKey] = column.DisplayIndex;
                }

                // Cache the local data
                File.WriteAllText(Pathing.GetDataCachePath(), JsonSerializer.Serialize(localDataCache, new JsonSerializerOptions() { WriteIndented = true }));
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

        /// <summary>
        /// The readable name of every installed collection, keyed by the source ID its mods carry. Name can be
        /// empty on a record built from a collection that never reported one, so the slug stands in for it, which
        /// is the same fallback GetAvailableProfileName uses when naming the generated profile.
        /// </summary>
        private static Dictionary<string, string> GetCollectionNamesBySourceId()
        {
            var namesBySourceId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var collection in CollectionCache.LoadAll())
            {
                namesBySourceId[collection.SourceId] = String.IsNullOrEmpty(collection.Name) ? collection.Slug : collection.Name;
            }

            return namesBySourceId;
        }

        /// <summary>
        /// The folders a discovery pass walks. Collections are installed outside the mod folder, so they are a root
        /// of their own rather than something the mod folder walk reaches on its way down.
        /// </summary>
        private static List<string> GetScanRoots(string modsFilePath)
        {
            var roots = new List<string>();
            if (String.IsNullOrEmpty(modsFilePath) is false && Directory.Exists(modsFilePath))
            {
                roots.Add(modsFilePath);
            }

            // Skipped when the mod folder already contains it, which would otherwise walk every collection twice
            var collectionsPath = Pathing.GetCollectionsFolderPath();
            if (Directory.Exists(collectionsPath) && roots.Any(r => collectionsPath.StartsWith(r, StringComparison.OrdinalIgnoreCase)) is false)
            {
                roots.Add(collectionsPath);
            }

            return roots;
        }

        /// <summary>
        /// Walks every root, keeping each file paired with the root it was found under. The root has to travel with
        /// the file because <see cref="ParentFolderContainsPeriod"/> measures from it, and the collections root sits
        /// under the application data folder, which is itself a dotted folder on Linux and macOS. Measuring a
        /// collection mod from the mod folder would find that period and hide every collection mod on those systems.
        /// </summary>
        private static List<(string Root, FileInfo File)> GetDiscoverableFiles(List<string> scanRoots, Func<DirectoryInfo, List<FileInfo>> walkRoot)
        {
            var found = new List<(string Root, FileInfo File)>();
            foreach (var root in scanRoots)
            {
                foreach (var file in walkRoot(new DirectoryInfo(root)))
                {
                    found.Add((root, file));
                }
            }

            return found;
        }

        public List<FileInfo> GetManifestFiles(DirectoryInfo modDirectory)
        {
            List<FileInfo> manifests = new List<FileInfo>();
            foreach (var directory in modDirectory.EnumerateDirectories())
            {
                try
                {
                    var localManifest = directory.EnumerateFiles().FirstOrDefault(file => file.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
                    if (localManifest is null)
                    {
                        manifests.AddRange(GetManifestFiles(directory));
                    }
                    else
                    {
                        manifests.Add(localManifest);
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"There was an error when attempting to get the manifest.json within the directory ({(directory is null ? String.Empty : directory.FullName)}): {ex}", Helper.Status.Alert);
                }
            }

            return manifests;
        }

        public bool HasModInstalled(string uniqueID)
        {
            return Mods.Any(m => m.UniqueId.Equals(uniqueID, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Only use to install new mods that don't currently exist within manager. Use MainWindow.AddMods for the safer method (update handling, various safety checks, etc.)
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public async Task<List<Mod>> DirectModInstallAsync(string? fileFullName)
        {
            List<Mod> addedMods = new List<Mod>();
            if (string.IsNullOrEmpty(fileFullName))
            {
                return addedMods;
            }

            try
            {
                // Extract the archive data
                using (var archive = ArchiveFactory.OpenArchive(fileFullName))
                {
                    Dictionary<string, Manifest?> pathToManifests = new Dictionary<string, Manifest?>();
                    foreach (var manifest in archive.Entries.Where(e => Path.GetFileName(e.Key)!.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (manifest.Key is not null)
                        {
                            Program.helper.Log(manifest.Key);

                            // Skip any mods that already are installed (don't handle updates)
                            var parsedManifest = await ManifestParser.GetDataAsync(manifest);
                            if (parsedManifest is null || HasModInstalled(parsedManifest.UniqueID) is true)
                            {
                                continue;
                            }
                            pathToManifests[manifest.Key] = parsedManifest;
                        }
                    }

                    // Warn and skip the install logic if the given archive has no manifest.json
                    if (pathToManifests.Count == 0)
                    {
                        Program.helper.Log(String.Format(Program.translation.Get("ui.warning.no_manifest"), fileFullName));
                        return addedMods;
                    }

                    int currentManifestIndex = 1;
                    bool alwaysAskToDelete = Program.settings.AlwaysAskToDelete;
                    foreach (var manifestPath in pathToManifests.Keys)
                    {
                        var manifest = pathToManifests[manifestPath];

                        // If the archive doesn't have a manifest, warn the user
                        if (manifest is not null)
                        {
                            var installPath = Program.settings.ModInstallPath;
                            if (String.IsNullOrEmpty(manifestPath.Replace("manifest.json", String.Empty, StringComparison.OrdinalIgnoreCase)))
                            {
                                installPath = Path.Combine(installPath, manifest.UniqueID);
                            }

                            // Create the base directory, if needed
                            if (Directory.Exists(installPath) is false)
                            {
                                Directory.CreateDirectory(installPath);
                            }

                            Program.helper.Log($"Install path for mod {manifest.UniqueID}:{installPath}");
                            var manifestFolderPath = manifestPath.Replace("manifest.json", String.Empty, StringComparison.OrdinalIgnoreCase);
                            foreach (var entry in archive.Entries.Where(e => e.Key.StartsWith(manifestFolderPath)))
                            {
                                if (entry.Key.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase) || entry.Key.Contains(".DS_Store", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                var outputPath = Path.Combine(installPath, manifestFolderPath, String.IsNullOrEmpty(manifestFolderPath) ? entry.Key : Path.GetRelativePath(manifestFolderPath, entry.Key));

                                if (String.IsNullOrEmpty(manifestFolderPath) is false)
                                {
                                    var installDirectory = new DirectoryInfo(installPath);
                                    var manifestDirectory = new DirectoryInfo(manifestFolderPath);
                                    if (installDirectory.Exists && (installDirectory.Name.Equals(manifestDirectory.Name, StringComparison.OrdinalIgnoreCase) || installDirectory.Name.Equals(manifest.UniqueID)))
                                    {
                                        outputPath = Path.Combine(installPath, String.IsNullOrEmpty(manifestFolderPath) ? entry.Key : Path.GetRelativePath(manifestFolderPath, entry.Key));

                                        Program.helper.Log(outputPath);
                                    }
                                }
                                outputPath = Regex.Replace(outputPath, @"\s+\/", "/");

                                // Create the default location if it doesn't existe
                                var outputFolder = Path.GetDirectoryName(outputPath);
                                if (String.IsNullOrEmpty(outputFolder))
                                {
                                    continue;
                                }
                                else if (Directory.Exists(outputFolder) is false)
                                {
                                    Directory.CreateDirectory(outputFolder);
                                }

                                if (entry.IsDirectory is false)
                                {
                                    Program.helper.Log($"Writing mod file to {outputPath}");
                                    await Task.Run(() => entry.WriteToFile(outputPath, new ExtractionOptions() { ExtractFullPath = false, Overwrite = true }));
                                }
                            }

                            addedMods.Add(new Mod(manifest, new FileInfo(Path.Join(installPath, manifestFolderPath)), manifest.UniqueID, manifest.Version, manifest.Name, manifest.Description, manifest.Author));
                        }
                        else
                        {
                            Program.helper.Log(String.Format(Program.translation.Get("ui.warning.no_manifest"), fileFullName));
                        }

                        currentManifestIndex += 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to unzip the file {fileFullName} due to the following error: {ex}", Utilities.Helper.Status.Warning);
            }

            return addedMods;
        }

        public void DiscoverMods(string modsFilePath)
        {
            if (Mods is null)
            {
                Mods = new ObservableCollection<Mod>();
            }
            Mods.Clear();

            var scanRoots = GetScanRoots(modsFilePath);
            if (scanRoots.Count == 0)
            {
                return;
            }

            // Get cached key data
            List<ModKeyInfo> modKeysCache = new List<ModKeyInfo>();
            if (File.Exists(Pathing.GetKeyCachePath()))
            {
                try
                {
                    modKeysCache = JsonSerializer.Deserialize<List<ModKeyInfo>>(File.ReadAllText(Pathing.GetKeyCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to parse cached mod keys: {ex}", Helper.Status.Alert);
                }
            }

            // Get the local data
            ClientData localDataCache = new ClientData();
            if (File.Exists(Pathing.GetDataCachePath()))
            {
                try
                {
                    localDataCache = JsonSerializer.Deserialize<ClientData>(File.ReadAllText(Pathing.GetDataCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to parse client data: {ex}", Helper.Status.Alert);
                }
            }

            // Read once for the whole pass rather than once per mod, as every mod in a collection resolves to the
            // same record and CollectionCache.Load goes to disk each time it is called
            var collectionNamesBySourceId = GetCollectionNamesBySourceId();

            foreach (var (scanRoot, fileInfo) in GetDiscoverableFiles(scanRoots, GetManifestFiles))
            {
                if (fileInfo.DirectoryName is null || (Program.settings.IgnoreHiddenFolders && ParentFolderContainsPeriod(scanRoot, fileInfo.Directory)))
                {
                    continue;
                }

                try
                {
                    var manifest = ManifestParser.GetData(File.ReadAllText(fileInfo.FullName));
                    if (manifest is null || String.IsNullOrEmpty(manifest.UniqueID))
                    {
                        Program.helper.Log($"The manifest.json was empty or not deserializable from {fileInfo.DirectoryName}", Helper.Status.Alert);
                        continue;
                    }

                    var mod = new Mod(manifest, fileInfo, manifest.UniqueID, manifest.Version, manifest.Name, manifest.Description, manifest.Author);
                    if (mod.SourceId is not null)
                    {
                        // Falls back to the source ID where no record was found, which happens for a collection
                        // folder whose cache record has been lost. Better a slug than an empty column
                        mod.CollectionName = collectionNamesBySourceId.TryGetValue(mod.SourceId, out var collectionName) ? collectionName : mod.SourceId;
                    }

                    if (manifest.ContentPackFor is not null && modKeysCache is not null)
                    {
                        var dependencyKey = modKeysCache.FirstOrDefault(m => m.UniqueId.Equals(manifest.ContentPackFor.UniqueID, StringComparison.OrdinalIgnoreCase));
                        mod.FrameworkID = manifest.ContentPackFor.UniqueID;
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

                    if (localDataCache is not null && localDataCache.ModInstallData is not null && localDataCache.ModInstallData.Any(m => m.UniqueId.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)))
                    {
                        mod.InstallTimestamp = localDataCache.ModInstallData.First(m => m.UniqueId.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)).InstallTimestamp;
                        mod.LastUpdateTimestamp = localDataCache.ModInstallData.First(m => m.UniqueId.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)).LastUpdateTimestamp;
                    }

                    // Check if any config file exists
                    var configPath = Path.Combine(fileInfo.DirectoryName, "config.json");
                    if (File.Exists(configPath) && new FileInfo(configPath) is FileInfo configInfo && configInfo is not null)
                    {
                        mod.Config = new Config() { UniqueId = mod.UniqueId, FilePath = configInfo.FullName, LastWriteTimeUtc = configInfo.LastWriteTimeUtc, Data = File.ReadAllText(configInfo.FullName) };
                    }

                    // Add or update the mod
                    // Identity is (SourceId, UniqueId), so a collection's pinned copy never displaces a loose install of the same mod
                    var modReference = mod.ToReference();
                    var existingMod = Mods.FirstOrDefault(m => modReference.Matches(m));
                    if (existingMod is null)
                    {
                        Mods.Add(mod);
                    }
                    else if (existingMod.Version.CompareSortOrderTo(mod.Version) < 0)
                    {
                        // Replace old mod with newer one
                        Mods[Mods.IndexOf(existingMod)] = mod;
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load the manifest.json from {fileInfo.DirectoryName}: {ex}", Helper.Status.Alert);
                }
            }

            if (Program.settings.ShowModThumbnails)
            {
                UpdateThumbnails();
            }

            // Update the local data
            var modInstallData = new List<ModInstallData>();
            foreach (var mod in Mods.Where(m => m is not null))
            {
                if (mod.InstallTimestamp is null)
                {
                    mod.InstallTimestamp = DateTime.Now;
                }

                modInstallData.Add(new ModInstallData() { UniqueId = mod.UniqueId, InstallTimestamp = mod.InstallTimestamp.Value, LastUpdateTimestamp = mod.LastUpdateTimestamp });
            }
            localDataCache.ModInstallData = modInstallData;

            // Cache the local data
            File.WriteAllText(Pathing.GetDataCachePath(), JsonSerializer.Serialize(localDataCache, new JsonSerializerOptions() { WriteIndented = true }));

            EvaluateRequirements();
            DiscoverConfigs(modsFilePath, useArchive: true);
            HideRequiredMods();

            HasCollectionMods = Mods.Any(m => m.IsFromCollection);
            RefreshModCounts();
        }

        public void HideRequiredMods()
        {
            var requiredModIds = new List<string> { "SMAPI.ConsoleCommands", "SMAPI.ErrorHandler", "SMAPI.SaveBackup" };
            foreach (var mod in Mods.Where(m => requiredModIds.Any(id => id.Equals(m.UniqueId, StringComparison.OrdinalIgnoreCase))))
            {
                mod.IsHidden = true;
                mod.IsEnabled = true;
            }

            RefreshModCounts();

            // Update data grid grouping
            UpdateDataGridGrouping();
        }

        /// <summary>
        /// Finds the copy of a dependency that will actually be loaded alongside the given mod. With collections in
        /// play the same unique ID can exist several times, so a copy that is already enabled is taken before any
        /// other, and a mod's dependency otherwise resolves within its own source first. Falling back to any copy
        /// would mark a requirement satisfied by a mod that is not enabled.
        /// </summary>
        internal Mod? ResolveRequirement(Mod dependent, string requirementUniqueId)
        {
            var candidates = Mods.Where(m => m.UniqueId.Equals(requirementUniqueId, StringComparison.OrdinalIgnoreCase)).ToList();

            // An enabled copy wins over everything else, the dependent's own source first. Each enabled mod is
            // handed to SMAPI as a junction of its own, so resolving past one that is already on and enabling a
            // second copy puts two folders claiming the same unique ID in the mods folder
            var enabledMatch = candidates.FirstOrDefault(m => m.IsEnabled && String.Equals(m.SourceId, dependent.SourceId, StringComparison.OrdinalIgnoreCase));
            if (enabledMatch is null)
            {
                enabledMatch = candidates.FirstOrDefault(m => m.IsEnabled);
            }

            if (enabledMatch is not null)
            {
                return enabledMatch;
            }

            // Nothing is on yet, so the copy that belongs alongside the dependent is the one to reach for
            var sameSourceMatch = candidates.FirstOrDefault(m => String.Equals(m.SourceId, dependent.SourceId, StringComparison.OrdinalIgnoreCase));
            if (sameSourceMatch is not null)
            {
                return sameSourceMatch;
            }

            // A collection mod can legitimately depend on something the user already had loose, so a loose install
            // is a valid fallback even while off. The reverse only holds through the enabled check above, as a
            // loose mod has no claim on a collection's copy unless that copy is already going to load
            if (dependent.IsFromCollection)
            {
                return candidates.FirstOrDefault(m => m.IsFromCollection is false);
            }

            return null;
        }

        /// <summary>
        /// The mods that would actually load the given mod as a requirement. The mirror of
        /// <see cref="ResolveRequirement"/>: a mod naming this unique ID only counts as a dependent when this is
        /// the copy its own source resolves to, so acting on a collection's copy leaves an identically named loose
        /// copy and everything depending on that one alone.
        /// </summary>
        internal List<Mod> GetDependents(Mod mod)
        {
            var dependents = new List<Mod>();
            foreach (var candidate in Mods.Where(m => m.Requirements.Any(r => r.IsRequired && r.UniqueID.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase))))
            {
                if (ReferenceEquals(ResolveRequirement(candidate, mod.UniqueId), mod))
                {
                    dependents.Add(candidate);
                }
            }

            return dependents;
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
                        var matchedMod = ResolveRequirement(mod, requirement.UniqueID);
                        if (matchedMod is null || matchedMod.IsModOutdated(requirement.MinimumVersion))
                        {
                            requirement.IsMissing = true;

                            if (modKeysCache is not null)
                            {
                                var dependencyKey = modKeysCache.FirstOrDefault(m => m.UniqueId.Equals(requirement.UniqueID, StringComparison.OrdinalIgnoreCase));
                                requirement.Name = dependencyKey is null ? requirement.UniqueID : dependencyKey.Name;
                            }
                        }
                        else
                        {
                            // Clear the flag, otherwise a requirement stays missing after it has been satisfied on
                            // any pass that does not rebuild the mod list first
                            requirement.IsMissing = false;
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
            foreach (var (scanRoot, fileInfo) in GetDiscoverableFiles(GetScanRoots(modsFilePath), GetConfigFiles))
            {
                if (fileInfo.DirectoryName is null || (Program.settings.IgnoreHiddenFolders && ParentFolderContainsPeriod(scanRoot, fileInfo.Directory)))
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

        internal List<Config> GetPendingConfigUpdates(Profile profile, bool excludeMissingConfigs = false, bool useArchiveAsBase = false)
        {
            // Merge any existing preserved configs
            List<Config> pendingConfigUpdates = new List<Config>();
            foreach (var reference in profile.EnabledModIds)
            {
                var modId = reference.UniqueId.ToLower();
                var mod = Mods.FirstOrDefault(m => reference.Matches(m));
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
                            var currentJson = mod.Config.Data;
                            var archivedJson = JsonTools.ParseDocumentToString(profile.PreservedModConfigs[modId]);
                            if (JsonDocumentEqualityComparer.Instance.Equals(JsonDocument.Parse(mod.Config.Data), profile.PreservedModConfigs[modId]) is false)
                            {
                                // JsonTools.Merge will preserve the originalJson values, but will add new properties from archivedJson
                                string mergedJson = String.Empty;
                                if (useArchiveAsBase is false)
                                {
                                    mergedJson = JsonTools.Merge(archivedJson, currentJson, false); ;
                                }
                                else
                                {
                                    mergedJson = JsonTools.Merge(currentJson, archivedJson, false);
                                }

                                // Apply the changes to the config file
                                //Program.helper.Log($"The mod {modId} does not have its current configuration preserved\nCurrent:\n{currentJson}\nArchived:\n{archivedJson}", Helper.Status.Warning);
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

        internal async void UpdateEndorsements()
        {
            if (Nexus.Client is null)
            {
                return;
            }

            var endorsements = await Nexus.Client.GetEndorsements();
            foreach (var mod in Mods.Where(m => m.HasUpdateKeys() && endorsements.Any(e => e.Id == m.NexusModId)))
            {
                mod.IsEndorsed = endorsements.First(e => e.Id == mod.NexusModId).IsEndorsed();
            }
        }

        internal async void UpdateThumbnails()
        {
            // Get all existing thumbnails
            IEnumerable<FileInfo> nexusModThumbnails = new List<FileInfo>();
            var thumbnailDirectory = new DirectoryInfo(Pathing.GetThumbnailsPath());
            if (thumbnailDirectory.Exists)
            {
                nexusModThumbnails = thumbnailDirectory.EnumerateFiles();
            }

            var modsWithWebpages = Mods.Where(m => m.NexusModId is not null).ToList();
            foreach (var mod in modsWithWebpages)
            {
                var thumbnail = nexusModThumbnails.FirstOrDefault(t => mod.NexusModId is not null && Path.GetFileNameWithoutExtension(t.Name).Equals(mod.NexusModId.ToString(), StringComparison.OrdinalIgnoreCase));
                if (thumbnail is not null)
                {
                    mod.NexusModThumbnailPath = thumbnail.FullName;
                }
                else if (Nexus.Client is not null && mod.NexusModThumbnailPath is null)
                {
                    mod.NexusModThumbnailPath = await Nexus.Client.DownloadThumbnail((int)mod.NexusModId);
                }
            }
        }

        internal void ReadModConfigs(Profile profile)
        {
            ReadModConfigs(profile, GetPendingConfigUpdates(profile));
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
            return WriteModConfigs(profile, GetPendingConfigUpdates(profile, useArchiveAsBase: true));
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
            // Cached so the source filter does not walk the reference list once per grid row
            _activeProfile = profile;
            _activeProfileReferences = new HashSet<ModReference>(profile.EnabledModIds);

            foreach (var mod in Mods)
            {
                mod.IsEnabled = false;
                if (profile.EnabledModIds.Any(reference => reference.Matches(mod)))
                {
                    mod.IsEnabled = true;
                }

                // Set any mod notes
                mod.Note = string.Empty;
                if (profile.Notes.FirstOrDefault(data => data.UniqueId.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)) is var noteData && noteData is not null)
                {
                    mod.Note = noteData.Note;
                }
            }
            HideRequiredMods();

            RefreshModCounts();
            UpdateFilter();
        }

        /// <summary>
        /// Whether a mod belongs to what the user is currently looking at. A collection profile shows the mods it
        /// references, including any it reuses from outside its own folder and anything the user has enabled since
        /// it was applied, while a plain profile shows everything no collection owns.
        /// </summary>
        private bool PassesSourceFilter(Mod mod)
        {
            if (_modSourceFilter is ModSourceFilter.All)
            {
                return true;
            }

            if (_activeProfile is not null && _activeProfile.IsFromCollection)
            {
                return _activeProfileReferences.Contains(mod.ToReference());
            }

            return mod.IsFromCollection is false;
        }

        /// <summary>
        /// Recalculates the footer counts. These follow the source filter, otherwise the totals disagree with what
        /// the grid is showing.
        /// </summary>
        public void RefreshModCounts()
        {
            EnabledModCount = Mods.Count(m => m.IsEnabled && m.IsHidden is false && PassesSourceFilter(m));
            ActualModCount = Mods.Count(m => m.IsHidden is false && PassesSourceFilter(m));
        }

        public void ForceModState(Profile profile, List<Mod> mods, bool modEnableState = false)
        {
            foreach (var mod in Mods)
            {
                if (mods.Any(m => m.UniqueId.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)) is false)
                {
                    continue;
                }
                mod.IsEnabled = modEnableState;
            }

            // Matched on the unique ID alone, so a mod a collection and the mod folder both provide has just had
            // every copy turned on
            if (modEnableState)
            {
                ResolveEnabledDuplicates();
            }

            RefreshModCounts();
        }

        /// <summary>
        /// Turns off every other enabled copy of the given mods' unique IDs. Each enabled mod is handed to SMAPI as
        /// a junction of its own, so two folders claiming one unique ID is an error rather than a preference, and
        /// the copy the user acted on is the one that stands.
        /// </summary>
        internal void DisableDuplicatesOf(IEnumerable<Mod> mods)
        {
            foreach (var mod in mods.Where(m => m.IsEnabled).ToList())
            {
                foreach (var duplicate in Mods.Where(m => m.IsEnabled && ReferenceEquals(m, mod) is false && m.UniqueId.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    Program.helper.Log($"Disabling {duplicate.Name} ({duplicate.SourceId ?? "local"}), as another copy of {duplicate.UniqueId} has been enabled");
                    duplicate.IsEnabled = false;
                }
            }
        }

        /// <summary>
        /// Collapses every unique ID down to one enabled copy. Used by the paths that turn mods on in bulk, where
        /// there is no single mod the user acted on to keep, so the copy belonging to the active profile wins,
        /// then a loose install, then whichever was found first.
        /// </summary>
        internal void ResolveEnabledDuplicates()
        {
            var preferredSourceId = _activeProfile is not null && _activeProfile.IsFromCollection ? _activeProfile.SourceId : null;
            foreach (var group in Mods.Where(m => m.IsEnabled).GroupBy(m => m.UniqueId, StringComparer.OrdinalIgnoreCase).ToList())
            {
                var copies = group.ToList();
                if (copies.Count < 2)
                {
                    continue;
                }

                var keptCopy = copies.FirstOrDefault(m => String.Equals(m.SourceId, preferredSourceId, StringComparison.OrdinalIgnoreCase));
                if (keptCopy is null)
                {
                    keptCopy = copies.FirstOrDefault(m => m.IsFromCollection is false);
                }

                if (keptCopy is null)
                {
                    keptCopy = copies.First();
                }

                DisableDuplicatesOf(new List<Mod> { keptCopy });
            }
        }

        internal void UpdateDataGridGrouping()
        {
            if (DataView is not null)
            {
                DataGridPathGroupDescription? currentGroupingMethod = null;
                switch (Program.settings.ModGroupingMethod)
                {
                    case ModGrouping.Folder:
                        currentGroupingMethod = _modPathGrouping;
                        break;
                    case ModGrouping.FolderCondensed:
                        currentGroupingMethod = _rootPathGrouping;
                        break;
                    case ModGrouping.ContentPack:
                        currentGroupingMethod = _frameworkGrouping;
                        break;
                }

                foreach (var grouping in DataView.GroupDescriptions.ToList())
                {
                    if (grouping == currentGroupingMethod)
                    {
                        continue;
                    }

                    DataView.GroupDescriptions.Remove(grouping);
                }

                if (currentGroupingMethod is not null && DataView.GroupDescriptions.Contains(currentGroupingMethod) is false)
                {
                    DataView.GroupDescriptions.Add(currentGroupingMethod);
                }

                HandleModGroupingSorting();
            }
        }

        internal void UpdateFilter()
        {
            if (DataView is not null)
            {
                TrackEnabledModsForSourceFilter();
                UpdateDataGridGrouping();

                DataView.Filter = null;
                DataView.Filter = ModFilter;
            }
        }

        /// <summary>
        /// Folds whatever is currently enabled into the set the source filter shows. Done here rather than at each
        /// place a mod is toggled, as this runs immediately before the filter is applied and so catches every path
        /// that could have changed the enabled state. The set only grows within a session and is rebuilt from the
        /// profile whenever one is applied, which is what keeps a mod on screen after the user disables it.
        /// </summary>
        private void TrackEnabledModsForSourceFilter()
        {
            if (_activeProfile is null || _activeProfile.IsFromCollection is false)
            {
                return;
            }

            foreach (var mod in Mods.Where(m => m.IsEnabled))
            {
                _activeProfileReferences.Add(mod.ToReference());
            }
        }

        private bool ModFilter(object item)
        {
            var mod = item as Mod;
            if (mod is null)
            {
                return false;
            }

            if (mod.IsHidden)
            {
                return false;
            }

            if (PassesSourceFilter(mod) is false)
            {
                return false;
            }

            if (_disabledModFilter == DisplayFilter.ShowEnabled && !mod.IsEnabled)
            {
                return false;
            }
            else if (_disabledModFilter == DisplayFilter.ShowDisabled && mod.IsEnabled)
            {
                return false;
            }
            else if (_disabledModFilter == DisplayFilter.RequireConfig && !mod.HasConfig)
            {
                return false;
            }

            if (_showUpdatableMods && String.IsNullOrEmpty(mod.ParsedStatus))
            {
                return false;
            }

            if (String.IsNullOrEmpty(_filterText) || _columnFilter is null || !_columnFilter.Any())
            {
                return true;
            }

            if (!String.IsNullOrEmpty(_filterText) && _columnFilter.Any())
            {
                var filterTextNoWhitespace = _filterText.Replace(" ", String.Empty);
                if (_columnFilter.Contains(Program.translation.Get("ui.main_window.combobox.mod_name")) && mod.Name.Replace(" ", String.Empty).Contains(filterTextNoWhitespace, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (_columnFilter.Contains(Program.translation.Get("ui.main_window.combobox.group")))
                {
                    ModGrouping modGroupingMethod = Program.settings.ModGroupingMethod;
                    switch (Program.settings.ModGroupingMethod)
                    {
                        case ModGrouping.Folder:
                            if (mod.Path.Replace(" ", String.Empty).Contains(filterTextNoWhitespace, StringComparison.OrdinalIgnoreCase) is true)
                            {
                                return true;
                            }
                            break;
                        case ModGrouping.FolderCondensed:
                            if (mod.RootPath.Replace(" ", String.Empty).Contains(filterTextNoWhitespace, StringComparison.OrdinalIgnoreCase) is true)
                            {
                                return true;
                            }
                            break;
                        case ModGrouping.ContentPack:
                            if (mod.FrameworkID is not null && mod.FrameworkID.Replace(" ", String.Empty).Contains(filterTextNoWhitespace, StringComparison.OrdinalIgnoreCase) is true)
                            {
                                return true;
                            }
                            break;
                    }
                }
                if (_columnFilter.Contains(Program.translation.Get("ui.main_window.combobox.top_level_group")))
                {
                    ModGrouping modGroupingMethod = Program.settings.ModGroupingMethod;
                    switch (Program.settings.ModGroupingMethod)
                    {
                        case ModGrouping.Folder:
                        case ModGrouping.FolderCondensed:
                            if (mod.RootPath is not null && mod.RootPath.Replace(" ", String.Empty).Contains(filterTextNoWhitespace, StringComparison.OrdinalIgnoreCase) is true)
                            {
                                return true;
                            }
                            break;
                        case ModGrouping.ContentPack:
                            if (mod.FrameworkID is not null && mod.FrameworkID.Replace(" ", String.Empty).Contains(filterTextNoWhitespace, StringComparison.OrdinalIgnoreCase) is true)
                            {
                                return true;
                            }
                            break;
                    }
                }
                if (_columnFilter.Contains(Program.translation.Get("ui.main_window.combobox.author")) && mod.Author.Replace(" ", String.Empty).Contains(filterTextNoWhitespace, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (_columnFilter.Contains(Program.translation.Get("ui.main_window.combobox.requirements")) && ((mod.HardRequirements is not null && mod.HardRequirements.Any(r => r.Name is null || r.Name.Replace(" ", String.Empty).Contains(filterTextNoWhitespace, StringComparison.OrdinalIgnoreCase))) || (mod.MissingRequirements is not null && mod.MissingRequirements.Any(r => r.Name is null || r.Name.Replace(" ", String.Empty).Contains(filterTextNoWhitespace, StringComparison.OrdinalIgnoreCase)))))
                {
                    return true;
                }
            }

            return false;
        }

        private void DataViewSortDescription_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            HandleModGroupingSorting();
        }

        private void HandleModGroupingSorting()
        {
            switch (Program.settings.ModGroupingMethod)
            {
                case ModGrouping.None:
                    break;
                case ModGrouping.Folder:
                    if (DataView.SortDescriptions.Any(d => d.PropertyPath == nameof(Mod.Path)) is false)
                    {
                        DataView.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(Mod.Path), ListSortDirection.Ascending));
                    }
                    break;
                case ModGrouping.FolderCondensed:
                    if (DataView.SortDescriptions.Any(d => d.PropertyPath == nameof(Mod.RootPath)) is false)
                    {
                        DataView.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(Mod.RootPath), ListSortDirection.Ascending));
                    }
                    break;
                case ModGrouping.ContentPack:
                    if (DataView.SortDescriptions.Any(d => d.PropertyPath == nameof(Mod.FrameworkID)) is false)
                    {
                        DataView.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(Mod.FrameworkID), ListSortDirection.Ascending));
                    }
                    break;
            }
        }
    }
}
