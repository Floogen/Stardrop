using Avalonia.Controls;
using Stardrop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Stardrop.ViewModels
{
    public class MissingModsWindowViewModel : ViewModelBase
    {
        public ObservableCollection<PortableModData> MissingMods { get; set; }
        
        public MissingModsWindowViewModel()
        {
            MissingMods = new ObservableCollection<PortableModData>();
            if (Design.IsDesignMode)
            {
                MissingMods =
                [
                    new PortableModData("Test.Main.UniqueID", "1.0.0", "Mod Name #1", "Some Author", "https://www.nexusmods.com/stardewvalley/mods/10455"),
                    new PortableModData("Test.Other.UniqueID", "2.1.4", "Mod Name #2", "Other Author", "https://www.nexusmods.com/stardewvalley/mods/10455")
                ];
            }
        }
    }
}
