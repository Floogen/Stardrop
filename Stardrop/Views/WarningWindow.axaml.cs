using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Stardrop.Utilities.External;
using Stardrop.ViewModels;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    public partial class WarningWindow : Window
    {
        private readonly MainWindowViewModel _mainWindowModel;
        private readonly WarningWindowViewModel _viewModel;
        private bool _closeOnExitSMAPI;
        private bool _closeOnParentUnlock;
        private CancellationTokenSource? _cancellationSource;

        public WarningWindow()
        {
            InitializeComponent();

            // Set the datacontext
            _viewModel = new WarningWindowViewModel();
            DataContext = _viewModel;

            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.SizeToContent = SizeToContent.Height;

#if DEBUG
            this.AttachDevTools();
#endif
        }

        /// <summary>
        /// Enable hyperlinks for messages that may carry web addresses, such as a collection's install report. The
        /// message is then laid out as words rather than as a single block of text, which also left aligns it.
        /// </summary>
        public WarningWindow(string warningText, string buttonText, double? windowWidth = null, bool enableHyperlinks = false) : this()
        {
            Program.helper.Log($"Created a warning window with the following text: [{buttonText}] {warningText}");

            _viewModel.WarningText = warningText;
            _viewModel.ButtonText = buttonText;
            _viewModel.IsButtonVisible = true;

            if (windowWidth is not null)
            {
                _viewModel.WindowWidth = windowWidth.Value;
            }

            if (enableHyperlinks)
            {
                _viewModel.EnableHyperlinks();
            }
        }

        public WarningWindow(string warningText, string buttonText, bool closeOnExitSMAPI) : this(warningText, buttonText)
        {
            _closeOnExitSMAPI = closeOnExitSMAPI;
        }

        /// <summary>
        /// The lock window variant. Supplying a cancellation source shows a cancel button, which signals the running
        /// operation rather than closing the window. Closing is left to whatever unlocks the main window, so the
        /// window stays up while the operation winds down.
        /// </summary>
        public WarningWindow(string warningText, MainWindowViewModel model, bool closeOnParentUnlock = true, CancellationTokenSource? cancellationSource = null) : this(warningText, String.Empty)
        {
            _mainWindowModel = model;
            _closeOnParentUnlock = closeOnParentUnlock;
            _cancellationSource = cancellationSource;

            _viewModel.IsButtonVisible = cancellationSource is not null;
            _viewModel.ButtonText = Program.translation.Get("internal.cancel");
            _viewModel.IsProgressBarVisible = false;
        }

        public void UpdateProgress(string? text = null, int? progress = null, int? maxProgress = null)
        {
            if (text is not null)
            {
                _viewModel.WarningText = text;
            }

            if (maxProgress is null || maxProgress == 0)
            {
                maxProgress = progress is null ? 1 : progress.Value;
            }

            _viewModel.IsProgressBarVisible = progress is not null && progress.Value >= 0;
            _viewModel.ProgressBarValue = progress is null ? 0 : (progress.Value / (double)maxProgress.Value) * 100;
        }

        public override void Show()
        {
            base.Show();

            if (_closeOnExitSMAPI)
            {
                WaitForProcessToClose();
            }

            if (_closeOnParentUnlock)
            {
                WaitForParentToUnlock();
            }
        }

        private async Task WaitForProcessToClose()
        {
            while (SMAPI.IsRunning)
            {
                await Task.Delay(500);
            }
            this.Close();
        }

        private async Task WaitForParentToUnlock()
        {
            while (_mainWindowModel.IsLocked)
            {
                await Task.Delay(500);
            }
            this.Close();
        }

        private void UnlockButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_cancellationSource is null)
            {
                this.Close();
                return;
            }

            // An in-flight download does not stop instantly, so the button reports that the request landed rather
            // than closing a window that is still showing a running operation
            Program.helper.Log("The user requested cancellation from the lock window");
            _cancellationSource.Cancel();

            _viewModel.IsButtonEnabled = false;
            _viewModel.ButtonText = Program.translation.Get("internal.cancelling");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
