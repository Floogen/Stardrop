using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Stardrop.Utilities;
using Stardrop.ViewModels;
using System;
using System.Net.WebSockets;

namespace Stardrop.Views
{
    public partial class NexusLogin : Window
    {
        private NexusWebsocket? _nexusWebsocket;
        public NexusLogin()
        {
            InitializeComponent();
            _nexusWebsocket = new NexusWebsocket();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public NexusLogin(MainWindowViewModel viewModel) : this()
        {
            HandleNexusFlow();
            // Handle buttons
            this.FindControl<Button>("cancelButton").Click += delegate { this.Close(null); };
            this.FindControl<Button>("exitButton").Click += delegate { this.Close(null); };
            this.FindControl<Button>("goToNexusButton").Click += delegate { viewModel.OpenBrowser(_nexusWebsocket.ssoUrl); };

            var applyButton = this.FindControl<Button>("applyButton");
            applyButton.Click += ApplyButton_Click;
            applyButton.IsEnabled = false;
        }

        private async void HandleNexusFlow()
        {
            var result = await _nexusWebsocket.ConnectAsync();

            if (result.Error is not null)
            {
                Program.helper.Log($"Error getting API key: {result.Error}", Helper.Status.Warning);
            }
            else
            {
                var apiKeyBox = this.FindControl<TextBox>("apiBox");
                apiKeyBox.Text = result.ApiKey ?? string.Empty;

                var applyButton = this.FindControl<Button>("applyButton");
                applyButton.IsEnabled = true;
            }
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
