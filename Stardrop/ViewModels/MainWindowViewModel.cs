using Stardrop.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stardrop.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public Mods Mods { get; set; }
        public string Greeting => "Welcome to Avalonia!";

        public MainWindowViewModel()
        {
            Mods = new Mods(null);
        }
    }
}
