using System.IO;
using Serilog;
using Serilog.Events;

namespace GameShrink.App;

public partial class App : System.Windows.Application
{
    public static ILogger Log { get; private set; } = Serilog.Log.Logger;
    public static string AppDataDir { get; private set; } = string.Empty;
    public static string LogFilePath { get; private set; } = string.Empty;

    public App()
    {
        try
        {
            var earlyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameShrink");
            Directory.CreateDirectory(earlyDir);
            var earlyLog = Path.Combine(earlyDir, "bootstrap.log");
            File.AppendAllText(earlyLog, $"[{DateTime.Now:O}] App ctor reached\n");
            File.AppendAllText(earlyLog, $"[{DateTime.Now:O}] BaseDirectory: {AppContext.BaseDirectory}\n");

            var nativePath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "e_sqlite3.dll");
            File.AppendAllText(earlyLog, $"[{DateTime.Now:O}] e_sqlite3 path: {nativePath}\n");
            File.AppendAllText(earlyLog, $"[{DateTime.Now:O}] e_sqlite3 exists: {File.Exists(nativePath)}\n");

            var tryLoad = System.Runtime.InteropServices.NativeLibrary.TryLoad("e_sqlite3", out var handle);
            File.AppendAllText(earlyLog, $"[{DateTime.Now:O}] NativeLibrary.TryLoad('e_sqlite3'): {tryLoad}\n");
            if (tryLoad && handle != IntPtr.Zero)
            {
                System.Runtime.InteropServices.NativeLibrary.Free(handle);
                File.AppendAllText(earlyLog, $"[{DateTime.Now:O}] NativeLibrary.Free(handle)\n");
            }
        }
        catch (Exception ex)
        {
            try
            {
                var earlyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameShrink");
                var earlyLog = Path.Combine(earlyDir, "bootstrap.log");
                File.AppendAllText(earlyLog, $"[{DateTime.Now:O}] bootstrap exception: {ex}\n");
            }
            catch
            {
                // swallow: best-effort only
            }
        }

        InitializeComponent();
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
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
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.ToString(), "Startup logging failure", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            throw;
        }

        // Apply theme once, globally (keeps per-window constructors clean).
        try
        {
            Themes.ThemeManager.ApplyDarkTheme();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Theme initialization failed");
            System.Windows.MessageBox.Show(ex.ToString(), "Theme initialization failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            throw;
        }

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
