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
        private Dictionary<string, IStyle> _themes = new Dictionary<string, IStyle>();

        public SettingsWindow()
        {
            InitializeComponent();
            this.SizeToContent = SizeToContent.Height;

            // Set the datacontext
            DataContext = new SettingsWindowViewModel();

            // Handle buttons
            this.FindControl<Button>("exitButton").Click += Exit_Click;
            this.FindControl<Button>("cancelButton").Click += Exit_Click;
            this.FindControl<Button>("smapiFolderButton").Click += SmapiFolderButton_Click;
            this.FindControl<Button>("modFolderButton").Click += ModFolderButton_Click;
            this.FindControl<Button>("modInstallButton").Click += ModInstallButton_Click;
            this.FindControl<Button>("registerNXMButton").Click += RegisterNXMButton_Click;
            this.FindControl<Button>("applyButton").Click += ApplyButton_Click;

            // Push the focus for the textboxes to the end of their strings
            var smapiTextBox = this.FindControl<TextBox>("smapiFolderPathBox");
            var modFolderTextBox = this.FindControl<TextBox>("modFolderPathBox");
            var modInstallTextBox = this.FindControl<TextBox>("modInstallPathBox");
            SetTextboxTextFocusToEnd(smapiTextBox, smapiTextBox.Text);
            SetTextboxTextFocusToEnd(modFolderTextBox, modFolderTextBox.Text);
            SetTextboxTextFocusToEnd(modInstallTextBox, modInstallTextBox.Text);

            // Handle adding the themes
            foreach (string fileFullName in Directory.EnumerateFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes"), "*.xaml"))
            {
                try
                {
                    var themeName = Path.GetFileNameWithoutExtension(fileFullName);
                    _themes[themeName] = AvaloniaRuntimeXamlLoader.Parse<Styles>(File.ReadAllText(fileFullName));
                    Program.helper.Log($"Loaded theme {Path.GetFileNameWithoutExtension(fileFullName)}", Helper.Status.Debug);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load theme on {Path.GetFileNameWithoutExtension(fileFullName)}: {ex}", Helper.Status.Warning);
                }
            }

            var themeComboBox = this.FindControl<ComboBox>("themeComboBox");
            themeComboBox.Items = _themes.Keys;
            themeComboBox.SelectedItem = !_themes.ContainsKey(Program.settings.Theme) ? _themes.Keys.First() : Program.settings.Theme;
            themeComboBox.SelectionChanged += (sender, e) =>
            {
                var themeName = themeComboBox.SelectedItem.ToString();
                Application.Current.Styles[0] = _themes[themeName];
                Program.settings.Theme = themeName;
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
            var descriptionToModGroupingEnum = new Dictionary<string, ModGrouping>();
            foreach (ModGrouping modGrouping in Enum.GetValues(typeof(ModGrouping)))
            {
                if (EnumParser.GetDescription(modGrouping) is not null)
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

        private async void RegisterNXMButton_Click(object? sender, RoutedEventArgs e)
        {
            if (NXMProtocol.Validate(Program.executablePath) is false)
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
            else
            {
                await new WarningWindow(Program.translation.Get("ui.warning.already_associated"), Program.translation.Get("internal.ok")).ShowDialog(this);
            }
        }

        private void Exit_Click(object? sender, RoutedEventArgs e)
        {
            Application.Current.Styles[0] = _themes[_oldSettings.Theme];
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

        private void ApplyButton_Click(object? sender, RoutedEventArgs e)
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
                new WarningWindow(Program.translation.Get("ui.warning.given_mod_folder_does_not_exist"), Program.translation.Get("internal.ok")).ShowDialog(this);
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

                    new WarningWindow(String.Format(Program.translation.Get("ui.warning.given_install_folder_not_exist_default"), modFolderPathBox.Text), Program.translation.Get("internal.ok")).ShowDialog(this);
                    SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                    return;
                }
                else
                {
                    new WarningWindow(String.Format(Program.translation.Get("ui.warning.given_install_folder_not_exist"), modFolderPathBox.Text), Program.translation.Get("internal.ok")).ShowDialog(this);
                    SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                    return;
                }
            }
            else if (!modInstallPathBox.Text.Contains(modFolderPathBox.Text, StringComparison.OrdinalIgnoreCase))
            {
                new WarningWindow(String.Format(Program.translation.Get("ui.warning.given_install_folder_not_under_mod_folder"), modFolderPathBox.Text), Program.translation.Get("internal.ok")).ShowDialog(this);
                SetTextboxTextFocusToEnd(modInstallPathBox, _oldSettings.ModInstallPath);
                return;
            }

            // Write the settings cache
            File.WriteAllText(Pathing.GetSettingsPath(), JsonSerializer.Serialize(Program.settings, new JsonSerializerOptions() { WriteIndented = true }));

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
