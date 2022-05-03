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
using System.Threading;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus.Web;

namespace Stardrop.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly ProfileEditorViewModel _editorView;
        private DispatcherTimer _searchBoxTimer;
        private DispatcherTimer _smapiProcessTimer;
        private DispatcherTimer _nxmSentinel;

        // Tracking related
        private bool shiftPressed;
        private bool ctrlPressed;

        public MainWindow()
        {
            InitializeComponent();

            // Set the main window view
            _viewModel = new MainWindowViewModel(Pathing.defaultModPath, Program.applicationVersion);
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
            if (_viewModel.IsCheckingForUpdates is false)
            {
                _viewModel.UpdateStatusText = Program.translation.Get("ui.main_window.button.update_status.updating");
                CheckForModUpdates(_viewModel.Mods.ToList(), useCache: true);
            }
            else
            {
                CheckForModUpdates(_viewModel.Mods.ToList(), probe: true);
            }

            // Check if we have a valid Nexus Mods key
            Nexus.SetDisplayWindow(_viewModel);
            CheckForNexusConnection();

            // Start sentinel for watching NXM files
            _nxmSentinel = new DispatcherTimer();
            _nxmSentinel.Interval = new TimeSpan(TimeSpan.TicksPerMillisecond * 1000);
            _nxmSentinel.Tick += _nxmSentinelTimer_Tick;
            _nxmSentinel.Start();

            // FOOTER: "Value cannot be null. (Parameter 'path1')" error clears removing the above chunk

            // Handle buttons
            this.FindControl<Button>("minimizeButton").Click += delegate { this.WindowState = WindowState.Minimized; };
            this.FindControl<Button>("maximizeButton").Click += delegate { AdjustWindowState(); };
            this.FindControl<Button>("exitButton").Click += Exit_Click;
            this.FindControl<Button>("editProfilesButton").Click += EditProfilesButton_Click;
            this.FindControl<Button>("saveConfigsToProfile").Click += SaveConfigButton_Click;
            this.FindControl<Button>("smapiButton").Click += Smapi_Click;
            this.FindControl<CheckBox>("showUpdatableMods").Click += ShowUpdatableModsButton_Click;
            this.FindControl<Button>("nexusModsButton").Click += NexusModsButton_Click;

            // Handle filtering via textbox
            this.FindControl<TextBox>("searchBox").AddHandler(KeyUpEvent, SearchBox_KeyUp);

            // Handle filtering by searchFilterColumnBox
            var searchFilterColumnBox = this.FindControl<ComboBox>("searchFilterColumnBox");
            searchFilterColumnBox.SelectedIndex = 0;
            searchFilterColumnBox.SelectionChanged += FilterComboBox_SelectionChanged;

            var disabledModFilterColumnBox = this.FindControl<ComboBox>("disabledModFilterColumnBox");
            disabledModFilterColumnBox.SelectedIndex = 0;
            disabledModFilterColumnBox.SelectionChanged += DisabledModComboBox_SelectionChanged;

            // Have to register this even here, as MacOS doesn't detect it via axaml during build
            this.PropertyChanged += MainWindow_PropertyChanged;

            // Hook into key related events
            this.KeyDown += MainWindow_KeyDown;
            this.KeyUp += MainWindow_KeyUp;

            // Check if SMAPI should be started immediately via --start-smapi
            if (Program.onBootStartSMAPI)
            {
                StartSMAPI();
            }

#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                shiftPressed = true;
            }
            else if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                ctrlPressed = true;
            }
        }

        private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                shiftPressed = false;
            }
            else if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                ctrlPressed = false;
            }
        }

        private async void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty && (WindowState)e.OldValue == WindowState.Minimized && SMAPI.IsRunning)
            {
                var warningWindow = new WarningWindow(Program.translation.Get("ui.warning.stardrop_locked"), Program.translation.Get("internal.unlock"), true);
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
                CreateWarningWindow(Program.translation.Get("ui.warning.unable_to_locate_smapi"), Program.translation.Get("internal.ok"));
            }

            if (Program.settings.IsAssociatedWithNXM is false)
            {
                var requestWindow = new MessageWindow(Program.translation.Get("ui.message.confirm_nxm_association"));
                if (await requestWindow.ShowDialog<bool>(this))
                {
                    NXMProtocol.Register(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Stardrop.exe"));
                    Program.settings.IsAssociatedWithNXM = true;
                }
            }
            else if (String.IsNullOrEmpty(Program.nxmLink) is false)
            {
                await ProcessNXMLink(Nexus.GetKey(), new NXM() { Link = Program.nxmLink, Timestamp = DateTime.Now });
                Program.nxmLink = null;
            }
        }

        private async Task CreateWarningWindow(string warningText, string buttonText)
        {
            var warningWindow = new WarningWindow(warningText, buttonText);
            await warningWindow.ShowDialog(this);
        }

        private async void Drop(object sender, DragEventArgs e)
        {
            if (Pathing.defaultModPath is null || !Directory.Exists(Pathing.defaultModPath))
            {
                CreateWarningWindow(Program.translation.Get("ui.warning.unable_to_locate_smapi"), Program.translation.Get("internal.ok"));
                return;
            }

            if (!e.Data.Contains(DataFormats.FileNames))
            {
                return;
            }

            var addedMods = await AddMods(e.Data.GetFileNames()?.ToArray());

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

        private async void _nxmSentinelTimer_Tick(object? sender, EventArgs e)
        {
            _nxmSentinel.Stop();

            if (File.Exists(Pathing.GetLinksCachePath()) is false)
            {
                return;
            }

            try
            {
                var apiKey = Nexus.GetKey();
                using (FileStream stream = new FileStream(Pathing.GetLinksCachePath(), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    foreach (var nxmLink in await JsonSerializer.DeserializeAsync<List<NXM>>(stream, new JsonSerializerOptions { AllowTrailingCommas = true }))
                    {
                        if (await ProcessNXMLink(apiKey, nxmLink) is false)
                        {
                            break;
                        }
                    }

                    // Clear the stream and empty out the file
                    stream.SetLength(0);

                    await JsonSerializer.SerializeAsync(stream, new List<NXM>(), new JsonSerializerOptions() { WriteIndented = true });
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to process the Links.json file: {ex}");
                File.Delete(Pathing.GetLinksCachePath());
            }

            _nxmSentinel.Start();
        }

        private void ModGridMenuColumn_ChangeRequirementVisibility(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _viewModel.ShowRequirements = !_viewModel.ShowRequirements;
        }

        private void ModGridMenuRow_ChangeState(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var modGrid = this.FindControl<DataGrid>("modGrid");
            if (modGrid is null)
            {
                return;
            }

            var selectedMod = (sender as MenuItem).DataContext as Mod;
            if (selectedMod is not null)
            {
                // Add the selected mod into the selection list if shift or ctrl is held, otherwise clear the current selection
                if (!modGrid.SelectedItems.Contains(selectedMod))
                {
                    if (!(ctrlPressed || shiftPressed))
                    {
                        modGrid.SelectedItems.Clear();
                    }
                    modGrid.SelectedItems.Add(selectedMod);
                }

                // Enable / disable all selected mods based on the clicked mod
                selectedMod.IsEnabled = !selectedMod.IsEnabled;
                foreach (Mod mod in modGrid.SelectedItems)
                {
                    mod.IsEnabled = selectedMod.IsEnabled;

                    if (selectedMod.IsEnabled)
                    {
                        // Enable any existing requirements
                        EnableRequirements(mod);
                    }
                    else
                    {
                        // Disable any mods that require it requirements
                        DisableRequirements(mod);
                    }
                }
            }

            UpdateProfile(GetCurrentProfile());
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

        private void ModGridMenuRow_OpenModPage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedMod = (sender as MenuItem).DataContext as Mod;
            if (selectedMod is null)
            {
                return;
            }

            _viewModel.OpenBrowser(selectedMod.ModPageUri);
        }

        private async void ModGridMenuRow_Delete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var modGrid = this.FindControl<DataGrid>("modGrid");
            if (modGrid is null)
            {
                return;
            }

            var selectedMod = (sender as MenuItem).DataContext as Mod;
            if (selectedMod is not null)
            {
                // Add the selected mod into the selection list if shift or ctrl is held, otherwise clear the current selection
                if (!modGrid.SelectedItems.Contains(selectedMod))
                {
                    if (!(ctrlPressed || shiftPressed))
                    {
                        modGrid.SelectedItems.Clear();
                    }
                    modGrid.SelectedItems.Add(selectedMod);
                }

                // Delete all selected mods, though ask before each instance
                bool hasDeletedAMod = false;
                foreach (Mod mod in modGrid.SelectedItems)
                {
                    var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_mod_deletion"), mod.Name));
                    if (await requestWindow.ShowDialog<bool>(this))
                    {
                        // Delete old vesrion
                        var targetDirectory = new DirectoryInfo(mod.ModFileInfo.DirectoryName);
                        if (targetDirectory is not null)
                        {
                            targetDirectory.Delete(true);
                        }

                        hasDeletedAMod = true;
                    }
                }

                if (hasDeletedAMod)
                {
                    // Update the current profile
                    UpdateProfile(GetCurrentProfile());

                    // Refresh mod list
                    _viewModel.DiscoverMods(Pathing.defaultModPath);

                    // Refresh enabled mods
                    _viewModel.EnableModsByProfile(GetCurrentProfile());

                    // Refresh the update data
                    await CheckForModUpdates(_viewModel.Mods.ToList(), probe: true);
                }
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
                var searchFilterColumnBox = this.FindControl<ComboBox>("searchFilterColumnBox");
                _viewModel.ColumnFilter = (searchFilterColumnBox.SelectedItem as ComboBoxItem).Content.ToString();
            }
        }

        private void FilterComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var searchFilterColumnBox = (e.Source as ComboBox);
            _viewModel.ColumnFilter = (searchFilterColumnBox.SelectedItem as ComboBoxItem).Content.ToString();
        }

        private void DisabledModComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var disabledModFilterColumnBox = (e.Source as ComboBox);
            var filterText = (disabledModFilterColumnBox.SelectedItem as ComboBoxItem).Content.ToString();

            if (filterText == Program.translation.Get("ui.main_window.combobox.show_all_mods"))
            {
                _viewModel.DisabledModFilter = Models.Data.Enums.DisplayFilter.None;
            }
            else if (filterText == Program.translation.Get("ui.main_window.combobox.hide_enabled_mods"))
            {
                _viewModel.DisabledModFilter = Models.Data.Enums.DisplayFilter.Show;
            }
            else if (filterText == Program.translation.Get("ui.main_window.buttons.hide_disabled_mods"))
            {
                _viewModel.DisabledModFilter = Models.Data.Enums.DisplayFilter.Hide;
            }
        }

        private void ShowUpdatableModsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var showUpdatableModsCheckBox = e.Source as CheckBox;
            _viewModel.ShowUpdatableMods = (bool)showUpdatableModsCheckBox.IsChecked;
        }

        private async void ProfileComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var profile = (e.Source as ComboBox).SelectedItem as Profile;
            if (profile is null)
            {
                return;
            }

            // Verify if any unsaved config changes need to be saved
            if (Program.settings.EnableProfileSpecificModConfigs && e.RemovedItems.Count > 0 && e.RemovedItems[0] is Profile oldProfile && oldProfile is not null)
            {
                _viewModel.DiscoverConfigs(Pathing.defaultModPath, useArchive: true);
                var pendingConfigUpdates = _viewModel.GetPendingConfigUpdates(oldProfile, inverseMerge: true, excludeMissingConfigs: true);
                if (pendingConfigUpdates.Count > 0 && await new MessageWindow(String.Format(Program.translation.Get("ui.message.unsaved_config_changes"), oldProfile.Name)).ShowDialog<bool>(this))
                {
                    _viewModel.ReadModConfigs(oldProfile, pendingConfigUpdates);
                    UpdateProfile(oldProfile);
                }
            }

            // Enable the mods for the selected profile
            _viewModel.EnableModsByProfile(profile);

            // Set the configs
            if (Program.settings.EnableProfileSpecificModConfigs && _viewModel.WriteModConfigs(profile))
            {
                UpdateProfile(profile);
            }

            // Update the EnabledModCount
            _viewModel.EnabledModCount = _viewModel.Mods.Where(m => m.IsEnabled && !m.IsHidden).Count();
        }

        private async void EndorsementButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var button = e.Source as Button;
            var modGrid = this.FindControl<DataGrid>("modGrid");
            if (button is null || modGrid is null)
            {
                return;
            }

            var apiKey = Nexus.GetKey();
            if (String.IsNullOrEmpty(apiKey))
            {
                return;
            }

            // Get the mod based on the checkbox's content (which contains the UniqueId)
            var clickedMod = _viewModel.Mods.FirstOrDefault(m => m.UniqueId.Equals(button.Tag));
            if (clickedMod is null || clickedMod.NexusModId is null)
            {
                return;
            }
            int modId = (int)clickedMod.NexusModId;

            bool targetState = !clickedMod.IsEndorsed;
            var result = await Nexus.SetModEndorsement(apiKey, modId, targetState);
            if (result == EndorsementResponse.Endorsed || result == EndorsementResponse.Abstained)
            {
                _viewModel.Mods.First(m => m.NexusModId == modId).IsEndorsed = targetState;
                Program.helper.Log($"Set endorsement state ({targetState}) for mod id: {modId}");
            }
            else if (result == EndorsementResponse.IsOwnMod)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_to_endorse"), Program.translation.Get("ui.warning.mod_owned")), Program.translation.Get("internal.ok"));
            }
            else if (result == EndorsementResponse.TooSoonAfterDownload)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_to_endorse"), Program.translation.Get("ui.warning.too_soon_after_download")), Program.translation.Get("internal.ok"));
            }
            else if (result == EndorsementResponse.NotDownloadedMod)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_to_endorse"), Program.translation.Get("ui.warning.not_downloaded")), Program.translation.Get("internal.ok"));
            }
            else
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_to_endorse"), Program.translation.Get("internal.unknown")), Program.translation.Get("internal.ok"));
            }
        }

        private async void InstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var button = e.Source as Button;
            var modGrid = this.FindControl<DataGrid>("modGrid");
            if (button is null || modGrid is null)
            {
                return;
            }

            // Get the mod based on the checkbox's content (which contains the UniqueId)
            var clickedMod = _viewModel.Mods.FirstOrDefault(m => m.UniqueId.Equals(button.Tag));
            if (clickedMod is null)
            {
                return;
            }
            var apiKey = Nexus.GetKey();

            // Install the mod
            var downloadedFilePath = await InstallModViaNexus(apiKey, clickedMod);
            if (String.IsNullOrEmpty(downloadedFilePath))
            {
                return;
            }

            var addedMods = await AddMods(new string[] { downloadedFilePath });
            await CheckForModUpdates(addedMods, useCache: true, skipCacheCheck: true);
            await GetCachedModUpdates(_viewModel.Mods.ToList(), skipCacheCheck: true);

            // Delete the downloaded archived mod
            if (File.Exists(downloadedFilePath))
            {
                File.Delete(downloadedFilePath);
            }

            _viewModel.EvaluateRequirements();
            _viewModel.UpdateEndorsements(apiKey);
            _viewModel.UpdateFilter();
        }

        private void EnabledBox_Clicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var checkBox = e.Source as CheckBox;
            var modGrid = this.FindControl<DataGrid>("modGrid");
            if (checkBox is null || modGrid is null)
            {
                return;
            }

            // Get the mod based on the checkbox's content (which contains the UniqueId)
            var clickedMod = _viewModel.Mods.FirstOrDefault(m => m.UniqueId.Equals(checkBox.Content));
            if (clickedMod is not null)
            {
                // Add the selected mod into the selection list if shift or ctrl is held, otherwise clear the current selection
                if (!modGrid.SelectedItems.Contains(clickedMod))
                {
                    if (!(ctrlPressed || shiftPressed))
                    {
                        modGrid.SelectedItems.Clear();
                    }
                    modGrid.SelectedItems.Add(clickedMod);
                }

                // Enable / disable all selected mods based on the clicked mod
                foreach (Mod mod in modGrid.SelectedItems)
                {
                    mod.IsEnabled = clickedMod.IsEnabled;

                    if (clickedMod.IsEnabled)
                    {
                        // Enable any existing requirements
                        EnableRequirements(mod);
                    }
                    else
                    {
                        // Disable any mods that require it requirements
                        DisableRequirements(mod);
                    }
                }
            }

            UpdateProfile(GetCurrentProfile());
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

        private void SaveConfigButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var profileComboBox = this.FindControl<ComboBox>("profileComboBox");
            var profile = profileComboBox.SelectedItem as Profile;

            if (profile is not null)
            {
                _viewModel.DiscoverConfigs(Pathing.defaultModPath, useArchive: true);
                _viewModel.ReadModConfigs(profile, _viewModel.GetPendingConfigUpdates(profile, inverseMerge: true));
                UpdateProfile(profile);

                if (!Program.settings.EnableProfileSpecificModConfigs)
                {
                    CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.mod_config_saved_but_not_enabled"), profile.Name), Program.translation.Get("internal.ok"));
                }
            }
        }

        private async void NexusModsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleNexusConnection();
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

        private void SmapiLogFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            OpenNativeExplorer(Pathing.GetSmapiLogFolderPath());
        }

        private void SmapiLogFile_Click(object? sender, EventArgs e)
        {
            OpenNativeExplorer(Pathing.GetSmapiLogFolderPath());
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
            await HandleStardropUpdateCheck(true);
        }

        private async void StardropUpdate_Click(object? sender, EventArgs e)
        {
            await HandleStardropUpdateCheck(true);
        }

        private async void ModListRefresh_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleModListRefresh();
        }

        private async void ModListRefresh_Click(object? sender, EventArgs e)
        {
            await HandleModListRefresh();
        }

        private async void NexusModBulkInstall_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleBulkModInstall();
        }

        private async void NexusModBulkInstall_Click(object? sender, EventArgs e)
        {
            await HandleBulkModInstall();
        }

        private async void NexusConnection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleNexusConnection();
        }

        private async void NexusConnection_Click(object? sender, EventArgs e)
        {
            await HandleNexusConnection();
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
            Program.helper.Log($"Starting SMAPI at path: {Program.settings.SMAPIFolderPath}", Helper.Status.Debug);
            if (Program.settings.SMAPIFolderPath is null || !File.Exists(Pathing.GetSmapiPath()))
            {
                CreateWarningWindow(Program.translation.Get("ui.warning.unable_to_locate_smapi"), Program.translation.Get("internal.ok"));
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

            // Get the currently selected profile
            var profile = this.FindControl<ComboBox>("profileComboBox").SelectedItem as Profile;
            if (profile is null)
            {
                CreateWarningWindow(Program.translation.Get("ui.warning.unable_to_determine_profile"), Program.translation.Get("internal.ok"));
                Program.helper.Log($"Unable to determine selected profile, SMAPI will not be started!", Helper.Status.Alert);
                return;
            }

            // Update the enabled mod folder linkage
            UpdateEnabledModsFolder(profile, enabledModsPath);

            // Set the config files
            if (Program.settings.EnableProfileSpecificModConfigs)
            {
                _viewModel.WriteModConfigs(profile);
            }

            // Update the profile's configurations
            if (Program.settings.EnableProfileSpecificModConfigs)
            {
                _viewModel.DiscoverConfigs(enabledModsPath, useArchive: true);
                _viewModel.ReadModConfigs(profile, _viewModel.GetPendingConfigUpdates(profile, inverseMerge: true));
                UpdateProfile(profile);
            }

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
                CreateWarningWindow(Program.translation.Get("ui.warning.unable_to_locate_smapi"), Program.translation.Get("internal.ok"));
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filters.Add(new FileDialogFilter() { Name = "Mod Archive (*.zip, *.7z, *.rar)", Extensions = { "zip", "7z", "rar" } });
            dialog.AllowMultiple = false;

            var addedMods = await AddMods(await dialog.ShowAsync(this));

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
                await HandleModListRefresh();
            }
        }

        private async Task HandleStardropUpdateCheck(bool manualCheck = false)
        {
            SemVersion? latestVersion = null;
            bool updateAvailable = false;

            // Check if current version is the latest
            var versionToUri = await GitHub.GetLatestRelease();
            if (versionToUri is not null && SemVersion.TryParse(versionToUri?.Key.Replace("v", String.Empty), out latestVersion) && SemVersion.TryParse(_viewModel.Version.Replace("v", String.Empty), out var currentVersion) && latestVersion > currentVersion)
            {
                updateAvailable = true;
            }
            else if (versionToUri is null)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.stardrop_unable_to_find_latest"), _viewModel.Version), Program.translation.Get("internal.ok"));
                return;
            }

            // If an update is available, notify the user otherwise let them know Stardrop is up-to-date
            if (updateAvailable)
            {
                var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.stardrop_update_available"), latestVersion));
                if (await requestWindow.ShowDialog<bool>(this))
                {
                    //_viewModel.OpenBrowser("https://www.nexusmods.com/stardewvalley/mods/10455?tab=files");
                    var extractedLatestReleasePath = await GitHub.DownloadLatestRelease(versionToUri?.Value);
                    if (String.IsNullOrEmpty(extractedLatestReleasePath))
                    {
                        await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.stardrop_unable_to_download_latest"), _viewModel.Version), Program.translation.Get("internal.ok"));
                        return;
                    }
                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.stardrop_update_downloaded"), _viewModel.Version), Program.translation.Get("internal.ok"));

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // Prepare the process
                        string[] arguments = new string[] { "timeout 1", $"move \"{Path.Combine(extractedLatestReleasePath, "*")}\" .", $"move \"{Path.Combine(extractedLatestReleasePath, "Themes", "*")}\" .\\Themes", $"move \"{Path.Combine(extractedLatestReleasePath, "i18n", "*")}\" .\\i18n", $"rmdir /s /q \"{extractedLatestReleasePath}\"", $"\"{Path.Combine(Directory.GetCurrentDirectory(), "Stardrop.exe")}\"" };
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = "cmd",
                            Arguments = $"/C {string.Join(" & ", arguments)}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };

                        try
                        {
                            Program.helper.Log($"Starting update process from {_viewModel.Version} to {versionToUri?.Key}");
                            Process.Start(processInfo);
                            this.Close();
                        }
                        catch (Exception ex)
                        {
                            Program.helper.Log($"Process failed to update Stardrop using {processInfo.FileName} with arguments: {processInfo.Arguments}");
                            Program.helper.Log($"Exception for failed update process: {ex}");
                        }
                    }
                    else
                    {
                        try
                        {
                            // Move the top level files
                            var adjustedExtractedLatestReleasePath = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? extractedLatestReleasePath : Path.Combine(extractedLatestReleasePath, "Contents", "MacOS");
                            foreach (var file in new DirectoryInfo(adjustedExtractedLatestReleasePath).GetFiles())
                            {
                                file.MoveTo(Path.Combine(Directory.GetCurrentDirectory(), file.Name), true);
                            }

                            // Move the child folders / files
                            foreach (var folder in new DirectoryInfo(adjustedExtractedLatestReleasePath).GetDirectories())
                            {
                                foreach (var file in folder.GetFiles())
                                {
                                    file.MoveTo(Path.Combine(Directory.GetCurrentDirectory(), folder.Name, file.Name), true);
                                }
                            }

                            // Apply specific update logic for MacOS
                            var macInfoPath = Path.Combine(extractedLatestReleasePath, "Contents", "Info.plist");
                            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && File.Exists(macInfoPath))
                            {
                                File.Copy(macInfoPath, Path.Combine(Directory.GetCurrentDirectory(), "..", "Info.plist"), true);
                            }

                            // Delete the update package
                            Directory.Delete(extractedLatestReleasePath, true);

                            // Restart the application
                            string scriptPath = $"'{Path.Combine(Directory.GetCurrentDirectory(), RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Stardrop.sh" : "Stardrop")}'";
                            string[] arguments = new string[] { $"chmod +x {scriptPath}", $"{scriptPath}" };
                            var processInfo = new ProcessStartInfo
                            {
                                FileName = "/bin/bash",
                                Arguments = $"-c \"{string.Join(" ; ", arguments)}\"",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };

                            using (var process = Process.Start(processInfo))
                            {
                                this.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            Program.helper.Log($"Process failed to update Stardrop");
                            Program.helper.Log($"Exception for failed update process: {ex}");
                        }
                    }
                }
            }
            else if (manualCheck)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.stardrop_up_to_date"), _viewModel.Version), Program.translation.Get("internal.ok"));
            }
        }

        private async Task HandleModUpdateCheck()
        {
            if (Pathing.defaultModPath is null)
            {
                await CreateWarningWindow(Program.translation.Get("ui.warning.unable_to_locate_smapi"), Program.translation.Get("internal.ok"));
                return;
            }

            if (_viewModel.IsCheckingForUpdates is false)
            {
                await CheckForModUpdates(_viewModel.Mods.ToList());
            }
        }

        private async Task HandleBulkModStateChange(bool enableState)
        {
            var requestWindow = new MessageWindow(enableState ? Program.translation.Get("ui.message.confirm_bulk_change_mod_states_enable") : Program.translation.Get("ui.message.confirm_bulk_change_mod_states_disable"));
            if (await requestWindow.ShowDialog<bool>(this))
            {
                foreach (var mod in _viewModel.Mods.Where(m => m.IsEnabled != enableState))
                {
                    mod.IsEnabled = enableState;
                }

                UpdateProfile(GetCurrentProfile());
            }
        }

        private async Task HandleBulkModInstall()
        {
            var apiKey = Nexus.GetKey();
            if (String.IsNullOrEmpty(apiKey))
            {
                return;
            }

            if (Program.settings.NexusDetails is null || Program.settings.NexusDetails.IsPremium is false)
            {
                await CreateWarningWindow(Program.translation.Get("ui.warning.download_without_premium"), Program.translation.Get("internal.ok"));
                return;
            }

            List<string> updateFilePaths = new List<string>();
            foreach (var mod in _viewModel.Mods.Where(m => String.IsNullOrEmpty(m.InstallStatus) is false))
            {
                var downloadFilePath = await InstallModViaNexus(apiKey, mod);

                if (String.IsNullOrEmpty(downloadFilePath))
                {
                    continue;
                }
                updateFilePaths.Add(downloadFilePath);
            }

            var addedMods = await AddMods(updateFilePaths.ToArray());
            await CheckForModUpdates(addedMods, useCache: true, skipCacheCheck: true);
            await GetCachedModUpdates(_viewModel.Mods.ToList(), skipCacheCheck: true);

            // Delete the downloaded archived mods
            foreach (var filePath in updateFilePaths.Where(p => File.Exists(p)))
            {
                File.Delete(filePath);
            }

            _viewModel.EvaluateRequirements();
            _viewModel.UpdateEndorsements(apiKey);
            _viewModel.UpdateFilter();
        }

        private async Task HandleNexusConnection()
        {
            if (String.IsNullOrEmpty(Nexus.GetKey()) || File.Exists(Pathing.GetNotionCachePath()) is false)
            {
                // Display the login window
                var loginWindow = new NexusLogin(_viewModel);
                loginWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                var apiKey = await loginWindow.ShowDialog<string>(this);

                if (String.IsNullOrEmpty(apiKey))
                {
                    return;
                }

                // Attempt to validate the API key
                var isKeyValid = await Nexus.ValidateKey(apiKey);
                if (isKeyValid is false)
                {
                    // Failed to validate, warn the user
                    await CreateWarningWindow(Program.translation.Get("ui.warning.unable_to_validate_nexus_key"), Program.translation.Get("internal.ok"));
                    return;
                }

                // Store the validated key
                var obscurer = new SimpleObscure();
                Program.settings.NexusDetails.Key = SimpleObscure.Encrypt(apiKey, obscurer.Key, obscurer.Vector);

                // Cache the required data
                File.WriteAllText(Pathing.GetNotionCachePath(), JsonSerializer.Serialize(new PairedKeys { Lock = obscurer.Key, Vector = obscurer.Vector }, new JsonSerializerOptions() { WriteIndented = true }));

                // Set the status
                _viewModel.NexusStatus = Program.translation.Get("internal.connected");

                // Update any required Nexus Mods related components
                CheckForNexusConnection();

                return;
            }

            // Display information window 
            var detailsWindow = new NexusInfo(Program.settings.NexusDetails);
            detailsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (await detailsWindow.ShowDialog<bool>(this) is true)
            {
                _viewModel.NexusStatus = Program.translation.Get("internal.disconnected");
                _viewModel.ShowEndorsements = false;
                _viewModel.ShowInstalls = false;
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

            CheckForNexusConnection();
        }

        private async Task<bool> ProcessNXMLink(string? apiKey, NXM nxmLink)
        {
            if (String.IsNullOrEmpty(apiKey))
            {
                await CreateWarningWindow(Program.translation.Get("ui.message.require_nexus_login"), Program.translation.Get("internal.ok"));
                return false;
            }

            Program.helper.Log($"Processing NXM link: {nxmLink}");
            var processedDownloadLink = await Nexus.GetFileDownloadLink(apiKey, nxmLink);
            Program.helper.Log($"Processed link: {processedDownloadLink}");

            if (String.IsNullOrEmpty(processedDownloadLink))
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_to_get_download_link"), nxmLink.Link), Program.translation.Get("internal.ok"));
                return false;
            }

            // Get the mod details
            var modDetails = await Nexus.GetModDetailsViaNXM(apiKey, nxmLink);
            if (modDetails is null || String.IsNullOrEmpty(modDetails.Name))
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_to_get_mod_details"), nxmLink.Link), Program.translation.Get("internal.ok"));
                return false;
            }

            // TODO: Make it a setting to automatically accept NXM files
            var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_nxm_install"), modDetails.Name));
            if (await requestWindow.ShowDialog<bool>(this))
            {
                var downloadedFilePath = await Nexus.DownloadFileAndGetPath(processedDownloadLink, modDetails.Name);
                if (downloadedFilePath is null)
                {
                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), modDetails.Name), Program.translation.Get("internal.ok"));
                    return false;
                }

                var addedMods = await AddMods(new string[] { downloadedFilePath });
                await CheckForModUpdates(addedMods, useCache: true, skipCacheCheck: true);
                await GetCachedModUpdates(_viewModel.Mods.ToList(), skipCacheCheck: true);

                // Delete the downloaded archived mod
                if (File.Exists(downloadedFilePath))
                {
                    File.Delete(downloadedFilePath);
                }

                _viewModel.EvaluateRequirements();
                _viewModel.UpdateEndorsements(apiKey);
                _viewModel.UpdateFilter();
            }

            return true;
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

            return true;
        }

        private TimeSpan GetTimeSpanBeforeAllowedUpdate()
        {
            if (!File.Exists(Pathing.GetVersionCachePath()))
            {
                return new TimeSpan(0);
            }

            var updateCache = JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(Pathing.GetVersionCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            if (updateCache is null)
            {
                return new TimeSpan(0);
            }

            return updateCache.LastRuntime - DateTime.Now.AddSeconds(-1);
        }

        private async Task<UpdateCache?> GetCachedModUpdates(List<Mod> mods, bool skipCacheCheck = false)
        {
            int modsToUpdate = 0;
            UpdateCache? oldUpdateCache = null;

            if (File.Exists(Pathing.GetVersionCachePath()))
            {
                oldUpdateCache = JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(Pathing.GetVersionCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
                if (oldUpdateCache is not null && (skipCacheCheck || _viewModel.IsCheckingForUpdates is false))
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
                            modItem.UpdateUri = modUpdateInfo.Link;
                            modItem.SuggestedVersion = modUpdateInfo.SuggestedVersion;
                            modItem.Status = modUpdateInfo.Status;

                            modsToUpdate++;
                        }
                        if (modUpdateInfo.Status != WikiCompatibilityStatus.Unknown && modUpdateInfo.Status != WikiCompatibilityStatus.Ok)
                        {
                            modItem.UpdateUri = modUpdateInfo.Link;
                            modItem.SuggestedVersion = modUpdateInfo.SuggestedVersion;
                            modItem.Status = modUpdateInfo.Status;
                        }
                    }
                }
            }

            // Update the status to let the user know the update is finished
            _viewModel.ModsWithCachedUpdates = modsToUpdate;
            _viewModel.UpdateStatusText = Program.translation.Get("ui.main_window.button.update_status.generic");

            return oldUpdateCache;
        }

        private async Task CheckForModUpdates(List<Mod> mods, bool useCache = false, bool probe = false, bool skipCacheCheck = false)
        {
            try
            {
                // Only check if Stardrop isn't currently checking for updates
                UpdateCache? oldUpdateCache = await GetCachedModUpdates(mods, skipCacheCheck);

                // Check if this was just a probe
                if (probe || _viewModel.IsCheckingForUpdates is true)
                {
                    return;
                }
                Program.helper.Log($"Attempting to check for mod updates {(useCache ? "via cache" : "via smapi.io")}");
                _viewModel.IsCheckingForUpdates = true;

                // Close the menu, as it will remain open until the process is complete
                var mainMenu = this.FindControl<Menu>("mainMenu");
                if (mainMenu.IsOpen)
                {
                    mainMenu.Close();
                }

                // Update the status to let the user know the update is polling
                _viewModel.UpdateStatusText = Program.translation.Get("ui.main_window.button.update_status.updating");

                // Set the environment variable for the mod path
                var enabledModsPath = Path.Combine(Pathing.GetSelectedModsFolderPath());
                Environment.SetEnvironmentVariable("SMAPI_MODS_PATH", enabledModsPath);

                if (Program.settings.GameDetails is null || Program.settings.GameDetails.HasBadGameVersion() || Program.settings.GameDetails.HasSMAPIUpdated(FileVersionInfo.GetVersionInfo(Pathing.GetSmapiPath()).ProductVersion))
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
                        await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_to_locate_log"), _viewModel.Version), Program.translation.Get("internal.ok"));
                        Program.helper.Log($"Unable to locate SMAPI-latest.txt", Helper.Status.Alert);

                        _viewModel.IsCheckingForUpdates = false;
                        return;
                    }
                }

                if (Program.settings.GameDetails is null)
                {
                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_to_read_log"), _viewModel.Version), Program.translation.Get("internal.ok"));
                    Program.helper.Log($"SMAPI started but Stardrop was unable to read SMAPI-latest.txt. Mods will not be checked for updates.", Helper.Status.Alert);

                    _viewModel.IsCheckingForUpdates = false;
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
                    var updateLink = String.Empty;
                    var modPageLink = String.Empty;
                    var recommendedVersion = String.Empty;
                    var status = WikiCompatibilityStatus.Unknown;

                    // Prep the data to be checked
                    var suggestedUpdateData = modUpdateData.Where(m => modItem.UniqueId.Equals(m.Id, StringComparison.OrdinalIgnoreCase) && m.SuggestedUpdate is not null).Select(m => m.SuggestedUpdate).FirstOrDefault();
                    var metaData = modUpdateData.Where(m => modItem.UniqueId.Equals(m.Id, StringComparison.OrdinalIgnoreCase) && m.Metadata is not null).Select(m => m.Metadata).FirstOrDefault();
                    if (suggestedUpdateData is not null)
                    {
                        updateLink = suggestedUpdateData.Url;
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
                        if (metaData.CompatibilityStatus == WikiCompatibilityStatus.Unofficial && metaData.Unofficial is not null && modItem.IsModOutdated(metaData.Unofficial.Version))
                        {
                            updateLink = metaData.Unofficial.Url;
                            recommendedVersion = metaData.Unofficial.Version;

                            modsToUpdate++;
                        }
                        else if (metaData.Main is not null)
                        {
                            updateLink = metaData.Main.Url;
                            recommendedVersion = metaData.Main.Version;
                        }
                    }

                    // Check for smapi.io's suggested webpage
                    if (metaData is not null)
                    {
                        modPageLink = metaData.CustomUrl;
                        if (String.IsNullOrEmpty(modPageLink) && metaData.Main is not null)
                        {
                            modPageLink = metaData.Main.Url;
                        }
                    }

                    modItem.UpdateUri = updateLink;
                    modItem.ModPageUri = modPageLink;
                    modItem.SuggestedVersion = recommendedVersion;
                    modItem.Status = status;

                    if (!String.IsNullOrEmpty(modItem.ParsedStatus))
                    {
                        Program.helper.Log($"Update available for {modItem.UniqueId} (v{modItem.SuggestedVersion}): {modItem.UpdateUri}");
                        if (updateCache.Mods.FirstOrDefault(m => m.UniqueId.Equals(modItem.UniqueId)) is ModUpdateInfo modInfo && modInfo is not null)
                        {
                            modInfo.SuggestedVersion = recommendedVersion;
                            modInfo.Status = status;
                        }
                        else
                        {
                            updateCache.Mods.Add(new ModUpdateInfo(modItem.UniqueId, recommendedVersion, status, modItem.UpdateUri));
                        }
                    }
                }

                // Cache the update data
                if (!Directory.Exists(Pathing.GetCacheFolderPath()))
                {
                    Directory.CreateDirectory(Pathing.GetCacheFolderPath());
                }
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
                    var modUrl = String.IsNullOrEmpty(modEntry.Metadata.CustomUrl) && modEntry.Metadata.Main is not null ? modEntry.Metadata.Main.Url : modEntry.Metadata.CustomUrl; ;
                    if (modKeysCache.FirstOrDefault(m => m.UniqueId.Equals(modEntry.Id)) is ModKeyInfo keyInfo && keyInfo is not null)
                    {
                        keyInfo.Name = modEntry.Metadata.Name;
                        keyInfo.PageUrl = modUrl;
                    }
                    else
                    {
                        modKeysCache.Add(new ModKeyInfo() { Name = modEntry.Metadata.Name, UniqueId = modEntry.Id, PageUrl = modUrl });
                    }
                }

                // Cache the key data
                File.WriteAllText(Pathing.GetKeyCachePath(), JsonSerializer.Serialize(modKeysCache, new JsonSerializerOptions() { WriteIndented = true }));

                // Re-evaluate all mod requirements (to check for cached names)
                _viewModel.EvaluateRequirements();

                // Update the status to let the user know the update is finished
                _viewModel.ModsWithCachedUpdates = modsToUpdate;
                _viewModel.UpdateStatusText = Program.translation.Get("ui.main_window.button.update_status.generic");

                Program.helper.Log($"Mod update check {(useCache ? "via cache" : "via smapi.io")} completed without error");
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to get mod updates via smapi.io: {ex}", Helper.Status.Alert);
                _viewModel.UpdateStatusText = Program.translation.Get("ui.main_window.button.update_status.failed");
            }

            _viewModel.IsCheckingForUpdates = false;
        }

        private async void CheckForNexusConnection()
        {
            var apiKey = Nexus.GetKey();
            if (String.IsNullOrEmpty(apiKey) is false && await Nexus.ValidateKey(apiKey))
            {
                _viewModel.NexusStatus = Program.translation.Get("internal.connected");
                _viewModel.NexusLimits = $"(Remaining Daily Requests: {Nexus.dailyRequestsRemaining}) ";

                // Gather any endorsements
                _viewModel.UpdateEndorsements(apiKey);

                // Show endorsements
                _viewModel.ShowEndorsements = true;

                // Show Nexus mod download column, if user is premium
                _viewModel.ShowInstalls = Program.settings.NexusDetails.IsPremium;
            }
        }

        private void AdjustWindowState()
        {
            this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void EnableRequirements(Mod mod)
        {
            foreach (var requirement in mod.Requirements.Where(r => r.IsRequired))
            {
                var requiredMod = _viewModel.Mods.FirstOrDefault(m => m.UniqueId.Equals(requirement.UniqueID, StringComparison.OrdinalIgnoreCase));
                if (requiredMod is not null)
                {
                    requiredMod.IsEnabled = true;

                    // Enable the requirement's requirements
                    EnableRequirements(requiredMod);
                }
            }
        }

        private void DisableRequirements(Mod mod)
        {
            foreach (var childMod in _viewModel.Mods.Where(m => m.Requirements.Any(r => r.IsRequired && r.UniqueID.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase))))
            {
                if (childMod is not null)
                {
                    childMod.IsEnabled = false;

                    // Disable the requirement's requirements
                    DisableRequirements(childMod);
                }
            }
        }

        private Profile GetCurrentProfile()
        {
            return this.FindControl<ComboBox>("profileComboBox").SelectedItem as Profile;
        }

        private void UpdateProfile(Profile profile)
        {
            // Hide the required mods
            _viewModel.HideRequiredMods();

            // Update the profile's enabled mods
            _editorView.UpdateProfile(profile, _viewModel.Mods.Where(m => m.IsEnabled).Select(m => m.UniqueId).ToList());

            // Update the EnabledModCount
            _viewModel.EnabledModCount = _viewModel.Mods.Where(m => m.IsEnabled && !m.IsHidden).Count();
        }

        private async Task<string?> InstallModViaNexus(string apiKey, Mod mod)
        {
            if (mod is null || mod.InstallState != InstallState.Unknown)
            {
                return null;
            }

            var modId = mod.GetNexusKey();
            if (modId is null || String.IsNullOrEmpty(apiKey))
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_nexus_install"), mod.Name), Program.translation.Get("internal.ok"));
                return null;
            }
            mod.InstallState = InstallState.Downloading;

            var modFile = await Nexus.GetFileByVersion(apiKey, (int)modId, mod.SuggestedVersion);
            if (modFile is null)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), mod.Name), Program.translation.Get("internal.ok"));
                mod.InstallState = InstallState.Unknown;
                return null;
            }

            var modDownloadLink = await Nexus.GetFileDownloadLink(apiKey, (int)modId, modFile.Id);
            if (modDownloadLink is null)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), mod.Name), Program.translation.Get("internal.ok"));
                mod.InstallState = InstallState.Unknown;
                return null;
            }

            var downloadedFilePath = await Nexus.DownloadFileAndGetPath(modDownloadLink, modFile.Name);
            if (downloadedFilePath is null)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), mod.Name), Program.translation.Get("internal.ok"));
                mod.InstallState = InstallState.Unknown;
                return null;
            }
            mod.InstallState = InstallState.Installing;

            return downloadedFilePath;
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

                        // Verify the archive has a top level single folder
                        bool hasTopLevelFolder = false;
                        if (archive.Entries.Count(e => e.Key.Count(k => k == '/') == 1 && e.IsDirectory) == 1)
                        {
                            hasTopLevelFolder = true;
                        }

                        // If the archive doesn't have a manifest, warn the user
                        if (manifest is not null)
                        {
                            string installPath = Program.settings.ModInstallPath;
                            if (_viewModel.Mods.FirstOrDefault(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase)) is Mod mod && mod is not null)
                            {
                                if (!manifest.DeleteOldVersion)
                                {
                                    // TODO: Update this warning to account for the config preserving setting, if enabled
                                    var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_mod_update_method"), manifest.Name));
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
                                installPath = mod.ModFileInfo.Directory.Parent.FullName;
                            }

                            // Correct the installPath if the archive doesn't come with a top level folder
                            if (!hasTopLevelFolder)
                            {
                                installPath = Path.Combine(installPath, manifest.UniqueID);
                            }

                            foreach (var entry in archive.Entries)
                            {
                                if (entry.Key.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                // Create the default location if it doesn't exist
                                if (!Directory.Exists(installPath))
                                {
                                    Directory.CreateDirectory(installPath);
                                }
                                entry.WriteToDirectory(installPath, new ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
                            }

                            addedMods.Add(new Mod(manifest, null, manifest.UniqueID, manifest.Version, manifest.Name, manifest.Description, manifest.Author));
                        }
                        else
                        {
                            CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.no_manifest"), fileFullName), Program.translation.Get("internal.ok"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_to_load_mod"), fileFullName), Program.translation.Get("internal.ok"));
                    Program.helper.Log($"Failed to unzip the file {fileFullName} due to the following error: {ex}", Utilities.Helper.Status.Warning);
                }
            }

            // Update the current profile
            UpdateProfile(GetCurrentProfile());

            // Refresh mod list
            _viewModel.DiscoverMods(Pathing.defaultModPath);

            // Refresh enabled mods
            _viewModel.EnableModsByProfile(GetCurrentProfile());

            return addedMods;
        }

        private void CreateDirectoryJunctions(List<string> arguments)
        {
            // Prepare the process
            var processInfo = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "/bin/bash",
                Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"/C {string.Join(" & ", arguments)}" : $"-c \"{string.Join(" ; ", arguments)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            try
            {
                Program.helper.Log($"Starting process to link folders via terminal using {processInfo.FileName} and an argument length of {processInfo.Arguments.Length}");

                using (var process = Process.Start(processInfo))
                {
                    // Synchronously read the standard output / error of the spawned process.
                    var standardOutput = process.StandardOutput.ReadToEnd();
                    var errorOutput = process.StandardError.ReadToEnd();

                    Program.helper.Log($"Standard Output: {(String.IsNullOrWhiteSpace(standardOutput) ? "Empty" : String.Concat(Environment.NewLine, standardOutput))}");
                    Program.helper.Log($"Error Output: {(String.IsNullOrWhiteSpace(errorOutput) ? "Empty" : String.Concat(Environment.NewLine, errorOutput))}");

                    if (!String.IsNullOrWhiteSpace(errorOutput))
                    {
                        Program.helper.Log($"Printing full argument chain due to error output being detected: {Environment.NewLine}{processInfo.Arguments}");
                    }

                    process.WaitForExit();
                }

                Program.helper.Log($"Link process completed");
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Process failed for creating mod folder links using {processInfo.FileName} with arguments: {processInfo.Arguments}");
                Program.helper.Log($"Exception for failed mod folder link creation: {ex}");
            }
        }

        private void UpdateEnabledModsFolder(Profile profile, string enabledModsPath)
        {
            // Clear any previous linked mods
            foreach (var linkedModFolder in new DirectoryInfo(enabledModsPath).GetDirectories())
            {
                linkedModFolder.Delete(true);
            }

            string spacing = String.Concat(Environment.NewLine, "\t");
            Program.helper.Log($"Creating links for the following enabled mods from profile {profile.Name}:{spacing}{String.Join(spacing, profile.EnabledModIds)}");

            // Link the enabled mods via a chained command
            List<string> arguments = new List<string>();
            foreach (string modId in _viewModel.Mods.Where(m => m.IsEnabled).Select(m => m.UniqueId))
            {
                var mod = _viewModel.Mods.FirstOrDefault(m => m.UniqueId == modId);
                if (mod is null)
                {
                    continue;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var longPathPrefix = @"\\?\";

                    var linkPath = Path.Combine(enabledModsPath, mod.ModFileInfo.Directory.Name);
                    if (linkPath.Length >= 260)
                    {
                        linkPath = longPathPrefix + linkPath;
                    }

                    var modDirectoryName = mod.ModFileInfo.DirectoryName;
                    if (Path.Combine(enabledModsPath, mod.ModFileInfo.Directory.Name).Length >= 260)
                    {
                        modDirectoryName = longPathPrefix + modDirectoryName;
                    }

                    arguments.Add($"mklink /J \"{linkPath}\" \"{modDirectoryName}\"");
                }
                else
                {
                    var edq = "\\\""; // Escaped double quotes, to prevent issues with paths that contain single quotes
                    arguments.Add($"ln -sf {edq}{mod.ModFileInfo.DirectoryName}{edq} {edq}{Path.Combine(enabledModsPath, mod.ModFileInfo.Directory.Name)}{edq}");
                }
            }

            // Attempt to create the directory junction
            try
            {
                int maxArgumentLength = 8000;
                if (arguments.Sum(a => a.Length) + (arguments.Count * 3) >= maxArgumentLength)
                {
                    int argumentIndex = 0;
                    var segmentedArguments = new List<string>();
                    while (arguments.ElementAtOrDefault(argumentIndex) is not null)
                    {
                        if (arguments[argumentIndex].Length + segmentedArguments.Sum(a => a.Length) + (segmentedArguments.Count * 3) >= maxArgumentLength)
                        {
                            // Create the process and clear segmentedArguments
                            CreateDirectoryJunctions(segmentedArguments);
                            segmentedArguments.Clear();
                        }
                        segmentedArguments.Add(arguments[argumentIndex]);
                        argumentIndex++;

                        // Check if the next index is null, if so then push the changes
                        if (arguments.ElementAtOrDefault(argumentIndex) is null && segmentedArguments.Count > 0)
                        {
                            CreateDirectoryJunctions(segmentedArguments);
                        }
                    }
                }
                else
                {
                    CreateDirectoryJunctions(arguments);
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to link all mod folders: {Environment.NewLine}{ex}");
            }

            Program.helper.Log($"Finished creating all linked mod folders");
        }

        private void OpenNativeExplorer(string folderPath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("explorer", folderPath.Replace("&", "^&"));
                }
                else
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