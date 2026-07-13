using System.Windows;
using System.IO;
using GameControlMapper.Services;
using GameControlMapper.UI.Views;
using GameControlMapper.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameControlMapper;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private Task? _startupOperation;
    private bool _diagnosticOnly;
    private int _crashDispatch;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _serviceProvider.GetRequiredService<DpiAwarenessDiagnostics>().LogCurrentContext();

        if (TryGetDiagnosticExportPath(e.Args, out var diagnosticPath))
        {
            _diagnosticOnly = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _startupOperation = ExportDiagnosticsAndShutdownAsync(diagnosticPath);
            return;
        }

        // Initialize Touch Backend and Scheduler
        var touchBackend = _serviceProvider.GetRequiredService<ITouchBackend>();
        if (!touchBackend.Initialize())
        {
            System.Windows.MessageBox.Show("Windows Touch Injection could not be initialized.", "Game Control Mapper", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }
        var touchScheduler = _serviceProvider.GetRequiredService<TouchScheduler>();
        touchScheduler.Start();

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        
    }

    internal static bool TryGetDiagnosticExportPath(IReadOnlyList<string> args, out string path)
    {
        path = string.Empty;
        if (args.Count != 2 || !string.Equals(args[0], "--export-diagnostics", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(args[1]))
        {
            return false;
        }

        path = Path.GetFullPath(args[1]);
        return string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ExportDiagnosticsAndShutdownAsync(string path)
    {
        try
        {
            await _serviceProvider!.GetRequiredService<DiagnosticExportService>().ExportAsync(path);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            _serviceProvider!.GetRequiredService<ILogger<App>>()
                .LogError(ex, "Command-line diagnostic export failed");
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider != null && !_diagnosticOnly)
        {
            var touchScheduler = _serviceProvider.GetService<TouchScheduler>();
            _serviceProvider.GetService<InputMappingEngine>()?.StopAsync("application shutdown").Wait(TimeSpan.FromSeconds(3));
            touchScheduler?.ShutdownAsync().GetAwaiter().GetResult();
            _serviceProvider.GetService<ITouchBackend>()?.Shutdown();
        }
        _serviceProvider?.Dispose();
        _serviceProvider = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var result=HandleCrashBounded("Dispatcher",e.Exception,TimeSpan.FromSeconds(4));
        e.Handled=result?.CanMarkDispatcherHandled==true;
    }
    private void OnDomainUnhandledException(object? sender,UnhandledExceptionEventArgs e)
    {
        if(e.ExceptionObject is Exception ex)_=HandleCrashBounded("AppDomain",ex,TimeSpan.FromSeconds(4));
    }
    private void OnUnobservedTaskException(object? sender,UnobservedTaskExceptionEventArgs e)
    {
        var result=HandleCrashBounded("TaskScheduler",e.Exception,TimeSpan.FromSeconds(4));
        if(result?.Started==true)e.SetObserved();
    }
    private CrashRecoveryResult? HandleCrashBounded(string source,Exception ex,TimeSpan timeout)
    {
        if(Interlocked.Exchange(ref _crashDispatch,1)!=0)return null;
        try
        {
            if(_serviceProvider is null)return null;
            var task=_serviceProvider.GetRequiredService<CrashHandlingService>().HandleAsync(source,ex);
            return task.WaitAsync(timeout).GetAwaiter().GetResult();
        }
        catch(Exception handlerException)
        {
            try{_serviceProvider?.GetService<FileLogSink>()?.WriteCrashHandlerFallback(source,handlerException);}catch{}
            return null;
        }
        finally{Volatile.Write(ref _crashDispatch,0);}
    }

    internal static void ConfigureServices(IServiceCollection services,bool startNativeHooks=true)
    {
        var logSink = new AppLogSink();
        var fileLog = new FileLogSink();
        services.AddSingleton(logSink);
        services.AddSingleton(fileLog);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(new AppLoggerProvider(logSink,fileLog));
        });

        services.AddSingleton<IMapperProfileValidator, MapperProfileValidator>();
        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<MappingSessionDiagnostics>();
        services.AddSingleton<DiagnosticExportService>();
        services.AddSingleton<HotkeyParser>();
        services.AddSingleton<KeyboardHookService>();
        services.AddSingleton(provider =>
        {
            var hook = new MouseHookService(provider.GetRequiredService<ILogger<MouseHookService>>());
            provider.GetRequiredService<IRelativeMouseInputSource>().Moved += hook.OnRawPhysicalMouseMoved;
            return hook;
        });
        services.AddSingleton<IRelativeMouseInputSource,RawMouseInputSource>();
        services.AddSingleton<IMouseCursorController, WindowsMouseCursorController>();
        services.AddSingleton(provider =>
        {
            var camera = new CameraMouseLookService(
                provider.GetRequiredService<TouchEngine>(),
                provider.GetRequiredService<ILogger<CameraMouseLookService>>(),
                provider.GetRequiredService<IMouseCursorController>(), provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<TargetWindowSessionManager>(),
                provider.GetRequiredService<TouchScheduler>());
            provider.GetRequiredService<IRelativeMouseInputSource>().Moved += camera.OnMouseMove;
            return camera;
        });
        services.AddSingleton(Models.ApplicationCapabilities.Beta);
        services.AddSingleton<RuntimeInputPolicy>();
        services.AddSingleton(provider=>new InputMappingEngine(
            provider.GetRequiredService<KeyboardHookService>(),provider.GetRequiredService<MouseHookService>(),
            provider.GetRequiredService<CameraMouseLookService>(),provider.GetRequiredService<TouchEngine>(),
            provider.GetRequiredService<TouchScheduler>(),provider.GetRequiredService<HotkeyParser>(),
            provider.GetRequiredService<TargetWindowSessionManager>(),provider.GetRequiredService<WindowCoordinateTransformer>(),
            provider.GetRequiredService<ILogger<InputMappingEngine>>(),provider.GetRequiredService<ITargetWindowActivationMonitor>(),
            provider.GetRequiredService<ITouchContactAllocator>(),provider.GetRequiredService<MappingSessionDiagnostics>(),
            provider.GetRequiredService<RuntimeInputPolicy>(),startNativeHooks));
        services.AddSingleton(provider=>new CrashHandlingService(provider.GetRequiredService<ILogger<CrashHandlingService>>(),()=>provider.GetRequiredService<InputMappingEngine>().StopAsync("unhandled exception"),()=>provider.GetRequiredService<CameraMouseLookService>().Stop(),provider.GetRequiredService<FileLogSink>()));
        services.AddSingleton<GameWindowService>();
        services.AddSingleton<IGameWindowNativeAdapter, WindowsGameWindowNativeAdapter>();
        services.AddSingleton<IGameWindowGeometryProvider, GameWindowGeometryProvider>();
        services.AddSingleton<ITargetWindowActivationNativeAdapter, WindowsTargetWindowActivationNativeAdapter>();
        services.AddSingleton<ITargetWindowActivationMonitor, TargetWindowActivationMonitor>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITargetWindowGeometryNativeAdapter, WindowsTargetWindowGeometryNativeAdapter>();
        services.AddSingleton<ITargetWindowGeometryMonitor, TargetWindowGeometryMonitor>();
        services.AddSingleton<TargetWindowSessionManager>();
        services.AddSingleton<ITargetWindowSessionValidator>(provider => provider.GetRequiredService<TargetWindowSessionManager>());
        services.AddSingleton<WindowCoordinateTransformer>();
        services.AddSingleton<DpiAwarenessDiagnostics>();
        services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher(Current.Dispatcher));
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
        services.AddSingleton<TouchDebugViewModel>();
        services.AddTransient<TouchDebugOverlay>();

        // New touch engine services
        services.AddSingleton<Models.FrameContext>();
        services.AddSingleton<Models.TouchCapabilities>(s => new Models.TouchCapabilities(10, true, false, true));
        services.AddSingleton<ContactManager>();
        services.AddSingleton<ITouchContactAllocator, TouchContactAllocator>();
        services.AddSingleton<TouchEngine>();
        services.AddSingleton<ITouchBackend, WindowsTouchBackend>();
        services.AddSingleton<TouchScheduler>();

    }
}
