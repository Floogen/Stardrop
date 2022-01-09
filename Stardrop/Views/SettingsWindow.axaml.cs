using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Stardrop.Models;
using Stardrop.Utilities;
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
            this.FindControl<Button>("applyButton").Click += ApplyButton_Click;

            // Push the focus for the textboxes to the end of their strings
            var smapiTextBox = this.FindControl<TextBox>("smapiFolderPathBox");
            var modFolderTextBox = this.FindControl<TextBox>("modFolderPathBox");
            SetTextboxTextFocusToEnd(smapiTextBox, smapiTextBox.Text);
            SetTextboxTextFocusToEnd(modFolderTextBox, modFolderTextBox.Text);

            // Handle adding the themes
            foreach (string fileFullName in Directory.EnumerateFiles("Themes", "*.xaml"))
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

            this.FontFamily = new Avalonia.Media.FontFamily("Segoe UI Symbol");

            // Cache the old settings
            _oldSettings = Program.settings.ShallowCopy();

#if DEBUG
            this.AttachDevTools();
#endif
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
                dialog.Filters.Add(new FileDialogFilter() { Name = "StardewModdingAPI" });
            }
            else
            {
                dialog.Filters.Add(new FileDialogFilter() { Name = "StardewModdingAPI", Extensions = { "*" } });
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
                SetTextboxTextFocusToEnd(this.FindControl<TextBox>("modFolderPathBox"), folderPath);
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

            SetTextboxTextFocusToEnd(this.FindControl<TextBox>("smapiFolderPathBox"), smapiFileInfo.DirectoryName);
            if (String.IsNullOrEmpty(Program.settings.ModFolderPath))
            {
                SetTextboxTextFocusToEnd(this.FindControl<TextBox>("modFolderPathBox"), Pathing.defaultModPath);
            }

            return true;
        }

        private string GetTargetSmapiName()
        {
            var targetSmapiName = "StardewModdingAPI.exe";
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                targetSmapiName = "StardewModdingAPI";
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
