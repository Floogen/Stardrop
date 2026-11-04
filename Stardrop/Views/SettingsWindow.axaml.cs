using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Stardrop.Models;
using Stardrop.Models.Data.Enums;
using Stardrop.Utilities;
using Stardrop.Utilities.Internal;
using Stardrop.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    public partial class SettingsWindow : Window
    {
        private Settings _oldSettings;
        private SettingsWindowViewModel _viewModel;

        public SettingsWindow()
        {
            InitializeComponent();

            // Set the datacontext
            _viewModel = new SettingsWindowViewModel();
            DataContext = _viewModel;

            // Handle buttons
            this.FindControl<Button>("exitButton")!.Click += Exit_Click;
            this.FindControl<Button>("cancelButton")!.Click += Exit_Click;
            this.FindControl<Button>("smapiFolderButton")!.Click += SmapiFolderButton_Click;
            this.FindControl<Button>("modFolderButton")!.Click += ModFolderButton_Click;
            this.FindControl<Button>("modInstallButton")!.Click += ModInstallButton_Click;
            this.FindControl<Button>("collectionInstallButton")!.Click += CollectionInstallButton_Click;
            if (OperatingSystem.IsWindows())
            {
                this.FindControl<Button>("registerNXMButton")!.Click += RegisterNXMButton_Click;
                this.FindControl<Button>("removeNXMButton")!.Click += RemoveNXMButton_Click;
            }
            this.FindControl<Button>("applyButton")!.Click += ApplyButton_Click;

            // Push the focus for the textboxes to the end of their strings
            var smapiTextBox = this.FindControl<TextBox>("smapiFolderPathBox")!;
            var modFolderTextBox = this.FindControl<TextBox>("modFolderPathBox")!;
            var modInstallTextBox = this.FindControl<TextBox>("modInstallPathBox")!;
            var collectionInstallTextBox = this.FindControl<TextBox>("collectionInstallPathBox")!;
            SetTextboxTextFocusToEnd(smapiTextBox, smapiTextBox.Text!);
            SetTextboxTextFocusToEnd(modFolderTextBox, modFolderTextBox.Text!);
            SetTextboxTextFocusToEnd(modInstallTextBox, modInstallTextBox.Text!);
            SetTextboxTextFocusToEnd(collectionInstallTextBox, collectionInstallTextBox.Text!);

            // Handle adding the themes
            LoadThemes();

            var themeComboBox = this.FindControl<ComboBox>("themeComboBox")!;
            themeComboBox.ItemsSource = _viewModel.Themes;
            themeComboBox.SelectedItem = GetThemeByName(Program.settings.Theme);
            themeComboBox.SelectionChanged += (sender, e) =>
            {
                Theme? theme = themeComboBox.SelectedItem as Theme;
                if (theme is not null && !string.IsNullOrEmpty(theme.FilePath))
                {
                    try
                    {
                        theme.Style = ThemeManager.Load(theme.FilePath);
                    }
                    catch (Exception ex)
                    {
                        Program.helper.Log($"Unable to load theme on {theme.Name}: {ex}", Helper.Status.Alert);
                        return;
                    }
                }
                if (theme?.Style is not null)
                {
                    Application.Current!.Styles[ThemeManager.THEME_STYLE_INDEX] = theme.Style;
                    Program.settings.Theme = theme.Name;
                }
            };
            this.FindControl<Button>("refreshThemesButton")!.Click += RefreshThemesButton_Click;

            // Handle Nexus Mods preferred server.
            // Keep the enum as the item's value, so localization cannot affect the saved setting.
            // The translated label is display-only: the value handed to the Nexus API is still the
            // server's [Description], see the GetFileDownloadLink calls in MainWindow.
            var serverDisplayKeys = new Dictionary<NexusServers, string>()
            {
                { NexusServers.NexusCDN, "ui.settings_window.nexus_servers.nexus_cdn" },
                { NexusServers.Chicago, "ui.settings_window.nexus_servers.chicago" },
                { NexusServers.Paris, "ui.settings_window.nexus_servers.paris" },
                { NexusServers.Amsterdam, "ui.settings_window.nexus_servers.amsterdam" },
                { NexusServers.Prague, "ui.settings_window.nexus_servers.prague" },
                { NexusServers.LosAngeles, "ui.settings_window.nexus_servers.los_angeles" },
                { NexusServers.Miami, "ui.settings_window.nexus_servers.miami" },
                { NexusServers.Singapore, "ui.settings_window.nexus_servers.singapore" }
            };

            var preferredComboBox = this.FindControl<ComboBox>("preferredServerBox")!;
            preferredComboBox.ItemsSource = Enum.GetValues<NexusServers>()
                .Select(server => new ComboBoxItem
                {
                    Content = serverDisplayKeys.TryGetValue(server, out var displayKey)
                        ? Program.translation.Get(displayKey)
                        : EnumParser.GetDescription(server),
                    Tag = server
                })
                .ToList();
            // Use FirstOrDefault instead of First: if the stored setting holds a value outside
            // NexusServers (a hand-edited config file), we simply leave the box unselected rather
            // than throwing, which is what the previous description-based lookup did.
            var selectedServer = preferredComboBox.ItemsSource.Cast<ComboBoxItem>()
                .FirstOrDefault(option => option.Tag is NexusServers server && server == Program.settings.PreferredNexusServer);
            if (selectedServer is not null)
            {
                preferredComboBox.SelectedItem = selectedServer;
            }
            preferredComboBox.SelectionChanged += (sender, e) =>
            {
                if (preferredComboBox.SelectedItem is ComboBoxItem { Tag: NexusServers server })
                {
                    Program.settings.PreferredNexusServer = server;
                }
            };

            // Handle adding the languages
            var languageComboBox = this.FindControl<ComboBox>("languageComboBox")!;
            var availableLanguages = Program.translation.GetAvailableTranslations();
            languageComboBox.ItemsSource = availableLanguages;
            languageComboBox.SelectedItem = string.IsNullOrEmpty(Program.settings.Language)
                ? availableLanguages.First()
                : availableLanguages.FirstOrDefault(language => string.Equals(language.Code, Program.translation.NormalizeLanguage(Program.settings.Language), StringComparison.OrdinalIgnoreCase)) ?? availableLanguages.First();
            languageComboBox.SelectionChanged += (sender, e) =>
            {
                if (languageComboBox.SelectedItem is Translation.LanguageOption language)
                {
                    Program.translation.SetLanguage(language.Code);
                    Program.settings.Language = language.Code;
                    UpdateGroupingComboBox();
                }
            };

            // Handle adding the mod grouping methods.
            UpdateGroupingComboBox();

            var groupingComboBox = this.FindControl<ComboBox>("groupingComboBox")!;
            groupingComboBox.SelectionChanged += (sender, e) =>
            {
                if (groupingComboBox.SelectedItem is ComboBoxItem { Tag: ModGrouping grouping })
                {
                    Program.settings.ModGroupingMethod = grouping;
                }
            };

            FontFamily = new Avalonia.Media.FontFamily("Segoe UI Symbol");

            // Cache the old settings
            _oldSettings = Program.settings.ShallowCopy();
        }

        public SettingsWindow(double parentWindowHeight) : this()
        {
            // Adjust the height of the this window to be slightly smaller than the parent
            Height = parentWindowHeight - (parentWindowHeight / 4);
        }
        private void UpdateGroupingComboBox()
        {
            var groupingComboBox = this.FindControl<ComboBox>("groupingComboBox")!;
            
            var currentSelectedGrouping = (groupingComboBox.SelectedItem as ComboBoxItem)?.Tag as ModGrouping? 
                                        ?? Program.settings.ModGroupingMethod;

            var items = new List<ComboBoxItem>
            {
                new() { Content = Program.translation.Get("ui.settings_window.mod_grouping.none"), Tag = ModGrouping.None },
                new() { Content = Program.translation.Get("ui.settings_window.mod_grouping.content_pack"), Tag = ModGrouping.ContentPack },
                new() { Content = Program.translation.Get("ui.settings_window.mod_grouping.folder"), Tag = ModGrouping.Folder },
                new() { Content = Program.translation.Get("ui.settings_window.mod_grouping.folder_condensed"), Tag = ModGrouping.FolderCondensed }
            };

            groupingComboBox.ItemsSource = items;
            
            groupingComboBox.SelectedItem = items.FirstOrDefault(i => i.Tag is ModGrouping g && g == currentSelectedGrouping);
        }

        [SupportedOSPlatform("windows")]
        private async void RegisterNXMButton_Click(object? sender, RoutedEventArgs e)
        {
            NXMAssociationState state = NXMProtocol.GetState(Program.executablePath);
            if (state.Status is NXMAssociationStatus.Registered)
            {
                await new WarningWindow(Program.translation.Get("ui.warning.already_associated"), Program.translation.Get("internal.ok")).ShowDialog(this);
                return;
            }

            if (state.Status is NXMAssociationStatus.Overridden)
            {
                // Registering still matters here, as the capability keys are what list Stardrop in Windows' picker
                if (state.IsStardropRegistered is false)
                {
                    if (NXMProtocol.Register(Program.executablePath) is false)
                    {
                        await new WarningWindow(Program.translation.Get("ui.warning.failed_to_set_association"), Program.translation.Get("internal.ok")).ShowDialog(this);
                        return;
                    }
                }

                await new WarningWindow(string.Format(Program.translation.Get("ui.warning.nxm_association_overridden"), state.HandlerName), Program.translation.Get("internal.ok")).ShowDialog(this);
                return;
            }

            var requestWindow = new MessageWindow(Program.translation.Get("ui.message.confirm_nxm_association"));
            if (await requestWindow.ShowDialog<bool>(this) is false)
            {
                return;
            }

            if (NXMProtocol.Register(Program.executablePath) is false)
            {
                await new WarningWindow(Program.translation.Get("ui.warning.failed_to_set_association"), Program.translation.Get("internal.ok")).ShowDialog(this);
            }
        }

        [SupportedOSPlatform("windows")]
        private async void RemoveNXMButton_Click(object? sender, RoutedEventArgs e)
        {
            if (OperatingSystem.IsWindows() is false)
            {
                await new WarningWindow(
                        Program.translation.Get("ui.warning.unsupported_platform"),
                        Program.translation.Get("internal.ok"))
                    .ShowDialog(this);
                return;
            }

            NXMAssociationState state = NXMProtocol.GetState(Program.executablePath);
            if (state.HasStardropRegistration is false)
            {
                await new WarningWindow(Program.translation.Get("ui.warning.not_associated"), Program.translation.Get("internal.ok")).ShowDialog(this);
                return;
            }

            var requestWindow = new MessageWindow(Program.translation.Get("ui.message.confirm_remove_nxm_association"));
            if (await requestWindow.ShowDialog<bool>(this) is false)
            {
                return;
            }

            NXMUnregistrationResult result = NXMProtocol.Unregister(Program.executablePath);
            if (result.Succeeded is false)
            {
                await new WarningWindow(Program.translation.Get("ui.warning.failed_to_remove_association"), Program.translation.Get("internal.ok")).ShowDialog(this);
                return;
            }

            // Windows can refuse to give up the UserChoice key, which would leave NXM links pointing at a handler that no longer exists
            if (result.RetainedUserChoice)
            {
                await new WarningWindow(Program.translation.Get("ui.warning.nxm_user_choice_retained"), Program.translation.Get("internal.ok")).ShowDialog(this);
            }
        }

        /// <summary>
        /// Reads every theme file from disk into the view model, replacing whatever was there before
        /// </summary>
        private void LoadThemes()
        {
            // Built as a new list rather than cleared in place, as the combo box only notices a changed reference
            var themes = new List<Theme>();
            string? lastContributorName = null;

            foreach (string fileFullName in ThemeManager.GetThemeFilePaths())
            {
                try
                {
                    var contributorName = new DirectoryInfo(Path.GetDirectoryName(fileFullName)!).Name;
                    if (contributorName is not null && contributorName.Equals(ThemeManager.THEMES_FOLDER_NAME, StringComparison.OrdinalIgnoreCase))
                    {
                        contributorName = null;
                    }

                    if (lastContributorName != contributorName)
                    {
                        // Add separator
                        themes.Add(new Theme()
                        {
                            Name = "------------",
                            IsEnabled = false
                        });
                    }
                    lastContributorName = contributorName;

                    var themeName = Path.GetFileNameWithoutExtension(fileFullName);
                    themes.Add(new Theme()
                    {
                        Name = themeName,
                        Author = contributorName is not null ? $"by {contributorName}" : "",
                        IsEnabled = true,
                        FilePath = fileFullName,
                        Style = null
                    });
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load theme on {Path.GetFileNameWithoutExtension(fileFullName)}: {ex}", Helper.Status.Warning);
                }
            }

            _viewModel.Themes = themes;
        }

        private Theme? GetThemeByName(string themeName)
        {
            return _viewModel.Themes.FirstOrDefault(theme => theme.IsEnabled && theme.Name.Equals(themeName, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshThemesButton_Click(object? sender, RoutedEventArgs e)
        {
            var themeComboBox = this.FindControl<ComboBox>("themeComboBox")!;
            var selectedThemeName = themeComboBox.SelectedItem is not Theme selectedTheme ? Program.settings.Theme : selectedTheme.Name;

            // Drop the cached defaults file too, otherwise edits to Themes/Defaults.xaml would be missed
            ThemeManager.ClearCache();
            LoadThemes();

            // Swapping the list clears the selection, which fires SelectionChanged with a null theme and is ignored
            themeComboBox.ItemsSource = _viewModel.Themes;

            // Reselecting is what pushes the freshly parsed style into the application
            themeComboBox.SelectedItem = GetThemeByName(selectedThemeName) ?? _viewModel.Themes.FirstOrDefault(theme => theme.IsEnabled);
        }

        private void Exit_Click(object? sender, RoutedEventArgs e)
        {
            var oldTheme = _viewModel.Themes.FirstOrDefault(t => t.Name.Equals(_oldSettings.Theme));
            if (oldTheme is not null && oldTheme.Style is not null)
            {
                Application.Current!.Styles[ThemeManager.THEME_STYLE_INDEX] = oldTheme.Style;
            }

            Program.settings = _oldSettings;
            Program.translation.SetLanguage(Program.settings.Language);

            Close(false);
        }

        private async void SmapiFolderButton_Click(object? sender, RoutedEventArgs e)
        {
            var fileTypes = new List<FilePickerFileType>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fileTypes.Add(new FilePickerFileType("StardewModdingAPI.exe") { Patterns = ["*.exe"] });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                fileTypes.Add(new FilePickerFileType("StardewModdingAPI.dll"));
            }
            else
            {
                fileTypes.Add(new FilePickerFileType("StardewModdingAPI.dll") { Patterns = ["*"] });
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = fileTypes
            });

            if (files.Count > 0)
            {
                SetSMAPIPath(files[0].Path.LocalPath);
            }
        }
        /// <summary>
        /// Converts a folder path string into an IStorageFolder for use as a picker's SuggestedStartLocation.
        /// Returns null when the path is empty, missing, or the platform cannot resolve it.
        /// </summary>
        private async Task<IStorageFolder?> GetFolderFromPathAsync(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return null;
            }

            return await StorageProvider.TryGetFolderFromPathAsync(new Uri(path));
        }

        private async void ModFolderButton_Click(object? sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Program.translation.Get("ui.settings_window.dialogs.select_mod_folder"),
                AllowMultiple = false,
                SuggestedStartLocation = await GetFolderFromPathAsync(Program.settings.ModFolderPath)
            });

            if (folders.Count > 0)
            {
                var folderPath = folders[0].Path.LocalPath;
                var modFolderPathBox = this.FindControl<TextBox>("modFolderPathBox")!;
                SetTextboxTextFocusToEnd(modFolderPathBox, folderPath);

                var modInstallPathBox = this.FindControl<TextBox>("modInstallPathBox")!;
                if (string.IsNullOrEmpty(modInstallPathBox.Text) || !Directory.Exists(modInstallPathBox.Text) || !modInstallPathBox.Text.Contains(folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    modInstallPathBox.Text = Path.Combine(folderPath, "Stardrop Installed Mods");
                    SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                }
            }
        }

        private async void ModInstallButton_Click(object? sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
    {
                Title = Program.translation.Get("ui.settings_window.dialogs.select_mod_install_folder"),
                AllowMultiple = false,
                SuggestedStartLocation = await GetFolderFromPathAsync(Program.settings.ModInstallPath)
            });

            if (folders.Count > 0)
            {
                SetTextboxTextFocusToEnd(this.FindControl<TextBox>("modInstallPathBox")!, folders[0].Path.LocalPath);
            }
        }

        private async void CollectionInstallButton_Click(object? sender, RoutedEventArgs e)
        {
            var collectionInstallPathBox = this.FindControl<TextBox>("collectionInstallPathBox")!;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Program.translation.Get("ui.settings_window.dialogs.select_collection_install_folder"),
                AllowMultiple = false,
                SuggestedStartLocation = await GetFolderFromPathAsync(collectionInstallPathBox.Text)
            });

            if (folders.Count > 0)
            {
                SetTextboxTextFocusToEnd(collectionInstallPathBox, folders[0].Path.LocalPath);
            }
        }

        /// <summary>
        /// Whether the given folder holds anything that changing the collection path would strand. Only the presence
        /// of a subfolder is checked, since a source ID folder is the unit the user has to move and the collection
        /// records that describe it live elsewhere.
        /// </summary>
        private static bool HasInstalledCollections(string collectionPath)
        {
            try
            {
                return Directory.Exists(collectionPath) && Directory.EnumerateDirectories(collectionPath).Any();
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Unable to check {collectionPath} for installed collections: {ex}", Helper.Status.Warning);
                return false;
            }
        }

        private async void ApplyButton_Click(object? sender, RoutedEventArgs e)
        {
            var smapiFolderPathBox = this.FindControl<TextBox>("smapiFolderPathBox")!;
            var smapiPath = string.IsNullOrEmpty(smapiFolderPathBox.Text) || smapiFolderPathBox.Text.Contains(GetTargetSmapiName(), StringComparison.OrdinalIgnoreCase) ? smapiFolderPathBox.Text : Path.Combine(smapiFolderPathBox.Text, GetTargetSmapiName());
            if (!SetSMAPIPath(smapiPath))
            {
                SetTextboxTextFocusToEnd(smapiFolderPathBox, _oldSettings.SMAPIFolderPath);
                return;
            }

            var modFolderPathBox = this.FindControl<TextBox>("modFolderPathBox")!;
            if (string.IsNullOrEmpty(modFolderPathBox.Text) || !Directory.Exists(modFolderPathBox.Text))
            {
                await new WarningWindow(Program.translation.Get("ui.warning.given_mod_folder_does_not_exist"), Program.translation.Get("internal.ok")).ShowDialog(this);
                SetTextboxTextFocusToEnd(modFolderPathBox, _oldSettings.ModFolderPath);
                return;
            }

            var modInstallPathBox = this.FindControl<TextBox>("modInstallPathBox")!;
            if (string.IsNullOrEmpty(modInstallPathBox.Text) || !Directory.Exists(modInstallPathBox.Text))
            {
                if (Directory.Exists(_oldSettings.ModInstallPath) is false)
                {
                    _oldSettings.ModInstallPath = Path.Combine(modFolderPathBox.Text, "Stardrop Installed Mods");
                    Directory.CreateDirectory(_oldSettings.ModInstallPath);

                    await new WarningWindow(string.Format(Program.translation.Get("ui.warning.given_install_folder_not_exist_default"), modFolderPathBox.Text), Program.translation.Get("internal.ok")).ShowDialog(this);
                    SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                    return;
                }
                else
                {
                    await new WarningWindow(string.Format(Program.translation.Get("ui.warning.given_install_folder_not_exist"), modFolderPathBox.Text), Program.translation.Get("internal.ok")).ShowDialog(this);
                    SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                    return;
                }
            }
            else if (!modInstallPathBox.Text.Contains(modFolderPathBox.Text, StringComparison.OrdinalIgnoreCase))
            {
                await new WarningWindow(string.Format(Program.translation.Get("ui.warning.given_install_folder_not_under_mod_folder"), modFolderPathBox.Text), Program.translation.Get("internal.ok")).ShowDialog(this);
                SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                return;
            }

            var collectionInstallPathBox = this.FindControl<TextBox>("collectionInstallPathBox")!;
            if (string.IsNullOrWhiteSpace(collectionInstallPathBox.Text) || !Directory.Exists(collectionInstallPathBox.Text))
            {
                await new WarningWindow(Program.translation.Get("ui.warning.given_collection_folder_does_not_exist"), Program.translation.Get("internal.ok")).ShowDialog(this);
                SetTextboxTextFocusToEnd(collectionInstallPathBox, _oldSettings.CollectionInstallPath);
                return;
            }

            // A collection installs its own copy of every mod it pins, so under the mod folder a SMAPI run started
            // outside Stardrop would see two copies of each and skip every duplicated unique ID rather than picking
            // one. The other direction is no better: the mod folder underneath this one would hand every loose mod a
            // source ID it does not have, which is what profiles match copies of a mod on. The install path is not
            // checked separately, as it is already required to sit under the mod folder
            if (Pathing.IsSameOrUnder(collectionInstallPathBox.Text, modFolderPathBox.Text) || Pathing.IsSameOrUnder(modFolderPathBox.Text, collectionInstallPathBox.Text))
            {
                await new WarningWindow(string.Format(Program.translation.Get("ui.warning.given_collection_folder_conflicts_with_mod_folder"), modFolderPathBox.Text), Program.translation.Get("internal.ok"), windowWidth: 500).ShowDialog(this);
                SetTextboxTextFocusToEnd(collectionInstallPathBox, _oldSettings.CollectionInstallPath);
                return;
            }

            // Both of these are rebuilt or written by Stardrop, so a collection installed into either would be
            // cleared out from under itself
            if (Pathing.IsSameOrUnder(collectionInstallPathBox.Text, Pathing.GetSelectedModsFolderPath()) || Pathing.IsSameOrUnder(collectionInstallPathBox.Text, Pathing.GetCollectionsCacheFolderPath()))
            {
                await new WarningWindow(Program.translation.Get("ui.warning.given_collection_folder_reserved"), Program.translation.Get("internal.ok"), windowWidth: 500).ShowDialog(this);
                SetTextboxTextFocusToEnd(collectionInstallPathBox, _oldSettings.CollectionInstallPath);
                return;
            }

            // Nothing is moved on the user's behalf, so this notice is the only thing standing between them and a
            // set of collections that quietly stops being found. Raised only where the old folder still holds one,
            // and worked out before the settings file is written so it can still name where the mods are now
            var oldCollectionPath = string.IsNullOrWhiteSpace(_oldSettings.CollectionInstallPath) ? Pathing.GetDefaultCollectionsFolderPath() : _oldSettings.CollectionInstallPath;
            string? collectionMoveNotice = null;
            if (Pathing.IsSamePath(collectionInstallPathBox.Text, oldCollectionPath) is false && HasInstalledCollections(oldCollectionPath))
            {
                collectionMoveNotice = string.Format(Program.translation.Get("ui.warning.collection_folder_changed"), oldCollectionPath, collectionInstallPathBox.Text);
            }

            // Write the settings cache
            File.WriteAllText(Pathing.GetSettingsPath(), JsonSerializer.Serialize(Program.settings, new JsonSerializerOptions() { WriteIndented = true }));

            // Awaited, unlike the warnings above, as those leave the window open behind them and this one is the last thing before it closes
            if (collectionMoveNotice is not null)
            {
                await new WarningWindow(collectionMoveNotice, Program.translation.Get("internal.ok"), windowWidth: 500).ShowDialog(this);
            }

            Close(true);
        }

        private bool SetSMAPIPath(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                new WarningWindow(string.Format(Program.translation.Get("ui.warning.given_invalid_smapi_executable"), GetTargetSmapiName()), Program.translation.Get("internal.ok")).ShowDialog(this);
                return false;
            }

            var smapiFileInfo = new FileInfo(filePath);
            if (!smapiFileInfo.Exists || !smapiFileInfo.Name.Equals(GetTargetSmapiName(), StringComparison.OrdinalIgnoreCase))
            {
                new WarningWindow(string.Format(Program.translation.Get("ui.warning.given_invalid_smapi_executable"), GetTargetSmapiName()), Program.translation.Get("internal.ok")).ShowDialog(this);
                return false;
            }

            var modFolderPathBox = this.FindControl<TextBox>("modFolderPathBox")!;
            var modInstallPathBox = this.FindControl<TextBox>("modInstallPathBox")!;

            SetTextboxTextFocusToEnd(this.FindControl<TextBox>("smapiFolderPathBox")!, smapiFileInfo.DirectoryName!);
            if (string.IsNullOrEmpty(Program.settings.ModFolderPath) || !Directory.Exists(modFolderPathBox.Text))
            {
                SetTextboxTextFocusToEnd(modFolderPathBox, Path.Combine(smapiFileInfo.DirectoryName!, "Mods"));
            }

            if (string.IsNullOrEmpty(Program.settings.ModInstallPath) || !Directory.Exists(modInstallPathBox.Text))
            {
                SetTextboxTextFocusToEnd(modInstallPathBox, Path.Combine(smapiFileInfo.DirectoryName!, "Mods", "Stardrop Installed Mods"));
            }

            return true;
        }

        private string GetTargetSmapiName()
        {
            var targetSmapiName = "StardewModdingAPI.exe";
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                targetSmapiName = "StardewModdingAPI.dll";
            }

            return targetSmapiName;
        }

        private void SetTextboxTextFocusToEnd(TextBox textBox, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            textBox.Text = text;
            textBox.CaretIndex = text.Length - 1;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
