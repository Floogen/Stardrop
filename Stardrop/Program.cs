using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;
using Stardrop.Models;
using Stardrop.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Stardrop
{
    class Program
    {
        internal static Helper helper;
        internal static Settings settings = new Settings();
        internal static readonly string defaultProfileName = "Default";
        internal static readonly Regex gameDetailsPattern = new Regex(@"SMAPI (?<smapiVersion>.+) with Stardew Valley (?<gameVersion>.+) on (?<system>.+)");

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Enforce the directory's path
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName));

            // Establish file and folders paths
            Pathing.SetHomePath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

            // Set up our logger
            helper = new Helper("log", ".txt", Pathing.GetLogFolderPath());

            try
            {
                // Verify the folder paths are created
                Directory.CreateDirectory(Pathing.GetCacheFolderPath());
                Directory.CreateDirectory(Pathing.GetLogFolderPath());
                Directory.CreateDirectory(Pathing.GetProfilesFolderPath());
                Directory.CreateDirectory(Pathing.GetSelectedModsFolderPath());

                // Verify the settings folder path is created
                if (File.Exists(Pathing.GetSettingsPath()))
                {
                    settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Pathing.GetSettingsPath()), new JsonSerializerOptions { AllowTrailingCommas = true });
                }
                Pathing.SetModPath(settings.SMAPIFolderPath);

                // Register icon provider(s)
                IconProvider.Register<MaterialDesignIconProvider>();

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                helper.Log(ex, Helper.Status.Alert);
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UseReactiveUI()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
