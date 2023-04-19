using ReactiveUI;

namespace Stardrop.ViewModels
{
    public class MessageWindowViewModel : ViewModelBase
    {
        private string _messageText;
        public string MessageText { get { return _messageText; } set { this.RaiseAndSetIfChanged(ref _messageText, value); } }
        private string _positiveButtonText;
        public string PositiveButtonText { get { return _positiveButtonText; } set { this.RaiseAndSetIfChanged(ref _positiveButtonText, value); } }
        private string _negativeButtonText;
        public string NegativeButtonText { get { return _negativeButtonText; } set { this.RaiseAndSetIfChanged(ref _negativeButtonText, value); } }
    }
}
