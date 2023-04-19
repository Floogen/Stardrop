using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Stardrop.Models.Nexus.Web;
using Stardrop.Utilities;
using Stardrop.Utilities.External;
using Stardrop.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Stardrop
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            // Verify that the helper is instantiated, if it isn't then this code is likely reached by Avalonia's previewer and bypassed Main
            if (Program.helper is null)
            {
                Program.helper = new Helper();
            }

            // Load in translations
            Program.translation.LoadTranslations();

            // Handle adding the themes
            Dictionary<string, IStyle> themes = new Dictionary<string, IStyle>();
            foreach (string fileFullName in Directory.EnumerateFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes"), "*.xaml"))
            {
                try
                {
                    var themeName = Path.GetFileNameWithoutExtension(fileFullName);
                    themes[themeName] = AvaloniaRuntimeXamlLoader.Parse<Styles>(File.ReadAllText(fileFullName));
                    Program.helper.Log($"Loaded theme {Path.GetFileNameWithoutExtension(fileFullName)}", Helper.Status.Debug);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load theme on {Path.GetFileNameWithoutExtension(fileFullName)}: {ex}", Helper.Status.Warning);
                }
            }

            Current.Styles.Insert(0, !themes.ContainsKey(Program.settings.Theme) ? themes.Values.First() : themes[Program.settings.Theme]);
        }

        private async void OnUrlsOpen(object? sender, UrlOpenedEventArgs e, MainWindow mainWindow)
        {
            foreach (string? url in e.Urls.Where(u => String.IsNullOrEmpty(u) is false))
            {
                await mainWindow.ProcessNXMLink(Nexus.GetKey(), new NXM() { Link = url, Timestamp = DateTime.Now });
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;

                // Register events
                this.UrlsOpened += (sender, e) => OnUrlsOpen(sender, e, mainWindow);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
