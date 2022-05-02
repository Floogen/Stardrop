using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.Utilities.External;
using Stardrop.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    public partial class WarningWindow : Window
    {
        private readonly WarningWindowViewModel _viewModel;
        private bool _closeOnTrue;

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
            _viewModel.WarningText = warningText;
            _viewModel.ButtonText = buttonText;

            Program.helper.Log($"Created a warning window with the following text: [{buttonText}] {warningText}");
        }

        public WarningWindow(string warningText, string buttonText, bool closeOnTrue) : this(warningText, buttonText)
        {
            _closeOnTrue = closeOnTrue;
        }

        public override void Show()
        {
            base.Show();

            if (_closeOnTrue)
            {
                WaitForProcessToClose();
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
