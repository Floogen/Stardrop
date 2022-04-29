using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Stardrop.ViewModels;

namespace Stardrop.Views
{
    public partial class NexusLogin : Window
    {
        public NexusLogin()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public NexusLogin(MainWindowViewModel viewModel) : this()
        {
            // Handle buttons
            this.FindControl<Button>("cancelButton").Click += delegate { this.Close(null); };
            this.FindControl<Button>("goToNexusButton").Click += delegate { viewModel.OpenBrowser("https://www.nexusmods.com/users/myaccount?tab=api"); };

            var applyButton = this.FindControl<Button>("applyButton");
            applyButton.Click += ApplyButton_Click;
            applyButton.IsEnabled = false;

            // Give focus to textbox
            var apiKeyBox = this.FindControl<TextBox>("apiBox");
            apiKeyBox.AttachedToVisualTree += (s, e) => apiKeyBox.Focus();
            apiKeyBox.KeyDown += KeyBox_KeyDown;
            apiKeyBox.KeyUp += KeyBox_KeyUp;
        }

        private void ApplyChanges()
        {
            var apiKeyBox = this.FindControl<TextBox>("apiBox");

            this.Close(apiKeyBox.Text);
        }

        private void KeyBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyChanges();
            }
        }

        private void KeyBox_KeyUp(object? sender, KeyEventArgs e)
        {
            var apiKeyBox = sender as TextBox;
            var applyButton = this.FindControl<Button>("applyButton");
            applyButton.IsEnabled = string.IsNullOrEmpty(apiKeyBox.Text) is false;
        }

        private void ApplyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ApplyChanges();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
