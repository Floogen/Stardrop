using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Stardrop.Views
{
    public partial class ProfileEditor : Window
    {
        private readonly ProfileEditorViewModel _viewModel;

        public ProfileEditor()
        {
            InitializeComponent();

#if DEBUG
            this.AttachDevTools();
#endif
        }

        public ProfileEditor(ProfileEditorViewModel viewModel) : this()
        {
            _viewModel = viewModel;

            // Load the profiles
            var profileListBox = this.FindControl<ListBox>("profileList");
            profileListBox.Items = _viewModel.Profiles;
            profileListBox.SelectedIndex = 0;
            profileListBox.SelectionChanged += ProfileListBox_SelectionChanged;

            // Handle the mainMenu bar for drag and related events
            var menuBar = this.FindControl<Menu>("menuBar");
            menuBar.PointerPressed += MainBar_PointerPressed;
            menuBar.DoubleTapped += MainBar_DoubleTapped;

            // Handle buttons
            this.FindControl<Button>("exitButton").Click += delegate { this.Close(); };
            this.FindControl<Button>("cancelButton").Click += delegate { this.Close(); };
            this.FindControl<Button>("applyButton").Click += ApplyButton_Click;
            this.FindControl<Button>("addButton").Click += AddButton_Click;
            this.FindControl<Button>("deleteButton").Click += DeleteButton_Click;
            this.FindControl<Button>("renameButton").Click += RenameButton_Click;
            this.FindControl<Button>("copyButton").Click += CopyButton_Click;
            this.FindControl<Button>("exportButton").Click += ExportButton_Click;
            this.FindControl<Button>("importButton").Click += ImportButton_Click;
        }

        private void ProfileListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var profile = this.FindControl<ListBox>("profileList").SelectedItem as Profile;
            if (profile is not null)
            {
                this.FindControl<Button>("deleteButton").IsEnabled = !profile.IsProtected;
                this.FindControl<Button>("renameButton").IsEnabled = !profile.IsProtected;
            }
        }

        private async void ImportButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Mod Profile",
                AllowMultiple = false,
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter
                    {
                        Name = "JSON files",
                        Extensions = new List<string> { "json" }
                    }
                }
            };

            string[]? files = await dialog.ShowAsync(this);
            if (files is not null && files.Length > 0)
            {
                try
                {
                    var externalProfile = JsonSerializer.Deserialize<ProfileExternal>(File.ReadAllText(files.First()), new JsonSerializerOptions { AllowTrailingCommas = true });
                    if (externalProfile is null)
                    {
                        Program.helper.Log($"Deserialized empty external profile during import");
                        return;
                    }

                    // Adjust the name if a copy of the name already exists
                    if (_viewModel.Profiles.Any(p => p.Name.Equals(externalProfile.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        externalProfile.Name = $"{externalProfile.Name} (Copy)";
                    }

                    // Verify all mods exist, otherwise alert on the ones that don't
                    var activeMods = _viewModel.GetMods();
                    var missingMods = new List<string>();

                    foreach (var modData in externalProfile.ModData)
                    {
                        if (modData is null)
                        {
                            continue;
                        }

                        if (activeMods.Any(m => m.UniqueId == modData.UniqueId) is false)
                        {
                            missingMods.Add(modData.UniqueId);
                        }
                    }

                    // Display warning for any missing mods
                    if (missingMods.Count > 0)
                    {
                        var requestWindow = new MessageWindow($"The following mods exist in the [{externalProfile.Name}] profile but are currently not installed:\n{string.Join(Environment.NewLine, missingMods)}\n\nContinue with profile import?");
                        if (await requestWindow.ShowDialog<bool>(this) is false)
                        {
                            return;
                        }
                    }

                    _viewModel.Profiles.Add(externalProfile);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed handle external profile import: {ex}");
                }
            }
        }

        private async void ExportButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedProfile = this.FindControl<ListBox>("profileList").SelectedItem as Profile;
            if (selectedProfile is null)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Profile",
                InitialFileName = $"{selectedProfile.Name}.json",
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "JSON", Extensions = { "json" } }
                }
            };

            string? path = await dialog.ShowAsync(this);
            if (path is not null)
            {
                var externalProfile = new ProfileExternal(_viewModel.GetMods()) { Name = selectedProfile.Name, EnabledModIds = selectedProfile.EnabledModIds, PreservedModConfigs = selectedProfile.PreservedModConfigs };
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(externalProfile, new JsonSerializerOptions() { WriteIndented = true }));
            }
        }

        private void CopyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var selectedProfile = this.FindControl<ListBox>("profileList").SelectedItem as Profile;

            int copyIndex = 1;
            var fileNameCopied = selectedProfile.Name + $" - Copy ({copyIndex})";
            while (_viewModel.Profiles.Any(p => p.Name == fileNameCopied))
            {
                copyIndex += 1;
                fileNameCopied = selectedProfile.Name + $" - Copy ({copyIndex})";
            }

            var copiedProfile = selectedProfile.ShallowCopy();
            copiedProfile.Name = fileNameCopied;
            copiedProfile.IsProtected = false;

            _viewModel.Profiles.Add(copiedProfile);
        }

        private void RenameButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var profileListBox = this.FindControl<ListBox>("profileList");
            var naming = new ProfileNaming(_viewModel, profileListBox.SelectedItem as Profile);
            naming.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            naming.ShowDialog(this);
        }

        private void DeleteButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var profile = this.FindControl<ListBox>("profileList").SelectedItem as Profile;
            _viewModel.Profiles.Remove(profile);
        }

        private async void AddButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var namingWindow = new ProfileNaming(_viewModel);
            namingWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var profile = await namingWindow.ShowDialog<Profile>(this);
            if (profile is not null && _viewModel.OldProfiles.Any(p => p.Name == profile.Name))
            {
                await new WarningWindow(String.Format(Program.translation.Get("ui.warning.unable_to_add_profile"), profile.Name), Program.translation.Get("internal.ok")).ShowDialog(this);
            }
        }

        private void ApplyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Save any changes made
            var oldProfileList = _viewModel.OldProfiles;
            var currentProfileList = _viewModel.Profiles;

            // Remove any deleted profiles
            foreach (var profile in oldProfileList.Where(old => !currentProfileList.Any(p => p.Name == old.Name)))
            {
                Program.helper.Log($"Deleting profile {profile.Name}");
                _viewModel.DeleteProfile(profile);
            }

            // Add any created profiles
            foreach (var profile in currentProfileList.Where(current => !oldProfileList.Any(p => p.Name == current.Name)))
            {
                Program.helper.Log($"Adding profile {profile.Name}");
                _viewModel.CreateProfile(profile);
            }

            _viewModel.OldProfiles = currentProfileList.ToList();
            this.Close();
        }

        private void MainBar_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!e.Handled)
            {
                this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
            }
        }

        private void MainBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.Pointer.IsPrimary && !e.Handled)
            {
                this.BeginMoveDrag(e);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
