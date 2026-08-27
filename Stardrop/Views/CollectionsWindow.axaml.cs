using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.Models.Data.Enums;
using Stardrop.Utilities;
using Stardrop.Utilities.Internal;
using Stardrop.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    /// <summary>
    /// View over the installed collection records. Its job is to give the entries a collection could not install
    /// somewhere the user can come back to, since the summary shown once at the end of an install is gone by the
    /// time they have finished fetching anything by hand. Removing a collection also lives here, as the profile
    /// editor now treats a generated profile as read-only.
    /// </summary>
    public partial class CollectionsWindow : Window
    {
        // Past this many pages the browser is handed enough tabs to be worth asking about first
        private const int _bulkOpenConfirmationThreshold = 5;
        // A browser given several addresses in one go will drop some of them, so they are spaced out
        private static readonly TimeSpan _bulkOpenDelay = TimeSpan.FromMilliseconds(250);

        private readonly CollectionsWindowViewModel _viewModel = new CollectionsWindowViewModel();
        private readonly ProfileEditorViewModel? _editorView;
        private readonly Action? _onCollectionRemoved;
        private readonly Func<string[], Task>? _onFilesDropped;
        private readonly Func<Task>? _onRefreshRequested;

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

            // Handled at the window, as the panel below is the only thing that takes a drop and the event bubbles
            // up to here from it
            AddHandler(DragDrop.DropEvent, OnFilesDropped);
            AddHandler(DragDrop.DragOverEvent, OnFilesDraggedOver);

            // Handle buttons
            this.FindControl<Button>("exitButton").Click += delegate { this.Close(); };
            this.FindControl<Button>("openPageButton").Click += OpenPageButton_Click;
            this.FindControl<Button>("openMissingButton").Click += OpenMissingButton_Click;
            this.FindControl<Button>("removeButton").Click += RemoveButton_Click;
            this.FindControl<Button>("refreshButton").Click += RefreshButton_Click;

            // Skipped in the previewer, which has no paths set up to read the cache from
            if (Design.IsDesignMode is false)
            {
                _viewModel.Load();
            }
        }

        public CollectionsWindow(ProfileEditorViewModel editorView, Action onCollectionRemoved, Func<string[], Task>? onFilesDropped = null, Func<Task>? onRefreshRequested = null) : this()
        {
            _editorView = editorView;
            _onCollectionRemoved = onCollectionRemoved;
            _onFilesDropped = onFilesDropped;
            _onRefreshRequested = onRefreshRequested;
        }

        /// <summary>
        /// Asks the main window to re-run the revision check, then reloads so the answer lands in the list. The
        /// records are written to the cache by the check itself, so nothing is handed back here.
        /// </summary>
        private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_onRefreshRequested is null || _viewModel.IsCheckingForUpdates)
            {
                return;
            }

            _viewModel.IsCheckingForUpdates = true;

            try
            {
                await _onRefreshRequested();
            }
            finally
            {
                _viewModel.IsCheckingForUpdates = false;
            }

            _viewModel.Load();
        }

        /// <summary>
        /// Takes in a file the user fetched themselves, leaving its checksum to say which entry it belongs to.
        /// </summary>
        private async void OnFilesDropped(object? sender, DragEventArgs e)
        {
            if (_onFilesDropped is null || e.Data.Contains(DataFormats.FileNames) is false)
            {
                return;
            }

            var filePaths = e.Data.GetFileNames()?.ToArray();
            if (filePaths is null || filePaths.Length == 0)
            {
                return;
            }

            await _onFilesDropped(filePaths);

            RefreshCollections();
        }

        private void OnFilesDraggedOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = _onFilesDropped is not null && e.Data.Contains(DataFormats.FileNames) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// <summary>
        /// Re-reads the cached records. Used when something lands while this window is open, such as a mod fetched
        /// through the link on one of its own rows, so that the entry stops reporting itself as missing. The
        /// selected collection is held across the reload, so the user is not moved off what they were looking at.
        /// </summary>
        public void RefreshCollections()
        {
            _viewModel.Load();
        }

        /// <summary>
        /// Puts the result of something that just landed in the footer, beside the button that opens the collection
        /// page. Takes the place of the dialog this used to raise, which a user working through a page of missing
        /// entries had to dismiss once per mod before the next one could be handled.
        /// </summary>
        public void ShowStatusMessage(string message, bool isFailure = false)
        {
            _viewModel.IsStatusMessageFailure = isFailure;
            _viewModel.StatusMessage = message;
        }

        private void OpenPageButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedCollection is null)
            {
                return;
            }

            Toolkit.OpenBrowser(_viewModel.SelectedCollection.PageUri);
        }

        /// <summary>
        /// Opens a tab for every entry the collection is still waiting on, so that a handful of manual downloads can
        /// be worked through in one pass rather than a double click at a time. The pages follow the order the list is
        /// sorted by and each is opened on its own, since a browser handed them all at once will lose some.
        /// </summary>
        private async void OpenMissingButton_Click(object? sender, RoutedEventArgs e)
        {
            var pageUris = _viewModel.GetMissingPageUris();
            if (pageUris.Count == 0)
            {
                return;
            }

            // A large collection can leave dozens of entries outstanding, which is more than someone reaching for
            // this button is likely to have meant
            if (pageUris.Count > _bulkOpenConfirmationThreshold)
            {
                var requestWindow = new MessageWindow(String.Format(Program.translation.Get("ui.message.confirm_open_missing_pages"), pageUris.Count))
                {
                    Topmost = true
                };

                if (await requestWindow.ShowDialog<bool>(this) is false)
                {
                    return;
                }
            }

            Program.helper.Log($"Opening {pageUris.Count} page(s) for the entries the collection {_viewModel.SelectedCollection?.SourceId} is still waiting on");

            foreach (var pageUri in pageUris)
            {
                Toolkit.OpenBrowser(pageUri);

                await Task.Delay(_bulkOpenDelay);
            }
        }

        /// <summary>
        /// Removes a collection, taking its downloaded mods with it unless the user says otherwise. Keeping them
        /// detaches the generated profile rather than deleting it, so the mods are still named by something after
        /// the record they came from is gone.
        /// </summary>
        private async void RemoveButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedCollection is not CollectionView collection || _editorView is null)
            {
                return;
            }

            var dependentProfiles = CollectionsWindowViewModel.GetDependentProfiles(_editorView.Profiles, collection);

            var message = String.Format(Program.translation.Get("ui.message.confirm_collection_delete"), collection.Name, collection.InstalledModCount);
            if (dependentProfiles.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + String.Format(Program.translation.Get("ui.message.collection_delete_dependents"), dependentProfiles.Count);
            }

            message += Environment.NewLine + Environment.NewLine + Program.translation.Get("ui.message.collection_delete_keep_note");

            var requestWindow = new FlexibleOptionWindow(message, Program.translation.Get("ui.message.collection_delete_with_mods"), Program.translation.Get("ui.message.collection_delete_keep_mods"), Program.translation.Get("internal.cancel"), windowWidth: 460)
            {
                Topmost = true
            };

            Choice response = await requestWindow.ShowDialog<Choice>(this);
            if (response == Choice.Third)
            {
                return;
            }

            var deleteInstalledMods = response == Choice.First;
            Program.helper.Log($"Removing the collection {collection.SourceId}{(deleteInstalledMods ? " along with its downloaded mods" : ", keeping its downloaded mods")}");

            CollectionCache.Delete(collection.SourceId, deleteInstalledMods);
            HandleGeneratedProfile(collection, deleteInstalledMods);

            _viewModel.Load();
            _onCollectionRemoved?.Invoke();
        }

        private void HandleGeneratedProfile(CollectionView collection, bool deleteInstalledMods)
        {
            if (_editorView is null || _editorView.Profiles.FirstOrDefault(p => p.Name.Equals(collection.ProfileName, StringComparison.OrdinalIgnoreCase)) is not Profile profile)
            {
                return;
            }

            // With the mods gone the profile would enable nothing, so it goes with them. Kept and detached
            // otherwise, since it is then the only record of which of those mods the curator had enabled
            if (deleteInstalledMods)
            {
                _editorView.RemoveProfileNow(profile);
                return;
            }

            _editorView.DetachCollectionProfile(profile);
        }

        /// <summary>
        /// Sends the user to the page for the revision they do not have. Nothing is installed from here, as the
        /// curator's notes are the only thing Stardrop can offer until the update path exists.
        /// </summary>
        private void OnUpdateStatusTapped(object? sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedCollection is null || String.IsNullOrEmpty(_viewModel.SelectedCollection.UpdatePageUri))
            {
                return;
            }

            Toolkit.OpenBrowser(_viewModel.SelectedCollection.UpdatePageUri);
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
