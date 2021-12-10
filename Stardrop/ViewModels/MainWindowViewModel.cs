using Avalonia.Collections;
using ReactiveUI;
using Stardrop.Models;
using Stardrop.Models.SMAPI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Stardrop.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private string _dragOverColor = "#ff9f2a";
        public string DragOverColor { get { return _dragOverColor; } set { this.RaiseAndSetIfChanged(ref _dragOverColor, value); } }
        private bool _isLocked;
        public bool IsLocked { get { return _isLocked; } set { this.RaiseAndSetIfChanged(ref _isLocked, value); } }
        public ObservableCollection<Mod> Mods { get; set; }
        private int _enabledModCount;
        public int EnabledModCount { get { return _enabledModCount; } set { this.RaiseAndSetIfChanged(ref _enabledModCount, value); } }
        public DataGridCollectionView DataView { get; set; }

        private bool _hideDisabledMods;
        public bool HideDisabledMods { get { return _hideDisabledMods; } set { _hideDisabledMods = value; UpdateFilter(); } }
        private string _filterText;
        public string FilterText { get { return _filterText; } set { _filterText = value; UpdateFilter(); } }
        private string _columnFilter;
        public string ColumnFilter { get { return _columnFilter; } set { _columnFilter = value; UpdateFilter(); } }
        private string _changeStateText;
        public string ChangeStateText { get { return _changeStateText; } set { this.RaiseAndSetIfChanged(ref _changeStateText, value); } }
        private string _updateStatusText = "Mods Ready to Update: Click to Refresh";
        public string UpdateStatusText { get { return _updateStatusText; } set { this.RaiseAndSetIfChanged(ref _updateStatusText, value); } }

        public MainWindowViewModel(string modsFilePath)
        {
            DiscoverMods(modsFilePath);

            // Create data view
            var dataGridSortDescription = DataGridSortDescription.FromPath(nameof(Mod.Name), ListSortDirection.Ascending);

            DataView = new DataGridCollectionView(Mods);
            DataView.SortDescriptions.Add(dataGridSortDescription);
        }

        public void OpenBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // If no associated application/json MimeType is found xdg-open opens retrun error
                // but it tries to open it anyway using the console editor (nano, vim, other..)
                ShellExec($"xdg-open {url}", waitForExit: false);
            }
            else
            {
                using Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? url : "open",
                    Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? $"{url}" : "",
                    CreateNoWindow = true,
                    UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                });
            }
        }

        private static void ShellExec(string cmd, bool waitForExit = true)
        {
            var escapedArgs = Regex.Replace(cmd, "(?=[`~!#&*()|;'<>])", "\\")
                .Replace("\"", "\\\\\\\"");

            using (var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"{escapedArgs}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            ))
            {
                if (waitForExit)
                {
                    process.WaitForExit();
                }
            }
        }

        public void DiscoverMods(string modsFilePath)
        {
            // TODO: Check for any cached update status file (within last hour)
            if (Mods is null)
            {
                Mods = new ObservableCollection<Mod>();
            }
            Mods.Clear();

            DirectoryInfo modDirectory = new DirectoryInfo(modsFilePath);
            foreach (var fileInfo in modDirectory.GetFiles("manifest.json", SearchOption.AllDirectories))
            {
                if (fileInfo.DirectoryName is null)
                {
                    continue;
                }

                try
                {
                    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(fileInfo.FullName), new JsonSerializerOptions { AllowTrailingCommas = true });
                    if (manifest is null)
                    {
                        Program.helper.Log($"The manifest.json was empty or not deserializable from {fileInfo.DirectoryName}", Utilities.Helper.Status.Alert);
                        continue;
                    }

                    var mod = new Mod(manifest, fileInfo, manifest.UniqueID, manifest.Version, manifest.Name, manifest.Description, manifest.Author);
                    if (!Mods.Any(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase)))
                    {
                        Mods.Add(mod);
                    }
                    else if (Mods.FirstOrDefault(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) && m.Version < mod.Version) is Mod oldMod && oldMod is not null)
                    {
                        // Replace old mod with newer one
                        int oldModIndex = Mods.IndexOf(Mods.First(m => m.UniqueId.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) && m.Version < mod.Version));
                        Mods[oldModIndex] = mod;
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load the manifest.json from {fileInfo.DirectoryName}: {ex}", Utilities.Helper.Status.Alert);
                }
            }
        }

        public void EnableModsByProfile(Profile profile)
        {
            foreach (var mod in Mods)
            {
                mod.IsEnabled = false;
                if (profile.EnabledModIds.Any(id => id.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase)))
                {
                    mod.IsEnabled = true;
                }
            }

            // Update the EnabledModCount
            EnabledModCount = Mods.Where(m => m.IsEnabled).Count();
        }

        internal void UpdateFilter()
        {
            DataView.Filter = null;
            DataView.Filter = ModFilter;
        }

        private bool ModFilter(object item)
        {
            var mod = item as Mod;

            if (_hideDisabledMods && !mod.IsEnabled)
            {
                return false;
            }
            if (!String.IsNullOrEmpty(_filterText) && !String.IsNullOrEmpty(_columnFilter))
            {
                if (_columnFilter == "Mod Name" && !mod.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else if (_columnFilter == "Author" && !mod.Author.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else if (_columnFilter == "Requirements" && !mod.Requirements.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
