using Stardrop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Stardrop.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<Mod> ModCollection { get; set; }

        public MainWindowViewModel()
        {
            //ModCollection = Mods.GetMods(@"E:\SteamLibrary\steamapps\common\Stardew Valley\Mods");
        }
    }
}
