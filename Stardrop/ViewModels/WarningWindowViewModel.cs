using ReactiveUI;
using Stardrop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Stardrop.ViewModels
{
    public class WarningWindowViewModel : ViewModelBase
    {
        private string _warningText;
        public string WarningText { get { return _warningText; } set { this.RaiseAndSetIfChanged(ref _warningText, value); } }
        private string _buttonText;
        public string ButtonText { get { return _buttonText; } set { this.RaiseAndSetIfChanged(ref _buttonText, value); } }
        private bool _isButtonVisible;
        public bool IsButtonVisible { get { return _isButtonVisible; } set { this.RaiseAndSetIfChanged(ref _isButtonVisible, value); } }

        public WarningWindowViewModel()
        {

        }
    }
}
