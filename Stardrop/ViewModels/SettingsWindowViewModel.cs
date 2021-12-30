using ReactiveUI;
using Stardrop.Models;
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
        public string ToolTip_SMAPI { get; set; }
        public string ToolTip_Theme { get; set; }
        public string ToolTip_IgnoreHiddenFolders { get; set; }
        public string ToolTip_Save { get; set; }
        public string ToolTip_Cancel { get; set; }

        public SettingsWindowViewModel()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ToolTip_SMAPI = "The file path of StardewModdingAPI";
                ToolTip_Theme = "The current theme of Stardrop";
                ToolTip_IgnoreHiddenFolders = "If checked, Stardrop will ignore any mods which have a parent folder that start with \".\"";
                ToolTip_Save = "Save Changes";
                ToolTip_Cancel = "Cancel";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // TEMPORARY FIX: Due to bug with Avalonia on Linux platforms, tooltips currently cause crashes when they disappear
                // To work around this, tooltips are purposely not displayed
            }
        }
    }
}
