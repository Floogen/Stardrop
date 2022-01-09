using ReactiveUI;
using Stardrop.Models;
using Stardrop.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Stardrop.ViewModels
{
    public class SettingsWindowViewModel : ViewModelBase
    {
        // Setting bindings
        public string SMAPIPath { get { return Program.settings.SMAPIFolderPath; } set { Program.settings.SMAPIFolderPath = value; Pathing.SetSmapiPath(Program.settings.SMAPIFolderPath, String.IsNullOrEmpty(Program.settings.ModFolderPath)); } }
        public string ModFolderPath { get { return Program.settings.ModFolderPath; } set { Program.settings.ModFolderPath = value; Pathing.SetModPath(Program.settings.ModFolderPath); } }
        public bool IgnoreHiddenFolders { get { return Program.settings.IgnoreHiddenFolders; } set { Program.settings.IgnoreHiddenFolders = value; } }
        public bool EnableProfileSpecificModConfigs { get { return Program.settings.EnableProfileSpecificModConfigs; } set { Program.settings.EnableProfileSpecificModConfigs = value; } }

        // Tooltips
        public string ToolTip_SMAPI { get; set; }
        public string ToolTip_ModFolder { get; set; }
        public string ToolTip_Theme { get; set; }
        public string ToolTip_Language { get; set; }
        public string ToolTip_IgnoreHiddenFolders { get; set; }
        public string ToolTip_EnableProfileSpecificModConfigs { get; set; }
        public string ToolTip_Save { get; set; }
        public string ToolTip_Cancel { get; set; }

        // Other UI controls
        public bool ShowMainMenu { get; set; }

        public SettingsWindowViewModel()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ToolTip_SMAPI = Program.translation.Get("ui.settings_window.tooltips.smapi");
                ToolTip_ModFolder = Program.translation.Get("ui.settings_window.tooltips.mod_folder_path");
                ToolTip_Theme = Program.translation.Get("ui.settings_window.tooltips.theme");
                ToolTip_Language = Program.translation.Get("ui.settings_window.tooltips.language");
                ToolTip_IgnoreHiddenFolders = Program.translation.Get("ui.settings_window.tooltips.ignore_hidden_folders");
                ToolTip_EnableProfileSpecificModConfigs = Program.translation.Get("ui.settings_window.tooltips.enable_profile_specific_configs");
                ToolTip_Save = Program.translation.Get("ui.settings_window.tooltips.save_changes");
                ToolTip_Cancel = Program.translation.Get("ui.settings_window.tooltips.cancel_changes");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // TEMPORARY FIX: Due to bug with Avalonia on Linux platforms, tooltips currently cause crashes when they disappear
                // To work around this, tooltips are purposely not displayed
            }

            ShowMainMenu = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }
    }
}
