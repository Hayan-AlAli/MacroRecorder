using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MacroApp.Core;
using MacroApp.Core.Models;
using MacroApp.Core.Playback;
using MacroApp.Core.Recording;
using MacroApp.NativeInterop;
using MacroApp.UI.ViewModels;

namespace MacroApp.UI;

/// <summary>
/// Application entry point. Sets up DI, logging, and global exception handling.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Global service provider for dependency injection.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Serilog logger instance.
    /// </summary>
    public static ILogger Logger { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure Serilog
        Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "macroapp-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Logger.Information("MacroApp {Version} starting up", AppConstants.AppVersion);

        // Configure DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        // NativeInterop
        services.AddSingleton<HookManager>();
        services.AddSingleton<InputSimulator>();
        services.AddSingleton<HotKeyManager>();

        // Core
        string storagePath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, AppConstants.DefaultMacroStoragePath);
        services.AddSingleton(sp => new MacroManager(storagePath));
        services.AddSingleton<RecordingEngine>();
        services.AddSingleton<PlaybackEngine>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MacroListViewModel>();
        services.AddTransient<EditorViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Information("MacroApp shutting down");

        // Ensure all hooks are uninstalled
        try
        {
            var hookManager = Services.GetService<HookManager>();
            hookManager?.Dispose();

            var hotKeyManager = Services.GetService<HotKeyManager>();
            hotKeyManager?.Dispose();

            var playbackEngine = Services.GetService<PlaybackEngine>();
            playbackEngine?.Dispose();

            var recordingEngine = Services.GetService<RecordingEngine>();
            recordingEngine?.Dispose();

            // Save all dirty macros
            var macroManager = Services.GetService<MacroManager>();
            macroManager?.SaveAllDirtyAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error during shutdown cleanup");
        }

        (Logger as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Logger.Fatal(ex, "Unhandled domain exception (IsTerminating={IsTerminating})", e.IsTerminating);

        // Crash recovery: try to save recording buffer
        TryCrashRecoverySave();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error(e.Exception, "Unhandled dispatcher exception");
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}",
            "MacroApp Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    private static void TryCrashRecoverySave()
    {
        try
        {
            var recordingEngine = Services?.GetService<RecordingEngine>();
            if (recordingEngine?.State == RecordingState.Recording)
            {
                var events = recordingEngine.Stop();
                if (events.Count > 0)
                {
                    var recoveryMacro = new Core.Models.Macro
                    {
                        Name = $"CrashRecovery_{DateTime.Now:yyyyMMdd_HHmmss}",
                        Events = events
                    };
                    string recoveryPath = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        $"crash_recovery_{DateTime.Now:yyyyMMdd_HHmmss}{AppConstants.MacroFileExtension}");
                    Core.Serialization.MacroSerializer.SaveSync(recoveryMacro, recoveryPath);
                    Logger.Information("Crash recovery: saved {Count} events to {Path}", events.Count, recoveryPath);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Crash recovery save failed");
        }
    }
}
