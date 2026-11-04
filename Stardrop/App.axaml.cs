using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Stardrop.Models.Nexus.Web;
using Stardrop.Utilities;
using Stardrop.Utilities.Internal;
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
                Program.translation.LoadTranslations();
            }

            // Handle adding the themes
            Dictionary<string, IStyle> themes = [];
            foreach (string fileFullName in ThemeManager.GetThemeFilePaths())
            {
                try
                {
                    var themeName = Path.GetFileNameWithoutExtension(fileFullName);
                    themes[themeName] = ThemeManager.Load(fileFullName);
                    Program.helper.Log($"Loaded theme {Path.GetFileNameWithoutExtension(fileFullName)}", Helper.Status.Debug);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Unable to load theme on {Path.GetFileNameWithoutExtension(fileFullName)}: {ex}", Helper.Status.Warning);
                }
            }

            Styles.Insert(ThemeManager.THEME_STYLE_INDEX, !themes.TryGetValue(Program.settings.Theme, out IStyle? value) ? themes.Values.First() : value);
#if DEBUG
                this.AttachDeveloperTools();
#endif
        }

        private static async void OnUrlsOpen(object? sender, UrlOpenedEventArgs e, MainWindow mainWindow)
        {
            foreach (string? url in e.Urls.Where(u => string.IsNullOrEmpty(u) is false))
            {
                await mainWindow.ProcessNXMLink(new NXM() { Link = url, Timestamp = DateTime.Now });
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;

                // Register events
                if (Current?.TryGetFeature<IActivatableLifetime>() is { } activatable)
                {
                    activatable.Activated += (sender, args) =>
                    {
                        if (args is ProtocolActivatedEventArgs p)
                            OnUrlsOpen(sender, new UrlOpenedEventArgs([p.Uri.ToString()]), mainWindow);
                    };
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
