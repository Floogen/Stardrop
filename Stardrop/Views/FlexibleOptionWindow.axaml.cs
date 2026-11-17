using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.Models.Data.Enums;
using Stardrop.ViewModels;

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

        }

        public FlexibleOptionWindow(string messageText, string? firstButtonText = null, string? secondButtonText = null, string? thirdButtonText = null, double? windowWidth = null) : this()
        {
            _viewModel.MessageText = messageText;

            if (string.IsNullOrEmpty(firstButtonText) is false)
            {
                _viewModel.FirstButtonText = firstButtonText;
                _viewModel.IsFirstButtonVisible = true;
            }
            if (string.IsNullOrEmpty(secondButtonText) is false)
            {
                _viewModel.SecondButtonText = secondButtonText;
                _viewModel.IsSecondButtonVisible = true;
            }
            if (string.IsNullOrEmpty(thirdButtonText) is false)
            {
                _viewModel.ThirdButtonText = thirdButtonText;
                _viewModel.IsThirdButtonVisible = true;
            }

            if (windowWidth is not null)
            {
                _viewModel.WindowWidth = windowWidth.Value;
            }

            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.Height;
        }

        private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            if (button.Content?.Equals(_viewModel.FirstButtonText) is true)
            {
                Close(Choice.First);
            }
            else if (button.Content?.Equals(_viewModel.SecondButtonText) is true)
            {
                Close(Choice.Second);
            }
            else if (button.Content?.Equals(_viewModel.ThirdButtonText) is true)
            {
                Close(Choice.Third);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
