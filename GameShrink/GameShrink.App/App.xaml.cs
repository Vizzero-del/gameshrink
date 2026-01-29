using System.IO;
using Serilog;
using Serilog.Events;

namespace GameShrink.App;

public partial class App : System.Windows.Application
{
    public static ILogger Log { get; private set; } = Serilog.Log.Logger;
    public static string AppDataDir { get; private set; } = string.Empty;
    public static string LogFilePath { get; private set; } = string.Empty;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameShrink");
        Directory.CreateDirectory(AppDataDir);

        // Use a rolling filename pattern so "Log" button can reliably find the latest file.
        // Serilog will create files like: gameshrink20260129.log
        LogFilePath = Path.Combine(AppDataDir, "gameshrink-.log");

        Log = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .WriteTo.File(LogFilePath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10, shared: true)
            .WriteTo.Console()
            .CreateLogger();

        Serilog.Log.Logger = Log;
        Log.Information("GameShrink started");

        // Apply theme once, globally (keeps per-window constructors clean).
        Themes.ThemeManager.ApplyDarkTheme();

        this.DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error(ex.Exception, "Unhandled UI exception");
            System.Windows.MessageBox.Show(ex.Exception.ToString(), "Unhandled error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            ex.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            try
            {
                Log.Error(ex.ExceptionObject as Exception, "Unhandled domain exception");
            }
            catch { /* ignore */ }
        };
    }
}
