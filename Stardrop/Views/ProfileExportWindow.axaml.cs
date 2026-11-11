using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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
        private readonly Profile? _exportProfile;
        private readonly List<Mod>? _mods;
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        public ProfileExportWindow() : base()
        {
            InitializeComponent();
            
            // Handle buttons
            this.FindControl<Button>("cancelButton")!.Click += delegate { Close(); };
            this.FindControl<Button>("applyButton")!.Click += ApplyButton_Click;
        }

        public ProfileExportWindow(Profile profile, List<Mod>? mods) : this()
        {
            _exportProfile = profile;
            _mods = mods;
        }

        private async void ApplyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_exportProfile is null || DataContext is not ProfileExportWindowViewModel viewModel)
            {
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Program.translation.Get("ui.window.profiles_export.name"),
                SuggestedFileName = $"{_exportProfile.Name}.json",
                FileTypeChoices = 
                [
                    new FilePickerFileType("JSON")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });

            if (file is not null)
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
                await File.WriteAllTextAsync(file.Path.LocalPath, JsonSerializer.Serialize(externalProfile, WriteOptions));

                Close();
            }
        }
    }
}