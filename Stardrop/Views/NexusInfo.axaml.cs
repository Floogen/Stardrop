using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.Models.Data;
using Stardrop.Models.Nexus;
using Stardrop.Utilities;
using System.IO;
using System.Text.Json;

namespace Stardrop.Views
{
    public partial class NexusInfo : Window
    {
        public NexusInfo()
        {
            InitializeComponent();
        }


        public NexusInfo(NexusUser nexusUser) : this()
        {
            // Handle buttons
            this.FindControl<Button>("exitButton")!.Click += delegate { Close(false); };
            this.FindControl<Button>("disconnectNexusButton")!.Click += DisconnectNexus_Click;

            this.FindControl<TextBlock>("nexusUserName")!.Text = string.Format(Program.translation.Get("ui.nexus_login.labels.username"), nexusUser.Username);
            this.FindControl<TextBlock>("nexusUserPremium")!.Text = string.Format(Program.translation.Get("ui.nexus_login.labels.is_premium"), nexusUser.IsPremium);
        }

        private void DisconnectNexus_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Program.settings.NexusDetails = new NexusUser();
            File.WriteAllText(Pathing.GetNotionCachePath(), JsonSerializer.Serialize(new PairedKeys(), new JsonSerializerOptions() { WriteIndented = true }));

            Close(true);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
