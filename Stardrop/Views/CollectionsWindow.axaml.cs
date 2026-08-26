using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Stardrop.Utilities;
using Stardrop.ViewModels;
using System;

namespace Stardrop.Views
{
    /// <summary>
    /// Read-only view over the installed collection records. Its job is to give the entries a collection could not
    /// install somewhere the user can come back to, since the summary shown once at the end of an install is gone
    /// by the time they have finished fetching anything by hand.
    /// </summary>
    public partial class CollectionsWindow : Window
    {
        private readonly CollectionsWindowViewModel _viewModel = new CollectionsWindowViewModel();

        public CollectionsWindow()
        {
            InitializeComponent();

#if DEBUG
            this.AttachDevTools();
#endif

            DataContext = _viewModel;

            // Handle the menu bar for drag and related events
            var menuBar = this.FindControl<Menu>("menuBar");
            menuBar.PointerPressed += MainBar_PointerPressed;
            menuBar.DoubleTapped += MainBar_DoubleTapped;

            // Handle buttons
            this.FindControl<Button>("exitButton").Click += delegate { this.Close(); };
            this.FindControl<Button>("openPageButton").Click += OpenPageButton_Click;

            // Skipped in the previewer, which has no paths set up to read the cache from
            if (Design.IsDesignMode is false)
            {
                _viewModel.Load();
            }
        }

        private void OpenPageButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedCollection is null)
            {
                return;
            }

            Toolkit.OpenBrowser(_viewModel.SelectedCollection.PageUri);
        }

        private void OnNameHeaderTapped(object? sender, RoutedEventArgs e)
        {
            _viewModel.SortBy(CollectionSortColumn.Name);
        }

        private void OnVersionHeaderTapped(object? sender, RoutedEventArgs e)
        {
            _viewModel.SortBy(CollectionSortColumn.Version);
        }

        private void OnStatusHeaderTapped(object? sender, RoutedEventArgs e)
        {
            _viewModel.SortBy(CollectionSortColumn.Status);
        }

        /// <summary>
        /// Opens an entry's page on a double click rather than a single one, so that selecting a row to read its
        /// status does not send the user to a browser they did not ask for.
        /// </summary>
        private void OnEntryDoubleTapped(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control { DataContext: CollectionEntryView entry } || String.IsNullOrEmpty(entry.PageUri))
            {
                return;
            }

            // A curator can point an entry anywhere, so an address that is neither of the two sites Stardrop sends
            // people to is left alone rather than handed to the browser
            if (Toolkit.IsFromNexusMods(entry.PageUri) is false && Toolkit.IsFromGitHub(entry.PageUri) is false)
            {
                Program.helper.Log($"Not opening the page for the collection entry {entry.Name}, as {entry.PageUri} is not a supported address");
                return;
            }

            Toolkit.OpenBrowser(entry.PageUri);
        }

        private void MainBar_DoubleTapped(object? sender, RoutedEventArgs e)
        {
            if (e.Handled is false)
            {
                this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
            }
        }

        private void MainBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.Pointer.IsPrimary && e.Handled is false)
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
