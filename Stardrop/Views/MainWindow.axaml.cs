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
            var mainMenu = this.FindControl<Menu>("mainMenu");
            mainMenu.PointerPressed += MainMenu_PointerPressed;
            mainMenu.DoubleTapped += MainMenu_DoubleTapped;

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
            this.FindControl<Button>("exitButton").Click += ExitButton_Click;
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
            warningWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            warningWindow.ShowDialog(this);
        }

        private void Drop(object sender, DragEventArgs e)
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
                    var archive = ArchiveFactory.Open(fileFullName);

                    // Verify the zip file has a manifest
                    var hasManifest = false;
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Key.Contains("manifest.json", StringComparison.OrdinalIgnoreCase))
                        {
                            hasManifest = true;
                        }
                    }

                    // If the archive doesn't have a manifest, warn the user
                    if (hasManifest)
                    {
                        foreach (var entry in archive.Entries)
                        {
                            entry.WriteToDirectory(Path.Combine(Program.defaultModPath, "Stardrop Installed Mods"), new ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
                        }
                    }
                    else
                    {
                        CreateWarningWindow($"No manifest.json found in \"{fileFullName}\"", "OK");
                    }
                }
                catch (Exception ex)
                {
                    CreateWarningWindow($"Unable to load the file located at \"{fileFullName}\". See log file for more information.", "OK");
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
                var mod = _viewModel.Mods.First(m => m.UniqueId == modId);
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

            // Update the profile's enabled mods
            var profile = this.FindControl<ComboBox>("profileComboBox").SelectedItem as Profile;
            _editorView.UpdateProfile(profile, _viewModel.Mods.Where(m => m.IsEnabled).Select(m => m.UniqueId).ToList());

            // Update the EnabledModCount
            _viewModel.EnabledModCount = _viewModel.Mods.Where(m => m.IsEnabled).Count();
        }

        private void EditProfilesButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var editorWindow = new ProfileEditor(_editorView);
            editorWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            editorWindow.ShowDialog(this);
        }

        private void ExitButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainMenu_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!e.Handled)
            {
                AdjustWindowState();
            }
        }

        private void MainMenu_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.Pointer.IsPrimary && !e.Handled)
            {
                this.BeginMoveDrag(e);
            }
        }

        private void AdjustWindowState()
        {
            this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
