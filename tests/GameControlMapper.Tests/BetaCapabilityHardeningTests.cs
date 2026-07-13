using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class BetaCapabilityHardeningTests
{
    private static ApplicationCapabilities Capabilities => ApplicationCapabilities.Beta;
    private static ProfileValidationResult Validate(MapperProfile profile) =>
        new MapperProfileValidator(new HotkeyParser()).Validate(profile);

    [Fact]
    public void Capabilities_DistinguishAutomatedExperimentalUnsupportedAndUnavailable()
    {
        Assert.All(new[] { "windows-touch", "keyboard-mouse", "target-window", "multitouch", "camera", "mixed-dpi", "multi-monitor", "negative-origin", "diagnostics", "profile-backup" },
            id => Assert.Equal(CapabilityStatus.AutomatedOnly, Find(id).Status));
        Assert.All(new[] { "tanks-blitz", "xvm" }, id => Assert.Equal(CapabilityStatus.Experimental, Find(id).Status));
        Assert.All(new[] { "xinput", "macro-sequence", "pinch", "rotation" }, id => Assert.Equal(CapabilityStatus.Unsupported, Find(id).Status));
        Assert.All(new[] { "raw-input-public", "interception", "vigem", "adb" }, id => Assert.Equal(CapabilityStatus.Unavailable, Find(id).Status));
    }

    [Fact]
    public void RuntimePolicy_AllowsOnlyImplementedRuntimeCapabilities()
    {
        var policy = new RuntimeInputPolicy(Capabilities);
        Assert.True(Capabilities.IsRuntimeAvailable("windows-touch"));
        Assert.False(Capabilities.IsRuntimeAvailable("xinput"));
        Assert.False(Capabilities.IsRuntimeAvailable("xvm"));
        Assert.True(policy.IsBindingKindSupported(BindingKind.Tap));
        Assert.False(policy.IsBindingKindSupported(BindingKind.Macro));
        Assert.False(policy.IsBindingKindSupported(BindingKind.Sequence));
    }

    [Fact]
    public void LegacyProfiles_LoadWithWarningsWithoutBeingModified()
    {
        var profile = MapperProfile.CreateDefault();
        profile.Gamepad.Enabled = true;
        profile.Bindings.Add(new ControlBinding { Name = "legacy", Hotkey = "F1", Kind = BindingKind.Macro });
        var before = System.Text.Json.JsonSerializer.Serialize(profile);

        var result = Validate(profile);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Code == "UnsupportedInBeta");
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(profile));
    }

    [Fact]
    public void ProductionContainer_ResolvesCurrentTouchPipelineWithoutLegacyServices()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services, startNativeHooks: false);
        services.AddSingleton<IRelativeMouseInputSource, FakeRelativeMouseInputSource>();
        services.AddSingleton<ITargetWindowActivationMonitor, FakeActivationMonitor>();
        services.AddSingleton<ITargetWindowGeometryMonitor, FakeGeometryMonitor>();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITouchBackend) && descriptor.ImplementationType == typeof(WindowsTouchBackend));
        Assert.DoesNotContain(services, descriptor => IsLegacyType(descriptor.ServiceType) || IsLegacyType(descriptor.ImplementationType));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var engine = provider.GetRequiredService<InputMappingEngine>();
        Assert.IsType<WindowsTouchBackend>(provider.GetRequiredService<ITouchBackend>());
    }

    [Theory]
    [InlineData("GameControlMapper.Services.WindowsTouchSimulator")]
    [InlineData("GameControlMapper.Services.SendInputSimulator")]
    [InlineData("GameControlMapper.Services.XInputGamepadMapper")]
    [InlineData("GameControlMapper.Services.CoordinateScaler")]
    [InlineData("GameControlMapper.Models.FixedContacts")]
    public void LegacyImplementations_AreNotCompiled(string fullTypeName) =>
        Assert.Null(typeof(InputMappingEngine).Assembly.GetType(fullTypeName, throwOnError: false));

    [Fact]
    public void InputMappingEngine_HasNoLegacyDependencyFields()
    {
        var fieldTypes = typeof(InputMappingEngine)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.FieldType.FullName ?? field.FieldType.Name)
            .ToArray();

        Assert.DoesNotContain(fieldTypes, IsLegacyTypeName);
    }

    [Fact]
    public void AllocatorLeases_PreserveExplicitOwnerMetadataForConcurrentContacts()
    {
        var allocator = new TouchContactAllocator(new TouchCapabilities(10, true, false, true), NullLogger<TouchContactAllocator>.Instance);
        var camera = allocator.TryAcquire(7, "camera:look");
        var joystick = allocator.TryAcquire(7, "joystick:move");
        var mouseArea = allocator.TryAcquire(7, "mouse-area:fire");

        Assert.NotNull(camera);
        Assert.NotNull(joystick);
        Assert.NotNull(mouseArea);
        Assert.Equal("camera:look", camera!.OwnerId);
        Assert.Equal("joystick:move", joystick!.OwnerId);
        Assert.Equal("mouse-area:fire", mouseArea!.OwnerId);
        Assert.Equal(3, allocator.ActiveLeases.Select(lease => lease.ContactId).Distinct().Count());
    }

    [Fact]
    public void AssemblyInformationalVersion_ContainsSourceRevisionMetadata()
    {
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains('+', version!);
    }

    private static ApplicationCapability Find(string id) => Assert.Single(Capabilities.Items, item => item.Id == id);
    private static bool IsLegacyType(Type? type) => type is not null && IsLegacyTypeName(type.FullName ?? type.Name);
    private static bool IsLegacyTypeName(string name) => new[]
    {
        "WindowsTouchSimulator", "SendInputSimulator", "XInputGamepadMapper", "CoordinateScaler", "FixedContacts", "ITouchSimulator", "IInputSimulator"
    }.Any(name.Contains);

    private sealed class FakeRelativeMouseInputSource : IRelativeMouseInputSource
    {
        public event Action<int, int>? Moved { add { } remove { } }
    }

    private sealed class FakeActivationMonitor : ITargetWindowActivationMonitor
    {
        public event EventHandler? ActivationChanged { add { } remove { } }
        public bool TryGetForeground(out nint rootWindow, out uint processId) { rootWindow = 0; processId = 0; return false; }
        public void Dispose() { }
    }

    private sealed class FakeGeometryMonitor : ITargetWindowGeometryMonitor
    {
        public event EventHandler<long>? Invalidated { add { } remove { } }
        public void Track(TargetWindowSession session) { }
        public void Stop(long generation) { }
        public void Dispose() { }
    }
}
