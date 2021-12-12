using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.Utilities;
using System;
using System.IO;
using System.Text.Json;

namespace Stardrop.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            // Handle buttons
            this.FindControl<Button>("exitButton").Click += delegate { this.Close(false); };
            this.FindControl<Button>("cancelButton").Click += delegate { this.Close(false); };
            this.FindControl<Button>("smapiFolderButton").Click += SmapiFolderButton_Click;
            this.FindControl<Button>("applyButton").Click += ApplyButton_Click;

            // Handle textbox
            if (Program.settings.SMAPIFolderPath is not null)
            {
                this.FindControl<TextBox>("smapiFolderPathBox").Text = Program.settings.SMAPIFolderPath;
            }
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private async void SmapiFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filters.Add(new FileDialogFilter() { Name = "StardewModdingAPI.exe", Extensions = { "exe" } });
            dialog.AllowMultiple = false;

            this.SetSMAPIPath(await dialog.ShowAsync(this));
        }

        private void SetSMAPIPath(string[]? filePaths)
        {
            if (filePaths is null)
            {
                return;
            }

            var smapiFileInfo = new FileInfo(filePaths[0]);
            if (!smapiFileInfo.Name.Equals("StardewModdingAPI.exe", StringComparison.OrdinalIgnoreCase))
            {
                new WarningWindow("The given file isn't StardewModdingAPI.exe\n\nReverting to previous path.", "OK").ShowDialog(this);
                return;
            }

            this.FindControl<TextBox>("smapiFolderPathBox").Text = smapiFileInfo.DirectoryName;
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
