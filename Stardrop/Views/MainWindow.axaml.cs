using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.ViewModels;
using System.IO;
using System.Linq;
using System;
using Avalonia.Input;
using Avalonia.Threading;
using System.Diagnostics;
using Stardrop.Utilities.Linkage;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Archives;
using SharpCompress.Readers;
using System.Text.Json;
using Stardrop.Models.SMAPI;
using Stardrop.Utilities.SMAPI;
using Stardrop.Models.SMAPI.Web;
using Stardrop.Models.Data;
using Stardrop.Utilities;
using static Stardrop.Models.SMAPI.Web.ModEntryMetadata;

namespace Stardrop.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly ProfileEditorViewModel _editorView;
        private DispatcherTimer _searchBoxTimer;

        public MainWindow()
        {
            InitializeComponent();

            // Set the main window view
            _viewModel = new MainWindowViewModel(Pathing.defaultModPath);
            DataContext = _viewModel;

            // Set the path according to the environmental variable SMAPI_MODS_PATH
            // SMAPI_MODS_PATH is set via the profile dropdown on the UI
            var modGrid = this.FindControl<DataGrid>("modGrid");
            modGrid.IsReadOnly = true;
            modGrid.LoadingRow += (sender, e) => { e.Row.Header = e.Row.GetIndex() + 1; };
            modGrid.Items = _viewModel.DataView;
            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, (sender, e) =>
            {
                _viewModel.DragOverColor = "#1cff96";
            });
            AddHandler(DragDrop.DragLeaveEvent, (sender, e) =>
            {
                _viewModel.DragOverColor = "#ff9f2a";
            });

            // Handle the mainMenu bar for drag and related events
            var menuBorder = this.FindControl<Border>("menuBorder");
            menuBorder.PointerPressed += MainBar_PointerPressed;
            menuBorder.DoubleTapped += MainBar_DoubleTapped;

            // Set profile list
            _editorView = new ProfileEditorViewModel(Pathing.GetProfilesFolderPath());
            var profileComboBox = this.FindControl<ComboBox>("profileComboBox");
            profileComboBox.Items = _editorView.Profiles;
            profileComboBox.SelectedIndex = 0;
            profileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;

            // Update selected mods
            var profile = profileComboBox.SelectedItem as Profile;
            _viewModel.EnableModsByProfile(profile);

            // Check if we have any cached updates for mods
            CheckForModUpdates(true);

            // Handle buttons
            this.FindControl<Button>("minimizeButton").Click += delegate { this.WindowState = WindowState.Minimized; };
            this.FindControl<Button>("maximizeButton").Click += delegate { AdjustWindowState(); };
            this.FindControl<Button>("exitButton").Click += Exit_Click;
            this.FindControl<Button>("editProfilesButton").Click += EditProfilesButton_Click;
            this.FindControl<Button>("smapiButton").Click += Smapi_Click;
            this.FindControl<CheckBox>("hideDisabledMods").Click += HideDisabledModsButton_Click;

            // Handle filtering via textbox
            this.FindControl<TextBox>("searchBox").AddHandler(KeyUpEvent, SearchBox_KeyUp);

            // Handle filtering by filterColumnBox
            var filterColumnBox = this.FindControl<ComboBox>("filterColumnBox");
            filterColumnBox.SelectedIndex = 0;
            filterColumnBox.SelectionChanged += FilterComboBox_SelectionChanged;

#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void CreateWarningWindow(string warningText, string buttonText)
        {
            var warningWindow = new WarningWindow(warningText, buttonText);
            warningWindow.ShowDialog(this);
        }

        private async void Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.Contains(DataFormats.FileNames))
            {
                return;
            }

            this.AddMods(e.Data.GetFileNames()?.ToArray());

            _viewModel.DragOverColor = "#ff9f2a";
        }

        private async void Smapi_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Set the environment variable for the mod path
            var enabledModsPath = Pathing.GetSelectedModsFolderPath();
            Environment.SetEnvironmentVariable("SMAPI_MODS_PATH", enabledModsPath);

            this.UpdateEnabledModsFolder(enabledModsPath);

            using (Process smapi = Process.Start(Pathing.GetSmapiPath()))
            {
                _viewModel.IsLocked = true;

                var warningWindow = new WarningWindow("Stardrop is locked while the SMAPI is running. Any changes made will not reflect until SMAPI is closed.", "Unlock", smapi);
                warningWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                await warningWindow.ShowDialog(this);

                await WaitForProcessToClose(smapi);
            }
        }

        private async Task WaitForProcessToClose(Process trackedProcess)
        {
            await trackedProcess.WaitForExitAsync();
            _viewModel.IsLocked = false;
        }

        private void GridMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var selectedMod = this.FindControl<DataGrid>("modGrid").SelectedItem as Mod;
            if (selectedMod is null)
            {
                return;
            }

            _viewModel.ChangeStateText = selectedMod.IsEnabled ? "Disable" : "Enable";
        }

        private void ModGridMenu_ChangeState(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedMod = this.FindControl<DataGrid>("modGrid").SelectedItem as Mod;
            if (selectedMod is null)
            {
                return;
            }

            selectedMod.IsEnabled = !selectedMod.IsEnabled;
            this.UpdateProfile(GetCurrentProfile());
        }

        private async void ModGridMenu_Delete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedMod = this.FindControl<DataGrid>("modGrid").SelectedItem as Mod;
            if (selectedMod is null)
            {
                return;
            }

            var requestWindow = new MessageWindow($"Are you sure you'd like to delete {selectedMod.Name}? This cannot be undone.");
            if (await requestWindow.ShowDialog<bool>(this))
            {
                // Delete old vesrion
                var targetDirectory = new DirectoryInfo(selectedMod.ModFileInfo.DirectoryName);
                if (targetDirectory is not null)
                {
                    targetDirectory.Delete(true);
                }

                // Update the current profile
                this.UpdateProfile(GetCurrentProfile());

                // Refresh mod list
                _viewModel.DiscoverMods(Pathing.defaultModPath);

                // Refresh enabled mods
                _viewModel.EnableModsByProfile(GetCurrentProfile());
            }
        }

        private void SearchBox_KeyUp(object? sender, KeyEventArgs e)
        {
            if (_searchBoxTimer is null)
            {
                _searchBoxTimer = new DispatcherTimer();
                _searchBoxTimer.Interval = new TimeSpan(TimeSpan.TicksPerMillisecond / 2);
                _searchBoxTimer.Tick += SearchBoxTimer_Tick;
                _searchBoxTimer.Start();
            }
        }

        private void SearchBoxTimer_Tick(object? sender, EventArgs e)
        {
            var filterText = this.FindControl<TextBox>("searchBox").Text;
            if (_viewModel.FilterText == filterText)
            {
                return;
            }
            _viewModel.FilterText = filterText;

            // Ensure the ColumnFilter is set
            if (String.IsNullOrEmpty(_viewModel.ColumnFilter))
            {
                var filterColumnBox = this.FindControl<ComboBox>("filterColumnBox");
                _viewModel.ColumnFilter = (filterColumnBox.SelectedItem as ComboBoxItem).Content.ToString();
            }
        }

        private void FilterComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var filterColumnBox = (e.Source as ComboBox);
            _viewModel.ColumnFilter = (filterColumnBox.SelectedItem as ComboBoxItem).Content.ToString();
        }

        private void HideDisabledModsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var hideDisabledModsCheckBox = e.Source as CheckBox;
            _viewModel.HideDisabledMods = (bool)hideDisabledModsCheckBox.IsChecked;
        }

        private void ProfileComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var profile = (e.Source as ComboBox).SelectedItem as Profile;
            _viewModel.EnableModsByProfile(profile);

            // Update the EnabledModCount
            _viewModel.EnabledModCount = _viewModel.Mods.Where(m => m.IsEnabled).Count();
        }

        private void EnabledBox_Clicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var checkBox = e.Source as CheckBox;
            if (checkBox is null)
            {
                return;
            }

            this.UpdateProfile(GetCurrentProfile());
        }

        private void EditProfilesButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var editorWindow = new ProfileEditor(_editorView);
            editorWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            editorWindow.ShowDialog(this);
        }

        private async void AddMod_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filters.Add(new FileDialogFilter() { Name = "Mod Archive (*.zip, *.7z, *.rar)", Extensions = { "zip", "7z", "rar" } });
            dialog.AllowMultiple = false;

            this.AddMods(await dialog.ShowAsync(this));
        }

        private async void ModUpdateCheck_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            CheckForModUpdates(false);
        }

        private async void EnableAllMods_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var requestWindow = new MessageWindow($"Enable all mods?\n\nNote: This cannot be undone.");
            if (await requestWindow.ShowDialog<bool>(this))
            {
                foreach (var mod in _viewModel.Mods.Where(m => !m.IsEnabled))
                {
                    mod.IsEnabled = true;
                }

                this.UpdateProfile(GetCurrentProfile());
            }
        }

        private async void DisableAllMods_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var requestWindow = new MessageWindow($"Disable all mods?\n\nNote: This cannot be undone.");
            if (await requestWindow.ShowDialog<bool>(this))
            {
                foreach (var mod in _viewModel.Mods.Where(m => m.IsEnabled))
                {
                    mod.IsEnabled = false;
                }

                this.UpdateProfile(GetCurrentProfile());
            }
        }

        private void Exit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainBar_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var menu = this.FindControl<Menu>("mainMenu");
            if (!menu.IsPointerOver && !e.Handled)
            {
                AdjustWindowState();
            }
        }

        private void MainBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            var menu = this.FindControl<Menu>("mainMenu");
            if (e.Pointer.IsPrimary && !menu.IsOpen && !e.Handled)
            {
                this.BeginMoveDrag(e);
            }
        }

        private async void CheckForModUpdates(bool probe)
        {
            int modsToUpdate = 0;

            // Only check once the previous check is over an hour old
            if (File.Exists(Pathing.GetVersionCachePath()))
            {
                var oldUpdateCache = JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(Pathing.GetVersionCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
                if (oldUpdateCache is not null && oldUpdateCache.LastRuntime > DateTime.Now.AddHours(-1))
                {
                    foreach (var modItem in _viewModel.Mods)
                    {
                        var modUpdateInfo = oldUpdateCache.Mods.FirstOrDefault(m => m.UniqueId.Equals(modItem.UniqueId));
                        if (modUpdateInfo is null)
                        {
                            continue;
                        }

                        if (modItem.IsModOutdated(modUpdateInfo.SuggestedVersion))
                        {
                            modItem.Uri = modUpdateInfo.Link;
                            modItem.SuggestedVersion = modUpdateInfo.SuggestedVersion;
                            modItem.Status = modUpdateInfo.Status;

                            modsToUpdate++;
                        }
                        if (modUpdateInfo.Status != WikiCompatibilityStatus.Unknown && modUpdateInfo.Status != WikiCompatibilityStatus.Ok)
                        {
                            modItem.Uri = modUpdateInfo.Link;
                            modItem.SuggestedVersion = modUpdateInfo.SuggestedVersion;
                            modItem.Status = modUpdateInfo.Status;
                        }
                    }

                    // Update the status to let the user know the update is finished
                    _viewModel.UpdateStatusText = $"Mods Ready to Update: {modsToUpdate}";
                    return;
                }
            }

            // Check if this was just a probe
            if (probe)
            {
                return;
            }

            // Close the menu, as it will remain open until the process is complete
            var mainMenu = this.FindControl<Menu>("mainMenu");
            if (mainMenu.IsOpen)
            {
                mainMenu.Close();
            }

            // Update the status to let the user know the update is polling
            _viewModel.UpdateStatusText = "Updating...";

            // Set the environment variable for the mod path
            var enabledModsPath = Path.Combine(Pathing.GetSelectedModsFolderPath());
            Environment.SetEnvironmentVariable("SMAPI_MODS_PATH", enabledModsPath);

            this.UpdateEnabledModsFolder(enabledModsPath);

            using (Process smapi = Process.Start(SMAPI.GetPrepareProcess(true)))
            {
                if (smapi is null)
                {
                    // TODO: Log failure here
                    return;
                }

                FileSystemWatcher observer = new FileSystemWatcher(Pathing.smapiLogPath) { Filter = "*.txt", EnableRaisingEvents = true, NotifyFilter = NotifyFilters.Size };
                var result = observer.WaitForChanged(WatcherChangeTypes.Changed, 60000);

                // Kill SMAPI
                smapi.Kill();

                // Check if our observer timed out
                FileInfo smapiLog = new FileInfo(Path.Combine(Pathing.smapiLogPath, result.Name));
                if (result.TimedOut || smapiLog is null)
                {
                    // TODO: Notify user that we were unable to check SMAPI's log
                    return;
                }

                // Parse SMAPI's log
                GameDetails? gameDetails = null;
                using (var fileStream = new FileStream(smapiLog.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fileStream))
                {
                    while (reader.Peek() >= 0)
                    {
                        var line = reader.ReadLine();
                        if (Program.gameDetailsPattern.IsMatch(line))
                        {
                            var match = Program.gameDetailsPattern.Match(line);
                            gameDetails = new GameDetails(match.Groups["gameVersion"].ToString(), match.Groups["smapiVersion"].ToString(), match.Groups["system"].ToString());
                        }
                    }
                }

                if (gameDetails is null)
                {
                    // TODO: Notify user that we were unable to parse SMAPI's log
                    return;
                }

                // Fetch the mods to see if there are updates available
                var updateCache = new UpdateCache(DateTime.Now);
                var modUpdateData = await SMAPI.GetModUpdateData(gameDetails, _viewModel.Mods.ToList());
                foreach (var modItem in _viewModel.Mods)
                {
                    var link = String.Empty;
                    var recommendedVersion = String.Empty;
                    var status = WikiCompatibilityStatus.Unknown;

                    // Prep the data to be checked
                    var suggestedUpdateData = modUpdateData.Where(m => modItem.UniqueId.Equals(m.Id, StringComparison.OrdinalIgnoreCase) && m.SuggestedUpdate is not null).Select(m => m.SuggestedUpdate).FirstOrDefault();
                    var metaData = modUpdateData.Where(m => modItem.UniqueId.Equals(m.Id, StringComparison.OrdinalIgnoreCase) && m.Metadata is not null).Select(m => m.Metadata).FirstOrDefault();
                    if (suggestedUpdateData is not null)
                    {
                        link = suggestedUpdateData.Url;
                        if (metaData is not null && metaData.CompatibilityStatus != WikiCompatibilityStatus.Ok)
                        {
                            status = metaData.CompatibilityStatus;
                        }
                        recommendedVersion = suggestedUpdateData.Version;

                        modsToUpdate++;
                    }
                    else if (metaData is not null && metaData.CompatibilityStatus != WikiCompatibilityStatus.Unknown && metaData.CompatibilityStatus != ModEntryMetadata.WikiCompatibilityStatus.Ok)
                    {
                        status = metaData.CompatibilityStatus;
                        if (metaData.CompatibilityStatus == WikiCompatibilityStatus.Unofficial && metaData.Unofficial is not null)
                        {
                            link = metaData.Unofficial.Url;
                            recommendedVersion = metaData.Unofficial.Version;

                            modsToUpdate++;
                        }
                        else if (metaData.Main is not null)
                        {
                            link = metaData.Main.Url;
                            recommendedVersion = metaData.Main.Version;
                        }
                    }

                    modItem.Uri = link;
                    modItem.SuggestedVersion = recommendedVersion;
                    modItem.Status = status;
                    if (!String.IsNullOrEmpty(modItem.ParsedStatus))
                    {
                        updateCache.Mods.Add(new ModUpdateInfo(modItem.UniqueId, recommendedVersion, status, modItem.Uri));
                    }
                }

                // Cache the update data
                Directory.CreateDirectory(Pathing.GetCacheFolderPath());
                File.WriteAllText(Pathing.GetVersionCachePath(), JsonSerializer.Serialize(updateCache, new JsonSerializerOptions() { WriteIndented = true }));

                // Update the status to let the user know the update is finished
                _viewModel.UpdateStatusText = $"Mods Ready to Update: {modsToUpdate}";
            }
        }

        private void AdjustWindowState()
        {
            this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private Profile GetCurrentProfile()
        {
            return this.FindControl<ComboBox>("profileComboBox").SelectedItem as Profile;
        }

        private void UpdateProfile(Profile profile)
        {
            // Update the profile's enabled mods
            _editorView.UpdateProfile(profile, _viewModel.Mods.Where(m => m.IsEnabled).Select(m => m.UniqueId).ToList());

            // Update the EnabledModCount
            _viewModel.EnabledModCount = _viewModel.Mods.Where(m => m.IsEnabled).Count();
        }

        private async void AddMods(string[]? filePaths)
        {
            if (filePaths is null)
            {
                return;
            }

            // Export zip to the default mods folder
            foreach (string fileFullName in filePaths)
            {
                try
                {
                    // Extract the archive data
                    using (var archive = ArchiveFactory.Open(fileFullName))
                    {
                        // Verify the zip file has a manifest
                        Manifest? manifest = null;
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.Key.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            else if (entry.Key.Contains("manifest.json", StringComparison.OrdinalIgnoreCase))
                            {
                                using (Stream stream = entry.OpenEntryStream())
                                {
                                    manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream);
                                }
                            }
                        }

                        // If the archive doesn't have a manifest, warn the user
                        if (manifest is not null)
                        {
                            string defaultInstallPath = Path.Combine(Pathing.defaultModPath, "Stardrop Installed Mods");
                            if (_viewModel.Mods.FirstOrDefault(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase)) is Mod mod && mod is not null)
                            {
                                if (!manifest.DeleteOldVersion)
                                {
                                    var requestWindow = new MessageWindow($"An previous version of {manifest.Name} has been detected. Would you like to clear the previous install?\n\nNote: Clearing previous versions is usually recommended, however any config files will be lost.");
                                    if (await requestWindow.ShowDialog<bool>(this))
                                    {
                                        // Delete old vesrion
                                        var targetDirectory = new DirectoryInfo(mod.ModFileInfo.DirectoryName);
                                        if (targetDirectory is not null)
                                        {
                                            targetDirectory.Delete(true);
                                        }
                                    }
                                }

                                defaultInstallPath = mod.ModFileInfo.Directory.Parent.FullName;
                            }
                            foreach (var entry in archive.Entries)
                            {
                                if (entry.Key.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                entry.WriteToDirectory(defaultInstallPath, new ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
                            }
                        }
                        else
                        {
                            CreateWarningWindow($"No manifest.json found in \"{fileFullName}\"", "OK");
                        }
                    }
                }
                catch (Exception ex)
                {
                    CreateWarningWindow($"Unable to load the file located at \"{fileFullName}\".\n\nSee log file for more information.", "OK");
                    Program.helper.Log($"Failed to unzip the file {fileFullName} due to the following error: {ex}", Utilities.Helper.Status.Warning);
                }
            }

            // Update the current profile
            this.UpdateProfile(GetCurrentProfile());

            // Refresh mod list
            _viewModel.DiscoverMods(Pathing.defaultModPath);

            // Refresh enabled mods
            _viewModel.EnableModsByProfile(GetCurrentProfile());
        }

        private void UpdateEnabledModsFolder(string enabledModsPath)
        {
            // Clear any previous linked mods
            foreach (var linkedModFolder in new DirectoryInfo(enabledModsPath).GetDirectories())
            {
                linkedModFolder.Delete(true);
            }

            // Link the currently enabled mods
            var profile = this.FindControl<ComboBox>("profileComboBox").SelectedItem as Profile;
            foreach (string modId in profile.EnabledModIds)
            {
                var mod = _viewModel.Mods.FirstOrDefault(m => m.UniqueId == modId);
                if (mod is null)
                {
                    continue;
                }
                DirectoryLink.Create(Path.Combine(enabledModsPath, mod.ModFileInfo.Directory.Name), mod.ModFileInfo.DirectoryName, true);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}