using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.ViewModels;

namespace Stardrop.Views
{
    public partial class ProfileNaming : Window
    {
        private readonly ProfileEditorViewModel _profileEditor = null!;
        private readonly Profile? _renameTarget;

        public ProfileNaming()
        {
            InitializeComponent();
        }

        public ProfileNaming(ProfileEditorViewModel parentView, Profile? renameTarget = null) : this()
        {
            _profileEditor = parentView;
            _renameTarget = renameTarget;

            // Handle buttons
            this.FindControl<Button>("cancelButton")!.Click += delegate { Close(null); };
            this.FindControl<Button>("applyButton")!.Click += ApplyButton_Click;

            // Give focus to textbox
            var profileNameBox = this.FindControl<TextBox>("profileNameBox")!;
            profileNameBox.AttachedToVisualTree += (s, e) => profileNameBox.Focus();
            profileNameBox.KeyDown += ProfileNameBox_KeyDown;
        }

        private void ApplyChanges()
        {
            // Save any changes made
            var profileNameBox = this.FindControl<TextBox>("profileNameBox")!;

            Profile? addedProfile = null;
            if (!string.IsNullOrEmpty(profileNameBox.Text))
            {
                if (_renameTarget is not null)
                {
                    _profileEditor.Profiles.Remove(_renameTarget);
                }

                // A rename carries the whole profile across, where rebuilding it from the enabled mods alone lost its notes and preserved configs
                addedProfile = _renameTarget is null ? new Profile(profileNameBox.Text) : _renameTarget.CopyWithName(profileNameBox.Text);
                if (_profileEditor.IsProfileNameTaken(addedProfile.Name) is false)
                {
                    _profileEditor.Profiles.Add(addedProfile);
                }
            }

            Close(addedProfile);
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
                WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
            }
        }

        private void MainMenu_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Pointer.IsPrimary && !e.Handled)
            {
                BeginMoveDrag(e);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
