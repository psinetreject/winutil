using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using WinUtil.App.DependencyInjection;
using WinUtil.Core.DependencyInjection;
using WinUtil.Platform.DependencyInjection;

namespace WinUtil.App;

/// <summary>
/// WPF application. The DI container is built inside <see cref="OnStartup"/> (not the constructor) and
/// the main window is shown synchronously on the UI thread — the robust WPF + DI pattern.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Attach global handlers first so nothing fails silently.
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            ShowFatal(ev.ExceptionObject as Exception, "AppDomain.UnhandledException");
        // A dispatcher (UI-thread) exception — often a recoverable binding glitch — must NOT kill the
        // app. Log it and keep running; only genuinely fatal startup failures shut the app down.
        DispatcherUnhandledException += (_, ev) =>
        {
            try { Log.Error(ev.Exception, "Unhandled dispatcher exception (recovered)"); }
            catch (Exception) { /* logging may be down */ }
            ev.Handled = true;
        };

        try
        {
            base.OnStartup(e);

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "winutil", "logs");
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(logDir, "winutil-.log"), rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddSerilog(dispose: true));
            services.AddWinUtilCore();      // config + tweak engine
            services.AddWinUtilPlatform();  // Windows system services
            services.AddWinUtilApp();       // view-models, views, theming
            _services = services.BuildServiceProvider();

            // Theme is applied by MainViewModel's constructor when the window resolves.
            _services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            ShowFatal(ex, "OnStartup");
        }
    }

    private void ShowFatal(Exception? ex, string origin)
    {
        var text = ex?.ToString() ?? "Unknown error (null exception).";
        try { Log.Fatal(ex, "Fatal startup error ({Origin})", origin); } catch (Exception) { /* logging may be down */ }
        MessageBox.Show(text, $"WinUtil failed to start ({origin})", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
