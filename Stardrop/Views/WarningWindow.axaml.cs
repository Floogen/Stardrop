using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Stardrop.Views
{
    public partial class WarningWindow : Window
    {
        private Process _trackedProcess;
        public WarningWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public WarningWindow(Process process) : this()
        {
            _trackedProcess = process;
        }

        public override void Show()
        {
            base.Show();

            WaitForProcessToClose();
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
