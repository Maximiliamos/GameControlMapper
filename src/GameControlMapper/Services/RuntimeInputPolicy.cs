using GameControlMapper.Models;

namespace GameControlMapper.Services;

/// <summary>Single capability policy shared by execution and input suppression.</summary>
public sealed class RuntimeInputPolicy
{
    private readonly ApplicationCapabilities _capabilities;

    public RuntimeInputPolicy(ApplicationCapabilities capabilities) => _capabilities = capabilities;

    public bool IsProfileModeSupported(InputMode mode) =>
        mode == InputMode.SendInput && IsAvailable("windows-touch") && IsAvailable("keyboard-mouse");

    public bool IsBindingKindSupported(BindingKind kind) => kind switch
    {
        BindingKind.Tap or BindingKind.DoubleTap or BindingKind.Hold or BindingKind.Swipe or BindingKind.MouseArea =>
            IsAvailable("windows-touch") && IsAvailable("keyboard-mouse"),
        BindingKind.Joystick => IsAvailable("windows-touch") && IsAvailable("keyboard-mouse"),
        BindingKind.Aim => IsAvailable("camera"),
        _ => false
    };

    public bool CanExecute(MapperProfile profile, ControlBinding binding) =>
        IsProfileModeSupported(profile.InputMode) && binding.IsActive && !binding.UseNativeInput &&
        IsBindingKindSupported(binding.Kind);

    private bool IsAvailable(string id) =>
        _capabilities.Items.Any(capability => capability.Id == id && capability.Status != CapabilityStatus.UnsupportedInBeta);
}
