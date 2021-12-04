using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Collections;
using Stardrop.Models;
using System.ComponentModel;
using Avalonia.Data;
using Stardrop.ViewModels;
using System.IO;
using System.Linq;

namespace Stardrop.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly ProfileEditorViewModel _editorView;

        public MainWindow()
        {
            InitializeComponent();

            // Set the main window view
            _viewModel = new MainWindowViewModel(Program.defaultModPath);

            var dataGridSortDescription = DataGridSortDescription.FromPath(nameof(Mod.Name), ListSortDirection.Ascending);

            // Set the path according to the environmental variable SMAPI_MODS_PATH
            // SMAPI_MODS_PATH is set via the profile dropdown on the UI
            var modView = new DataGridCollectionView(_viewModel.Mods);
            modView.SortDescriptions.Add(dataGridSortDescription);
            var modGrid = this.FindControl<DataGrid>("modGrid");
            modGrid.IsReadOnly = true;
            modGrid.LoadingRow += Dg1_LoadingRow;
            modGrid.Sorting += (s, a) =>
            {
                var binding = (a.Column as DataGridBoundColumn)?.Binding as Binding;

                if (binding?.Path is string property
                    && property == dataGridSortDescription.PropertyPath
                    && !modView.SortDescriptions.Contains(dataGridSortDescription))
                {
                    modView.SortDescriptions.Add(dataGridSortDescription);
                }
            };
            modGrid.Items = modView;

            // Handle the mainMenu bar for drag and related events
            var mainMenu = this.FindControl<Menu>("mainMenu");
            mainMenu.PointerPressed += MainMenu_PointerPressed;
            mainMenu.DoubleTapped += MainMenu_DoubleTapped;

            // Set profile list
            _editorView = new ProfileEditorViewModel(Path.Combine(Program.defaultHomePath, "Profiles"));
            var profileComboBox = this.FindControl<ComboBox>("profileComboBox");
            profileComboBox.Items = _editorView.Profiles;
            profileComboBox.SelectedIndex = 0;

            // Handle buttons
            this.FindControl<Button>("minimizeButton").Click += delegate { this.WindowState = WindowState.Minimized; };
            this.FindControl<Button>("maximizeButton").Click += delegate { AdjustWindowState(); };
            this.FindControl<Button>("exitButton").Click += ExitButton_Click;
            this.FindControl<Button>("editProfilesButton").Click += EditProfiles_Click;
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void EnabledBox_Clicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var checkBox = e.Source as CheckBox;
            if (checkBox is null)
            {
                return;
            }

            // Update the profile's enabled mods
            var profile = this.FindControl<ComboBox>("profileComboBox").SelectedItem as Profile;
            var enabledModIds = this.FindControl<DataGrid>("modGrid").Items.Cast<Mod>().Where(m => m.IsEnabled).Select(m => m.UniqueId).ToList();
            _editorView.UpdateProfile(profile, enabledModIds);
        }

        private void EditProfiles_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var editorWindow = new ProfileEditor(_editorView);
            editorWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            editorWindow.ShowDialog(this);
        }

        private void ExitButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainMenu_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!e.Handled)
            {
                AdjustWindowState();
            }
        }

        private void MainMenu_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.Pointer.IsPrimary && !e.Handled)
            {
                this.BeginMoveDrag(e);
            }
        }

        private void Dg1_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.Header = e.Row.GetIndex() + 1;
        }

        private void AdjustWindowState()
        {
            this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
