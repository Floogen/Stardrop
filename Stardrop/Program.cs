using Avalonia;
using Avalonia.ReactiveUI;
using CommandLine;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;
using Semver;
using Stardrop.Models;
using Stardrop.Models.Nexus;
using Stardrop.Models.Nexus.Web;
using Stardrop.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Stardrop
{
    class Program
    {
        internal static Helper helper;
        internal static Settings settings = new Settings();
        internal static Translation translation = new Translation();

        internal static bool onBootStartSMAPI = false;
        internal static string? nxmLink = null;
        internal static readonly string defaultProfileName = "Default";
        internal static readonly string executablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Stardrop.exe");
        internal static readonly Regex gameDetailsPattern = new Regex(@"SMAPI (?<smapiVersion>.+) with Stardew Valley (?<gameVersion>.+) on (?<system>.+)");

        public static string ApplicationVersion { get { return $"{_applicationVersion.WithoutMetadata()}"; } }
        private static readonly SemVersion _applicationVersion = SemVersion.Parse(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion, SemVersionStyles.Any);

        public class Options
        {
            [Option("start-smapi", Required = false, HelpText = "Automatically starts SMAPI based on the last selected mod profile.")]
            public bool StartSmapi { get; set; }
            [Option("nxm", Required = false, HelpText = "Downloads the given NXM file from Nexus Mods.")]
            public string? NXMLink { get; set; }
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

            // Verify if another instance is already running
            if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Count() > 1 && RuntimeInformation.IsOSPlatform(OSPlatform.OSX) is false)
            {
                helper = new Helper($"nxm", ".txt", Pathing.GetLogFolderPath());

                HandleSecondaryInstance(args);
                return;
            }

            // Set up our logger
            helper = new Helper("log", ".txt", Pathing.GetLogFolderPath());

            try
            {
                var operatingSystem = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Unix" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Unknown";
                helper.Log($"{Environment.NewLine}-- Startup Data --{Environment.NewLine}Time: {DateTime.Now}{Environment.NewLine}OS: [{operatingSystem}] {RuntimeInformation.OSDescription}{Environment.NewLine}Settings Directory: {Pathing.defaultHomePath}{Environment.NewLine}Active Directory: {Directory.GetCurrentDirectory()}{Environment.NewLine}Version: v{ApplicationVersion}{Environment.NewLine}----------------------{Environment.NewLine}");
                helper.Log($"Started with the following arguments: {String.Join('|', args)}");

                // Set the argument values
                Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(o =>
                {
                    onBootStartSMAPI = o.StartSmapi;
                    nxmLink = o.NXMLink;
                });

                // Verify the folder paths are created
                Directory.CreateDirectory(Pathing.GetCacheFolderPath());
                Directory.CreateDirectory(Pathing.GetLogFolderPath());
                Directory.CreateDirectory(Pathing.GetProfilesFolderPath());
                Directory.CreateDirectory(Pathing.GetSelectedModsFolderPath());
                Directory.CreateDirectory(Pathing.GetNexusPath());
                Directory.CreateDirectory(Pathing.GetSmapiUpgradeFolderPath());

                // Verify the settings folder path is created
                if (File.Exists(Pathing.GetSettingsPath()))
                {
                    try
                    {
                        settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Pathing.GetSettingsPath()), new JsonSerializerOptions { AllowTrailingCommas = true });
                    }
                    catch (JsonException ex)
                    {
                        settings = new Settings();
                        helper.Log($"Reset the settings.json file as it was unreadable: {ex}", Helper.Status.Alert);
                    }
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

                // Set the default mod install path (for mods that are installed by Stardrop)
                if (!String.IsNullOrEmpty(Pathing.defaultModPath) && String.IsNullOrEmpty(settings.ModInstallPath))
                {
                    settings.ModInstallPath = Path.Combine(Pathing.defaultModPath, "Stardrop Installed Mods");
                }

                // Set the default Nexus Mods information
                if (settings.NexusDetails is null)
                {
                    settings.NexusDetails = new NexusUser();
                }

                // Delete any files underneath the Nexus folder
                var nexusDirectory = new DirectoryInfo(Pathing.GetNexusPath());
                foreach (FileInfo file in nexusDirectory.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in nexusDirectory.GetDirectories())
                {
                    dir.Delete(true);
                }

                // Load the translations
                if (String.IsNullOrEmpty(settings.Language))
                {
                    settings.Language = translation.GetLanguageFromAbbreviation(CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
                }
                translation.LoadTranslations(translation.GetLanguage(settings.Language));

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                helper.Log(ex, Helper.Status.Alert);
            }
        }

        private static void HandleSecondaryInstance(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(o =>
            {
                nxmLink = o.NXMLink;
            });

            // Verify NXM link is valid
            if (String.IsNullOrEmpty(nxmLink))
            {
                Program.helper.Log("Given empty NXM link");
                return;
            }

            // Write to bridge file
            int attempts = 0;

            Program.helper.Log("STARTING");
            while (true)
            {
                try
                {
                    using (FileStream stream = new FileStream(Pathing.GetLinksCachePath(), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        List<NXM> links;
                        try
                        {
                            links = JsonSerializer.DeserializeAsync<List<NXM>>(stream, new JsonSerializerOptions { AllowTrailingCommas = true }).Result;

                            if (links is null)
                            {
                                links = new List<NXM>();
                            }
                        }
                        catch (JsonException ex)
                        {
                            links = new List<NXM>();
                        }

                        try
                        {
                            links.Add(new NXM() { Link = nxmLink, Timestamp = DateTime.Now });
                            Program.helper.Log(links.Count());

                            stream.SetLength(0);

                            JsonSerializer.SerializeAsync(stream, links, new JsonSerializerOptions() { WriteIndented = true });

                            break;
                        }
                        catch (Exception ex)
                        {
                            Program.helper.Log(ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.helper.Log(ex);
                }

                if (attempts >= 3)
                {
                    return;
                }
                else
                {
                    attempts += 1;
                    Thread.Sleep(500);
                }
            }

            Program.helper.Log("DONE");
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UseReactiveUI()
                .UsePlatformDetect()
                .LogToTrace()
                .WithIcons(container => container
                .Register<MaterialDesignIconProvider>());
        }
    }
}
