using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
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
            this.FindControl<Button>("exitButton").Click += Exit_Click;
            this.FindControl<Button>("cancelButton").Click += Exit_Click;
            this.FindControl<Button>("smapiFolderButton").Click += SmapiFolderButton_Click;
            this.FindControl<Button>("modFolderButton").Click += ModFolderButton_Click;
            this.FindControl<Button>("modInstallButton").Click += ModInstallButton_Click;
            this.FindControl<Button>("collectionInstallButton").Click += CollectionInstallButton_Click;
            this.FindControl<Button>("registerNXMButton").Click += RegisterNXMButton_Click;
            this.FindControl<Button>("applyButton").Click += ApplyButton_Click;

            // Push the focus for the textboxes to the end of their strings
            var smapiTextBox = this.FindControl<TextBox>("smapiFolderPathBox");
            var modFolderTextBox = this.FindControl<TextBox>("modFolderPathBox");
            var modInstallTextBox = this.FindControl<TextBox>("modInstallPathBox");
            var collectionInstallTextBox = this.FindControl<TextBox>("collectionInstallPathBox");
            SetTextboxTextFocusToEnd(smapiTextBox, smapiTextBox.Text);
            SetTextboxTextFocusToEnd(modFolderTextBox, modFolderTextBox.Text);
            SetTextboxTextFocusToEnd(modInstallTextBox, modInstallTextBox.Text);
            SetTextboxTextFocusToEnd(collectionInstallTextBox, collectionInstallTextBox.Text);

            // Handle adding the themes
            string? lastContributorName = null;
            foreach (string fileFullName in Directory.EnumerateFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes"), "*.xaml", SearchOption.AllDirectories))
            {
                try
                {
                    var contributorName = new DirectoryInfo(Path.GetDirectoryName(fileFullName)).Name;
                    if (contributorName is not null && contributorName.Equals("Themes", StringComparison.OrdinalIgnoreCase))
                    {
                        contributorName = null;
                    }

                    if (lastContributorName != contributorName)
                    {
                        // Add separator
                        _viewModel.Themes.Add(new Theme()
                        {
                            Name = "------------",
                            IsEnabled = false
                        });
                    }
                    lastContributorName = contributorName;

                    var themeName = Path.GetFileNameWithoutExtension(fileFullName);
                    var style = AvaloniaRuntimeXamlLoader.Parse<Styles>(File.ReadAllText(fileFullName));

                    _viewModel.Themes.Add(new Theme()
                    {
                        Author = contributorName is not null ? $"by {contributorName}" : "",
                        Name = themeName,
                        Style = style,
                        IsEnabled = true
                    });

                    Program.helper.Log($"Loaded theme {Path.GetFileNameWithoutExtension(fileFullName)}", Helper.Status.Debug);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load theme on {Path.GetFileNameWithoutExtension(fileFullName)}: {ex}", Helper.Status.Warning);
                }
            }

            var themeComboBox = this.FindControl<ComboBox>("themeComboBox");
            themeComboBox.Items = _viewModel.Themes;
            var currentTheme = _viewModel.Themes.FirstOrDefault(t => t.Name.Equals(Program.settings.Theme, StringComparison.OrdinalIgnoreCase));
            if (currentTheme is not null)
            {
                themeComboBox.SelectedItem = currentTheme;
            }
            themeComboBox.SelectionChanged += (sender, e) =>
            {
                Theme? theme = themeComboBox.SelectedItem as Theme;
                if (theme is not null && theme.Style is not null)
                {
                    Application.Current.Styles[0] = theme.Style;
                    Program.settings.Theme = theme.Name;
                }
            };

            // Handle Nexus Mods preferred server
            var descriptionToServerEnum = new Dictionary<string, NexusServers>();
            foreach (NexusServers serverName in Enum.GetValues(typeof(NexusServers)))
            {
                if (EnumParser.GetDescription(serverName) is not null)
                {
                    descriptionToServerEnum[EnumParser.GetDescription(serverName)] = serverName;
                }
            }

            var preferredComboBox = this.FindControl<ComboBox>("preferredServerBox");
            preferredComboBox.Items = descriptionToServerEnum.Keys;
            preferredComboBox.SelectedItem = EnumParser.GetDescription(Program.settings.PreferredNexusServer);
            preferredComboBox.SelectionChanged += (sender, e) =>
            {
                Program.settings.PreferredNexusServer = descriptionToServerEnum[preferredComboBox.SelectedItem.ToString()];
            };

            // Handle adding the languages
            var languageComboBox = this.FindControl<ComboBox>("languageComboBox");
            languageComboBox.Items = Program.translation.GetAvailableTranslations();
            languageComboBox.SelectedItem = String.IsNullOrEmpty(Program.settings.Language) ? Program.translation.GetAvailableTranslations().First() : Program.translation.GetLanguage(Program.settings.Language);
            languageComboBox.SelectionChanged += (sender, e) =>
            {
                var language = languageComboBox.SelectedItem.ToString();
                Program.translation.SetLanguage(language);
                Program.settings.Language = language;
            };

            // Handle adding the mod grouping methods
            var descriptionToModGroupingEnum = new Dictionary<string, ModGrouping>()
            {
                { "None", ModGrouping.None }
            };

            foreach (ModGrouping modGrouping in Enum.GetValues(typeof(ModGrouping)).Cast<ModGrouping>().OrderBy(g => EnumParser.GetDescription(g)))
            {
                if (modGrouping != ModGrouping.None && EnumParser.GetDescription(modGrouping) is not null)
                {
                    descriptionToModGroupingEnum[EnumParser.GetDescription(modGrouping)] = modGrouping;
                }
            }

            var groupingComboBox = this.FindControl<ComboBox>("groupingComboBox");
            groupingComboBox.Items = descriptionToModGroupingEnum.Keys;
            groupingComboBox.SelectedItem = EnumParser.GetDescription(Program.settings.ModGroupingMethod);
            groupingComboBox.SelectionChanged += (sender, e) =>
            {
                Program.settings.ModGroupingMethod = descriptionToModGroupingEnum[groupingComboBox.SelectedItem.ToString()];
            };

            this.FontFamily = new Avalonia.Media.FontFamily("Segoe UI Symbol");

            // Cache the old settings
            _oldSettings = Program.settings.ShallowCopy();

#if DEBUG
            this.AttachDevTools();
#endif
        }

        public SettingsWindow(double parentWindowHeight) : this()
        {
            // Adjust the height of the this window to be slightly smaller than the parent
            this.Height = parentWindowHeight - (parentWindowHeight / 4);
        }

        private async void RegisterNXMButton_Click(object? sender, RoutedEventArgs e)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) is false)
            {
                await new WarningWindow(
                        Program.translation.Get("ui.warning.unsupported_platform"),
                        Program.translation.Get("internal.ok"))
                    .ShowDialog(this);
                return;
            }

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

                await new WarningWindow(String.Format(Program.translation.Get("ui.warning.nxm_association_overridden"), state.HandlerName), Program.translation.Get("internal.ok")).ShowDialog(this);
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

        private void Exit_Click(object? sender, RoutedEventArgs e)
        {
            var oldTheme = _viewModel.Themes.FirstOrDefault(t => t.Name.Equals(_oldSettings.Theme));
            if (oldTheme is not null && oldTheme.Style is not null)
            {
                Application.Current.Styles[0] = oldTheme.Style;
            }

            Program.settings = _oldSettings;
            Program.translation.SetLanguage(String.IsNullOrEmpty(Program.settings.Language) ? Program.translation.GetAvailableTranslations().First() : Program.translation.GetLanguage(Program.settings.Language));

            this.Close(false);
        }

        private async void SmapiFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                dialog.Filters.Add(new FileDialogFilter() { Name = "StardewModdingAPI.exe", Extensions = { "exe" } });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                dialog.Filters.Add(new FileDialogFilter() { Name = "StardewModdingAPI.dll" });
            }
            else
            {
                dialog.Filters.Add(new FileDialogFilter() { Name = "StardewModdingAPI.dll", Extensions = { "*" } });
            }
            dialog.AllowMultiple = false;

            var filePaths = await dialog.ShowAsync(this);
            if (filePaths is not null && filePaths.Count() > 0)
            {
                this.SetSMAPIPath(filePaths.First());
            }
        }

        private async void ModFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog()
            {
                Title = "Select the mod folder"
            };

            if (!String.IsNullOrEmpty(Program.settings.ModFolderPath))
            {
                dialog.Directory = Program.settings.ModFolderPath;
            }

            var folderPath = await dialog.ShowAsync(this);
            if (!String.IsNullOrEmpty(folderPath))
            {
                var modFolderPathBox = this.FindControl<TextBox>("modFolderPathBox");
                SetTextboxTextFocusToEnd(modFolderPathBox, folderPath);

                var modInstallPathBox = this.FindControl<TextBox>("modInstallPathBox");
                if (String.IsNullOrEmpty(modInstallPathBox.Text) || !Directory.Exists(modInstallPathBox.Text) || !modInstallPathBox.Text.Contains(modFolderPathBox.Text, StringComparison.OrdinalIgnoreCase))
                {
                    modInstallPathBox.Text = Path.Combine(modFolderPathBox.Text, "Stardrop Installed Mods");
                    SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                    return;
                }
            }
        }

        private async void ModInstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog()
            {
                Title = "Select the output folder for mods installed via Stardrop"
            };

            if (!String.IsNullOrEmpty(Program.settings.ModInstallPath))
            {
                dialog.Directory = Program.settings.ModInstallPath;
            }

            var folderPath = await dialog.ShowAsync(this);
            if (!String.IsNullOrEmpty(folderPath))
            {
                SetTextboxTextFocusToEnd(this.FindControl<TextBox>("modInstallPathBox"), folderPath);
            }
        }

        private async void CollectionInstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog()
            {
                Title = "Select the folder that collections install their mods to"
            };

            var collectionInstallPathBox = this.FindControl<TextBox>("collectionInstallPathBox");
            if (!String.IsNullOrEmpty(collectionInstallPathBox.Text))
            {
                dialog.Directory = collectionInstallPathBox.Text;
            }

            var folderPath = await dialog.ShowAsync(this);
            if (!String.IsNullOrEmpty(folderPath))
            {
                SetTextboxTextFocusToEnd(collectionInstallPathBox, folderPath);
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
            var smapiFolderPathBox = this.FindControl<TextBox>("smapiFolderPathBox");
            var smapiPath = String.IsNullOrEmpty(smapiFolderPathBox.Text) || smapiFolderPathBox.Text.Contains(GetTargetSmapiName(), StringComparison.OrdinalIgnoreCase) ? smapiFolderPathBox.Text : Path.Combine(smapiFolderPathBox.Text, GetTargetSmapiName());
            if (!SetSMAPIPath(smapiPath))
            {
                SetTextboxTextFocusToEnd(smapiFolderPathBox, _oldSettings.SMAPIFolderPath);
                return;
            }

            var modFolderPathBox = this.FindControl<TextBox>("modFolderPathBox");
            if (String.IsNullOrEmpty(modFolderPathBox.Text) || !Directory.Exists(modFolderPathBox.Text))
            {
                await new WarningWindow(Program.translation.Get("ui.warning.given_mod_folder_does_not_exist"), Program.translation.Get("internal.ok")).ShowDialog(this);
                SetTextboxTextFocusToEnd(modFolderPathBox, _oldSettings.ModFolderPath);
                return;
            }

            var modInstallPathBox = this.FindControl<TextBox>("modInstallPathBox");
            if (String.IsNullOrEmpty(modInstallPathBox.Text) || !Directory.Exists(modInstallPathBox.Text))
            {
                if (Directory.Exists(_oldSettings.ModInstallPath) is false)
                {
                    _oldSettings.ModInstallPath = Path.Combine(modFolderPathBox.Text, "Stardrop Installed Mods");
                    Directory.CreateDirectory(_oldSettings.ModInstallPath);

                    await new WarningWindow(String.Format(Program.translation.Get("ui.warning.given_install_folder_not_exist_default"), modFolderPathBox.Text), Program.translation.Get("internal.ok")).ShowDialog(this);
                    SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                    return;
                }
                else
                {
                    await new WarningWindow(String.Format(Program.translation.Get("ui.warning.given_install_folder_not_exist"), modFolderPathBox.Text), Program.translation.Get("internal.ok")).ShowDialog(this);
                    SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                    return;
                }
            }
            else if (!modInstallPathBox.Text.Contains(modFolderPathBox.Text, StringComparison.OrdinalIgnoreCase))
            {
                await new WarningWindow(String.Format(Program.translation.Get("ui.warning.given_install_folder_not_under_mod_folder"), modFolderPathBox.Text), Program.translation.Get("internal.ok")).ShowDialog(this);
                SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                return;
            }

            var collectionInstallPathBox = this.FindControl<TextBox>("collectionInstallPathBox");
            if (String.IsNullOrWhiteSpace(collectionInstallPathBox.Text) || !Directory.Exists(collectionInstallPathBox.Text))
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
                await new WarningWindow(String.Format(Program.translation.Get("ui.warning.given_collection_folder_conflicts_with_mod_folder"), modFolderPathBox.Text), Program.translation.Get("internal.ok"), windowWidth: 500).ShowDialog(this);
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
            var oldCollectionPath = String.IsNullOrWhiteSpace(_oldSettings.CollectionInstallPath) ? Pathing.GetDefaultCollectionsFolderPath() : _oldSettings.CollectionInstallPath;
            string? collectionMoveNotice = null;
            if (Pathing.IsSamePath(collectionInstallPathBox.Text, oldCollectionPath) is false && HasInstalledCollections(oldCollectionPath))
            {
                collectionMoveNotice = String.Format(Program.translation.Get("ui.warning.collection_folder_changed"), oldCollectionPath, collectionInstallPathBox.Text);
            }

            // Write the settings cache
            File.WriteAllText(Pathing.GetSettingsPath(), JsonSerializer.Serialize(Program.settings, new JsonSerializerOptions() { WriteIndented = true }));

            // Awaited, unlike the warnings above, as those leave the window open behind them and this one is the last thing before it closes
            if (collectionMoveNotice is not null)
            {
                await new WarningWindow(collectionMoveNotice, Program.translation.Get("internal.ok"), windowWidth: 500).ShowDialog(this);
            }

            this.Close(true);
        }

        private bool SetSMAPIPath(string filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                new WarningWindow(String.Format(Program.translation.Get("ui.warning.given_invalid_smapi_executable"), GetTargetSmapiName()), Program.translation.Get("internal.ok")).ShowDialog(this);
                return false;
            }

            var smapiFileInfo = new FileInfo(filePath);
            if (!smapiFileInfo.Exists || !smapiFileInfo.Name.Equals(GetTargetSmapiName(), StringComparison.OrdinalIgnoreCase))
            {
                new WarningWindow(String.Format(Program.translation.Get("ui.warning.given_invalid_smapi_executable"), GetTargetSmapiName()), Program.translation.Get("internal.ok")).ShowDialog(this);
                return false;
            }

            var modFolderPathBox = this.FindControl<TextBox>("modFolderPathBox");
            var modInstallPathBox = this.FindControl<TextBox>("modInstallPathBox");

            SetTextboxTextFocusToEnd(this.FindControl<TextBox>("smapiFolderPathBox"), smapiFileInfo.DirectoryName);
            if (String.IsNullOrEmpty(Program.settings.ModFolderPath) || !Directory.Exists(modFolderPathBox.Text))
            {
                SetTextboxTextFocusToEnd(this.FindControl<TextBox>("modFolderPathBox"), Path.Combine(smapiFileInfo.DirectoryName, "Mods"));
            }

            if (String.IsNullOrEmpty(Program.settings.ModInstallPath) || !Directory.Exists(modInstallPathBox.Text))
            {
                SetTextboxTextFocusToEnd(this.FindControl<TextBox>("modInstallPathBox"), Path.Combine(smapiFileInfo.DirectoryName, "Mods", "Stardrop Installed Mods"));
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
            if (String.IsNullOrEmpty(text))
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
