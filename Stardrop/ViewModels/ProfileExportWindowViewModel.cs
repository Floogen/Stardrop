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
