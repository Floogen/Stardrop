using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    public partial class WarningWindow : Window
    {
        private readonly WarningWindowViewModel _viewModel;
        private Process _trackedProcess;

        public WarningWindow()
        {
            InitializeComponent();
            // Set the main window view
            _viewModel = new WarningWindowViewModel();
            DataContext = _viewModel;

#if DEBUG
            this.AttachDevTools();
#endif
        }

        public WarningWindow(string warningText, string buttonText) : this()
        {
            _viewModel.WarningText = warningText;
            _viewModel.ButtonText = buttonText;
        }

        public WarningWindow(string warningText, string buttonText, Process process) : this(warningText, buttonText)
        {
            _trackedProcess = process;
        }

        public override void Show()
        {
            base.Show();

            if (_trackedProcess is not null)
            {
                WaitForProcessToClose();
            }
        }

        private async Task WaitForProcessToClose()
        {
            await _trackedProcess.WaitForExitAsync();
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
