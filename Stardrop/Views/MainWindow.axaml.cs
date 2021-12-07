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
            _viewModel = new MainWindowViewModel(Program.defaultModPath);
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
            _editorView = new ProfileEditorViewModel(Path.Combine(Program.defaultHomePath, "Profiles"));
            var profileComboBox = this.FindControl<ComboBox>("profileComboBox");
            profileComboBox.Items = _editorView.Profiles;
            profileComboBox.SelectedIndex = 0;
            profileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;

            // Update selected mods
            var profile = profileComboBox.SelectedItem as Profile;
            _viewModel.EnableModsByProfile(profile);

            // Handle buttons
            this.FindControl<Button>("minimizeButton").Click += delegate { this.WindowState = WindowState.Minimized; };
            this.FindControl<Button>("maximizeButton").Click += delegate { AdjustWindowState(); };
            this.FindControl<Button>("exitButton").Click += Exit_Click;
            this.FindControl<Button>("editProfilesButton").Click += EditProfilesButton_Click;
            this.FindControl<Button>("smapiButton").Click += SmapiButton_Click;
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

            // Export zip to the default mods folder
            foreach (string fileFullName in e.Data.GetFileNames())
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
                            if (entry.Key.Contains("manifest.json", StringComparison.OrdinalIgnoreCase))
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
                            string defaultInstallPath = Path.Combine(Program.defaultModPath, "Stardrop Installed Mods");
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

            // Refresh mod list
            _viewModel.DiscoverMods(Program.defaultModPath);

            _viewModel.DragOverColor = "#ff9f2a";
        }

        private async void SmapiButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Set the environment variable for the mod path
            var enabledModsPath = Path.Combine(Program.defaultHomePath, "Selected Mods");
            Environment.SetEnvironmentVariable("SMAPI_MODS_PATH", enabledModsPath);

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

            using (Process smapi = Process.Start(Path.Combine(Program.defaultGamePath, "StardewModdingAPI.exe")))
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
            this.UpdateCurrentProfile();
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

                // Refresh mod list
                _viewModel.DiscoverMods(Program.defaultModPath);
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

            this.UpdateCurrentProfile();
        }

        private void EditProfilesButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var editorWindow = new ProfileEditor(_editorView);
            editorWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            editorWindow.ShowDialog(this);
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

        private void AdjustWindowState()
        {
            this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void UpdateCurrentProfile()
        {
            // Update the profile's enabled mods
            var profile = this.FindControl<ComboBox>("profileComboBox").SelectedItem as Profile;
            _editorView.UpdateProfile(profile, _viewModel.Mods.Where(m => m.IsEnabled).Select(m => m.UniqueId).ToList());

            // Update the EnabledModCount
            _viewModel.EnabledModCount = _viewModel.Mods.Where(m => m.IsEnabled).Count();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
