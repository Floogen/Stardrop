using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Stardrop.Utilities.External;
using Stardrop.ViewModels;
using System;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    public partial class WarningWindow : Window
    {
        private readonly MainWindowViewModel _mainWindowModel;
        private readonly WarningWindowViewModel _viewModel;
        private bool _closeOnExitSMAPI;
        private bool _closeOnParentUnlock;

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

        public WarningWindow(string warningText, string buttonText) : this()
        {
            Program.helper.Log($"Created a warning window with the following text: [{buttonText}] {warningText}");

            _viewModel.WarningText = warningText;
            _viewModel.ButtonText = buttonText;
            _viewModel.IsButtonVisible = true;
        }

        /// <summary>
        /// For messages that carry more than a line or two, such as the collection install summary. Passing a width
        /// above the standard one also left aligns the text, as centring turns a list into a ragged column. The
        /// message scrolls once it outgrows the window.
        /// </summary>
        public WarningWindow(string warningText, string buttonText, double windowWidth) : this(warningText, buttonText)
        {
            _viewModel.WindowWidth = windowWidth;
        }

        public WarningWindow(string warningText, string buttonText, bool closeOnExitSMAPI) : this(warningText, buttonText)
        {
            _closeOnExitSMAPI = closeOnExitSMAPI;
        }

        public WarningWindow(string warningText, MainWindowViewModel model, bool closeOnParentUnlock = true) : this(warningText, String.Empty)
        {
            _mainWindowModel = model;
            _closeOnParentUnlock = closeOnParentUnlock;
            _viewModel.IsButtonVisible = false;
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
            this.Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
