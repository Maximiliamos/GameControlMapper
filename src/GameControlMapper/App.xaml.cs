using System.Windows;
using GameControlMapper.Services;
using GameControlMapper.UI.Views;
using GameControlMapper.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameControlMapper;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        _serviceProvider.GetRequiredService<DpiAwarenessDiagnostics>().LogCurrentContext();

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

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider != null)
        {
            var touchScheduler = _serviceProvider.GetService<TouchScheduler>();
            touchScheduler?.ShutdownAsync().GetAwaiter().GetResult();
            _serviceProvider.GetService<ITouchBackend>()?.Shutdown();
        }
        _serviceProvider?.Dispose();
        _serviceProvider = null;
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var logSink = new AppLogSink();
        services.AddSingleton(logSink);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(new AppLoggerProvider(logSink));
        });

        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<CoordinateScaler>();
        services.AddSingleton<IInputSimulator, SendInputSimulator>();
        services.AddSingleton<ITouchSimulator, WindowsTouchSimulator>();
        services.AddSingleton<HotkeyParser>();
        services.AddSingleton<KeyboardHookService>();
        services.AddSingleton<MouseHookService>();
        services.AddSingleton(provider =>
        {
            var camera = new CameraMouseLookService(
                provider.GetRequiredService<TouchEngine>(),
                provider.GetRequiredService<ILogger<CameraMouseLookService>>());
            provider.GetRequiredService<MouseHookService>().MouseMoved += camera.OnMouseMove;
            return camera;
        });
        services.AddSingleton<XInputGamepadMapper>();
        services.AddSingleton<InputMappingEngine>();
        services.AddSingleton<GameWindowService>();
        services.AddSingleton<IGameWindowNativeAdapter, WindowsGameWindowNativeAdapter>();
        services.AddSingleton<IGameWindowGeometryProvider, GameWindowGeometryProvider>();
        services.AddSingleton<ITargetWindowActivationNativeAdapter, WindowsTargetWindowActivationNativeAdapter>();
        services.AddSingleton<ITargetWindowActivationMonitor, TargetWindowActivationMonitor>();
        services.AddSingleton<TargetWindowSessionManager>();
        services.AddSingleton<ITargetWindowSessionValidator>(provider => provider.GetRequiredService<TargetWindowSessionManager>());
        services.AddSingleton<WindowCoordinateTransformer>();
        services.AddSingleton<DpiAwarenessDiagnostics>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
        services.AddSingleton<TouchDebugViewModel>();
        services.AddTransient<TouchDebugOverlay>();

        // New touch engine services
        services.AddSingleton<Models.FrameContext>();
        services.AddSingleton<Models.TouchCapabilities>(s => new Models.TouchCapabilities(10, true, false, true));
        services.AddSingleton<ContactManager>();
        services.AddSingleton<TouchEngine>();
        services.AddSingleton<ITouchBackend, WindowsTouchBackend>();
        services.AddSingleton<TouchScheduler>();

    }
}
