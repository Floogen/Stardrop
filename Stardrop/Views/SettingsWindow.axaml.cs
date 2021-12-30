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

            this.SetSMAPIPath(await dialog.ShowAsync(this));
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
            // Write the settings cache
            File.WriteAllText(Pathing.GetSettingsPath(), JsonSerializer.Serialize(Program.settings, new JsonSerializerOptions() { WriteIndented = true }));

            this.Close(true);
        }

        private void SetSMAPIPath(string[]? filePaths)
        {
            if (filePaths is null || filePaths.Count() == 0)
            {
                return;
            }


            var targetSmapiName = "StardewModdingAPI.exe";
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                targetSmapiName = "StardewModdingAPI";
            }

            var smapiFileInfo = new FileInfo(filePaths[0]);
            if (!smapiFileInfo.Name.Equals(targetSmapiName, StringComparison.OrdinalIgnoreCase))
            {
                new WarningWindow($"The given file isn't {targetSmapiName}\n\nReverting to previous path.", "OK").ShowDialog(this);
                return;
            }

            SetTextboxTextFocusToEnd(this.FindControl<TextBox>("smapiFolderPathBox"), smapiFileInfo.DirectoryName);
            if (String.IsNullOrEmpty(Program.settings.ModFolderPath))
            {
                SetTextboxTextFocusToEnd(this.FindControl<TextBox>("modFolderPathBox"), Pathing.defaultModPath);
            }
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
