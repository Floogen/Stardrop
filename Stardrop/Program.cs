using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;
using Stardrop.Utilities;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Stardrop
{
    class Program
    {
        internal static Helper helper = new Helper();
        internal static readonly string defaultProfileName = "Default";
        internal static readonly Regex gameDetailsPattern = new Regex(@"SMAPI (?<smapiVersion>.+) with Stardew Valley (?<gameVersion>.+) on (?<system>.+)");

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Register icon provider(s)
            IconProvider.Register<MaterialDesignIconProvider>();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            // TODO: Load settings file here and pass it to Pathing
            Pathing.EstablishPaths();
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
        }
    }
}
