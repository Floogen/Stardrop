using ReactiveUI;
using Stardrop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Text.Json;

namespace Stardrop.ViewModels
{
    public class FlexibleOptionWindowViewModel : ViewModelBase
    {
        private string _messageText;
        public string MessageText { get { return _messageText; } set { this.RaiseAndSetIfChanged(ref _messageText, value); } }
        private string _firstButtonText;
        public string FirstButtonText { get { return _firstButtonText; } set { this.RaiseAndSetIfChanged(ref _firstButtonText, value); } }
        private string _secondButtonText;
        public string SecondButtonText { get { return _secondButtonText; } set { this.RaiseAndSetIfChanged(ref _secondButtonText, value); } }
        private string _thirdButtonText;
        public string ThirdButtonText { get { return _thirdButtonText; } set { this.RaiseAndSetIfChanged(ref _thirdButtonText, value); } }

        private bool _isFirstButtonVisible;
        public bool IsFirstButtonVisible { get { return _isFirstButtonVisible; } set { this.RaiseAndSetIfChanged(ref _isFirstButtonVisible, value); } }
        private bool _isSecondButtonVisible;
        public bool IsSecondButtonVisible { get { return _isSecondButtonVisible; } set { this.RaiseAndSetIfChanged(ref _isSecondButtonVisible, value); } }
        private bool _isThirdButtonVisible;
        public bool IsThirdButtonVisible { get { return _isThirdButtonVisible; } set { this.RaiseAndSetIfChanged(ref _isThirdButtonVisible, value); } }
    }
}
