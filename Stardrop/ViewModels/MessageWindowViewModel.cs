using ReactiveUI.Reactive;

namespace Stardrop.ViewModels
{
    public class MessageWindowViewModel : ViewModelBase
    {
        private string _messageText = null!;
        public string MessageText { get { return _messageText; } set { this.RaiseAndSetIfChanged(ref _messageText, value); } }
        private string _positiveButtonText = null!;
        public string PositiveButtonText { get { return _positiveButtonText; } set { this.RaiseAndSetIfChanged(ref _positiveButtonText, value); } }
        private string _negativeButtonText = null!;
        public string NegativeButtonText { get { return _negativeButtonText; } set { this.RaiseAndSetIfChanged(ref _negativeButtonText, value); } }
    }
}
