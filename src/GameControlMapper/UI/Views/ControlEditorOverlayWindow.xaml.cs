using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GameControlMapper.Models;
using GameControlMapper.Services;
using GameControlMapper.ViewModels;
using GameControlMapper.Win32;

namespace GameControlMapper.UI.Views;

public partial class ControlEditorOverlayWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly IGameWindowGeometryProvider _geometryProvider;
    private readonly nint _targetWindow;
    private readonly uint _targetProcessId;
    private readonly DispatcherTimer _targetTracker;
    private readonly ControlEditorSession _session;
    private System.Windows.Point _pendingAddPoint;
    private PhysicalClientRect? _lastTargetRect;
    private bool _isSaving;
    private bool _allowCloseDuringSave;
    private bool _targetLost;
    private int _consecutiveTargetFailures;

    public ControlEditorOverlayWindow(
        MainViewModel mainViewModel,
        IGameWindowGeometryProvider geometryProvider,
        nint targetWindow)
    {
        ArgumentNullException.ThrowIfNull(mainViewModel);
        ArgumentNullException.ThrowIfNull(geometryProvider);

        _mainViewModel = mainViewModel;
        _geometryProvider = geometryProvider;
        _targetWindow = targetWindow;
        NativeMethods.GetWindowThreadProcessId(targetWindow, out _targetProcessId);
        _session = new ControlEditorSession(mainViewModel.CurrentProfile);
        _pendingAddPoint = new System.Windows.Point(_session.ProfileWidth / 2d, _session.ProfileHeight / 2d);

        InitializeComponent();
        DataContext = _session;

        _targetTracker = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _targetTracker.Tick += TargetTracker_Tick;
        Closed += Window_Closed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TOOLWINDOW);

        if (!ApplyTargetBounds())
        {
            _targetLost = true;
            Dispatcher.BeginInvoke(Close);
            return;
        }

        _targetTracker.Start();
    }

    private bool ApplyTargetBounds()
    {
        if (_targetProcessId == 0 ||
            NativeMethods.GetWindowThreadProcessId(_targetWindow, out var currentProcessId) == 0 ||
            currentProcessId != _targetProcessId)
        {
            return false;
        }

        var geometry = _geometryProvider.GetClientRect(_targetWindow);
        if (!geometry.Succeeded)
        {
            return false;
        }

        var rect = geometry.ClientRect;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return false;
        }

        if (_lastTargetRect == rect &&
            NativeMethods.GetWindowRect(handle, out var actual) &&
            actual.Left == rect.Left &&
            actual.Top == rect.Top &&
            actual.Right - actual.Left == rect.Width &&
            actual.Bottom - actual.Top == rect.Height)
        {
            return true;
        }

        var positioned = NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        if (positioned)
        {
            _lastTargetRect = rect;
        }

        return positioned;
    }

    private void TargetTracker_Tick(object? sender, EventArgs e)
    {
        if (ApplyTargetBounds())
        {
            _consecutiveTargetFailures = 0;
            return;
        }

        if (++_consecutiveTargetFailures < 3)
        {
            return;
        }

        _targetTracker.Stop();
        _targetLost = true;
        System.Windows.MessageBox.Show(
            "Окно игры закрыто, скрыто или свёрнуто. Изменения редактора не применены.",
            "Редактор управления",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Close();
    }

    private void ProfileCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindBinding(e.OriginalSource as DependencyObject) is null)
        {
            _session.SelectedBinding = null;
        }
    }

    private void ProfileCanvas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var binding = FindBinding(e.OriginalSource as DependencyObject);
        if (binding is not null)
        {
            _session.SelectedBinding = binding;
            ShowBindingMenu();
        }
        else
        {
            _pendingAddPoint = e.GetPosition(ProfileCanvas);
            ShowPaletteMenu(ProfileCanvas, true);
        }

        e.Handled = true;
    }

    private void Binding_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingViewModel binding)
        {
            _session.SelectedBinding = binding;
        }
    }

    private void Binding_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingViewModel binding)
        {
            _session.SelectedBinding = binding;
            ShowBindingMenu();
            e.Handled = true;
        }
    }

    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingViewModel binding)
        {
            _session.SelectedBinding = binding;
            _session.Move(binding, e.HorizontalChange, e.VerticalChange);
        }
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingViewModel binding)
        {
            _session.SelectedBinding = binding;
            _session.Resize(binding, e.HorizontalChange, e.VerticalChange);
        }
    }

    private void AddControlButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingAddPoint = new System.Windows.Point(_session.ProfileWidth / 2d, _session.ProfileHeight / 2d);
        ShowPaletteMenu(AddControlButton, false);
    }

    private void ShowPaletteMenu(UIElement placementTarget, bool atMousePointer)
    {
        var menu = CreateBaseContextMenu();
        foreach (var paletteItem in ControlEditorSession.Palette)
        {
            var item = new MenuItem
            {
                Tag = paletteItem.Kind,
                Header = CreatePaletteHeader(paletteItem),
                Style = (Style)FindResource("OverlayMenuItem")
            };
            item.Click += PaletteItem_Click;
            menu.Items.Add(item);
        }

        menu.PlacementTarget = placementTarget;
        menu.Placement = atMousePointer ? PlacementMode.MousePoint : PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void PaletteItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is BindingKind kind)
        {
            var existingSingleton = kind is BindingKind.Aim or BindingKind.Joystick
                ? _session.Bindings.FirstOrDefault(binding => binding.Kind == kind)
                : null;
            _session.AddBinding(kind, _pendingAddPoint.X, _pendingAddPoint.Y);
            if (existingSingleton is not null)
            {
                System.Windows.MessageBox.Show(
                    kind == BindingKind.Aim
                        ? "Элемент камеры уже существует. Он выделен на поле."
                        : "Элемент движения WASD уже существует. Он выделен на поле.",
                    kind == BindingKind.Aim ? "Обзор камерой" : "Движение WASD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }

    private void ShowBindingMenu()
    {
        var menu = CreateBaseContextMenu();
        var duplicate = new MenuItem
        {
            Header = "⧉  Дублировать",
            Style = (Style)FindResource("OverlayMenuItem"),
            IsEnabled = _session.CanDuplicateSelection
        };
        duplicate.Click += DuplicateButton_Click;
        menu.Items.Add(duplicate);

        var delete = new MenuItem
        {
            Header = "⌫  Удалить",
            Style = (Style)FindResource("OverlayMenuItem"),
            IsEnabled = _session.CanDeleteSelection
        };
        delete.Click += DeleteButton_Click;
        menu.Items.Add(delete);
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private ContextMenu CreateBaseContextMenu() => new()
    {
        Style = (Style)FindResource("OverlayContextMenu")
    };

    private static FrameworkElement CreatePaletteHeader(ControlPaletteItem item)
    {
        var grid = new Grid { Width = 280, Margin = new Thickness(2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new TextBlock
        {
            Text = item.Icon,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 213, 255)),
            FontSize = 24,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(icon);

        var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = item.Title,
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14
        });
        text.Children.Add(new TextBlock
        {
            Text = item.Description,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(159, 176, 199)),
            FontSize = 11
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session.DuplicateSelected() is null && _session.SelectedBinding?.Kind == BindingKind.Aim)
        {
            System.Windows.MessageBox.Show(
                "Камера — единственный системный элемент, поэтому её нельзя дублировать.",
                "Редактор управления",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session.SelectedBinding?.Kind == BindingKind.Aim)
        {
            System.Windows.MessageBox.Show(
                "Камера нужна для режима обзора и не может быть удалена в этом редакторе.",
                "Редактор управления",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _session.DeleteSelected();
    }

    private void ClosePropertiesButton_Click(object sender, RoutedEventArgs e) => _session.SelectedBinding = null;

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving)
        {
            return;
        }

        _isSaving = true;
        _targetTracker.Stop();
        IsEnabled = false;
        try
        {
            if (!ApplyTargetBounds())
            {
                System.Windows.MessageBox.Show(
                    "Окно игры недоступно. Изменения не сохранены.",
                    "Редактор управления",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (await _mainViewModel.CommitControlEditorAsync(_session.ExportBindings()))
            {
                _allowCloseDuringSave = true;
                Close();
            }
        }
        finally
        {
            _isSaving = false;
            if (IsVisible)
            {
                IsEnabled = true;
                _targetTracker.Start();
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void HotkeyTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void MouseHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string hotkey } &&
            _session.SelectedBinding is { IsHotkeyReadOnly: false } binding)
        {
            binding.Hotkey = hotkey;
        }
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox { IsReadOnly: false } textBox)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Clear or Key.None)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
            key is Key.A or Key.C or Key.V or Key.X or Key.Y or Key.Z)
        {
            return;
        }

        if (key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9)
        {
            var modifiers = new List<string>();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers.Add("Ctrl");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers.Add("Shift");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers.Add("Alt");
            if (modifiers.Count == 0)
            {
                return;
            }

            modifiers.Add(key.ToString());
            textBox.Text = string.Join('+', modifiers);
            textBox.CaretIndex = textBox.Text.Length;
            e.Handled = true;
            return;
        }

        if (key is Key.Back or Key.Delete or Key.Left or Key.Right or Key.Home or Key.End)
        {
            return;
        }

        textBox.Text = key.ToString();
        textBox.CaretIndex = textBox.Text.Length;
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.FocusedElement != HotkeyTextBox)
        {
            Close();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.S)
        {
            SaveButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.D)
        {
            DuplicateButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _targetTracker.Stop();
        _targetTracker.Tick -= TargetTracker_Tick;
        Closed -= Window_Closed;

        if (_targetLost)
        {
            _mainViewModel.NotifyControlEditorTargetLost();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isSaving && !_allowCloseDuringSave)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private static BindingViewModel? FindBinding(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: BindingViewModel binding })
            {
                return binding;
            }
        }

        return null;
    }
}
