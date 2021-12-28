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
using Stardrop.Utilities.External;
using Stardrop.Models.SMAPI.Web;
using Stardrop.Models.Data;
using Stardrop.Utilities;
using static Stardrop.Models.SMAPI.Web.ModEntryMetadata;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using Semver;

namespace Stardrop.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly ProfileEditorViewModel _editorView;
        private DispatcherTimer _searchBoxTimer;
        private DispatcherTimer _smapiProcessTimer;

        public MainWindow()
        {
            InitializeComponent();

            // Set the main window view
            _viewModel = new MainWindowViewModel(Pathing.defaultModPath, typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
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

            // HEADER: "Value cannot be null. (Parameter 'path1')" error clears removing the below chunk

            // Set profile list
            _editorView = new ProfileEditorViewModel(Pathing.GetProfilesFolderPath());
            var profileComboBox = this.FindControl<ComboBox>("profileComboBox");
            profileComboBox.Items = _editorView.Profiles;
            profileComboBox.SelectedIndex = 0;
            if (_editorView.Profiles.FirstOrDefault(p => p.Name == Program.settings.LastSelectedProfileName) is Profile oldProfile && oldProfile is not null)
            {
                profileComboBox.SelectedItem = oldProfile;
            }
            profileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;

            // Update selected mods
            var profile = profileComboBox.SelectedItem as Profile;
            _viewModel.EnableModsByProfile(profile);

            // Check if we have any cached updates for mods
            if (!IsUpdateCacheValid())
            {
                _viewModel.UpdateStatusText = "Updating"; // "Mods Ready to Update: Click to Refresh";
                CheckForModUpdates(_viewModel.Mods.ToList(), useCache: true);
            }
            else
            {
                CheckForModUpdates(_viewModel.Mods.ToList(), probe: true);
            }

            // FOOTER: "Value cannot be null. (Parameter 'path1')" error clears removing the above chunk

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

            // Have to register this even here, as MacOS doesn't detect it via axaml during build
            this.PropertyChanged += MainWindow_PropertyChanged;

#if DEBUG
            this.AttachDevTools();
#endif
        }

        private async void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty && (WindowState)e.OldValue == WindowState.Minimized && SMAPI.IsRunning)
            {
                var warningWindow = new WarningWindow("Stardrop is locked while the SMAPI is running. Any changes made will not reflect until SMAPI is closed.", "Unlock", true);
                warningWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                await warningWindow.ShowDialog(this);
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Program.settings.LastSelectedProfileName = GetCurrentProfile().Name;

            // Write the settings cache
            File.WriteAllText(Pathing.GetSettingsPath(), JsonSerializer.Serialize(Program.settings, new JsonSerializerOptions() { WriteIndented = true }));
        }

        private async void MainWindow_Opened(object? sender, EventArgs e)
        {
            await HandleStardropUpdateCheck();

            if (Pathing.defaultModPath is null || !Directory.Exists(Pathing.defaultModPath))
            {
                CreateWarningWindow($"Unable to locate StardewModdingAPI.exe\n\nPlease set the correct file path under\nView > Settings", "OK");
            }
        }

        private void CreateWarningWindow(string warningText, string buttonText)
        {
            var warningWindow = new WarningWindow(warningText, buttonText);
            warningWindow.ShowDialog(this);
        }

        private async void Drop(object sender, DragEventArgs e)
        {
            if (Pathing.defaultModPath is null || !Directory.Exists(Pathing.defaultModPath))
            {
                CreateWarningWindow($"Unable to locate StardewModdingAPI.exe\n\nPlease set the correct file path under\nView > Settings", "OK");
                return;
            }

            if (!e.Data.Contains(DataFormats.FileNames))
            {
                return;
            }

            var addedMods = await this.AddMods(e.Data.GetFileNames()?.ToArray());

            // TODO: Add optional setting to disable checking for updates when a new mod is installed?
            await CheckForModUpdates(addedMods, useCache: true, skipCacheCheck: true);
            await GetCachedModUpdates(_viewModel.Mods.ToList(), skipCacheCheck: true);

            _viewModel.EvaluateRequirements();

            _viewModel.DragOverColor = "#ff9f2a";
        }

        private void _smapiProcessTimer_Tick(object? sender, EventArgs e)
        {
            if (SMAPI.Process is null)
            {
                SMAPI.Process = Process.GetProcessesByName(SMAPI.GetProcessName()).FirstOrDefault();
            }
            else if (SMAPI.Process.HasExited || Process.GetProcessesByName(SMAPI.GetProcessName()).FirstOrDefault() is null)
            {
                Program.helper.Log("SMAPI has exited, restoring Stardrop", Helper.Status.Debug);

                SMAPI.Process = null;
                SMAPI.IsRunning = false;

                _viewModel.IsLocked = false;
                _smapiProcessTimer.IsEnabled = false;

                this.WindowState = WindowState.Normal;
            }
        }

        private void ModGridMenuRow_ChangeState(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedMod = (sender as MenuItem).DataContext as Mod;
            if (selectedMod is null)
            {
                return;
            }

            selectedMod.IsEnabled = !selectedMod.IsEnabled;
            this.UpdateProfile(GetCurrentProfile());
        }

        private void ModGridMenuRow_OpenFolderPath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedMod = (sender as MenuItem).DataContext as Mod;
            if (selectedMod is null)
            {
                return;
            }

            OpenNativeExplorer(selectedMod.ModFileInfo.DirectoryName);
        }

        private async void ModGridMenuRow_Delete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedMod = (sender as MenuItem).DataContext as Mod;
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

                // Refresh the update data
                await CheckForModUpdates(_viewModel.Mods.ToList(), probe: true);
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
            if (profile is null)
            {
                return;
            }

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

            // Get the mod based on the checkbox's content (which contains the UniqueId)
            var mod = _viewModel.Mods.FirstOrDefault(m => m.UniqueId.Equals(checkBox.Content));
            if (mod is not null)
            {
                if (mod.IsEnabled)
                {
                    // Enable any existing requirements
                    foreach (var requirement in mod.Requirements.Where(r => r.IsRequired))
                    {
                        var requiredMod = _viewModel.Mods.FirstOrDefault(m => m.UniqueId.Equals(requirement.UniqueID, StringComparison.OrdinalIgnoreCase));
                        if (requiredMod is not null)
                        {
                            requiredMod.IsEnabled = true;
                        }
                    }
                }
                else
                {
                    // Disable any mods that require it requirements
                    foreach (var childMod in _viewModel.Mods.Where(m => m.Requirements.Any(r => r.UniqueID.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase))))
                    {
                        if (childMod is not null)
                        {
                            childMod.IsEnabled = false;
                        }
                    }
                }
            }

            this.UpdateProfile(GetCurrentProfile());
        }

        private async void EditProfilesButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var profileComboBox = this.FindControl<ComboBox>("profileComboBox");
            var oldProfile = profileComboBox.SelectedItem as Profile;

            var editorWindow = new ProfileEditor(_editorView);
            editorWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await editorWindow.ShowDialog(this);

            // Restore the previously selected profile
            if (_editorView.Profiles.Any(p => p.Name == oldProfile.Name))
            {
                profileComboBox.SelectedItem = oldProfile;
            }
            else
            {
                profileComboBox.SelectedIndex = 0;
            }
        }

        // Menu related click events
        private void Smapi_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            StartSMAPI();
        }

        private void Smapi_Click(object? sender, EventArgs e)
        {
            StartSMAPI();
        }

        private async void AddMod_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleModAdd();
        }

        private async void AddMod_Click(object? sender, EventArgs e)
        {
            await HandleModAdd();
        }

        private async void Settings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await DisplaySettingsWindow();
        }

        private async void Settings_Click(object? sender, EventArgs e)
        {
            await DisplaySettingsWindow();
        }

        private void LogFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            OpenNativeExplorer(Pathing.GetLogFolderPath());
        }

        private void LogFile_Click(object? sender, EventArgs e)
        {
            OpenNativeExplorer(Pathing.GetLogFolderPath());
        }

        private async void ModUpdateCheck_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleModUpdateCheck();
        }

        private async void ModUpdateCheck_Click(object? sender, EventArgs e)
        {
            await HandleModUpdateCheck();
        }

        private async void StardropUpdate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleStardropUpdateCheck();
        }

        private async void StardropUpdate_Click(object? sender, EventArgs e)
        {
            await HandleStardropUpdateCheck();
        }

        private async void ModListRefresh_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleModListRefresh();
        }

        private async void ModListRefresh_Click(object? sender, EventArgs e)
        {
            await HandleModListRefresh();
        }

        private async void EnableAllMods_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleBulkModStateChange(true);
        }

        private async void EnableAllMods_Click(object? sender, EventArgs e)
        {
            await HandleBulkModStateChange(true);
        }

        private async void DisableAllMods_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleBulkModStateChange(false);
        }

        private async void DisableAllMods_Click(object? sender, EventArgs e)
        {
            await HandleBulkModStateChange(false);
        }

        private void Exit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            this.Close();
        }

        private void Exit_Click(object? sender, EventArgs e)
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

        // End of events
        private void StartSMAPI()
        {
            Program.helper.Log("Starting SMAPI", Helper.Status.Debug);
            if (Program.settings.SMAPIFolderPath is null || !File.Exists(Pathing.GetSmapiPath()))
            {
                CreateWarningWindow($"Unable to locate StardewModdingAPI\n\nPlease set the correct file path under\nView > Settings", "OK");
                if (Program.settings.SMAPIFolderPath is null)
                {
                    Program.helper.Log("No path given for StardewModdingAPI.", Helper.Status.Warning);
                }
                else
                {
                    Program.helper.Log($"Bad path given for StardewModdingAPI: {Pathing.GetSmapiPath()}", Helper.Status.Warning);
                }
                return;
            }

            // Set the environment variable for the mod path
            var enabledModsPath = Pathing.GetSelectedModsFolderPath();
            Environment.SetEnvironmentVariable("SMAPI_MODS_PATH", enabledModsPath);

            this.UpdateEnabledModsFolder(enabledModsPath);

            using (Process smapi = Process.Start(SMAPI.GetPrepareProcess(false)))
            {
                SMAPI.IsRunning = true;
                _viewModel.IsLocked = true;

                _smapiProcessTimer = new DispatcherTimer();
                _smapiProcessTimer.Interval = new TimeSpan(TimeSpan.TicksPerMillisecond * 500);
                _smapiProcessTimer.Tick += _smapiProcessTimer_Tick;
                _smapiProcessTimer.Start();

                this.WindowState = WindowState.Minimized;
            }
        }

        private async Task HandleModAdd()
        {
            if (Pathing.defaultModPath is null || !Directory.Exists(Pathing.defaultModPath))
            {
                CreateWarningWindow($"Unable to locate StardewModdingAPI.exe\n\nPlease set the correct file path under\nView > Settings", "OK");
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filters.Add(new FileDialogFilter() { Name = "Mod Archive (*.zip, *.7z, *.rar)", Extensions = { "zip", "7z", "rar" } });
            dialog.AllowMultiple = false;

            var addedMods = await this.AddMods(await dialog.ShowAsync(this));

            await CheckForModUpdates(addedMods, useCache: true, skipCacheCheck: true);
            await GetCachedModUpdates(_viewModel.Mods.ToList(), skipCacheCheck: true);

            _viewModel.EvaluateRequirements();
        }

        private async Task DisplaySettingsWindow()
        {
            var editorWindow = new SettingsWindow();
            editorWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (await editorWindow.ShowDialog<bool>(this))
            {
                _viewModel.DiscoverMods(Pathing.defaultModPath);
            }
        }

        private async Task HandleStardropUpdateCheck()
        {
            // Check if current version is the latest
            var versionToUri = await GitHub.GetLatestRelease();
            if (versionToUri is not null && SemVersion.TryParse(versionToUri?.Key.Replace("v", String.Empty), out var latestVersion) && SemVersion.TryParse(_viewModel.Version.Replace("v", String.Empty), out var currentVersion) && latestVersion > currentVersion)
            {
                var requestWindow = new MessageWindow($"An update (v{latestVersion}) is available for Stardrop.\n\nWould you like to download it now?");
                if (await requestWindow.ShowDialog<bool>(this))
                {
                    _viewModel.OpenBrowser("https://github.com/Floogen/Stardrop/releases/latest");
                }
            }
        }

        private async Task HandleModUpdateCheck()
        {
            if (Pathing.defaultModPath is null)
            {
                CreateWarningWindow($"Unable to locate StardewModdingAPI.exe\n\nPlease set the correct file path under\nView > Settings", "OK");
                return;
            }

            if (!IsUpdateCacheValid())
            {
                await CheckForModUpdates(_viewModel.Mods.ToList());
            }
            else
            {
                CreateWarningWindow($"Updates can only be requested once an hour.\n\nPlease try again in {GetMinutesBeforeAllowedUpdate()} minute(s).", "OK");
            }
        }

        private async Task HandleBulkModStateChange(bool enableState)
        {
            var requestWindow = new MessageWindow($"{(enableState ? "Enable" : "Disable")} all mods?\n\nNote: This cannot be undone.");
            if (await requestWindow.ShowDialog<bool>(this))
            {
                foreach (var mod in _viewModel.Mods.Where(m => m.IsEnabled != enableState))
                {
                    mod.IsEnabled = enableState;
                }

                this.UpdateProfile(GetCurrentProfile());
            }
        }

        private async Task HandleModListRefresh()
        {
            // Refresh mod list
            _viewModel.DiscoverMods(Pathing.defaultModPath);

            // Refresh enabled mods
            _viewModel.EnableModsByProfile(GetCurrentProfile());

            // Refresh cached mods
            await GetCachedModUpdates(_viewModel.Mods.ToList(), skipCacheCheck: true);

            _viewModel.EvaluateRequirements();
        }

        private bool IsUpdateCacheValid()
        {
            if (!File.Exists(Pathing.GetVersionCachePath()))
            {
                return false;
            }

            var updateCache = JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(Pathing.GetVersionCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            if (updateCache is null)
            {
                return false;
            }

            return updateCache.LastRuntime > DateTime.Now.AddHours(-1);
        }

        private int GetMinutesBeforeAllowedUpdate()
        {
            if (!File.Exists(Pathing.GetVersionCachePath()))
            {
                return 0;
            }

            var updateCache = JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(Pathing.GetVersionCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            if (updateCache is null)
            {
                return 0;
            }

            return (int)(updateCache.LastRuntime - DateTime.Now.AddHours(-1)).TotalMinutes;
        }

        private async Task<UpdateCache?> GetCachedModUpdates(List<Mod> mods, bool skipCacheCheck = false)
        {
            int modsToUpdate = 0;
            UpdateCache? oldUpdateCache = null;

            if (File.Exists(Pathing.GetVersionCachePath()))
            {
                oldUpdateCache = JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(Pathing.GetVersionCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
                if (oldUpdateCache is not null && (skipCacheCheck || oldUpdateCache.LastRuntime > DateTime.Now.AddHours(-1)))
                {
                    foreach (var modItem in mods)
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
                }
            }

            // Update the status to let the user know the update is finished
            _viewModel.ModsWithCachedUpdates = modsToUpdate;
            _viewModel.UpdateStatusText = $"Mods Ready to Update: {modsToUpdate}";

            return oldUpdateCache;
        }

        private async Task CheckForModUpdates(List<Mod> mods, bool useCache = false, bool probe = false, bool skipCacheCheck = false)
        {
            try
            {
                // Only check once the previous check is over an hour old
                UpdateCache? oldUpdateCache = await GetCachedModUpdates(mods, skipCacheCheck);

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

                if (Program.settings.GameDetails is null || Program.settings.GameDetails.HasSMAPIUpdated(FileVersionInfo.GetVersionInfo(Pathing.GetSmapiPath()).ProductVersion))
                {
                    var smapiLogPath = Path.Combine(Pathing.GetSmapiLogFolderPath(), "SMAPI-latest.txt");
                    if (File.Exists(smapiLogPath))
                    {
                        // Parse SMAPI's log
                        Program.helper.Log($"Grabbing game details (SMAPI / SDV versions) from SMAPI's log file.");

                        using (var fileStream = new FileStream(smapiLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(fileStream))
                        {
                            while (reader.Peek() >= 0)
                            {
                                var line = reader.ReadLine();
                                if (Program.gameDetailsPattern.IsMatch(line))
                                {
                                    var match = Program.gameDetailsPattern.Match(line);
                                    Program.settings.GameDetails = new GameDetails(match.Groups["gameVersion"].ToString(), match.Groups["smapiVersion"].ToString(), match.Groups["system"].ToString());
                                }
                            }
                        }
                    }
                    else
                    {
                        CreateWarningWindow("Unable to locate SMAPI-latest.txt! SMAPI is required to run successfully at least once for Stardrop to detect game details.", "OK");
                        Program.helper.Log($"Unable to locate SMAPI-latest.txt", Helper.Status.Alert);
                        return;
                    }
                }

                if (Program.settings.GameDetails is null)
                {
                    CreateWarningWindow($"Unable to read SMAPI's log file to grab game version.\n\nMods will not be checked for updates.", "OK");
                    Program.helper.Log($"SMAPI started but Stardrop was unable to read SMAPI-latest.txt. Mods will not be checked for updates.", Helper.Status.Alert);
                    return;
                }

                // Fetch the mods to see if there are updates available
                if (useCache && oldUpdateCache is not null)
                {
                    oldUpdateCache.LastRuntime = DateTime.Now;
                }

                int modsToUpdate = 0;
                var updateCache = useCache && oldUpdateCache is not null ? oldUpdateCache : new UpdateCache(DateTime.Now);
                var modUpdateData = await SMAPI.GetModUpdateData(Program.settings.GameDetails, mods);
                foreach (var modItem in mods)
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
                        if (updateCache.Mods.FirstOrDefault(m => m.UniqueId.Equals(modItem.UniqueId)) is ModUpdateInfo modInfo && modInfo is not null)
                        {
                            modInfo.SuggestedVersion = recommendedVersion;
                            modInfo.Status = status;
                        }
                        else
                        {
                            updateCache.Mods.Add(new ModUpdateInfo(modItem.UniqueId, recommendedVersion, status, modItem.Uri));
                        }
                    }
                }

                // Cache the update data
                File.WriteAllText(Pathing.GetVersionCachePath(), JsonSerializer.Serialize(updateCache, new JsonSerializerOptions() { WriteIndented = true }));

                // Get cached key data
                List<ModKeyInfo> modKeysCache = new List<ModKeyInfo>();
                if (File.Exists(Pathing.GetKeyCachePath()))
                {
                    modKeysCache = JsonSerializer.Deserialize<List<ModKeyInfo>>(File.ReadAllText(Pathing.GetKeyCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
                }

                // Update the cached key data
                foreach (var modEntry in modUpdateData.Where(m => m.Metadata is not null))
                {
                    if (modKeysCache.FirstOrDefault(m => m.UniqueId.Equals(modEntry.Id)) is ModKeyInfo keyInfo && keyInfo is not null)
                    {
                        keyInfo.Name = modEntry.Metadata.Name;
                    }
                    else
                    {
                        modKeysCache.Add(new ModKeyInfo() { Name = modEntry.Metadata.Name, UniqueId = modEntry.Id });
                    }
                }

                // Cache the key data
                File.WriteAllText(Pathing.GetKeyCachePath(), JsonSerializer.Serialize(modKeysCache, new JsonSerializerOptions() { WriteIndented = true }));

                // Re-evaluate all mod requirements (to check for cached names)
                _viewModel.EvaluateRequirements();

                // Update the status to let the user know the update is finished
                _viewModel.ModsWithCachedUpdates = modsToUpdate;
                _viewModel.UpdateStatusText = $"Mods Ready to Update: {modsToUpdate}";
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get mod updates via smapi.io: {ex}", Helper.Status.Alert);
                _viewModel.UpdateStatusText = $"Mod Update Check Failed";
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

        private async Task<List<Mod>> AddMods(string[]? filePaths)
        {
            var addedMods = new List<Mod>();
            if (filePaths is null)
            {
                return addedMods;
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
                                    manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream, new JsonSerializerOptions() { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true });
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

                            addedMods.Add(new Mod(manifest, null, manifest.UniqueID, manifest.Version, manifest.Name, manifest.Description, manifest.Author));
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

            return addedMods;
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

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    DirectoryLink.Create(Path.Combine(enabledModsPath, mod.ModFileInfo.Directory.Name), mod.ModFileInfo.DirectoryName, true);
                }
                else
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"ln -s '{mod.ModFileInfo.DirectoryName}' '{Path.Combine(enabledModsPath, mod.ModFileInfo.Directory.Name)}'\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    var process = Process.Start(processInfo);
                }
            }
        }

        private void OpenNativeExplorer(string folderPath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("explorer", folderPath.Replace("&", "^&"));
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", folderPath);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = folderPath,
                        CreateNoWindow = false,
                        UseShellExecute = true
                    };

                    var process = Process.Start(processInfo);
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to open the folder path ({folderPath}) due to the following exception: {ex}", Helper.Status.Alert);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}