using Avalonia.Layout;
using Avalonia.Media;
using ReactiveUI;

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
        private bool _isButtonEnabled = true;
        public bool IsButtonEnabled { get { return _isButtonEnabled; } set { this.RaiseAndSetIfChanged(ref _isButtonEnabled, value); } }
        private bool _isProgressBarVisible;
        public bool IsProgressBarVisible { get { return _isProgressBarVisible; } set { this.RaiseAndSetIfChanged(ref _isProgressBarVisible, value); } }
        private double _progressBarValue;
        public double ProgressBarValue { get { return _progressBarValue; } set { this.RaiseAndSetIfChanged(ref _progressBarValue, value); } }
        /// <summary>The standard width. Anything wider is assumed to be carrying a list rather than a single line</summary>
        public const double DefaultWidth = 300;

        private double _windowWidth = DefaultWidth;
        public double WindowWidth { get { return _windowWidth; } set { this.RaiseAndSetIfChanged(ref _windowWidth, value); } }
        /// <summary>
        /// Centred suits a one line warning. Anything carrying a list, such as the collection install summary, is
        /// far easier to read left aligned.
        /// </summary>
        private TextAlignment _messageTextAlignment = TextAlignment.Center;
        public TextAlignment MessageTextAlignment { get { return _messageTextAlignment; } set { this.RaiseAndSetIfChanged(ref _messageTextAlignment, value); } }
        private HorizontalAlignment _messageHorizontalAlignment = HorizontalAlignment.Center;
        public HorizontalAlignment MessageHorizontalAlignment { get { return _messageHorizontalAlignment; } set { this.RaiseAndSetIfChanged(ref _messageHorizontalAlignment, value); } }

        public WarningWindowViewModel()
        {

        }
    }
}
