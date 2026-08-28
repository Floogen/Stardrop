using Avalonia.Layout;
using Avalonia.Media;
using ReactiveUI;
using Stardrop.Models.Data;
using Stardrop.Utilities;
using Stardrop.Utilities.Internal;
using System.Collections.ObjectModel;

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

        private double _windowWidth = 300;
        public double WindowWidth
        {
            get { return _windowWidth; }
            set
            {
                this.RaiseAndSetIfChanged(ref _windowWidth, value);
                this.RaisePropertyChanged(nameof(LinkMaxWidth));
            }
        }

        private TextAlignment _messageTextAlignment = TextAlignment.Center;
        public TextAlignment MessageTextAlignment { get { return _messageTextAlignment; } set { this.RaiseAndSetIfChanged(ref _messageTextAlignment, value); } }
        private HorizontalAlignment _messageHorizontalAlignment = HorizontalAlignment.Center;
        public HorizontalAlignment MessageHorizontalAlignment { get { return _messageHorizontalAlignment; } set { this.RaiseAndSetIfChanged(ref _messageHorizontalAlignment, value); } }

        private bool _isRichTextVisible;
        /// <summary>Swaps the plain text block out for the segment based display, which can render links</summary>
        public bool IsRichTextVisible { get { return _isRichTextVisible; } set { this.RaiseAndSetIfChanged(ref _isRichTextVisible, value); } }
        private ObservableCollection<RichTextLine> _warningLines = new ObservableCollection<RichTextLine>();
        public ObservableCollection<RichTextLine> WarningLines { get { return _warningLines; } set { this.RaiseAndSetIfChanged(ref _warningLines, value); } }

        /// <summary>Padding kept clear on either side of a link, covering the window border and the scroll bar</summary>
        private const double _linkWidthPadding = 60;
        /// <summary>
        /// Caps how wide a single link may get. A link is laid out as one item rather than as words, so an unbroken
        /// address would otherwise run past the edge of the window.
        /// </summary>
        public double LinkMaxWidth => _windowWidth - _linkWidthPadding;

        public WarningWindowViewModel()
        {

        }

        /// <summary>
        /// Reparses the current message so that any web addresses within it become links. Called after the text has
        /// been set, as the parse works off <see cref="WarningText"/>.
        /// </summary>
        public void EnableHyperlinks()
        {
            WarningLines = new ObservableCollection<RichTextLine>(HyperlinkParser.Parse(WarningText));
            IsRichTextVisible = WarningLines.Count > 0;
        }

        public void OpenLink(string url)
        {
            if (Toolkit.TryGetWebAddress(url, out var webAddress) is false)
            {
                Program.helper.Log($"Ignored a request to open a link that is not a web address: {url}", Helper.Status.Warning);
                return;
            }

            Toolkit.OpenBrowser(webAddress);
        }
    }
}
