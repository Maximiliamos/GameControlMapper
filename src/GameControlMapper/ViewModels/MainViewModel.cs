using System.Collections.ObjectModel;
using System.Windows;
using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Reflection;

namespace GameControlMapper.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IProfileStore _profileStore;
    private readonly InputMappingEngine _mappingEngine;
    private readonly ILogger<MainViewModel> _logger;
    private readonly DiagnosticExportService _diagnostics;
    private readonly IMapperProfileValidator _profileValidator;
    private readonly GameWindowService _gameWindowService;
    private MapperProfile _currentProfile = MapperProfile.CreateDefault();
    private string? _selectedProfileName;
    private BindingViewModel? _selectedBinding;
    private ControlBinding? _clipboardBinding;
    private bool _isMappingActive;
    private BindingKind _newBindingKind = BindingKind.Tap;
    private GameWindowInfo? _selectedTargetWindow;

    public MainViewModel(
        IProfileStore profileStore,
        InputMappingEngine mappingEngine,
        AppLogSink logSink,
        ILogger<MainViewModel> logger, DiagnosticExportService diagnostics,IMapperProfileValidator profileValidator,GameWindowService gameWindowService)
    {
        _profileStore = profileStore;
        _mappingEngine = mappingEngine;
        _logger = logger;
        _diagnostics=diagnostics;
        _profileValidator=profileValidator;
        _gameWindowService=gameWindowService;
        Logs = logSink.Entries;
        BindingKinds = Enum.GetValues<BindingKind>().Where(k=>k is not BindingKind.Macro and not BindingKind.Sequence).ToArray();

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
        ActivateCommand = new AsyncRelayCommand(_ => ActivateMappingAsync());
        DeactivateCommand = new AsyncRelayCommand(_ => DeactivateMappingAsync());
        SelectAreaCommand = new RelayCommand(_ => SelectAreaRequested?.Invoke(this, EventArgs.Empty), _ => SelectedBinding is not null);
        PickCenterCommand = new RelayCommand(_ => PickCenterRequested?.Invoke(this, EventArgs.Empty), _ => SelectedBinding is not null);
        RefreshWindowsCommand = new RelayCommand(_ => RefreshTargetWindows());
        OpenControlEditorCommand = new AsyncRelayCommand(_ => OpenControlEditorAsync());

        _mappingEngine.ActiveChanged += (_, active) =>
        {
            IsMappingActive = active;
            if (!active)
            {
                RaiseOnUi(() => HideOverlayRequested?.Invoke(this, EventArgs.Empty));
            }
        };
        _mappingEngine.OverlayToggleRequested += (_, _) => RaiseOnUi(() => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty));
        _mappingEngine.EditorRequested += (_, _) => RaiseOnUi(() => EditorRequested?.Invoke(this, EventArgs.Empty));
        _ = InitializeAsync();
    }

    public ObservableCollection<string> Profiles { get; } = [];
    public ObservableCollection<BindingViewModel> Bindings { get; } = [];
    public ObservableCollection<string> Logs { get; }
    public ObservableCollection<GameWindowInfo> TargetWindows { get; }=[];
    public IReadOnlyList<BindingKind> BindingKinds { get; }
    public IReadOnlyList<ApplicationCapability> Capabilities => ApplicationCapabilities.Beta.Items;
    public string BetaVersion
    {
        get
        {
            var assembly=Assembly.GetEntryAssembly()??Assembly.GetExecutingAssembly();var info=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??assembly.GetName().Version?.ToString()??"unknown";
            var commit=info.Split('+').LastOrDefault();return $"Beta {assembly.GetName().Version}"+(commit is {Length:>=7}?$" ({commit[..7]})":"");
        }
    }

    public event EventHandler? SelectAreaRequested;
    public event EventHandler? PickCenterRequested;
    public event EventHandler? ToggleOverlayRequested;
    public event EventHandler? ShowOverlayRequested;
    public event EventHandler? HideOverlayRequested;
    public event EventHandler? EditorRequested;
    public event EventHandler? ControlEditorRequested;

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
    public AsyncRelayCommand ActivateCommand { get; }
    public AsyncRelayCommand DeactivateCommand { get; }
    public RelayCommand SelectAreaCommand { get; }
    public RelayCommand PickCenterCommand { get; }
    public RelayCommand RefreshWindowsCommand { get; }
    public AsyncRelayCommand OpenControlEditorCommand { get; }

    public GameWindowInfo? SelectedTargetWindow
    {
        get=>_selectedTargetWindow;
        set
        {
            if(!SetProperty(ref _selectedTargetWindow,value)||value is null)return;
            CurrentProfile.Window.ProcessName=value.ProcessName;CurrentProfile.Window.WindowTitle=value.Title;CurrentProfile.Window.WindowHandle=value.Handle.ToInt64();CurrentProfile.Window.X=value.X;CurrentProfile.Window.Y=value.Y;CurrentProfile.Window.Width=value.Width;CurrentProfile.Window.Height=value.Height;
            OnPropertyChanged(nameof(TargetWindowStatus));
            _mappingEngine.SetProfile(CurrentProfile);
            _logger.LogInformation("Target window selected: {ProcessName}; handle=0x{Handle:X}",value.ProcessName,value.Handle.ToInt64());
        }
    }
    public string TargetWindowStatus=>CurrentProfile.Window.WindowHandle==0?"Целевое окно не выбрано":"Выбрано: "+CurrentProfile.Window.WindowTitle;

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
            if (ReferenceEquals(_selectedBinding, value))
            {
                return;
            }

            if (_selectedBinding is not null)
            {
                _selectedBinding.IsSelected = false;
            }

            if (SetProperty(ref _selectedBinding, value))
            {
                if (_selectedBinding is not null)
                {
                    _selectedBinding.IsSelected = true;
                }

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

    public async Task<bool> CommitControlEditorAsync(IReadOnlyList<ControlBinding> editedBindings)
    {
        ArgumentNullException.ThrowIfNull(editedBindings);

        var committedBindings = editedBindings.Select(ControlEditorSession.CopyExact).ToList();
        var candidate = ControlEditorSession.CopyProfileWithBindings(CurrentProfile, committedBindings);
        var camera = candidate.Bindings.FirstOrDefault(binding => binding.Kind == BindingKind.Aim);
        if (camera is not null)
        {
            candidate.Camera.ActivationHotkey = camera.Hotkey;
            candidate.Camera.AnchorX = camera.CenterX;
            candidate.Camera.AnchorY = camera.CenterY;
        }

        var semanticErrors = ValidateEditorSemantics(candidate);
        var validation = _profileValidator.Validate(candidate);
        if (!validation.IsValid || semanticErrors.Count > 0)
        {
            _logger.LogWarning(
                "Overlay editor rejected invalid profile: structural={StructuralErrors}; semantic={SemanticErrors}",
                string.Join("; ", validation.Errors.Select(error => $"{error.FieldPath}:{error.Code}")),
                string.Join("; ", semanticErrors));
            System.Windows.MessageBox.Show(
                "Не удалось сохранить схему: проверьте названия, клавиши и положение элементов. WASD можно назначить только одному джойстику; F8–F11 зарезервированы программой.",
                "Редактор управления",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        try
        {
            await _profileStore.SaveAsync(candidate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overlay editor failed to save profile");
            System.Windows.MessageBox.Show(
                "Не удалось сохранить профиль. Исходная схема оставлена без изменений.",
                "Редактор управления",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        // From this point the candidate is durable on disk; memory and runtime must
        // converge to that exact saved object and must never claim a rollback.
        CurrentProfile = candidate;
        Bindings.Clear();
        foreach (var binding in candidate.Bindings)
        {
            Bindings.Add(new BindingViewModel(binding));
        }

        SelectedBinding = Bindings.FirstOrDefault();
        _mappingEngine.SetProfile(candidate);
        try
        {
            await RefreshProfilesAsync(candidate.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Profile was saved, but the profile list could not be refreshed");
        }

        _logger.LogInformation("Overlay editor changes committed: {Count} bindings", candidate.Bindings.Count);
        return true;
    }

    internal static IReadOnlyList<string> ValidateEditorSemantics(MapperProfile profile)
    {
        var errors = new List<string>();
        var joysticks = profile.Bindings.Count(binding => binding.Kind == BindingKind.Joystick);
        var cameras = profile.Bindings.Count(binding => binding.Kind == BindingKind.Aim);
        if (joysticks > 1) errors.Add("Only one WASD joystick is supported.");
        if (cameras > 1) errors.Add("Only one camera binding is supported.");

        var reservedHotkeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            profile.EnableHotkey,
            profile.DisableHotkey,
            profile.ToggleOverlayHotkey,
            profile.EditorHotkey
        };

        foreach (var binding in profile.Bindings)
        {
            var isWasd = string.Equals(binding.Hotkey, "WASD", StringComparison.OrdinalIgnoreCase);
            if (binding.Kind == BindingKind.Joystick && !isWasd)
            {
                errors.Add($"Joystick '{binding.Name}' must use WASD.");
            }
            else if (binding.Kind != BindingKind.Joystick && isWasd)
            {
                errors.Add($"Binding '{binding.Name}' cannot use WASD.");
            }

            if (reservedHotkeys.Contains(binding.Hotkey))
            {
                errors.Add($"Binding '{binding.Name}' uses a reserved lifecycle hotkey.");
            }
        }

        return errors;
    }

    public void NotifyControlEditorTargetLost()
    {
        _logger.LogInformation("Overlay editor closed because the selected target window became unavailable");
    }

    private async Task InitializeAsync()
    {
        var profileNames = await _profileStore.ListProfilesAsync();
        if (profileNames.Count == 0)
        {
            var profile = MapperProfile.CreateDefault("Основной — Tanks Blitz");
            await _profileStore.SaveAsync(profile);
            profileNames = await _profileStore.ListProfilesAsync();
        }

        Profiles.Clear();
        foreach (var profileName in profileNames)
        {
            Profiles.Add(profileName);
        }

        SelectedProfileName = Profiles.FirstOrDefault();
        RefreshTargetWindows();
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
        var capabilityWarnings=_profileValidator.Validate(profile).Warnings.Where(x=>x.Code=="UnsupportedInBeta").ToArray();
        if(capabilityWarnings.Length>0)
        {
            _logger.LogWarning("Profile {Profile} loaded with capability warnings: {Warnings}",profile.Name,string.Join("; ",capabilityWarnings.Select(x=>$"{x.FieldPath}:{x.Code}")));
            System.Windows.MessageBox.Show("Профиль загружен, но содержит функции, отключённые в beta. Они не будут запущены.","Ограничения Beta",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        EnsureCameraBinding(profile);
        CurrentProfile = profile;
        Bindings.Clear();
        foreach (var binding in profile.Bindings)
        {
            Bindings.Add(new BindingViewModel(binding));
        }

        SelectedBinding = Bindings.FirstOrDefault();
        _mappingEngine.SetProfile(profile);
        RefreshTargetWindows();
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

    private async Task ActivateMappingAsync()
    {
        CurrentProfile.Bindings = Bindings.Select(binding => binding.Model).ToList();
        if(CurrentProfile.Window.WindowHandle==0)
        {
            _logger.LogWarning("Mapping start rejected: target window is not selected");
            System.Windows.MessageBox.Show("Сначала выберите окно игры или эмулятора в списке «Целевое окно».","Не выбрано окно игры",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        if(!_gameWindowService.ActivateWindow(new IntPtr(CurrentProfile.Window.WindowHandle)))
        {
            _logger.LogWarning("Mapping start rejected: target window could not be activated");
            System.Windows.MessageBox.Show("Не удалось активировать выбранное окно. Разверните MuMu Player и попробуйте снова.","Окно игры недоступно",MessageBoxButton.OK,MessageBoxImage.Warning);
            return;
        }
        await Task.Delay(200);
        var unsupported=CurrentProfile.Bindings.Where(x=>x.IsActive&&x.Kind is BindingKind.Macro or BindingKind.Sequence).Select(x=>x.Name).ToArray();
        if(CurrentProfile.Gamepad.Enabled||unsupported.Length>0)
        {
            _logger.LogWarning("UnsupportedInBeta: activation contains XInput={XInput}, unsupported bindings={Bindings}",CurrentProfile.Gamepad.Enabled,string.Join(",",unsupported));
            System.Windows.MessageBox.Show("Часть функций профиля отключена в beta-версии (XInput или Macro/Sequence). Они не будут запущены.","Ограничения Beta",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        _mappingEngine.SetProfile(CurrentProfile);
        _mappingEngine.Start();
        if (_mappingEngine.IsActive)
        {
            ShowOverlayRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task OpenControlEditorAsync()
    {
        if (CurrentProfile.Window.WindowHandle == 0)
        {
            _logger.LogInformation("Overlay editor was not opened because no target window is selected");
            System.Windows.MessageBox.Show(
                "Сначала выберите окно игры в разделе «Целевое окно».",
                "Редактор управления",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsMappingActive)
        {
            var shutdown = await _mappingEngine.StopAsync("open control editor");
            if (!shutdown.Succeeded)
            {
                _logger.LogWarning("Overlay editor rejected because touch shutdown did not complete successfully");
                System.Windows.MessageBox.Show(
                    "Редактор не открыт: не удалось безопасно завершить активные касания. Нажмите F9 и повторите попытку.",
                    "Редактор управления",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        if (!_gameWindowService.ActivateWindow(new IntPtr(CurrentProfile.Window.WindowHandle)))
        {
            _logger.LogWarning("Overlay editor rejected because the selected target could not be activated");
            System.Windows.MessageBox.Show(
                "Не удалось показать выбранное окно игры. Разверните его, обновите список окон и повторите попытку.",
                "Редактор управления",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await Task.Delay(120);

        ControlEditorRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshTargetWindows()
    {
        var currentHandle=CurrentProfile.Window.WindowHandle;var windows=_gameWindowService.FindWindows().Where(x=>x.Handle!=0).ToArray();
        TargetWindows.Clear();foreach(var window in windows)TargetWindows.Add(window);
        SelectedTargetWindow=TargetWindows.FirstOrDefault(x=>x.Handle.ToInt64()==currentHandle)??TargetWindows.Where(x=>x.ProcessName.Contains("MuMu",StringComparison.OrdinalIgnoreCase)||x.ProcessName.Contains("Nemu",StringComparison.OrdinalIgnoreCase)||x.Title.Contains("Tanks Blitz",StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>x.ProcessName.Equals("MuMuNxDevice",StringComparison.OrdinalIgnoreCase)?5:x.Title.Contains("Tanks Blitz",StringComparison.OrdinalIgnoreCase)?4:x.ProcessName.Equals("MuMuNxMain",StringComparison.OrdinalIgnoreCase)?3:1).FirstOrDefault();
        OnPropertyChanged(nameof(TargetWindowStatus));
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
