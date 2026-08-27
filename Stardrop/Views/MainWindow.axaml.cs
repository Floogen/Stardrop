using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using Semver;
using SharpCompress.Archives;
using SharpCompress.Common;
using Stardrop.Models;
using Stardrop.Models.Data;
using Stardrop.Models.Data.Enums;
using Stardrop.Models.Nexus.Web;
using Stardrop.Models.SMAPI;
using Stardrop.Models.SMAPI.Web;
using Stardrop.Utilities;
using Stardrop.Utilities.Extension;
using Stardrop.Utilities.External;
using Stardrop.Utilities.Internal;
using Stardrop.ViewModels;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static Stardrop.Models.SMAPI.Web.ModEntryMetadata;

namespace Stardrop.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly ProfileEditorViewModel _editorView;
        private DispatcherTimer _searchBoxTimer;
        private DispatcherTimer _smapiProcessTimer;
        private DispatcherTimer _lockSentinel;
        private DispatcherTimer _nxmSentinel;

        // Tracking related
        private bool _shiftPressed;
        private bool _ctrlPressed;

        private string _lockReason;
        private CancellationTokenSource? _lockCancellationSource;
        /// <summary>The window the lock state opened, so that unlocking closes that one rather than every dialog the main window owns</summary>
        private WarningWindow? _lockWindow;
        /// <summary>The collections window while it is open, which is the one window an nxm link is not made to wait on</summary>
        private CollectionsWindow? _collectionsWindow;

        /// <summary>Whether a tick is still working through the links an earlier one read out of the cache file</summary>
        private bool _isProcessingNXMLinks;
        /// <summary>Keeps the sentinel from writing a line a second for as long as the links are being held back</summary>
        private bool _hasLoggedNXMHold;

        // Session related
        private LastSessionData _lastSessionDate;

        public MainWindow()
        {
            InitializeComponent();

            // Set the main window view
            _viewModel = new MainWindowViewModel(Pathing.defaultModPath, Program.ApplicationVersion);
            DataContext = _viewModel;

            // Set the path according to the environmental variable SMAPI_MODS_PATH
            // SMAPI_MODS_PATH is set via the profile dropdown on the UI
            var modGrid = this.FindControl<DataGrid>("modGrid");
            modGrid.IsReadOnly = true;
            modGrid.LoadingRow += (sender, e) => { e.Row.Header = e.Row.GetIndex() + 1; };
            modGrid.Items = _viewModel.DataView;
            modGrid.LoadingRowGroup += ModGrid_LoadingRowGroup;
            modGrid.ColumnReordered += (sender, e) => { _viewModel.SetColumnOrder(modGrid); };
            modGrid.AddHandler(InputElement.LostFocusEvent, OnGridLostFocus, RoutingStrategies.Bubble);

            var dragOverBorder = this.FindControl<Border>("dragOverBorder");
            dragOverBorder.AddHandler(InputElement.PointerPressedEvent, OnGridBackgroundPressed, RoutingStrategies.Tunnel);

            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, (sender, e) =>
            {
                _viewModel.DragOverColor = "#1cff96";
            });
            AddHandler(DragDrop.DragLeaveEvent, (sender, e) =>
            {
                _viewModel.DragOverColor = "#ff9f2a";
            });

            // Get the local data
            ClientData localDataCache = new ClientData();
            if (File.Exists(Pathing.GetDataCachePath()))
            {
                try
                {
                    localDataCache = JsonSerializer.Deserialize<ClientData>(File.ReadAllText(Pathing.GetDataCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
                }
                catch
                {
                    localDataCache = new ClientData();
                }
            }

            // Set the application's position and size
            if (localDataCache.LastSessionData is not null)
            {
                _lastSessionDate = localDataCache.LastSessionData;
            }

            // Sets the grid's column visibility, based on previous session
            if (localDataCache.ColumnActiveStates is not null)
            {
                var gridColumnContextMenu = this.FindControl<ContextMenu>("gridColumnContextMenu");
                foreach (MenuItem column in gridColumnContextMenu.Items)
                {
                    string columnName = (string)column.Header;
                    if (localDataCache.ColumnActiveStates.ContainsKey(columnName))
                    {
                        _viewModel.SetColumnVisibility(column, modGrid, localDataCache.ColumnActiveStates[columnName]);
                    }
                }
            }

            // Sets the grid's column order, based on previous session
            if (localDataCache.ColumnOrder is not null)
            {
                foreach (var column in modGrid.Columns)
                {
                    var columnKey = ColumnExtensions.GetKey(column);
                    if (string.IsNullOrEmpty(columnKey))
                    {
                        Program.helper.Log($"Failed to reorder column {column.Header.ToString()}: it lacks an ext:ColumnExtensions.Key value in the XAML.");
                        continue;
                    }
                    else if (localDataCache.ColumnOrder.ContainsKey(columnKey) is false)
                    {
                        continue;
                    }

                    column.DisplayIndex = localDataCache.ColumnOrder[columnKey];
                }
            }
            // Handle the mainMenu bar for drag and related events
            var menuBorder = this.FindControl<Border>("menuBorder");
            menuBorder.PointerPressed += MainBar_PointerPressed;
            menuBorder.DoubleTapped += MainBar_DoubleTapped;

            // Set profile list
            _editorView = new ProfileEditorViewModel(Pathing.GetProfilesFolderPath(), _viewModel.Mods.ToList());
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

            // Start sentinel for watching NXM files
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) is false)
            {
                _nxmSentinel = new DispatcherTimer();
                _nxmSentinel.Interval = new TimeSpan(TimeSpan.TicksPerMillisecond * 1000);
                _nxmSentinel.Tick += _nxmSentinelTimer_Tick;
                _nxmSentinel.Start();
            }

            // Start sentinel for watching for lock state
            _lockSentinel = new DispatcherTimer();
            _lockSentinel.Interval = new TimeSpan(TimeSpan.TicksPerMillisecond * 100);
            _lockSentinel.Tick += _lockSentinelTimer_Tick;
            _lockSentinel.Start();

            // FOOTER: "Value cannot be null. (Parameter 'path1')" error clears removing the above chunk

            // Handle buttons
            this.FindControl<Button>("minimizeButton").Click += delegate { this.WindowState = WindowState.Minimized; };
            this.FindControl<Button>("maximizeButton").Click += delegate { AdjustWindowState(); };
            this.FindControl<Button>("exitButton").Click += Exit_Click;
            this.FindControl<Button>("editProfilesButton").Click += EditProfilesButton_Click;
            this.FindControl<Button>("saveConfigsToProfile").Click += SaveConfigButton_Click;
            this.FindControl<Button>("saveProfileChanges").Click += SaveProfileChanges_Click;
            this.FindControl<Button>("smapiButton").Click += Smapi_Click;
            this.FindControl<CheckBox>("showUpdatableMods").Click += ShowUpdatableModsButton_Click;
            this.FindControl<CheckBox>("showAllModSources").Click += ShowAllModSourcesButton_Click;
            this.FindControl<Button>("nexusModsButton").Click += NexusModsButton_Click;
            this.FindControl<Button>("modGroupStateButton").Click += ModGroupStateButton;

            // Handle filtering via textbox
            this.FindControl<TextBox>("searchBox").AddHandler(KeyUpEvent, SearchBox_KeyUp);

            // Handle filtering by searchFilterColumnBox
            var searchFilterColumnBox = this.FindControl<ListBox>("searchFilterColumnBox");
            searchFilterColumnBox.SelectionChanged += FilterListBox_SelectionChanged;
            searchFilterColumnBox.SelectedItem = searchFilterColumnBox.Items.Cast<ListBoxItem>().First(c => c.Content.ToString() == Program.translation.Get("ui.main_window.combobox.mod_name"));

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

            Program.helper.Log($"Initialization complete!");

#if DEBUG
            this.AttachDevTools();
#endif

            if (Design.IsDesignMode)
            {
                var testManifest = new Manifest() { Author = "Author", Name = "Test Mod", UniqueID = "Stardrop.TestMod", Version = "1.0.0" };
                _viewModel.Mods.Add(new Mod(testManifest));
            }
        }

        private void OnGridBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            // Doing this to force the Note textbox to lose focus (and therefore save) when clicking elsewhere
            if (e.Source is not TextBox textBox || !textBox.Classes.Contains("notes"))
            {
                // Move focus to the grid itself, pulling it off the TextBox
                this.FindControl<DataGrid>("modGrid").Focus();
            }
        }

        private void OnGridLostFocus(object? sender, RoutedEventArgs e)
        {
            // Save Note when textbox loses focus
            if (e.Source is TextBox textBox && textBox.Classes.Contains("notes"))
            {
                if (Program.settings.ShouldAutomaticallySaveProfileChanges)
                {
                    UpdateProfile(GetCurrentProfile());
                }
                else
                {
                    _viewModel.ShowSaveProfileChanges = true;
                }
            }
        }

        private void ModGrid_LoadingRowGroup(object? sender, DataGridRowGroupHeaderEventArgs e)
        {
            _viewModel.ModGroupsStateButtonText = Program.translation.Get("ui.main_window.buttons.mod_groups_state.collapse");
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _shiftPressed = true;
            }
            else if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                _ctrlPressed = true;
            }
            else
            {
                // Only grab search box if not currently typing in notes
                var focusedElement = FocusManager.Instance?.Current;
                if (focusedElement is not null && (focusedElement is not TextBox textBox || !textBox.Classes.Contains("notes")))
                {
                    var searchBox = this.FindControl<TextBox>("searchBox");
                    searchBox.Focus();
                    SearchBox_KeyUp(sender, e);
                }
            }
        }

        private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _shiftPressed = false;
            }
            else if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                _ctrlPressed = false;
            }
        }

        private async void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (this.IsVisible is false)
            {
                return;
            }

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

            // Cache the local data with latest values
            if (File.Exists(Pathing.GetDataCachePath()))
            {
                ClientData localDataCache = JsonSerializer.Deserialize<ClientData>(File.ReadAllText(Pathing.GetDataCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });

                if (localDataCache is not null)
                {
                    localDataCache.LastSessionData = new LastSessionData()
                    {
                        Height = this.Height,
                        Width = this.Width,
                        PositionX = this.Position.X,
                        PositionY = this.Position.Y
                    };
                }

                File.WriteAllText(Pathing.GetDataCachePath(), JsonSerializer.Serialize(localDataCache, new JsonSerializerOptions() { WriteIndented = true }));
            }
        }

        private async void MainWindow_Opened(object? sender, EventArgs e)
        {
            if (_lastSessionDate is not null)
            {
                try
                {
                    Program.helper.Log($"Setting window size according to settings: {_lastSessionDate.Width}x{_lastSessionDate.Height} ({_lastSessionDate.PositionX}, {_lastSessionDate.PositionY})");

                    this.Width = _lastSessionDate.Width;
                    this.Height = _lastSessionDate.Height;

                    var screen = this.Screens.ScreenFromBounds(new PixelRect(_lastSessionDate.PositionX, _lastSessionDate.PositionY, (int)_lastSessionDate.Width, (int)_lastSessionDate.Height));
                    if (screen is not null)
                    {
                        this.Position = new PixelPoint(_lastSessionDate.PositionX, _lastSessionDate.PositionY);
                        this.WindowStartupLocation = WindowStartupLocation.Manual;
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to restore window settings!");
                }
            }

            // Check for Stardrop updates
            await HandleStardropUpdateCheck();

            // Check if Stardrop should display the changelog (if it has updated since last boot)
            if (String.IsNullOrEmpty(Program.settings.Version))
            {
                Program.settings.Version = _viewModel.Version.Replace("v", String.Empty);
            }
            else if (SemVersion.TryParse(Program.settings.Version, SemVersionStyles.Any, out var cachedVersion) && SemVersion.TryParse(_viewModel.Version.Replace("v", String.Empty), SemVersionStyles.Any, out var currentVersion) && cachedVersion.CompareSortOrderTo(currentVersion) < 0)
            {
                // Display message with link to release notes
                var requestWindow = new MessageWindow(Program.translation.Get("ui.message.stardrop_update_complete"));
                if (await requestWindow.ShowDialog<bool>(this))
                {
                    Toolkit.OpenBrowser("https://github.com/Floogen/Stardrop/releases/latest");
                }

                Program.settings.Version = _viewModel.Version.Replace("v", String.Empty);
            }

            if (Pathing.defaultGamePath is null || File.Exists(Pathing.GetSmapiPath()) is false)
            {
                await DisplayInvalidSMAPIWarning();
            }
            else
            {
                await HandleSMAPIUpdateCheck(false);
            }

            // Register a handler to watch whenever the Nexus client changes, so setpu and teardown get handled automatically
            Nexus.ClientChanged += NexusClientChanged;

            // Set up the Nexus Mods connection and attempt to register for the NXM URI protocol
            await CheckForNexusConnection();

            if (String.IsNullOrEmpty(Program.nxmLink) is false)
            {
                await ProcessNXMLink(new NXM() { Link = Program.nxmLink, Timestamp = DateTime.Now });
                Program.nxmLink = null;
            }

            // Set up handler for listening to download count
            SetupDownloadCountListener();
        }

        /// <summary>
        /// Opens a warning window. Pass a wider windowWidth for messages carrying lists rather than a single line,
        /// which also switches the text to left aligned. Pass enableHyperlinks for messages that may hold web
        /// addresses, such as anything sourced from a collection's curator.
        /// </summary>
        private async Task CreateWarningWindow(string warningText, string buttonText, double? windowWidth = null, bool enableHyperlinks = false)
        {
            var warningWindow = new WarningWindow(warningText, buttonText, windowWidth, enableHyperlinks);
            KeepDialogAboveSiblings(warningWindow);

            await warningWindow.ShowDialog(this);
        }

        private async void Drop(object sender, DragEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(Pathing.defaultModPath) || Directory.Exists(Pathing.defaultModPath) is false)
            {
                await DisplayInvalidSMAPIWarning();
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
            _viewModel.UpdateEndorsements();
            _viewModel.UpdateFilter();

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
            if (File.Exists(Pathing.GetLinksCachePath()) is false)
            {
                return;
            }

            // Handling a link locks the main window, and a lock arriving under a dialog would close it out from
            // underneath whoever is waiting on it. The links stay in the cache file until the way is clear rather
            // than being read out and dropped, as a collection's summary is exactly the window a user is looking at
            // while they fetch the downloads it lists
            if (_isProcessingNXMLinks || _viewModel.IsLocked || IsBlockedByOpenWindow())
            {
                if (_hasLoggedNXMHold is false)
                {
                    Program.helper.Log($"Holding the pending NXM link(s), as the main window is busy or has a window open over it", Helper.Status.Debug);
                    _hasLoggedNXMHold = true;
                }

                return;
            }

            _hasLoggedNXMHold = false;
            _isProcessingNXMLinks = true;

            try
            {
                var nxmLinks = new List<NXM>();

                // Gather the NXM links, then clear the file
                using (FileStream stream = new FileStream(Pathing.GetLinksCachePath(), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var pendingLinks = await JsonSerializer.DeserializeAsync<List<NXM>>(stream, new JsonSerializerOptions { AllowTrailingCommas = true });
                    if (pendingLinks is not null)
                    {
                        foreach (var nxmLink in pendingLinks)
                        {
                            nxmLinks.Add(nxmLink);
                        }
                    }

                    // Clear the stream and empty out the file
                    stream.SetLength(0);

                    await JsonSerializer.SerializeAsync(stream, new List<NXM>(), new JsonSerializerOptions() { WriteIndented = true });
                }

                // Process each link
                foreach (var nxmLink in nxmLinks)
                {
                    // Only a blocked link stops the run. A single mod failing or being backed out of says nothing
                    // about the ones queued behind it, which the user asked for just as deliberately
                    if (await ProcessNXMLink(nxmLink) is NXMLinkResult.Blocked)
                    {
                        break;
                    }
                }
            }
            catch (IOException ex)
            {
                Program.helper.Log($"Unable to access the Links.json file");
                return;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to process the Links.json file: {ex}");

                try
                {
                    if (File.Exists(Pathing.GetLinksCachePath()))
                    {
                        File.Delete(Pathing.GetLinksCachePath());
                    }
                }
                catch (IOException ioEx)
                {
                    Program.helper.Log($"Failed to delete the Links.json file: {ioEx}");
                }
            }
            finally
            {
                _isProcessingNXMLinks = false;
            }
        }

        private async void _lockSentinelTimer_Tick(object? sender, EventArgs e)
        {
            if (_lockWindow is not null || this.OwnedWindows.Any(w => w is WarningWindow) || _viewModel.IsLocked is false || String.IsNullOrEmpty(_lockReason))
            {
                return;
            }

            Program.helper.Log($"Detected lock state request ({_lockReason}): Locking main window!");

            var warningWindow = new WarningWindow(_lockReason, _viewModel, cancellationSource: _lockCancellationSource);
            warningWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            KeepDialogAboveSiblings(warningWindow);

            // Held onto so that SetLockState and UpdateLockWindow have this window to work with, rather than
            // reaching for whichever one the main window happens to own at the time
            _lockWindow = warningWindow;

            try
            {
                await warningWindow.ShowDialog(this);
            }
            finally
            {
                // Only when it is still the current one, since SetLockState may already have replaced it
                if (ReferenceEquals(_lockWindow, warningWindow))
                {
                    _lockWindow = null;
                }
            }
        }

        private void ModGridMenuRow_ChangeState(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var modGrid = this.FindControl<DataGrid>("modGrid");
            if (modGrid is null)
            {
                return;
            }
            var selectedMod = (sender as MenuItem).DataContext as Mod;
            if (selectedMod is null)
            {
                return;
            }
            // Add the selected mod into the selection list if shift or ctrl is held, otherwise clear the current
            //  selection.
            if (!modGrid.SelectedItems.Contains(selectedMod))
            {
                if (!(_ctrlPressed || _shiftPressed))
                {
                    modGrid.SelectedItems.Clear();
                }
                modGrid.SelectedItems.Add(selectedMod);
            }

            EnableDisableSelectedMods(modGrid, selectedMod);
        }

        /// <summary>
        /// Enable/Disable all mods for the mod group(s) of the selected mod(s).
        /// </summary>
        ///
        /// <param name="sender">
        /// The currently selected mod of the whole mod group whose mods to enable/disable.
        /// </param>
        private void ModGridMenuRow_ChangeWholeModGroupState(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var modGrid = this.FindControl<DataGrid>("modGrid");
            if (modGrid is null)
            {
                return;
            }
            var selectedMod = (sender as MenuItem)?.DataContext as Mod;
            if (selectedMod is null)
            {
                return;
            }

            // Select all mods of the mod group(s) whose mod 'selectedMod' is currently selected or whose mods have 
            //  been already selected previously.
            var originallySelectedModPaths = new List<string>();
            foreach (Mod mod in modGrid.SelectedItems)
            {
                originallySelectedModPaths.Add(mod.Path);
            }
            if (originallySelectedModPaths.Count is 0)
            {
                originallySelectedModPaths.Add(selectedMod.Path);
            }
            foreach (Mod mod in modGrid.Items)
            {
                if (originallySelectedModPaths.Contains(mod.Path))
                {
                    modGrid.SelectedItems.Add(mod);
                }
            }

            EnableDisableSelectedMods(modGrid, selectedMod);
        }

        /// <summary>
        /// Enable/Disable selected mods.
        /// </summary>
        ///
        /// <param name="selectedMod">The currently selected which the enabling/disabling is performed on.</param>
        private void EnableDisableSelectedMods(DataGrid? modGrid, Mod? selectedMod)
        {
            if (selectedMod is null || modGrid is null)
            {
                return;
            }
            // Enable / disable all selected mods based on the currently selected mod.
            selectedMod.IsEnabled = !selectedMod.IsEnabled;
            foreach (Mod mod in modGrid.SelectedItems)
            {
                mod.IsEnabled = selectedMod.IsEnabled;
                if (selectedMod.IsEnabled)
                {
                    EnableRequirements(mod);
                }
                else
                {
                    DisableRequirements(mod);
                }
            }

            if (Program.settings.ShouldAutomaticallySaveProfileChanges)
            {
                UpdateProfile(GetCurrentProfile());
            }
            else
            {
                _viewModel.ShowSaveProfileChanges = true;
            }
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

            Toolkit.OpenBrowser(selectedMod.ModPageUri);
        }

        private void ModGridMenuRow_ShowWholeModGroup(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedMod = (sender as MenuItem)?.DataContext as Mod;
            if (selectedMod is null)
            {
                return;
            }

            var searchFilterColumnBox = this.FindControl<ListBox>("searchFilterColumnBox");
            searchFilterColumnBox.SelectedItem = searchFilterColumnBox.Items.Cast<ListBoxItem>().First(c => c.Content.ToString() == Program.translation.Get("ui.main_window.combobox.group"));

            var filterText = string.Empty;
            switch (Program.settings.ModGroupingMethod)
            {
                case ModGrouping.None:
                    break;
                case ModGrouping.Folder:
                    filterText = selectedMod.Path;
                    break;
                case ModGrouping.FolderCondensed:
                    filterText = selectedMod.RootPath;
                    break;
                case ModGrouping.ContentPack:
                    filterText = selectedMod.FrameworkID;
                    break;
            }

            this.FindControl<TextBox>("searchBox").Text = filterText;
            _viewModel.FilterText = filterText;
        }

        private void ModGridMenuRow_ShowAuthorsMods(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedMod = (sender as MenuItem)?.DataContext as Mod;
            if (selectedMod is null)
            {
                return;
            }

            var searchFilterColumnBox = this.FindControl<ListBox>("searchFilterColumnBox");
            searchFilterColumnBox.SelectedItem = searchFilterColumnBox.Items.Cast<ListBoxItem>().First(c => c.Content.ToString() == Program.translation.Get("ui.main_window.combobox.author"));

            this.FindControl<TextBox>("searchBox").Text = selectedMod.Author;
            _viewModel.FilterText = selectedMod.Author;
        }

        private void ModGridMenuRow_ClearFilter(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedMod = (sender as MenuItem)?.DataContext as Mod;
            if (selectedMod is null)
            {
                return;
            }

            this.FindControl<TextBox>("searchBox").Text = String.Empty;
            _viewModel.FilterText = String.Empty;
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
                    if (!(_ctrlPressed || _shiftPressed))
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
                        if (TryDeleteMod(mod) is false)
                        {
                            await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_to_delete"), mod.Name), Program.translation.Get("internal.ok"));
                        }
                        else
                        {
                            hasDeletedAMod = true;
                        }
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
            if (_viewModel.ColumnFilter is null || _viewModel.ColumnFilter.Any() is false)
            {
                var searchFilterColumnBox = this.FindControl<ListBox>("searchFilterColumnBox");
                _viewModel.ColumnFilter = searchFilterColumnBox.SelectedItems.Cast<ListBoxItem>().Select(i => i.Content.ToString()).ToList();
            }
        }

        private void FilterListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var searchFilterColumnBox = (e.Source as ListBox);
            _viewModel.ColumnFilter = searchFilterColumnBox.SelectedItems.Cast<ListBoxItem>().Select(i => i.Content.ToString()).ToList();

            int selectedItemCount = searchFilterColumnBox.SelectedItems.Count;
            this.FindControl<Button>("searchFilterColumnButton").Content = selectedItemCount > 0 ? String.Format(Program.translation.Get("ui.main_window.buttons.active_search_filters"), selectedItemCount) : Program.translation.Get("ui.main_window.buttons.no_search_filters");
        }

        private void DisabledModComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var disabledModFilterColumnBox = (e.Source as ComboBox);
            var filterText = (disabledModFilterColumnBox.SelectedItem as ComboBoxItem).Content.ToString();

            if (filterText == Program.translation.Get("ui.main_window.combobox.show_all_mods"))
            {
                _viewModel.DisabledModFilter = Models.Data.Enums.DisplayFilter.None;
            }
            else if (filterText == Program.translation.Get("ui.main_window.combobox.show_mods_with_configs"))
            {
                _viewModel.DisabledModFilter = Models.Data.Enums.DisplayFilter.RequireConfig;
            }
            else if (filterText == Program.translation.Get("ui.main_window.combobox.show_enabled_mods"))
            {
                _viewModel.DisabledModFilter = Models.Data.Enums.DisplayFilter.ShowEnabled;
            }
            else if (filterText == Program.translation.Get("ui.main_window.combobox.show_disabled_mods"))
            {
                _viewModel.DisabledModFilter = Models.Data.Enums.DisplayFilter.ShowDisabled;
            }
        }

        private void ShowUpdatableModsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var showUpdatableModsCheckBox = e.Source as CheckBox;
            _viewModel.ShowUpdatableMods = (bool)showUpdatableModsCheckBox.IsChecked;
        }

        private void ShowAllModSourcesButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var showAllModSourcesCheckBox = e.Source as CheckBox;
            if (showAllModSourcesCheckBox is null)
            {
                return;
            }

            _viewModel.ModSourceFilter = showAllModSourcesCheckBox.IsChecked is true ? ModSourceFilter.All : ModSourceFilter.ActiveProfile;
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
                var pendingConfigUpdates = _viewModel.GetPendingConfigUpdates(oldProfile, excludeMissingConfigs: true);
                foreach (var pendingConfigUpdate in pendingConfigUpdates)
                {
                    Program.helper.Log($"The mod {pendingConfigUpdate.UniqueId} for profile \"{oldProfile.Name}\" does not have its current configuration preserved ->\nModified configuration:\n{pendingConfigUpdate.Data}", Helper.Status.Warning);
                }

                if (pendingConfigUpdates.Count > 0 && await new MessageWindow(String.Format(Program.translation.Get("ui.message.unsaved_config_changes"), oldProfile.Name)).ShowDialog<bool>(this))
                {
                    _viewModel.ReadModConfigs(oldProfile, pendingConfigUpdates);
                    UpdateProfile(oldProfile);
                }
            }

            // Enable the mods for the selected profile
            _viewModel.EnableModsByProfile(profile);

            _viewModel.RefreshModCounts();

            Program.settings.ShouldWriteToModConfigs = true;
        }

        private async void EndorsementButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var button = e.Source as Button;
            var modGrid = this.FindControl<DataGrid>("modGrid");
            if (button is null || modGrid is null)
            {
                return;
            }

            if (Nexus.Client is null)
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
            var result = await Nexus.Client.SetModEndorsement(modId, targetState);
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

            // Take the mod from the row's own data context, as a unique ID lookup can land on a collection copy
            var clickedMod = button.DataContext as Mod;
            if (clickedMod is null)
            {
                return;
            }

            // Install the mod
            var downloadedFilePath = await InstallModViaNexus(clickedMod);
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
            _viewModel.UpdateEndorsements();
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

            // Taken from the row the checkbox belongs to rather than looked up by unique ID. A collection can pin
            // a mod the user also has installed loosely, so that ID matches more than one mod and the lookup was
            // free to return the copy that was not clicked
            var clickedMod = checkBox.DataContext as Mod;
            if (clickedMod is not null)
            {
                // Add the selected mod into the selection list if shift or ctrl is held, otherwise clear the current selection
                if (!modGrid.SelectedItems.Contains(clickedMod))
                {
                    if (!(_ctrlPressed || _shiftPressed))
                    {
                        modGrid.SelectedItems.Clear();
                    }

                    // Guarded, as SelectedItems is validated against the filtered view rather than the mod list and
                    // throws for anything the current filter is hiding
                    if (_viewModel.DataView.Contains(clickedMod))
                    {
                        modGrid.SelectedItems.Add(clickedMod);
                    }
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

                // Turning a copy on stands the others down rather than sitting alongside them, as only one folder
                // per unique ID can be linked into the mods folder
                if (clickedMod.IsEnabled)
                {
                    _viewModel.DisableDuplicatesOf(modGrid.SelectedItems.OfType<Mod>().ToList());
                }
            }

            if (Program.settings.ShouldAutomaticallySaveProfileChanges)
            {
                UpdateProfile(GetCurrentProfile());
            }
            else
            {
                _viewModel.ShowSaveProfileChanges = true;
            }
        }

        private async void EditProfilesButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var profileComboBox = this.FindControl<ComboBox>("profileComboBox");
            var oldProfile = profileComboBox.SelectedItem as Profile;

            var editorWindow = new ProfileEditor(_editorView, _viewModel.DirectModInstallAsync, HandleModListRefresh);
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
                _viewModel.ReadModConfigs(profile, _viewModel.GetPendingConfigUpdates(profile));
                UpdateProfile(profile);

                if (!Program.settings.EnableProfileSpecificModConfigs)
                {
                    CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.mod_config_saved_but_not_enabled"), profile.Name), Program.translation.Get("internal.ok"));
                }

                Program.settings.ShouldWriteToModConfigs = true;
            }
        }

        private void ModGroupStateButton(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            bool areModGroupsExpanded = _viewModel.ModGroupsStateButtonText == Program.translation.Get("ui.main_window.buttons.mod_groups_state.collapse");

            var modGrid = this.FindControl<DataGrid>("modGrid");
            foreach (DataGridCollectionViewGroup group in _viewModel.DataView.Groups.Where(g => g is DataGridCollectionViewGroup))
            {
                if (group is null)
                {
                    continue;
                }

                try
                {
                    if (areModGroupsExpanded)
                    {
                        modGrid.CollapseRowGroup(group, true);
                    }
                    else
                    {
                        modGrid.ExpandRowGroup(group, true);
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to change group collapse / expand state: {ex}");
                }
            }

            _viewModel.ModGroupsStateButtonText = !areModGroupsExpanded ? Program.translation.Get("ui.main_window.buttons.mod_groups_state.collapse") : Program.translation.Get("ui.main_window.buttons.mod_groups_state.expand");
        }

        private async void NexusModsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleNexusConnection();
        }

        // Menu related click events
        private async void SaveProfileChanges_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await SaveChanges();
        }

        private async void SaveProfileChanges_Click(object? sender, EventArgs e)
        {
            await SaveChanges();
        }

        private async void Smapi_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await StartSMAPI();
        }

        private async void Smapi_Click(object? sender, EventArgs e)
        {
            await StartSMAPI();
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

        private async void Collections_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await DisplayCollectionsWindow();
        }

        private async void Collections_Click(object? sender, EventArgs e)
        {
            await DisplayCollectionsWindow();
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

        private async void SMAPIUpdate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleSMAPIUpdateCheck(true);
        }

        private async void SMAPIUpdate_Click(object? sender, EventArgs e)
        {
            await HandleSMAPIUpdateCheck(true);
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

        private async void NexusModBulkInstallEnabledOnly_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await HandleBulkModInstall(enabledModsOnly: true);
        }

        private async void NexusModBulkInstallEnabledOnly_Click(object? sender, EventArgs e)
        {
            await HandleBulkModInstall(enabledModsOnly: true);
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
        private async Task SaveChanges()
        {
            UpdateProfile(GetCurrentProfile());

            _viewModel.ShowSaveProfileChanges = false;
        }

        private async Task StartSMAPI()
        {
            await SaveChanges();

            Program.helper.Log($"Starting SMAPI at path: {Program.settings.SMAPIFolderPath}", Helper.Status.Debug);
            if (await ValidateSMAPIPath() is false)
            {
                return;
            }

            // Set the environment variable for the mod path
            var enabledModsPath = Pathing.GetSelectedModsFolderPath();
            Environment.SetEnvironmentVariable("SMAPI_MODS_PATH", enabledModsPath);

            // Get the currently selected profile
            var profile = this.FindControl<ComboBox>("profileComboBox").SelectedItem as Profile;
            if (profile is null)
            {
                await CreateWarningWindow(Program.translation.Get("ui.warning.unable_to_determine_profile"), Program.translation.Get("internal.ok"));
                Program.helper.Log($"Unable to determine selected profile, SMAPI will not be started!", Helper.Status.Alert);
                return;
            }

            // Update the enabled mod folder linkage
            UpdateEnabledModsFolder(profile, enabledModsPath);

            // Update the profile's configurations
            if (Program.settings.EnableProfileSpecificModConfigs && Program.settings.ShouldWriteToModConfigs)
            {
                // Set the config files
                _viewModel.WriteModConfigs(profile);

                Program.settings.ShouldWriteToModConfigs = false;

                // Write the settings cache
                File.WriteAllText(Pathing.GetSettingsPath(), JsonSerializer.Serialize(Program.settings, new JsonSerializerOptions() { WriteIndented = true }));
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
                await DisplayInvalidSMAPIWarning();
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filters.Add(new FileDialogFilter() { Name = "Mod Archive (*.zip, *.7z, *.rar)", Extensions = { "zip", "7z", "rar" } });
            dialog.AllowMultiple = true;

            var addedMods = await AddMods(await dialog.ShowAsync(this));

            await CheckForModUpdates(addedMods, useCache: true, skipCacheCheck: true);
            await GetCachedModUpdates(_viewModel.Mods.ToList(), skipCacheCheck: true);

            _viewModel.EvaluateRequirements();
            _viewModel.UpdateEndorsements();
            _viewModel.UpdateFilter();
        }

        private async Task DisplaySettingsWindow()
        {
            Program.helper.Log($"Opening settings window");

            var editorWindow = new SettingsWindow(this.Height);
            editorWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (await editorWindow.ShowDialog<bool>(this))
            {
                await HandleModListRefresh();

                _viewModel.ShowSaveProfileChanges = !Program.settings.ShouldAutomaticallySaveProfileChanges;
                _viewModel.AreModGroupsEnabled = Program.settings.ModGroupingMethod != ModGrouping.None;
                _viewModel.ShowModThumbnails = Program.settings.ShowModThumbnails;
            }
        }

        /// <summary>
        /// Opens the collections window. It reads the cached records itself, so the only things handed to it are the
        /// profile list, since removing a collection has to deal with the profile that was generated for it, and a
        /// callback for putting the grid back in order afterwards.
        /// </summary>
        private async Task DisplayCollectionsWindow()
        {
            Program.helper.Log($"Opening collections window");

            var collectionsWindow = new CollectionsWindow(_editorView, HandleCollectionRemoved, HandleCollectionFileDrop);
            collectionsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Held so that an arriving nxm link can tell this window apart from a dialog waiting on an answer, and
            // so that anything installed while it is open can put it back in step
            _collectionsWindow = collectionsWindow;

            try
            {
                await collectionsWindow.ShowDialog(this);
            }
            finally
            {
                _collectionsWindow = null;
            }
        }

        /// <summary>
        /// Rebuilds the grid after a collection is removed. Its mod folder went with it, so the list has to be
        /// discovered again before any profile is applied against it, and the selection has to fall back where the
        /// profile that was selected is one of the things that just went away.
        /// </summary>
        private void HandleCollectionRemoved()
        {
            var profileComboBox = this.FindControl<ComboBox>("profileComboBox");
            var selectedProfile = profileComboBox.SelectedItem as Profile;

            _viewModel.DiscoverMods(Pathing.defaultModPath);

            // Applied by hand where the profile survived, as it is the same instance and reassigning it would raise
            // nothing, leaving every freshly discovered mod disabled
            if (selectedProfile is not null && _editorView.Profiles.Contains(selectedProfile))
            {
                _viewModel.EnableModsByProfile(selectedProfile);
            }
            else
            {
                profileComboBox.SelectedIndex = 0;
            }

            _viewModel.RefreshModCounts();
            _viewModel.UpdateFilter();
        }

        private async Task HandleStardropUpdateCheck(bool manualCheck = false)
        {
            SemVersion? latestVersion = null;
            bool updateAvailable = false;

            // Check if current version is the latest
            var versionToUri = await GitHub.GetLatestStardropRelease();
            if (versionToUri is not null && SemVersion.TryParse(versionToUri?.Key.Replace("v", String.Empty), out latestVersion) && SemVersion.TryParse(_viewModel.Version.Replace("v", String.Empty), out var currentVersion) && latestVersion.CompareSortOrderTo(currentVersion) > 0)
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
                    SetLockState(true, Program.translation.Get("ui.warning.stardrop_downloading"));
                    var extractedLatestReleasePath = await GitHub.DownloadLatestStardropRelease(versionToUri?.Value);
                    if (String.IsNullOrEmpty(extractedLatestReleasePath))
                    {
                        await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.stardrop_unable_to_download_latest"), _viewModel.Version), Program.translation.Get("internal.ok"));
                        SetLockState(false);
                        return;
                    }
                    SetLockState(false);

                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.stardrop_update_downloaded"), _viewModel.Version), Program.translation.Get("internal.ok"));
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // Prepare the process
                        string[] arguments = new string[] { "timeout 1", $"move \"{Path.Combine(extractedLatestReleasePath, "*")}\" .", $"robocopy \"{Path.Combine(extractedLatestReleasePath, "Themes")}\" Themes /E /MOVE", $"move \"{Path.Combine(extractedLatestReleasePath, "i18n", "*")}\" .\\i18n", $"rmdir /s /q \"{extractedLatestReleasePath}\"", $"\"{Path.Combine(Directory.GetCurrentDirectory(), "Stardrop.exe")}\"" };
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
                                FileName = "/usr/bin/env",
                                Arguments = $"bash -c \"{string.Join(" ; ", arguments)}\"",
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

        private async Task HandleSMAPIUpdateCheck(bool manualCheck = false)
        {
            // Handle failure gracefully with a warning.
            if (await ValidateSMAPIPath() is false)
            {
                return;
            }

            // Check for SMAPI updates
            var currentSmapiVersion = SMAPI.GetVersion();
            if (currentSmapiVersion is not null && Program.settings.GameDetails is not null)
            {
                Program.settings.GameDetails.SmapiVersion = currentSmapiVersion.ToString();
                _viewModel.SmapiVersion = Program.settings.GameDetails.SmapiVersion;
            }

            KeyValuePair<string, string>? latestSmapiToUri = await GitHub.GetLatestSMAPIRelease();
            if (latestSmapiToUri is not null && SemVersion.TryParse(latestSmapiToUri?.Key, SemVersionStyles.Any, out var latestVersion) && currentSmapiVersion is not null && latestVersion.CompareSortOrderTo(currentSmapiVersion) > 0)
            {
                var confirmationWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.SMAPI_update_available"), latestVersion));
                if (await confirmationWindow.ShowDialog<bool>(this) is false)
                {
                    Program.helper.Log("Player opted to not install the latest version of SMAPI");
                    return;
                }

                SetLockState(true, Program.translation.Get("ui.warning.SMAPI_downloading"));
                var extractedLatestReleasePath = await GitHub.DownloadLatestSMAPIRelease(latestSmapiToUri?.Value);
                if (String.IsNullOrEmpty(extractedLatestReleasePath))
                {
                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.SMAPI_unable_to_download_latest"), _viewModel.Version), Program.translation.Get("internal.ok"));
                    SetLockState(false);
                    return;
                }

                // Get the install.dat archive
                var subFolderName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "linux";
                var filePath = Path.Combine(extractedLatestReleasePath, "internal", subFolderName, "install.dat");

                // Verify it exists
                if (File.Exists(filePath) is false)
                {
                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.SMAPI_unable_to_download_latest"), _viewModel.Version), Program.translation.Get("internal.ok"));
                    SetLockState(false);
                    return;
                }

                // Attempt to install
                SetLockState(true, Program.translation.Get("ui.warning.SMAPI_installing"));
                try
                {
                    // Extract the items from internal/OS_TYPE/install.dat to Pathing.defaultGamePath
                    ArchiveFactory.WriteToDirectory(filePath, Pathing.defaultGamePath, new ExtractionOptions() { Overwrite = true, ExtractFullPath = true });

                    // Create a copy of Stardew Valley.deps.json with the name of StardewModdingAPI.deps.json
                    if (File.Exists(Path.Combine(Pathing.defaultGamePath, "Stardew Valley.deps.json")))
                    {
                        File.Copy(Path.Combine(Pathing.defaultGamePath, "Stardew Valley.deps.json"), Path.Combine(Pathing.defaultGamePath, "StardewModdingAPI.deps.json"), true);
                    }

                    // For Linux / macOS, follow the steps defined here: https://github.com/Pathoschild/SMAPI/blob/c1342bd4cd6b75b24d11275bdd73ebf893f916ea/src/SMAPI.Installer/InteractiveInstaller.cs#L398
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) is false && File.Exists(Path.Combine(Pathing.defaultGamePath, "StardewModdingAPI")))
                    {
                        File.Move(Path.Combine(Pathing.defaultGamePath, "unix-launcher.sh"), Path.Combine(Pathing.defaultGamePath, "StardewValley"), true);

                        foreach (string path in new[] { Path.Combine(Pathing.defaultGamePath, "StardewValley"), Path.Combine(Pathing.defaultGamePath, "StardewModdingAPI") })
                        {
                            new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "chmod",
                                    Arguments = $"755 \"{path}\"",
                                    CreateNoWindow = true
                                }
                            }.Start();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to install SMAPI's update due to the following error: {ex}");

                    SetLockState(false);
                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.SMAPI_unable_to_install_latest"), _viewModel.Version), Program.translation.Get("internal.ok"));

                    OpenNativeExplorer(extractedLatestReleasePath);
                    return;
                }

                // Update the setting version
                Program.settings.GameDetails.SmapiVersion = latestVersion.ToString();
                _viewModel.SmapiVersion = Program.settings.GameDetails.SmapiVersion;

                // Delete any files underneath the SMAPI upgrade folder
                var upgradeDirectory = new DirectoryInfo(Pathing.GetSmapiUpgradeFolderPath());
                foreach (FileInfo file in upgradeDirectory.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in upgradeDirectory.GetDirectories())
                {
                    dir.Delete(true);
                }
                SetLockState(false);

                // Display message with link to release notes
                var requestWindow = new MessageWindow(Program.translation.Get("ui.message.SMAPI_update_complete"));
                if (await requestWindow.ShowDialog<bool>(this))
                {
                    Toolkit.OpenBrowser($"https://smapi.io/release/{latestSmapiToUri?.Key.Replace(".", String.Empty)}");
                }
            }
            else if (manualCheck is true)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.SMAPI_up_to_date"), _viewModel.SmapiVersion), Program.translation.Get("internal.ok"));
            }
        }

        private async Task HandleModUpdateCheck()
        {
            if (Pathing.defaultModPath is null)
            {
                await DisplayInvalidSMAPIWarning();
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

                // Every copy of a mod that a collection and the mod folder both provide has just been turned on,
                // so the unique IDs have to be collapsed back down to one enabled copy each
                if (enableState)
                {
                    _viewModel.ResolveEnabledDuplicates();
                    _viewModel.RefreshModCounts();
                }

                if (Program.settings.ShouldAutomaticallySaveProfileChanges)
                {
                    UpdateProfile(GetCurrentProfile());
                }
                else
                {
                    _viewModel.ShowSaveProfileChanges = true;
                }
            }
        }

        private async Task HandleBulkModInstall(bool enabledModsOnly = false)
        {
            if (Nexus.Client is null)
            {
                return;
            }

            if (Program.settings.NexusDetails is null || Program.settings.NexusDetails.IsPremium is false)
            {
                await CreateWarningWindow(Program.translation.Get("ui.warning.download_without_premium"), Program.translation.Get("internal.ok"));
                return;
            }
            else if (_viewModel.Mods.Where(m => String.IsNullOrEmpty(m.InstallStatus) is false).Count() == 0)
            {
                await CreateWarningWindow(Program.translation.Get("ui.warning.no_downloads_available"), Program.translation.Get("internal.ok"));
                return;
            }

            List<string> updateFilePaths = new List<string>();
            foreach (var mod in _viewModel.Mods.Where(m => String.IsNullOrEmpty(m.InstallStatus) is false))
            {
                if (enabledModsOnly is true && mod.IsEnabled is false)
                {
                    Program.helper.Log($"Skipping mod update install for {mod.Name}: Mod was not enabled when using the \"Install Updates (Enabled Mods Only)\" option");
                    continue;
                }

                var downloadFilePath = await InstallModViaNexus(mod);

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
            _viewModel.UpdateEndorsements();
            _viewModel.UpdateFilter();
        }

        private async Task HandleNexusConnection()
        {
            // If the user is logged out
            if (Nexus.Client is null)
            {
                // Check if they have a cached key
                string? apiKey = Nexus.GetCachedKey();
                if (apiKey is null)
                {
                    // If not, display the login window
                    var loginWindow = new NexusLogin(_viewModel);
                    loginWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    apiKey = await loginWindow.ShowDialog<string>(this);

                    if (String.IsNullOrEmpty(apiKey))
                    {
                        return;
                    }
                }

                await SetupNexusConnection(apiKey);
                if (Nexus.Client is null)
                {
                    // Failed to create, warn the user
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

                // Update any required NexusClient Mods related components
                await CheckForNexusConnection();

                return;
            }

            // If the user is logged in
            // Display information window
            var detailsWindow = new NexusInfo(Program.settings.NexusDetails);
            detailsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (await detailsWindow.ShowDialog<bool>(this) is true)
            {
                Nexus.ClearClient();
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

            // Evaluate mod requirements
            _viewModel.EvaluateRequirements();

            // Check for Nexus Mods connection perform related tasks
            await CheckForNexusConnection();

            // Hide the required mods
            _viewModel.HideRequiredMods();
        }

        internal async Task<NXMLinkResult> ProcessNXMLink(NXM nxmLink)
        {
            // An nxm link arrives unannounced, so it waits its turn rather than starting an install underneath
            // whatever the user is currently in the middle of
            await WaitForMainWindowToBeFree();

            if (Nexus.Client is null)
            {
                await CreateWarningWindow(Program.translation.Get("ui.message.require_nexus_login"), Program.translation.Get("internal.ok"));
                return NXMLinkResult.Blocked;
            }

            // Collection links follow an entirely different flow, so split them off before the mod handling below
            if (nxmLink.ResolvePurpose() is NXM.NXMPurpose.Collection)
            {
                if (await ValidateSMAPIPath() is false)
                {
                    return NXMLinkResult.Blocked;
                }

                return await ProcessCollectionLink(nxmLink) ? NXMLinkResult.Success : NXMLinkResult.Failed;
            }


            if (await ValidateSMAPIPath() is false)
            {
                return NXMLinkResult.Blocked;
            }

            // A file that a collection is still waiting on installs into that collection rather than loose, which is
            // the only route a non-premium account has into one Stardrop could not fetch on its behalf
            if (await TryProcessCollectionEntryLink(nxmLink))
            {
                return NXMLinkResult.Success;
            }

            Program.helper.Log($"Processing NXM link: {nxmLink.Link}");
            var processedDownloadLink = await Nexus.Client.GetFileDownloadLink(nxmLink, EnumParser.GetDescription(Program.settings.PreferredNexusServer));
            Program.helper.Log($"Processed link: {processedDownloadLink}");

            if (String.IsNullOrEmpty(processedDownloadLink))
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_to_get_download_link"), nxmLink.Link), Program.translation.Get("internal.ok"));
                return NXMLinkResult.Failed;
            }

            // Get the mod details
            var modDetails = await Nexus.Client.GetModDetailsViaNXM(nxmLink);
            if (modDetails is null || String.IsNullOrEmpty(modDetails.Name))
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_to_get_mod_details"), nxmLink.Link), Program.translation.Get("internal.ok"));
                return NXMLinkResult.Failed;
            }

            bool? fileSafetyResults = await Nexus.Client.ValidateFileSafety(nxmLink);
            if (fileSafetyResults is null)
            {
                // Unable to verify mod scan status on Nexus Mods, ask user if they want to continue
                if (await new MessageWindow(Program.translation.Get("ui.warning.failed_to_verify_mod_file")).ShowDialog<bool>(this) is false)
                {
                    return NXMLinkResult.Canceled;
                }
            }
            else if (fileSafetyResults is false)
            {
                // Reject downloading any quarantined mods on Nexus Mods
                await CreateWarningWindow(Program.translation.Get("ui.warning.file_quarantined"), Program.translation.Get("internal.ok"));
                return NXMLinkResult.Failed;
            }

            var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_nxm_install"), modDetails.Name));
            if (Program.settings.IsAskingBeforeAcceptingNXM is false || await requestWindow.ShowDialog<bool>(this))
            {
                var downloadResult = await Nexus.Client.DownloadFileAndGetPath(processedDownloadLink, modDetails.Name);
                if (downloadResult.ResultKind is DownloadResultKind.Failed)
                {
                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), modDetails.Name), Program.translation.Get("internal.ok"));
                    return NXMLinkResult.Failed;
                }
                if (downloadResult.ResultKind is DownloadResultKind.UserCanceled)
                {
                    // No need for a warning, this is something the user chose intentionally
                    return NXMLinkResult.Canceled;
                }
                string downloadedFilePath = downloadResult.DownloadedModFilePath!;

                var addedMods = await AddMods(new string[] { downloadedFilePath });
                await CheckForModUpdates(addedMods, useCache: true, skipCacheCheck: true);
                await GetCachedModUpdates(_viewModel.Mods.ToList(), skipCacheCheck: true);

                // Delete the downloaded archived mod
                if (File.Exists(downloadedFilePath))
                {
                    File.Delete(downloadedFilePath);
                }

                _viewModel.EvaluateRequirements();
                _viewModel.UpdateEndorsements();
                _viewModel.UpdateFilter();

                // Let the user know that the mod was installed via NXM
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.message.succeeded_nexus_install"), modDetails.Name), Program.translation.Get("internal.ok"));

                return NXMLinkResult.Success;
            }

            // Declining the confirmation is a choice rather than a fault, so the links behind it still go ahead
            return NXMLinkResult.Canceled;
        }

        /// <summary>
        /// Waits until nothing is sitting over the main window. Installing locks the window, and a lock closes the
        /// lock window, so starting one while a dialog is open would close that dialog out from underneath whoever
        /// is waiting on its answer.
        /// </summary>
        private async Task WaitForMainWindowToBeFree()
        {
            if (_viewModel.IsLocked is false && IsBlockedByOpenWindow() is false)
            {
                return;
            }

            Program.helper.Log($"Waiting for the main window to be free before handling an NXM link");

            while (_viewModel.IsLocked || IsBlockedByOpenWindow())
            {
                await Task.Delay(500);
            }
        }

        /// <summary>
        /// Whether anything the main window owns would be disturbed by an install starting. The collections window
        /// is left out, as fetching the entries a collection is missing is the whole reason it exists: holding the
        /// links it produces until it closes leaves the user clicking through mods to no visible effect.
        /// </summary>
        private bool IsBlockedByOpenWindow()
        {
            return this.OwnedWindows.Any(w => ReferenceEquals(w, _collectionsWindow) is false);
        }

        /// <summary>
        /// Puts a dialog above the ones the main window already owns. Two dialogs sharing an owner have no order
        /// between them, so one opened while another is up can land behind it. Left alone when there is nothing to
        /// sit above, since a topmost window would otherwise float over other applications.
        /// </summary>
        private void KeepDialogAboveSiblings(Window dialog)
        {
            dialog.Topmost = this.OwnedWindows.Any(w => ReferenceEquals(w, dialog) is false);
        }

        /// <summary>
        /// Locks or unlocks the main window. The lock window itself is opened by the sentinel timer from this state,
        /// so calling UpdateLockWindow without having called this first does nothing.
        /// </summary>
        /// <param name="cancellationSource">Supply one to give the lock window a cancel button wired to it</param>
        private void SetLockState(bool isWindowLocked, string? lockReason = null, CancellationTokenSource? cancellationSource = null)
        {
            _viewModel.IsLocked = isWindowLocked;
            _lockReason = lockReason is null ? String.Empty : lockReason;
            _lockCancellationSource = isWindowLocked ? cancellationSource : null;

            // Only the window this state opened. Closing everything the main window owns takes any dialog the user
            // is partway through with it, and an awaited ShowDialog closed from underneath returns the same result
            // as the user dismissing it, so its caller carries on as though they had answered
            if (_lockWindow is not null)
            {
                _lockWindow.Close();
                _lockWindow = null;
            }
        }

        private void UpdateLockWindow(string? lockReason = null, int? progress = null, int? maxProgress = null)
        {
            Program.helper.Log($"Attempting to update lock window with the following parameters: {lockReason} {progress} {maxProgress}");
            if (_viewModel.IsLocked is false)
            {
                return;
            }

            if (_lockWindow is null)
            {
                return;
            }

            Program.helper.Log($"Successfully updated lock window!");
            _lockWindow.UpdateProgress(lockReason, progress, maxProgress);
        }

        private async Task<UpdateCache?> GetCachedModUpdates(List<Mod> mods, bool skipCacheCheck = false)
        {
            int modsToUpdate = 0;
            UpdateCache? oldUpdateCache = null;

            // Cached update data is never applied to a collection mod, as its collection owns the version. The cache
            // is keyed by unique ID alone, which a collection copy can share with a loose copy
            mods = mods.Where(m => m.IsFromCollection is false).ToList();

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

                if (Program.settings.GameDetails is null || String.IsNullOrEmpty(Program.settings.GameDetails.SmapiVersion) || Program.settings.GameDetails.HasBadGameVersion() || Program.settings.GameDetails.HasSMAPIUpdated(SMAPI.GetVersion()))
                {
                    var smapiLogPath = Path.Combine(Pathing.GetSmapiLogFolderPath(), "SMAPI-latest.txt");
                    Program.helper.Log($"Checking for SMAPI-latest.txt at path: {Pathing.GetSmapiLogFolderPath()}");
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

                if (Program.settings.GameDetails is null || String.IsNullOrEmpty(Program.settings.GameDetails.SmapiVersion))
                {
                    await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_to_read_log"), _viewModel.Version), Program.translation.Get("internal.ok"));
                    Program.helper.Log($"SMAPI started but Stardrop was unable to read SMAPI-latest.txt. Mods will not be checked for updates.", Helper.Status.Alert);

                    _viewModel.IsCheckingForUpdates = false;
                    return;
                }
                else
                {
                    _viewModel.SmapiVersion = Program.settings.GameDetails.SmapiVersion;
                }

                // Fetch the mods to see if there are updates available
                if (useCache && oldUpdateCache is not null)
                {
                    oldUpdateCache.LastRuntime = DateTime.Now;
                }

                int modsToUpdate = 0;
                var updateCache = useCache && oldUpdateCache is not null ? oldUpdateCache : new UpdateCache(DateTime.Now);
                var modUpdateData = await SMAPI.GetModUpdateData(Program.settings.GameDetails, mods);

                // The full list still goes to GetModUpdateData so requirement names keep resolving, but only mods
                // outside a collection take update data back out of it
                foreach (var modItem in mods.Where(m => m.IsFromCollection is false))
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
                    var modUrl = String.IsNullOrEmpty(modEntry.Metadata.CustomUrl) && modEntry.Metadata.Main is not null ? modEntry.Metadata.Main.Url : modEntry.Metadata.CustomUrl;
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

        private async Task CheckForNexusConnection()
        {
            // Create the client and open access to Nexus if we haven't already done it
            await SetupNexusConnection(Nexus.GetCachedKey());

            Program.helper.Log($"Attempting to check for Nexus Mods connection (Has valid client: {Nexus.Client is not null})");

            if (Nexus.Client is null)
            {
                return;
            }

            // Validate the user's cached key to ensure it's still valid
            bool isKeyValid = await Nexus.Client.ValidateKey();

            if (isKeyValid is true)
            {
                _viewModel.NexusStatus = Program.translation.Get("internal.connected");
                _viewModel.NexusLimits = $"(Remaining Daily Requests: {Nexus.Client.DailyRequestsRemaining}) ";

                // Gather any endorsements
                _viewModel.UpdateEndorsements();

                // Show endorsements
                _viewModel.ShowEndorsements = true;

                // Show thumbnails
                if (Program.settings.ShowModThumbnails)
                {
                    _viewModel.UpdateThumbnails();
                }

                // Show Nexus mod download column, if user is premium
                _viewModel.ShowInstalls = Program.settings.NexusDetails.IsPremium;
            }
            else
            {
                Program.helper.Log($"Nexus Mods connection failed.");

                Program.settings.NexusDetails = new Models.Nexus.NexusUser();
                Nexus.ClearClient();
            }
        }

        private async Task SetupNexusConnection(string? apiKey)
        {
            if (apiKey is null)
            {
                return;
            }

            if (Nexus.Client is not null)
            {
                return;
            }

            // Create a global Nexus client. Further setup gets taken care of in NexusClientChanged.
            await Nexus.CreateClient(apiKey);
        }

        private async void NexusClientChanged(NexusClient? oldClient, NexusClient? newClient)
        {
            // Tear down old client stuff, if an old client is being discarded
            if (oldClient is not null)
            {
                oldClient.DailyRequestLimitsChanged -= NexusDailyLimitsChanged;
                _viewModel.NexusStatus = Program.translation.Get("internal.disconnected");
                _viewModel.ShowEndorsements = false;
                _viewModel.ShowInstalls = false;
            }

            if (newClient is not null)
            {
                newClient.DailyRequestLimitsChanged += NexusDailyLimitsChanged;

                // Verify NXM protocol usage
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && NXMProtocol.Validate(Program.executablePath) is false)
                {
                    var requestWindow = new MessageWindow(Program.translation.Get("ui.message.confirm_nxm_association"));
                    if (await requestWindow.ShowDialog<bool>(this))
                    {
                        if (NXMProtocol.Register(Program.executablePath) is false)
                        {
                            await new WarningWindow(Program.translation.Get("ui.warning.failed_to_set_association"), Program.translation.Get("internal.ok")).ShowDialog(this);
                        }
                    }
                }
            }
        }

        private void NexusDailyLimitsChanged(object? sender, EventArgs e)
        {
            NexusClient? client = sender as NexusClient;
            if (client is not null)
            {
                _viewModel.NexusStatus = Program.translation.Get("internal.connected");
                _viewModel.NexusLimits = $"(Remaining Daily Requests: {client.DailyRequestsRemaining}) ";
            }
        }

        private void AdjustWindowState()
        {
            this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        /// <summary>
        /// Enable all existing requirements for <paramref name="mod" />.
        /// </summary>
        /// <param name="mod">The mod whose requirements to enable.</param>
        private void EnableRequirements(Mod mod)
        {
            foreach (var requirement in mod.Requirements.Where(r => r.IsRequired))
            {
                // Resolved within the mod's own source first, so a collection mod enables the copy of the
                // dependency that will actually load alongside it rather than an identically named loose one
                var requiredMod = _viewModel.ResolveRequirement(mod, requirement.UniqueID);
                if (requiredMod is not null)
                {
                    requiredMod.IsEnabled = true;

                    // Enable the requirement's requirements
                    EnableRequirements(requiredMod);
                }
            }
        }

        /// <summary>
        /// Disable all mods that require the mod <paramref name="mod" />.
        /// </summary>
        /// <param name="mod">The mod to look for in requirements.</param>
        private void DisableRequirements(Mod mod)
        {
            // Only the mods that would actually load this copy. Matching on the unique ID alone would disable
            // everything depending on an identically named mod in another collection, or on a loose install
            foreach (var childMod in _viewModel.GetDependents(mod))
            {
                childMod.IsEnabled = false;

                // Disable the requirement's requirements
                DisableRequirements(childMod);
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
            _editorView.UpdateProfile(profile, _viewModel.Mods);

            _viewModel.RefreshModCounts();
        }

        private async Task<string?> InstallModViaNexus(Mod mod)
        {
            if (mod is null || mod.InstallState != InstallState.Unknown)
            {
                return null;
            }

            // A collection owns the versions of the mods it installs, so updating one here would put the profile out
            // of step with what the collection pins
            if (mod.IsFromCollection is true)
            {
                return null;
            }

            var modId = mod.GetNexusId();
            if (modId is null || Nexus.Client is null)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.unable_nexus_install"), mod.Name), Program.translation.Get("internal.ok"));
                return null;
            }
            mod.InstallState = InstallState.Downloading;

            var modFile = await Nexus.Client.GetFileByVersion(modId.Value, mod.SuggestedVersion, modFlag: mod.GetNexusFlag());
            if (modFile is null)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), mod.Name), Program.translation.Get("internal.ok"));
                mod.InstallState = InstallState.Unknown;
                return null;
            }

            var modDownloadLink = await Nexus.Client.GetFileDownloadLink(modId.Value, modFile.Id, serverName: EnumParser.GetDescription(Program.settings.PreferredNexusServer));
            if (modDownloadLink is null)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), mod.Name), Program.translation.Get("internal.ok"));
                mod.InstallState = InstallState.Unknown;
                return null;
            }

            bool? fileSafetyResults = await Nexus.Client.ValidateFileSafety(modId.Value, modFile.Id);
            if (fileSafetyResults is null)
            {
                // Unable to verify mod scan status on Nexus Mods, ask user if they want to continue
                if (await new MessageWindow(Program.translation.Get("ui.warning.failed_to_verify_mod_file")).ShowDialog<bool>(this) is false)
                {
                    mod.InstallState = InstallState.Unknown;
                    return null;
                }
            }
            else if (fileSafetyResults is false)
            {
                // Reject downloading any quarantined mods on Nexus Mods
                await CreateWarningWindow(Program.translation.Get("ui.warning.file_quarantined"), Program.translation.Get("internal.ok"));
                mod.InstallState = InstallState.Unknown;
                return null;
            }

            var downloadResult = await Nexus.Client.DownloadFileAndGetPath(modDownloadLink, modFile.Name);
            if (downloadResult.ResultKind is DownloadResultKind.UserCanceled)
            {
                mod.InstallState = InstallState.Unknown;
                // No warning, as the user triggered this intentionally
                return null;
            }
            if (downloadResult.ResultKind is DownloadResultKind.Failed)
            {
                await CreateWarningWindow(String.Format(Program.translation.Get("ui.warning.failed_nexus_install"), mod.Name), Program.translation.Get("internal.ok"));
                mod.InstallState = InstallState.Unknown;
                return null;
            }

            mod.InstallState = InstallState.Installing;

            return downloadResult.DownloadedModFilePath;
        }

        public bool TryDeleteMod(Mod mod, int retries = 3)
        {
            try
            {
                DeleteMod(mod);
            }
            catch (Exception ex)
            {
                if (retries > 0)
                {
                    return TryDeleteMod(mod, retries - 1);
                }

                Program.helper.Log($"Failed to delete the mod {mod.Name}: {ex}", Utilities.Helper.Status.Warning);
                return false;
            }

            return true;
        }

        private void DeleteMod(Mod mod)
        {
            Program.helper.Log($"Attempting to delete the mod {mod.Name}");

            var targetDirectory = new DirectoryInfo(mod.ModFileInfo.DirectoryName);
            if (targetDirectory is not null && targetDirectory.Exists)
            {
                targetDirectory.Delete(true);
            }
        }

        /// <summary>
        /// Installs the mods contained in the given archives.
        /// </summary>
        /// <param name="filePaths">Archives to install from</param>
        /// <param name="installPathOverride">Where to install to. Defaults to Settings.ModInstallPath</param>
        /// <param name="installedModsByArchive">When supplied, is filled with the mods each archive produced. A single archive can hold several mods, so each entry is a list</param>
        private async Task<List<Mod>> AddMods(string[]? filePaths, string? installPathOverride = null, IDictionary<string, List<Mod>>? installedModsByArchive = null)
        {
            Guid request = Guid.NewGuid();

            // Wait until current lock is finished before doing further installs
            Program.helper.Log($"Add mods request received ({request}): Pending");
            while (_viewModel.IsLocked)
            {
                await Task.Delay(500);
            }
            Program.helper.Log($"Add mods request received ({request}): Accepted");

            await HandleModListRefresh();

            var addedMods = new List<Mod>();
            if (filePaths is null)
            {
                return addedMods;
            }

            // Lock the window
            int totalMods = filePaths.Length;
            SetLockState(true, String.Format(Program.translation.Get("ui.warning.install_mod_attempt_count"), totalMods));

            // Get the local data
            ClientData localDataCache = new ClientData();
            if (File.Exists(Pathing.GetDataCachePath()))
            {
                localDataCache = JsonSerializer.Deserialize<ClientData>(File.ReadAllText(Pathing.GetDataCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            }

            // Export zip to the default mods folder
            int currentModIndex = 1;
            List<string> warnings = new List<string>();
            foreach (string fileFullName in filePaths)
            {
                try
                {
                    // Extract the archive data
                    // TODO refactor this to use async
                    using (var archive = ArchiveFactory.OpenArchive(fileFullName))
                    {
                        Dictionary<string, Manifest?> pathToManifests = new Dictionary<string, Manifest?>();
                        foreach (var manifest in archive.Entries.Where(e => Path.GetFileName(e.Key)!.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (manifest.Key is not null)
                            {
                                Program.helper.Log(manifest.Key);
                                pathToManifests[manifest.Key] = await ManifestParser.GetDataAsync(manifest);
                            }
                        }

                        // Warn and skip the install logic if the given archive has no manifest.json
                        if (pathToManifests.Count == 0)
                        {
                            warnings.Add(String.Format(Program.translation.Get("ui.warning.no_manifest"), fileFullName));
                            continue;
                        }

                        // If this is a mod update and if the Manifest.UpdateCautionMessage has a value, display message (and confirm if user wants to continue with mod update)
                        bool shouldProceedWithUpdate = true;
                        foreach (var manifest in pathToManifests.Values.Where(m => m is not null && _viewModel.HasModInstalled(m.UniqueID) is true && string.IsNullOrEmpty(m.UpdateCautionMessage) is false))
                        {
                            var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_mod_update_caution"), manifest!.Name, manifest!.UpdateCautionMessage)) { Topmost = true };
                            if (await requestWindow.ShowDialog<bool>(this) is false)
                            {
                                Program.helper.Log($"User elected to skip mod update due to given Manifest.UpdateCautionMessage message for mod {manifest!.UniqueID}:{manifest!.UpdateCautionMessage}");
                                shouldProceedWithUpdate = false;
                                break;
                            }
                        }

                        // Skip updating if user elected to skip any of the bundled mods due to Manifest.UpdateCautionMessage
                        if (shouldProceedWithUpdate is false)
                        {
                            continue;
                        }

                        int currentManifestIndex = 1;
                        bool alwaysAskToDelete = Program.settings.AlwaysAskToDelete;
                        foreach (var manifestPath in pathToManifests.Keys)
                        {
                            var manifest = pathToManifests[manifestPath];

                            // If the archive doesn't have a manifest, warn the user
                            bool isUpdate = false;
                            if (manifest is not null)
                            {
                                var installPath = String.IsNullOrEmpty(installPathOverride) ? Program.settings.ModInstallPath : installPathOverride;

                                // Only treat this as an update of a mod from the same source, otherwise installing a
                                // collection would overwrite the user's loose copy of the same mod
                                var targetSourceId = Pathing.GetCollectionSourceId(installPath);
                                if (_viewModel.Mods.FirstOrDefault(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) && String.Equals(m.SourceId, targetSourceId, StringComparison.OrdinalIgnoreCase)) is Mod mod && mod is not null && mod.ModFileInfo.Directory is not null)
                                {
                                    if (manifest.DeleteOldVersion is false && alwaysAskToDelete is true)
                                    {
                                        string warningMessage = Program.translation.Get("ui.message.confirm_mod_update_method_no_config");
                                        if (mod.HasConfig)
                                        {
                                            warningMessage = Program.translation.Get("ui.message.confirm_mod_update_method");
                                            if (Program.settings.EnableProfileSpecificModConfigs)
                                            {
                                                warningMessage = Program.translation.Get("ui.message.confirm_mod_update_method_preserved");
                                            }
                                        }

                                        var requestWindow = new FlexibleOptionWindow(String.Format(warningMessage, manifest.Name), Program.translation.Get("internal.yes"), Program.translation.Get("internal.yes_all"), Program.translation.Get("internal.no"))
                                        {
                                            Topmost = true
                                        };
                                        Choice response = await requestWindow.ShowDialog<Choice>(this);
                                        if (response == Choice.First || response == Choice.Second)
                                        {
                                            if (response == Choice.Second)
                                            {
                                                alwaysAskToDelete = false;
                                            }

                                            // Delete old version
                                            UpdateLockWindow(String.Format(Program.translation.Get("ui.warning.mod_deleting"), manifest.Name), currentManifestIndex, pathToManifests.Keys.Count);
                                            if (TryDeleteMod(mod) is false)
                                            {
                                                warnings.Add(String.Format(Program.translation.Get("ui.warning.failed_to_delete_during_update"), mod.Name));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Delete old version
                                        UpdateLockWindow(String.Format(Program.translation.Get("ui.warning.mod_deleting"), manifest.Name), currentManifestIndex, pathToManifests.Keys.Count);
                                        if (TryDeleteMod(mod) is false)
                                        {
                                            warnings.Add(String.Format(Program.translation.Get("ui.warning.failed_to_delete_during_update"), mod.Name));
                                        }
                                    }

                                    isUpdate = true;
                                    installPath = mod.ModFileInfo.Directory.FullName;

                                    // Set the LastUpdateTimestamp
                                    if (localDataCache.ModInstallData is not null && localDataCache.ModInstallData.Any(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        var updatedTimestamp = DateTime.Now;
                                        mod.LastUpdateTimestamp = updatedTimestamp;
                                        localDataCache.ModInstallData.First(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase)).LastUpdateTimestamp = updatedTimestamp;
                                    }
                                }
                                else if (String.IsNullOrEmpty(manifestPath.Replace("manifest.json", String.Empty, StringComparison.OrdinalIgnoreCase)))
                                {
                                    installPath = Path.Combine(installPath, manifest.UniqueID);
                                }

                                // Create the base directory, if needed
                                if (Directory.Exists(installPath) is false)
                                {
                                    Directory.CreateDirectory(installPath);
                                }

                                // Set lock state to let user know we are installing the mod
                                string individualProgressText = String.Format(isUpdate ? Program.translation.Get("ui.warning.mod_updating") : Program.translation.Get("ui.warning.mod_installing"), manifest.Name);
                                string modProgressText = String.Format("[{0} / {1}] Mods", currentModIndex, totalMods);
                                string manifestProgressText = String.Format("[{0} / {1}] Manifests", currentManifestIndex, pathToManifests.Keys.Count);
                                UpdateLockWindow(String.Concat(modProgressText, "\n", manifestProgressText, "\n\n", individualProgressText), currentManifestIndex, pathToManifests.Keys.Count);

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

                                var addedMod = new Mod(manifest, new FileInfo(Path.Join(installPath, manifestFolderPath)), manifest.UniqueID, manifest.Version, manifest.Name, manifest.Description, manifest.Author);
                                addedMods.Add(addedMod);

                                // Record which archive produced this mod, so callers do not have to guess afterwards
                                if (installedModsByArchive is not null)
                                {
                                    if (installedModsByArchive.ContainsKey(fileFullName) is false)
                                    {
                                        installedModsByArchive[fileFullName] = new List<Mod>();
                                    }

                                    installedModsByArchive[fileFullName].Add(addedMod);
                                }
                            }
                            else
                            {
                                warnings.Add(String.Format(Program.translation.Get("ui.warning.no_manifest"), fileFullName));
                            }

                            currentManifestIndex += 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to unzip the file {fileFullName} due to the following error: {ex}", Utilities.Helper.Status.Warning);
                    warnings.Add(String.Format(Program.translation.Get("ui.warning.unable_to_load_mod"), fileFullName));
                }

                currentModIndex += 1;
            }

            // Display warnings
            SetLockState(false);
            foreach (string warningMessage in warnings)
            {
                await CreateWarningWindow(warningMessage, Program.translation.Get("internal.ok"));
            }

            // Cache the local data
            File.WriteAllText(Pathing.GetDataCachePath(), JsonSerializer.Serialize(localDataCache, new JsonSerializerOptions() { WriteIndented = true }));

            // Refresh mod list
            _viewModel.DiscoverMods(Pathing.defaultModPath);

            // Refresh enabled mods
            _viewModel.EnableModsByProfile(GetCurrentProfile());

            // Handle automatically enabling the added mods, if the setting is enabled
            if (Program.settings.EnableModsOnAdd is true)
            {
                _viewModel.ForceModState(GetCurrentProfile(), addedMods, modEnableState: true);

                foreach (var mod in addedMods)
                {
                    EnableRequirements(mod);
                }
            }

            // Update the current profile
            UpdateProfile(GetCurrentProfile());

            Program.helper.Log($"Add mods request received ({request}): Processed");
            return addedMods;
        }

        private void CreateDirectoryJunctions(List<string> arguments)
        {
            // Prepare the process
            var processInfo = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "/usr/bin/env",
                Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"/C {string.Join(" & ", arguments)}" : $"bash -c \"{string.Join(" ; ", arguments)}\"",
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
            Program.helper.Log($"Creating links for the following enabled mods from profile {profile.Name}:{spacing}{String.Join(spacing, profile.EnabledModIds.Select(r => r.ToString()))}");

            // Link the enabled mods via a chained command
            List<string> arguments = new List<string>();
            List<string> usedLinkNames = new List<string>();
            List<string> linkedUniqueIds = new List<string>();

            // Ordered so that a duplicated unique ID resolves to the copy this profile owns, as the guard below
            // keeps whichever is reached first
            var enabledMods = _viewModel.Mods.Where(m => m.IsEnabled).OrderByDescending(m => String.Equals(m.SourceId, profile.SourceId, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var mod in enabledMods)
            {
                if (mod is null || mod.ModFileInfo is null || mod.ModFileInfo.Directory is null)
                {
                    continue;
                }

                // The last gate before SMAPI. Two folders claiming one unique ID is an error SMAPI reports rather
                // than resolves, and a profile saved while both copies were enabled still carries both
                if (linkedUniqueIds.Any(id => id.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)))
                {
                    Program.helper.Log($"Skipping the link for {mod.Name} ({mod.ModFileInfo.DirectoryName}), as another copy of {mod.UniqueId} has already been linked", Helper.Status.Warning);
                    continue;
                }
                linkedUniqueIds.Add(mod.UniqueId);

                // SMAPI does not care about folder names, so a collision between a collection mod and a loose one can be resolved by suffixing
                var linkName = mod.ModFileInfo.Directory.Name;
                if (usedLinkNames.Any(n => n.Equals(linkName, StringComparison.OrdinalIgnoreCase)))
                {
                    linkName = $"{linkName} ({(mod.IsFromCollection ? mod.SourceId : "local")})";
                }
                usedLinkNames.Add(linkName);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var longPathPrefix = @"\\?\";

                    var linkPath = Path.Combine(enabledModsPath, linkName);
                    if (linkPath.Length >= 260)
                    {
                        linkPath = longPathPrefix + linkPath;
                    }

                    var modDirectoryName = mod.ModFileInfo.DirectoryName;
                    if (Path.Combine(enabledModsPath, linkName).Length >= 260)
                    {
                        modDirectoryName = longPathPrefix + modDirectoryName;
                    }

                    arguments.Add($"mklink /J \"{linkPath}\" \"{modDirectoryName}\"");
                }
                else
                {
                    var edq = "\\\""; // Escaped double quotes, to prevent issues with paths that contain single quotes
                    arguments.Add($"ln -sf {edq}{mod.ModFileInfo.DirectoryName}{edq} {edq}{Path.Combine(enabledModsPath, linkName)}{edq}");
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

        private async Task<bool> ValidateSMAPIPath()
        {
            if (Program.settings.SMAPIFolderPath is not null && File.Exists(Pathing.GetSmapiPath()))
            {
                return true;
            }

            await DisplayInvalidSMAPIWarning();

            Program.helper.Log(
                Program.settings.SMAPIFolderPath is null
                    ? "No path given for StardewModdingAPI."
                    : $"Bad path given for StardewModdingAPI: {Pathing.GetSmapiPath()}", Helper.Status.Warning);

            return false;
        }

        private async Task DisplayInvalidSMAPIWarning()
        {
            await CreateWarningWindow(Program.translation.Get("ui.warning.unable_to_locate_smapi"), Program.translation.Get("internal.ok"));

            await DisplaySettingsWindow();
        }

        private void SetupDownloadCountListener()
        {
            var downloadPanel = this.FindControl<DownloadPanel>("DownloadPanel");
            if (downloadPanel is null)
            {
                return;
            }

            if (downloadPanel.DataContext is not DownloadPanelViewModel panelVM)
            {
                return;
            }

            // Change listener and intial value setter
            // Both of these need to have a .StartWith(), because a) CombineLatest() will never emit anything until both
            // sources have emitted *something* and b) this also ensures that we do intial value setting before 
            // Downloads count or selected language otherwise changes.
            Observable.CombineLatest(
                first: panelVM.InProgressDownloads.StartWith(0),
                second: Program.translation.WhenAnyPropertyChanged().StartWith(Program.translation),
                resultSelector: (int count, Translation? translation) => (count, translation!)
            ).Subscribe(((int downloadCount, Translation translation) x) =>
            {
                _viewModel.DownloadsButtonText = String.Format(x.translation.Get("ui.main_window.buttons.downloads.label"), x.downloadCount);
            });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
