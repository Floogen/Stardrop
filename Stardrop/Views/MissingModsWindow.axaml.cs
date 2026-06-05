using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Stardrop.Models;
using Stardrop.Utilities;
using Stardrop.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    public partial class MissingModsWindow : BaseWindow
    {
        private readonly Func<string, Task<List<Mod>>>? _addModDirectly;

        public MissingModsWindow() : base()
        {
            AvaloniaXamlLoader.Load(this);

            // Handle buttons
            this.FindControl<Button>("exitButton").Click += delegate { this.Close(); };
            this.FindControl<Button>("continueButton").Click += delegate { this.Close(); };

            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        public MissingModsWindow(List<PortableModData> missingMods, Func<string, Task<List<Mod>>> addModDirectlyAction) : this()
        {
            _addModDirectly = addModDirectlyAction;

            if (DataContext is MissingModsWindowViewModel viewModel)
            {
                foreach (var missingMod in missingMods)
                {
                    viewModel.MissingMods.Add(missingMod);
                }
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            // Only accept the drop if it actually contains files
            e.DragEffects = e.Data.Contains(DataFormats.FileNames) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void OnDrop(object? sender, DragEventArgs e)
        {
            if (_addModDirectly is null)
            {
                return;
            }

            if (!e.Data.Contains(DataFormats.FileNames))
            {
                return;
            }

            var files = e.Data.GetFileNames(); 
            if (files is not null && DataContext is MissingModsWindowViewModel viewModel)
            {
                foreach (var path in files)
                {
                    foreach (var addedMod in (await _addModDirectly.Invoke(path)).Where(m => m is not null))
                    {
                        if (viewModel.MissingMods.FirstOrDefault(m => m.UniqueId.Equals(addedMod.UniqueId, StringComparison.OrdinalIgnoreCase)) is PortableModData portableModData && portableModData is not null)
                        {
                            viewModel.MissingMods.Remove(portableModData);
                        }
                    }
                }
            }
        }

        private void OnGridTapped(object? sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: PortableModData modData })
            {
                if (string.IsNullOrEmpty(modData.ModPageUri))
                {
                    return;
                }
                else if (Toolkit.IsFromNexusMods(modData.ModPageUri) is false && Toolkit.IsFromGitHub(modData.ModPageUri) is false)
                {
                    Program.helper.Log($"Unsupported ModPageUri detected for mod {modData.UniqueId}: {modData.ModPageUri}");
                    return;
                }

                Toolkit.OpenBrowser(modData.ModPageUri);
            }
        }
    }
}