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
        private IStyle _oldTheme;
        private string _oldThemeName;

        public SettingsWindow()
        {
            InitializeComponent();

            // Set the datacontext
            DataContext = new SettingsWindowViewModel();

            // Handle buttons
            this.FindControl<Button>("exitButton").Click += Exit_Click;
            this.FindControl<Button>("cancelButton").Click += Exit_Click;
            this.FindControl<Button>("smapiFolderButton").Click += SmapiFolderButton_Click;
            this.FindControl<Button>("applyButton").Click += ApplyButton_Click;

            // Handle textbox
            if (Program.settings.SMAPIFolderPath is not null)
            {
                this.FindControl<TextBox>("smapiFolderPathBox").Text = Program.settings.SMAPIFolderPath;
            }

            // Handle adding the themes
            Dictionary<string, IStyle> themes = new Dictionary<string, IStyle>();
            foreach (string fileFullName in Directory.EnumerateFiles("Themes", "*.xaml"))
            {
                try
                {
                    var themeName = Path.GetFileNameWithoutExtension(fileFullName);
                    themes[themeName] = AvaloniaRuntimeXamlLoader.Parse<Styles>(File.ReadAllText(fileFullName));
                    Program.helper.Log($"Loaded theme {Path.GetFileNameWithoutExtension(fileFullName)}", Helper.Status.Debug);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load theme on {Path.GetFileNameWithoutExtension(fileFullName)}: {ex}", Helper.Status.Warning);
                }
            }

            var themeComboBox = this.FindControl<ComboBox>("themeComboBox");
            themeComboBox.Items = themes.Keys;
            themeComboBox.SelectedItem = !themes.ContainsKey(Program.settings.Theme) ? themes.Keys.First() : Program.settings.Theme;
            themeComboBox.SelectionChanged += (sender, e) =>
            {
                var themeName = themeComboBox.SelectedItem.ToString();
                Application.Current.Styles[0] = themes[themeName];
                Program.settings.Theme = themeName;
            };

            _oldTheme = Application.Current.Styles[0];
            _oldThemeName = Program.settings.Theme;

#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void Exit_Click(object? sender, RoutedEventArgs e)
        {
            Application.Current.Styles[0] = _oldTheme;
            Program.settings.Theme = _oldThemeName;
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

        private void ApplyButton_Click(object? sender, RoutedEventArgs e)
        {
            // Update our local settings
            Program.settings.SMAPIFolderPath = this.FindControl<TextBox>("smapiFolderPathBox").Text;
            Pathing.SetModPath(Program.settings.SMAPIFolderPath);

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

            this.FindControl<TextBox>("smapiFolderPathBox").Text = smapiFileInfo.DirectoryName;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
