using Avalonia.Controls;
using ReactiveUI.Reactive;
using Stardrop.Models;
using System.Collections.ObjectModel;

namespace Stardrop.ViewModels
{
    public class MissingModsWindowViewModel : ViewModelBase
    {
        private ObservableCollection<PortableModData> _missingMods = [];
        public ObservableCollection<PortableModData> MissingMods
        {
            get { return _missingMods; }
            set { this.RaiseAndSetIfChanged(ref _missingMods, value); }
        }

        public MissingModsWindowViewModel()
        {
            MissingMods = [];
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
