using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.ViewModels;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Stardrop.Views
{
    public partial class ProfileExportWindow : BaseWindow
    {
        private Profile? _exportProfile;
        private List<Mod>? _mods;

        public ProfileExportWindow() : base()
        {
            AvaloniaXamlLoader.Load(this);

            // Handle buttons
            this.FindControl<Button>("cancelButton").Click += delegate { this.Close(); };
            this.FindControl<Button>("applyButton").Click += ApplyButton_Click;
        }

        public ProfileExportWindow(Profile profile, List<Mod>? mods) : this()
        {
            _exportProfile = profile;
            _mods = mods;
        }

        private async void ApplyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_exportProfile is null || DataContext is not ProfileExportWindowViewModel viewModel || viewModel is null)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Profile",
                InitialFileName = $"{_exportProfile.Name}.json",
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "JSON", Extensions = { "json" } }
                }
            };

            string? path = await dialog.ShowAsync(this);
            if (path is not null)
            {
                var externalProfile = new ProfileExternal() { Name = _exportProfile.Name, EnabledModIds = _exportProfile.EnabledModIds };
                if (viewModel.IncludeModNotes)
                {
                    externalProfile.Notes = _exportProfile.Notes;
                }

                // Optionally include mod configs
                if (viewModel.IncludeModConfigs)
                {
                    externalProfile.PreservedModConfigs = _exportProfile.PreservedModConfigs;
                }

                // Add the mod list (optionally include disabled mods)
                if (_mods is not null)
                {
                    externalProfile.AddMods(viewModel.IncludeDisabledMods ? _mods : _mods.Where(m => m.IsEnabled).ToList());
                }

                // Save the exported profile
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(externalProfile, new JsonSerializerOptions() { WriteIndented = true }));

                this.Close();
            }
        }
    }
}