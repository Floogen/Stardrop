using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using System;
using System.IO;

namespace Stardrop.Views
{
    public partial class ProfileEditor : Window
    {
        public ProfileEditor()
        {
            InitializeComponent();

            // Load the profiles
            var profileView = Profiles.GetProfiles(Path.Combine(Program.defaultHomePath, "Profiles"));
            var profileList = this.FindControl<ListBox>("profileList");
            profileList.Items = profileView;

            // Handle the mainMenu bar for drag and related events
            var menuBar = this.FindControl<Menu>("menuBar");
            menuBar.PointerPressed += MainMenu_PointerPressed;
            menuBar.DoubleTapped += MainMenu_DoubleTapped;

            // Handle buttons
            this.FindControl<Button>("exitButton").Click += delegate { this.Close(); };

#if DEBUG
            this.AttachDevTools();
#endif
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
