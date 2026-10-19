using Avalonia;
using System;
using System.Threading.Tasks;

namespace OmniConnect.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Surface unexpected failures instead of letting the process vanish.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportFatal(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ReportFatal(e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void ReportFatal(Exception? ex)
    {
        try
        {
            Console.Error.WriteLine($"[OmniConnect] Fatal: {ex}");
        }
        catch { /* no console attached */ }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
