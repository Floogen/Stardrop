using Stardrop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Stardrop.ViewModels
{
    public class ProfileEditorViewModel : ViewModelBase
    {
        public ObservableCollection<Profile> ProfileCollection { get; set; }

        public ProfileEditorViewModel()
        {
            //ProfileCollection = ...;
        }
    }
}
