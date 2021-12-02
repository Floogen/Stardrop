using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stardrop
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Mod> Mods { get; set; }

        public MainWindow()
        {
            Mods = new ObservableCollection<Mod>()
            {
                new Mod() { Name = "TEST 1" },
                new Mod() { Name = "TEST 2" }
            };


            DataContext = this;
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            this.Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }

    public class Mod
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool IsSelected { get; set; }
    }
}
