using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using CommandLine;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;
using Stardrop.Models;
using Stardrop.Utilities;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Stardrop
{
    class Program
    {
        internal static Helper helper;
        internal static Settings settings = new Settings();
        internal static Translation translation = new Translation();

        internal static bool onBootStartSMAPI = false;
        internal static readonly string defaultProfileName = "Default";
        internal static readonly Regex gameDetailsPattern = new Regex(@"SMAPI (?<smapiVersion>.+) with Stardew Valley (?<gameVersion>.+) on (?<system>.+)");

        public class Options
        {
            [Option("start-smapi", Required = false, HelpText = "Automatically starts SMAPI based on the last selected mod profile.")]
            public bool StartSmapi { get; set; }
        }

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
                helper.Log($"{Environment.NewLine}-- Startup Data --{Environment.NewLine}Time: {DateTime.Now}{Environment.NewLine}OS: {RuntimeInformation.OSDescription}{Environment.NewLine}Settings Directory: {Pathing.defaultHomePath}{Environment.NewLine}Active Directory: {Directory.GetCurrentDirectory()}{Environment.NewLine}Version: {typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion}{Environment.NewLine}----------------------{Environment.NewLine}");
                helper.Log($"Started with the following arguments: {String.Join('|', args)}");

                // Set the argument values
                Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(o =>
                {
                    onBootStartSMAPI = o.StartSmapi;
                });

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

                // Set the default paths
                if (!String.IsNullOrEmpty(settings.ModFolderPath))
                {
                    Pathing.SetSmapiPath(settings.SMAPIFolderPath);
                    Pathing.SetModPath(settings.ModFolderPath);
                }
                else
                {
                    Pathing.SetSmapiPath(settings.SMAPIFolderPath, true);
                    settings.ModFolderPath = Pathing.defaultModPath;
                }

                // Load the translations
                if (String.IsNullOrEmpty(settings.Language))
                {
                    settings.Language = translation.GetLanguageFromAbbreviation(CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
                }
                translation.LoadTranslations(translation.GetLanguage(settings.Language));

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
