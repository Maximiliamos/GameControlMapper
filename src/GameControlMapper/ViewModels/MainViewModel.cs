using System.Collections.ObjectModel;
using System.Windows;
using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GameControlMapper.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IProfileStore _profileStore;
    private readonly InputMappingEngine _mappingEngine;
    private readonly ILogger<MainViewModel> _logger;
    private readonly DiagnosticExportService _diagnostics;
    private MapperProfile _currentProfile = MapperProfile.CreateDefault();
    private string? _selectedProfileName;
    private BindingViewModel? _selectedBinding;
    private ControlBinding? _clipboardBinding;
    private bool _isMappingActive;
    private BindingKind _newBindingKind = BindingKind.Tap;

    public MainViewModel(
        IProfileStore profileStore,
        InputMappingEngine mappingEngine,
        AppLogSink logSink,
        ILogger<MainViewModel> logger, DiagnosticExportService diagnostics)
    {
        _profileStore = profileStore;
        _mappingEngine = mappingEngine;
        _logger = logger;
        _diagnostics=diagnostics;
        Logs = logSink.Entries;
        BindingKinds = Enum.GetValues<BindingKind>();

        NewProfileCommand = new AsyncRelayCommand(_ => NewProfileAsync());
        LoadProfileCommand = new AsyncRelayCommand(parameter => LoadProfileAsync(parameter as string), parameter => parameter is string);
        SaveProfileCommand = new AsyncRelayCommand(_ => SaveProfileAsync());
        DeleteProfileCommand = new AsyncRelayCommand(_ => DeleteProfileAsync(), _ => SelectedProfileName is not null);
        ImportProfileCommand = new AsyncRelayCommand(_ => ImportProfileAsync());
        ExportProfileCommand = new AsyncRelayCommand(_ => ExportProfileAsync());
        ExportDiagnosticsCommand = new AsyncRelayCommand(_ => ExportDiagnosticsAsync());
        AddBindingCommand = new RelayCommand(_ => AddBinding());
        DuplicateBindingCommand = new RelayCommand(_ => DuplicateSelected(), _ => SelectedBinding is not null);
        DeleteBindingCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedBinding is not null);
        CopyBindingCommand = new RelayCommand(_ => CopySelected(), _ => SelectedBinding is not null);
        PasteBindingCommand = new RelayCommand(_ => PasteBinding(), _ => _clipboardBinding is not null);
        CopyLogsCommand = new RelayCommand(_ => CopyLogs());
        ActivateCommand = new RelayCommand(_ => ActivateMapping());
        DeactivateCommand = new AsyncRelayCommand(_ => DeactivateMappingAsync());
        SelectAreaCommand = new RelayCommand(_ => SelectAreaRequested?.Invoke(this, EventArgs.Empty), _ => SelectedBinding is not null);
        PickCenterCommand = new RelayCommand(_ => PickCenterRequested?.Invoke(this, EventArgs.Empty), _ => SelectedBinding is not null);

        _mappingEngine.ActiveChanged += (_, active) => IsMappingActive = active;
        _mappingEngine.OverlayToggleRequested += (_, _) => RaiseOnUi(() => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty));
        _mappingEngine.EditorRequested += (_, _) => RaiseOnUi(() => EditorRequested?.Invoke(this, EventArgs.Empty));
        _ = InitializeAsync();
    }

    public ObservableCollection<string> Profiles { get; } = [];
    public ObservableCollection<BindingViewModel> Bindings { get; } = [];
    public ObservableCollection<string> Logs { get; }
    public IReadOnlyList<BindingKind> BindingKinds { get; }

    public event EventHandler? SelectAreaRequested;
    public event EventHandler? PickCenterRequested;
    public event EventHandler? ToggleOverlayRequested;
    public event EventHandler? EditorRequested;

    public AsyncRelayCommand NewProfileCommand { get; }
    public AsyncRelayCommand LoadProfileCommand { get; }
    public AsyncRelayCommand SaveProfileCommand { get; }
    public AsyncRelayCommand DeleteProfileCommand { get; }
    public AsyncRelayCommand ImportProfileCommand { get; }
    public AsyncRelayCommand ExportProfileCommand { get; }
    public AsyncRelayCommand ExportDiagnosticsCommand { get; }
    public RelayCommand AddBindingCommand { get; }
    public RelayCommand DuplicateBindingCommand { get; }
    public RelayCommand DeleteBindingCommand { get; }
    public RelayCommand CopyBindingCommand { get; }
    public RelayCommand PasteBindingCommand { get; }
    public RelayCommand CopyLogsCommand { get; }
    public RelayCommand ActivateCommand { get; }
    public AsyncRelayCommand DeactivateCommand { get; }
    public RelayCommand SelectAreaCommand { get; }
    public RelayCommand PickCenterCommand { get; }

    public string? SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            if (SetProperty(ref _selectedProfileName, value) && value is not null)
            {
                LoadProfileCommand.Execute(value);
            }
        }
    }

    public MapperProfile CurrentProfile
    {
        get => _currentProfile;
        private set
        {
            if (SetProperty(ref _currentProfile, value))
            {
                OnPropertyChanged(nameof(ProfileName));
                OnPropertyChanged(nameof(ResolutionText));
            }
        }
    }

    public string ProfileName
    {
        get => CurrentProfile.Name;
        set
        {
            if (CurrentProfile.Name != value)
            {
                CurrentProfile.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public string ResolutionText => $"{CurrentProfile.ResolutionWidth} x {CurrentProfile.ResolutionHeight}";

    public BindingKind NewBindingKind
    {
        get => _newBindingKind;
        set => SetProperty(ref _newBindingKind, value);
    }

    public BindingViewModel? SelectedBinding
    {
        get => _selectedBinding;
        set
        {
            if (SetProperty(ref _selectedBinding, value))
            {
                DuplicateBindingCommand.RaiseCanExecuteChanged();
                DeleteBindingCommand.RaiseCanExecuteChanged();
                CopyBindingCommand.RaiseCanExecuteChanged();
                SelectAreaCommand.RaiseCanExecuteChanged();
                PickCenterCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsMappingActive
    {
        get => _isMappingActive;
        private set => RaiseOnUi(() => SetProperty(ref _isMappingActive, value));
    }

    public void ApplySelectedArea(Rect area)
    {
        if (SelectedBinding is null)
        {
            return;
        }

        SelectedBinding.X = Math.Round(area.X, 2);
        SelectedBinding.Y = Math.Round(area.Y, 2);
        SelectedBinding.Width = Math.Round(area.Width, 2);
        SelectedBinding.Height = Math.Round(area.Height, 2);
    }

    public void ApplySelectedCenter(System.Windows.Point point)
    {
        if (SelectedBinding is null)
        {
            return;
        }

        SelectedBinding.X = Math.Round(point.X - SelectedBinding.Width / 2d, 2);
        SelectedBinding.Y = Math.Round(point.Y - SelectedBinding.Height / 2d, 2);
    }

    public void MoveSelected(double dx, double dy)
    {
        if (SelectedBinding is null)
        {
            return;
        }

        SelectedBinding.X = Math.Max(0, SelectedBinding.X + dx);
        SelectedBinding.Y = Math.Max(0, SelectedBinding.Y + dy);
    }

    public void ResizeSelected(double dx, double dy)
    {
        if (SelectedBinding is null)
        {
            return;
        }

        SelectedBinding.Width = Math.Max(10, SelectedBinding.Width + dx);
        SelectedBinding.Height = Math.Max(10, SelectedBinding.Height + dy);
    }

    private async Task InitializeAsync()
    {
        var profileNames = await _profileStore.ListProfilesAsync();
        if (profileNames.Count == 0)
        {
            var profile = MapperProfile.CreateDefault("Основной");
            await _profileStore.SaveAsync(profile);
            profileNames = await _profileStore.ListProfilesAsync();
        }

        Profiles.Clear();
        foreach (var profileName in profileNames)
        {
            Profiles.Add(profileName);
        }

        SelectedProfileName = Profiles.FirstOrDefault();
    }

    private async Task NewProfileAsync()
    {
        var baseName = "Новый профиль";
        var index = 1;
        var name = baseName;
        while (Profiles.Contains(name))
        {
            name = $"{baseName} {++index}";
        }

        await LoadProfileObjectAsync(MapperProfile.CreateDefault(name));
        await SaveProfileAsync();
        await RefreshProfilesAsync(name);
    }

    private async Task LoadProfileAsync(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        var profile = await _profileStore.LoadAsync(profileName);
        await LoadProfileObjectAsync(profile);
    }

    private Task LoadProfileObjectAsync(MapperProfile profile)
    {
        EnsureCameraBinding(profile);
        CurrentProfile = profile;
        Bindings.Clear();
        foreach (var binding in profile.Bindings)
        {
            Bindings.Add(new BindingViewModel(binding));
        }

        SelectedBinding = Bindings.FirstOrDefault();
        _mappingEngine.SetProfile(profile);
        _logger.LogInformation("Profile loaded: {Profile}", profile.Name);
        return Task.CompletedTask;
    }

    private static void EnsureCameraBinding(MapperProfile profile)
    {
        var hasCamera = profile.Bindings.Any(binding =>
            binding.Kind == BindingKind.Aim &&
            (binding.Hotkey.Equals(profile.Camera.ActivationHotkey, StringComparison.OrdinalIgnoreCase) ||
             binding.Hotkey.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
             binding.Hotkey.Equals("LeftCtrl", StringComparison.OrdinalIgnoreCase)));

        if (hasCamera)
        {
            return;
        }

        profile.Bindings.Add(new ControlBinding
        {
            Name = "Camera",
            Hotkey = profile.Camera.ActivationHotkey,
            Kind = BindingKind.Aim,
            X = profile.Camera.AnchorX - 90,
            Y = profile.Camera.AnchorY - 90,
            Width = 180,
            Height = 180,
            Color = "#8B5CF6",
            Opacity = 0.32
        });
    }

    private static void ApplyTanksBlitz1920Layout(MapperProfile profile)
    {
        profile.ResolutionWidth = 1920;
        profile.ResolutionHeight = 1080;
        profile.Camera.ActivationHotkey = "LeftCtrl";
        profile.Camera.UseMouseDrag = true;
        profile.Camera.SensitivityX = 0.28;
        profile.Camera.SensitivityY = 0.24;
        profile.Camera.DeadZone = 2.0;
        profile.Camera.Smooth = 0.86;
        profile.Camera.MaxSpeed = 10;
        profile.Camera.AnchorX = 1010;
        profile.Camera.AnchorY = 535;
        profile.Camera.DragRadius = 85;
        profile.Bindings.RemoveAll(binding =>
            binding.Name.Equals("Jump", StringComparison.OrdinalIgnoreCase) ||
            binding.Hotkey.Equals("Space", StringComparison.OrdinalIgnoreCase));

        UpsertBinding(profile, new ControlBinding
        {
            Name = "Move",
            Hotkey = "WASD",
            Kind = BindingKind.Joystick,
            X = 210,
            Y = 755,
            Width = 220,
            Height = 220,
            Color = "#4CC9F0",
            Opacity = 0.30,
            UseNativeInput = false,
            Comment = "Touch joystick controlled by W/A/S/D."
        });

        UpsertBinding(profile, new ControlBinding
        {
            Name = "Camera",
            Hotkey = "LeftCtrl",
            Kind = BindingKind.Aim,
            X = 955,
            Y = 480,
            Width = 110,
            Height = 110,
            Color = "#8B5CF6",
            Opacity = 0.22,
            UseNativeInput = false,
            Comment = "Hold Ctrl and move mouse: injected touch drag for camera."
        });

        UpsertBinding(profile, new ControlBinding
        {
            Name = "Fire",
            Hotkey = "MouseLeft",
            Kind = BindingKind.MouseArea,
            X = 1585,
            Y = 628,
            Width = 130,
            Height = 130,
            Color = "#FF6B6B",
            Opacity = 0.28,
            UseNativeInput = false,
            Comment = "Touch fire button."
        });

        UpsertBinding(profile, new ControlBinding
        {
            Name = "Aim",
            Hotkey = "MouseRight",
            Kind = BindingKind.MouseArea,
            X = 1645,
            Y = 790,
            Width = 125,
            Height = 125,
            Color = "#FFD166",
            Opacity = 0.28,
            UseNativeInput = false,
            Comment = "Touch aim button."
        });

        UpsertBinding(profile, new ControlBinding
        {
            Name = "Repair",
            Hotkey = "Q",
            Kind = BindingKind.Tap,
            X = 80,
            Y = 510,
            Width = 82,
            Height = 92,
            Color = "#4CC9F0",
            Opacity = 0.30
        });

        UpsertBinding(profile, new ControlBinding
        {
            Name = "Consumable 2",
            Hotkey = "E",
            Kind = BindingKind.Tap,
            X = 18,
            Y = 478,
            Width = 76,
            Height = 76,
            Color = "#38BDF8",
            Opacity = 0.28
        });

        UpsertBinding(profile, new ControlBinding
        {
            Name = "Consumable 3",
            Hotkey = "R",
            Kind = BindingKind.Tap,
            X = 18,
            Y = 568,
            Width = 76,
            Height = 76,
            Color = "#94A3B8",
            Opacity = 0.28
        });

        UpsertBinding(profile, new ControlBinding
        {
            Name = "Shell 1",
            Hotkey = "1",
            Kind = BindingKind.Tap,
            X = 1828,
            Y = 454,
            Width = 76,
            Height = 76,
            Color = "#60A5FA",
            Opacity = 0.28
        });
    }

    private static void UpsertBinding(MapperProfile profile, ControlBinding desired)
    {
        var existing = profile.Bindings.FirstOrDefault(binding =>
            binding.Name.Equals(desired.Name, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(binding.Hotkey) && binding.Hotkey.Equals(desired.Hotkey, StringComparison.OrdinalIgnoreCase) &&
             binding.Kind == desired.Kind));

        if (existing is null)
        {
            profile.Bindings.Add(desired);
            return;
        }

        existing.Name = desired.Name;
        existing.Hotkey = desired.Hotkey;
        existing.Kind = desired.Kind;
        existing.X = desired.X;
        existing.Y = desired.Y;
        existing.Width = desired.Width;
        existing.Height = desired.Height;
        existing.Color = desired.Color;
        existing.Opacity = desired.Opacity;
        existing.UseNativeInput = desired.UseNativeInput;
        existing.Comment = desired.Comment;
        existing.IsActive = true;
    }

    private async Task SaveProfileAsync()
    {
        CurrentProfile.Bindings = Bindings.Select(binding => binding.Model).ToList();
        try { await _profileStore.SaveAsync(CurrentProfile); await RefreshProfilesAsync(CurrentProfile.Name); }
        catch (ProfileValidationException ex) { _logger.LogError(ex,"Profile validation failed during save"); System.Windows.MessageBox.Show("Профиль содержит недопустимые данные. Подробности записаны в журнал.","Профиль",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }

    private async Task DeleteProfileAsync()
    {
        if (SelectedProfileName is null)
        {
            return;
        }

        await _profileStore.DeleteAsync(SelectedProfileName);
        await RefreshProfilesAsync();
        if (Profiles.Count == 0)
        {
            await NewProfileAsync();
        }
        else
        {
            SelectedProfileName = Profiles[0];
        }
    }

    private async Task ImportProfileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Профили JSON (*.json)|*.json|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            try { var profile = await _profileStore.ImportAsync(dialog.FileName); await RefreshProfilesAsync(profile.Name); await LoadProfileObjectAsync(profile); }
            catch (ProfileValidationException ex) { _logger.LogError(ex,"Profile import validation failed: {Path}",dialog.FileName); System.Windows.MessageBox.Show("Импорт отклонён: файл повреждён или содержит недопустимые данные.","Импорт профиля",MessageBoxButton.OK,MessageBoxImage.Warning); }
        }
    }

    private async Task ExportProfileAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Профили JSON (*.json)|*.json",
            FileName = $"{CurrentProfile.Name}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            await _profileStore.ExportAsync(CurrentProfile, dialog.FileName);
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter="ZIP (*.zip)|*.zip",FileName="game-control-mapper-diagnostics.zip"};
        if(dialog.ShowDialog()!=true)return;
        try{await _diagnostics.ExportAsync(dialog.FileName);}
        catch(Exception ex){_logger.LogError(ex,"Diagnostic export failed");System.Windows.MessageBox.Show("Не удалось экспортировать диагностику. Журналы не изменены.","Диагностика",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }

    private void AddBinding()
    {
        var binding = new ControlBinding
        {
            Kind = NewBindingKind,
            Name = NewBindingKind switch
            {
                BindingKind.Tap => "Нажатие", BindingKind.Hold => "Удержание",
                BindingKind.DoubleTap => "Двойное нажатие", BindingKind.Swipe => "Свайп",
                BindingKind.Joystick => "Джойстик", BindingKind.Aim => "Камера",
                BindingKind.Macro => "Макрос", BindingKind.Sequence => "Последовательность",
                BindingKind.MouseArea => "Кнопка мыши", _ => "Новая зона"
            },
            Hotkey = NewBindingKind == BindingKind.Joystick ? "WASD" : "Q",
            X = 520 + Bindings.Count * 18,
            Y = 320 + Bindings.Count * 18
        };
        var viewModel = new BindingViewModel(binding);
        Bindings.Add(viewModel);
        SelectedBinding = viewModel;
    }

    private void DuplicateSelected()
    {
        if (SelectedBinding is null)
        {
            return;
        }

        var duplicate = new BindingViewModel(SelectedBinding.Model.Clone());
        Bindings.Add(duplicate);
        SelectedBinding = duplicate;
    }

    private void DeleteSelected()
    {
        if (SelectedBinding is null)
        {
            return;
        }

        var index = Bindings.IndexOf(SelectedBinding);
        Bindings.Remove(SelectedBinding);
        SelectedBinding = Bindings.Count == 0 ? null : Bindings[Math.Clamp(index, 0, Bindings.Count - 1)];
    }

    private void CopySelected()
    {
        _clipboardBinding = SelectedBinding?.Model.Clone();
        PasteBindingCommand.RaiseCanExecuteChanged();
    }

    private void PasteBinding()
    {
        if (_clipboardBinding is null)
        {
            return;
        }

        _clipboardBinding.X += 16;
        _clipboardBinding.Y += 16;
        var pasted = new BindingViewModel(_clipboardBinding.Clone());
        Bindings.Add(pasted);
        SelectedBinding = pasted;
    }

    private void CopyLogs()
    {
        RaiseOnUi(() =>
        {
            try
            {
                var allLogs = string.Join(Environment.NewLine, Logs.Reverse());
                // Use SetDataObject to avoid some clipboard issues
                System.Windows.Clipboard.SetDataObject(allLogs, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy logs to clipboard");
            }
        });
    }

    private void ActivateMapping()
    {
        CurrentProfile.Bindings = Bindings.Select(binding => binding.Model).ToList();
        _mappingEngine.SetProfile(CurrentProfile);
        _mappingEngine.Start();
        ToggleOverlayRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task DeactivateMappingAsync()
    {
        await _mappingEngine.StopAsync();
    }

    private async Task RefreshProfilesAsync(string? selectName = null)
    {
        var profileNames = await _profileStore.ListProfilesAsync();
        Profiles.Clear();
        foreach (var profileName in profileNames)
        {
            Profiles.Add(profileName);
        }

        if (selectName is not null && Profiles.Contains(selectName))
        {
            _selectedProfileName = selectName;
            OnPropertyChanged(nameof(SelectedProfileName));
        }
    }

    private static void RaiseOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
