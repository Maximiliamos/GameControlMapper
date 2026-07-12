using GameControlMapper.Models;
using GameControlMapper.Services;

namespace GameControlMapper.ViewModels;

public class TouchDebugViewModel : ObservableObject
{
    private readonly ContactManager _contactManager;
    private readonly TouchScheduler _touchScheduler;
    
    private bool _cameraActive;
    public bool CameraActive
    {
        get => _cameraActive;
        set => SetProperty(ref _cameraActive, value);
    }
    
    private bool _joystickActive;
    public bool JoystickActive
    {
        get => _joystickActive;
        set => SetProperty(ref _joystickActive, value);
    }
    
    private bool _fireDown;
    public bool FireDown
    {
        get => _fireDown;
        set => SetProperty(ref _fireDown, value);
    }
    
    private bool _aimDown;
    public bool AimDown
    {
        get => _aimDown;
        set => SetProperty(ref _aimDown, value);
    }
    
    private double _injectedFps;
    public double InjectedFps
    {
        get => _injectedFps;
        set => SetProperty(ref _injectedFps, value);
    }

    public TouchDebugViewModel(ContactManager contactManager, TouchScheduler touchScheduler)
    {
        _contactManager = contactManager;
        _touchScheduler = touchScheduler;
        
        _contactManager.ContactsChanged += OnContactsChanged;
        _touchScheduler.FpsChanged += OnFpsChanged;
        
        UpdateContacts();
    }

    private void OnContactsChanged(object? sender, EventArgs e)
    {
        UpdateContacts();
    }
    
    private void OnFpsChanged(object? sender, EventArgs e)
    {
        InjectedFps = _touchScheduler.CurrentFps;
    }
    
    private void UpdateContacts()
    {
        var contacts = _contactManager.ActiveContacts;
        CameraActive = contacts.ContainsKey((int)FixedContacts.Camera) && contacts[(int)FixedContacts.Camera].IsActive;
        JoystickActive = contacts.ContainsKey((int)FixedContacts.Joystick) && contacts[(int)FixedContacts.Joystick].IsActive;
        FireDown = contacts.ContainsKey((int)FixedContacts.Fire) && contacts[(int)FixedContacts.Fire].IsActive;
        AimDown = contacts.ContainsKey((int)FixedContacts.Aim) && contacts[(int)FixedContacts.Aim].IsActive;
    }
}
