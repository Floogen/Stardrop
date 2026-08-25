using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.Models.Data.Enums;
using Stardrop.Utilities.Internal;
using Stardrop.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    public partial class ProfileEditor : Window
    {
        private readonly ProfileEditorViewModel _viewModel;
        private readonly Func<string, Task<List<Mod>>>? _addModDirectly;
        private readonly Func<Task>? _refreshModList;
        private bool _hasAppliedChanges;

        public ProfileEditor()
        {
            InitializeComponent();

#if DEBUG
            this.AttachDevTools();
#endif
        }

        public ProfileEditor(ProfileEditorViewModel viewModel, Func<string, Task<List<Mod>>> addModDirectly, Func<Task>? refreshModList) : this()
        {
            _viewModel = viewModel;
            _addModDirectly = addModDirectly;
            _refreshModList = refreshModList;

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
                // A collection profile is protected against editing, though it still has to be removable
                this.FindControl<Button>("deleteButton").IsEnabled = profile.IsProtected is false || profile.IsFromCollection;
                this.FindControl<Button>("renameButton").IsEnabled = profile.IsProtected is false;
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
                    var missingMods = new List<PortableModData>();

                    foreach (var modData in externalProfile.ModData)
                    {
                        if (modData is null)
                        {
                            continue;
                        }

                        if (activeMods.Any(m => m.UniqueId == modData.UniqueId) is false)
                        {
                            missingMods.Add(modData);
                        }
                    }

                    // For any missing mods that exist on the profile but are not currently installed: ask if user wants to manually add them, skip the missing ones or cancel
                    if (missingMods.Count > 0)
                    {
                        int displayOffsetCount = 5;
                        var missingModParsed = string.Join(Environment.NewLine, missingMods.Select(m => m.UniqueId).Take(displayOffsetCount));
                        if (missingMods.Count > displayOffsetCount)
                        {
                            missingModParsed += String.Format(Program.translation.Get("ui.message.missing_mod_extra_count"), missingMods.Count - displayOffsetCount);
                        }

                        //String.Format(Program.translation.Get("ui.message.confirm_mod_deletion"), mod.Name))
                        var requestWindow = new FlexibleOptionWindow(String.Format(Program.translation.Get("ui.message.confirm_missing_mod_handling"), externalProfile.Name, missingModParsed), Program.translation.Get("internal.yes"), Program.translation.Get("internal.ignore"), Program.translation.Get("internal.cancel"))
                        {
                            Topmost = true
                        };

                        Choice response = await requestWindow.ShowDialog<Choice>(this);
                        if (response == Choice.First)
                        {
                            // Open window for showing missing mods with their URI
                            var missingModsWindow = new MissingModsWindow(missingMods, _addModDirectly);
                            missingModsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            await missingModsWindow.ShowDialog(this);

                            // Handle refreshing mod list, to handle any new mod additions
                            if (_refreshModList is not null)
                            {
                                await _refreshModList.Invoke();
                            }
                        }
                        else if (response == Choice.Third)
                        {
                            // Cancel import
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

            // Display the ProfileExportWindow
            var profileExportWindow = new ProfileExportWindow(selectedProfile, _viewModel.GetMods());
            profileExportWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await profileExportWindow.ShowDialog(this);
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

        private async void DeleteButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var profile = this.FindControl<ListBox>("profileList").SelectedItem as Profile;
            if (profile is null)
            {
                return;
            }

            // Removing a collection takes its downloaded mods with it unless the user says otherwise, so it needs
            // asking about rather than being a silent side effect of deleting a profile
            if (profile.IsFromCollection && String.IsNullOrEmpty(profile.SourceId) is false)
            {
                var collection = CollectionCache.Load(profile.SourceId);
                var modCount = collection is null ? 0 : collection.Mods.Count(m => m.Status is CollectionModStatus.Installed);

                var requestWindow = new FlexibleOptionWindow(String.Format(Program.translation.Get("ui.message.confirm_collection_delete"), profile.Name, modCount), Program.translation.Get("ui.message.collection_delete_with_mods"), Program.translation.Get("ui.message.collection_delete_keep_mods"), Program.translation.Get("internal.cancel"), windowWidth: 420)
                {
                    Topmost = true
                };

                Choice response = await requestWindow.ShowDialog<Choice>(this);
                if (response == Choice.Third)
                {
                    return;
                }

                _viewModel.MarkCollectionForRemoval(profile.SourceId, response == Choice.First);
            }

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

            _hasAppliedChanges = true;
            this.Close();
        }

        /// <summary>
        /// Anything short of applying discards the changes. Handled here rather than on the cancel button so that
        /// the exit button and the escape key behave the same way.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_hasAppliedChanges is false && _viewModel is not null)
            {
                Program.helper.Log("Discarding unapplied profile editor changes");
                _viewModel.RevertChanges();
            }

            base.OnClosing(e);
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
