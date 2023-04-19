using Stardrop.Utilities;
using System;
using System.Runtime.InteropServices;

namespace Stardrop.ViewModels
{
    public class SettingsWindowViewModel : ViewModelBase
    {
        // Setting bindings
        public string SMAPIPath { get { return Program.settings.SMAPIFolderPath; } set { Program.settings.SMAPIFolderPath = value; Pathing.SetSmapiPath(Program.settings.SMAPIFolderPath, String.IsNullOrEmpty(Program.settings.ModFolderPath)); } }
        public string ModFolderPath { get { return Program.settings.ModFolderPath; } set { Program.settings.ModFolderPath = value; Pathing.SetModPath(Program.settings.ModFolderPath); } }
        public string ModInstallPath { get { return Program.settings.ModInstallPath; } set { Program.settings.ModInstallPath = value; } }
        public bool IgnoreHiddenFolders { get { return Program.settings.IgnoreHiddenFolders; } set { Program.settings.IgnoreHiddenFolders = value; } }
        public bool IsAskingBeforeAcceptingNXM { get { return Program.settings.IsAskingBeforeAcceptingNXM; } set { Program.settings.IsAskingBeforeAcceptingNXM = value; } }
        public bool EnableProfileSpecificModConfigs { get { return Program.settings.EnableProfileSpecificModConfigs; } set { Program.settings.EnableProfileSpecificModConfigs = value; } }
        public bool EnableModsOnAdd { get { return Program.settings.EnableModsOnAdd; } set { Program.settings.EnableModsOnAdd = value; } }

        // Tooltips
        public string ToolTip_SMAPI { get; set; }
        public string ToolTip_ModFolder { get; set; }
        public string ToolTip_ModInstall { get; set; }
        public string ToolTip_Theme { get; set; }
        public string ToolTip_Language { get; set; }
        public string ToolTip_IgnoreHiddenFolders { get; set; }
        public string ToolTip_PreferredServer { get; set; }
        public string ToolTip_NXMAssociation { get; set; }
        public string ToolTip_AlwaysAskNXMFiles { get; set; }
        public string ToolTip_EnableProfileSpecificModConfigs { get; set; }
        public string ToolTip_EnableModsOnAdd { get; set; }
        public string ToolTip_Save { get; set; }
        public string ToolTip_Cancel { get; set; }

        // Other UI controls
        public bool ShowMainMenu { get; set; }
        public bool ShowNXMAssociationButton { get; set; }
        public bool ShowNexusServers { get; set; }

        public SettingsWindowViewModel()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ToolTip_SMAPI = Program.translation.Get("ui.settings_window.tooltips.smapi");
                ToolTip_ModFolder = Program.translation.Get("ui.settings_window.tooltips.mod_folder_path");
                ToolTip_ModInstall = Program.translation.Get("ui.settings_window.tooltips.mod_install_path");
                ToolTip_Theme = Program.translation.Get("ui.settings_window.tooltips.theme");
                ToolTip_Language = Program.translation.Get("ui.settings_window.tooltips.language");
                ToolTip_IgnoreHiddenFolders = Program.translation.Get("ui.settings_window.tooltips.ignore_hidden_folders");
                ToolTip_PreferredServer = Program.translation.Get("ui.settings_window.tooltips.preferred_server");
                ToolTip_NXMAssociation = Program.translation.Get("ui.settings_window.tooltips.nxm_file_association");
                ToolTip_AlwaysAskNXMFiles = Program.translation.Get("ui.settings_window.tooltips.always_ask_nxm_files");
                ToolTip_EnableProfileSpecificModConfigs = Program.translation.Get("ui.settings_window.tooltips.enable_profile_specific_configs");
                ToolTip_EnableModsOnAdd = Program.translation.Get("ui.settings_window.tooltips.enable_mods_on_add");
                ToolTip_Save = Program.translation.Get("ui.settings_window.tooltips.save_changes");
                ToolTip_Cancel = Program.translation.Get("ui.settings_window.tooltips.cancel_changes");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // TEMPORARY FIX: Due to bug with Avalonia on Linux platforms, tooltips currently cause crashes when they disappear
                // To work around this, tooltips are purposely not displayed
            }

            ShowMainMenu = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            ShowNXMAssociationButton = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            ShowNexusServers = Program.settings.NexusDetails is not null && Program.settings.NexusDetails.IsPremium;
        }
    }
}
