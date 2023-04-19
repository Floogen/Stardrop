using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.Models.Data.Enums;
using Stardrop.ViewModels;
using System;

namespace Stardrop.Views
{
    public partial class FlexibleOptionWindow : Window
    {
        private readonly FlexibleOptionWindowViewModel _viewModel;

        public FlexibleOptionWindow()
        {
            InitializeComponent();

            // Set the main window view
            _viewModel = new FlexibleOptionWindowViewModel();
            DataContext = _viewModel;

#if DEBUG
            this.AttachDevTools();
#endif
        }

        public FlexibleOptionWindow(string messageText, string? firstButtonText = null, string? secondButtonText = null, string? thirdButtonText = null) : this()
        {
            _viewModel.MessageText = messageText;

            if (String.IsNullOrEmpty(firstButtonText) is false)
            {
                _viewModel.FirstButtonText = firstButtonText;
                _viewModel.IsFirstButtonVisible = true;
            }
            if (String.IsNullOrEmpty(secondButtonText) is false)
            {
                _viewModel.SecondButtonText = secondButtonText;
                _viewModel.IsSecondButtonVisible = true;
            }
            if (String.IsNullOrEmpty(thirdButtonText) is false)
            {
                _viewModel.ThirdButtonText = thirdButtonText;
                _viewModel.IsThirdButtonVisible = true;
            }

            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.SizeToContent = SizeToContent.Height;
        }

        private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Button? button = sender as Button;
            if (button is null)
            {
                return;
            }

            if (button.Content.Equals(_viewModel.FirstButtonText))
            {
                this.Close(Choice.First);
            }
            else if (button.Content.Equals(_viewModel.SecondButtonText))
            {
                this.Close(Choice.Second);
            }
            else if (button.Content.Equals(_viewModel.ThirdButtonText))
            {
                this.Close(Choice.Third);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
