using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.ViewModels;
using System;
using System.Linq;

namespace Stardrop.Views
{
    public partial class ProfileNaming : Window
    {
        private readonly ProfileEditorViewModel _profileEditor;
        private Profile? _renameTarget;

        public ProfileNaming()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public ProfileNaming(ProfileEditorViewModel parentView, Profile? renameTarget = null) : this()
        {
            _profileEditor = parentView;
            _renameTarget = renameTarget;

            // Handle buttons
            this.FindControl<Button>("cancelButton").Click += delegate { this.Close(null); };
            this.FindControl<Button>("applyButton").Click += ApplyButton_Click;

            // Give focus to textbox
            var profileNameBox = this.FindControl<TextBox>("profileNameBox");
            profileNameBox.AttachedToVisualTree += (s, e) => profileNameBox.Focus();
            profileNameBox.KeyDown += ProfileNameBox_KeyDown;
        }

        private void ApplyChanges()
        {
            // Save any changes made
            var profileNameBox = this.FindControl<TextBox>("profileNameBox");

            Profile addedProfile = null;
            if (!String.IsNullOrEmpty(profileNameBox.Text))
            {
                if (_renameTarget is not null)
                {
                    _profileEditor.Profiles.Remove(_renameTarget);
                }

                addedProfile = new Profile(profileNameBox.Text, false, _renameTarget is null ? null : _renameTarget.EnabledModIds);
                if (!_profileEditor.Profiles.Any(p => p.Name == addedProfile.Name))
                {
                    _profileEditor.Profiles.Add(addedProfile);
                }
            }

            this.Close(addedProfile);
        }

        private void ProfileNameBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyChanges();
            }
        }

        private void ApplyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ApplyChanges();
        }

        private void MainMenu_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!e.Handled)
            {
                this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
            }
        }

        private void MainMenu_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
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
