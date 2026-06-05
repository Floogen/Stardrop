using Avalonia.Controls;

namespace Stardrop.ViewModels
{
    public class ProfileExportWindowViewModel : ViewModelBase
    {
        public bool IncludeModConfigs { get; set; }
        public bool IncludeDisabledMods { get; set; }
        public bool IncludeModNotes { get; set; }

        public ProfileExportWindowViewModel()
        {
            if (Design.IsDesignMode)
            {

            }
        }
    }
}
