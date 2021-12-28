using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.ViewModels;
using System;
using System.IO;
using System.Linq;

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

            _viewModel.Profiles.Add(new Profile(fileNameCopied, false, selectedProfile.EnabledModIds));
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
                await new WarningWindow($"Unable to add {profile.Name}, a profile already exists under that name!", "OK").ShowDialog(this);
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
