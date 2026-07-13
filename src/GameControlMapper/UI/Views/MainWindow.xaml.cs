using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GameControlMapper.Services;
using GameControlMapper.ViewModels;

namespace GameControlMapper.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly InputMappingEngine _mappingEngine;
    private readonly IGameWindowGeometryProvider _geometryProvider;
    private OverlayWindow? _overlayWindow;
    private ControlEditorOverlayWindow? _controlEditorWindow;
    private bool _isClosing;

    public MainWindow(
        MainViewModel viewModel,
        InputMappingEngine mappingEngine,
        IGameWindowGeometryProvider geometryProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _mappingEngine = mappingEngine;
        _geometryProvider = geometryProvider;
        DataContext = viewModel;
        _viewModel.SelectAreaRequested += OnSelectAreaRequested;
        _viewModel.PickCenterRequested += OnPickCenterRequested;
        _viewModel.ToggleOverlayRequested += OnToggleOverlayRequested;
        _viewModel.ShowOverlayRequested += OnShowOverlayRequested;
        _viewModel.HideOverlayRequested += OnHideOverlayRequested;
        _viewModel.EditorRequested += OnEditorRequested;
        _viewModel.ControlEditorRequested += OnControlEditorRequested;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void BindingThumb_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingViewModel binding)
        {
            _viewModel.SelectedBinding = binding;
            e.Handled = false;
        }
    }

    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingViewModel binding)
        {
            _viewModel.SelectedBinding = binding;
            binding.X = Math.Max(0, binding.X + e.HorizontalChange);
            binding.Y = Math.Max(0, binding.Y + e.VerticalChange);
        }
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingViewModel binding)
        {
            _viewModel.SelectedBinding = binding;
            binding.Width = Math.Max(10, binding.Width + e.HorizontalChange);
            binding.Height = Math.Max(10, binding.Height + e.VerticalChange);
        }
    }

    private void EditorCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender)
        {
            _viewModel.SelectedBinding = null;
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
        {
            _viewModel.CopyBindingCommand.Execute(null);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V)
        {
            _viewModel.PasteBindingCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _viewModel.DeleteBindingCommand.Execute(null);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                 (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift &&
                 e.Key == Key.S)
        {
            _viewModel.SaveProfileCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSelectAreaRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnSelectAreaRequested(sender, e));
            return;
        }

        var capture = new RegionCaptureWindow { Owner = this };
        if (capture.ShowDialog() == true)
        {
            _viewModel.ApplySelectedArea(capture.SelectedRect);
        }
    }

    private void OnPickCenterRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnPickCenterRequested(sender, e));
            return;
        }

        var capture = new PointCaptureWindow { Owner = this };
        if (capture.ShowDialog() == true)
        {
            _viewModel.ApplySelectedCenter(capture.SelectedPoint);
        }
    }

    private void OnToggleOverlayRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnToggleOverlayRequested(sender, e));
            return;
        }

        if (_isClosing || _controlEditorWindow?.IsVisible == true)
        {
            return;
        }

        if (_overlayWindow?.IsVisible == true)
        {
            _overlayWindow.Hide();
            return;
        }

        ShowPassiveOverlay();
    }

    private void OnShowOverlayRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnShowOverlayRequested(sender, e));
            return;
        }

        if (_isClosing || _controlEditorWindow?.IsVisible == true || _overlayWindow?.IsVisible == true)
        {
            return;
        }

        ShowPassiveOverlay();
    }

    private void OnHideOverlayRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnHideOverlayRequested(sender, e));
            return;
        }

        _overlayWindow?.Hide();
    }

    private void ShowPassiveOverlay()
    {
        _overlayWindow?.Close();
        _overlayWindow = new OverlayWindow(
            _viewModel,
            _geometryProvider,
            new IntPtr(_viewModel.CurrentProfile.Window.WindowHandle));
        _overlayWindow.Show();
    }

    private void OnEditorRequested(object? sender, EventArgs e)
    {
        _viewModel.OpenControlEditorCommand.Execute(null);
    }

    private void OnControlEditorRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnControlEditorRequested(sender, e));
            return;
        }

        if (_isClosing)
        {
            return;
        }

        if (_controlEditorWindow?.IsVisible == true)
        {
            _controlEditorWindow.Activate();
            return;
        }

        _overlayWindow?.Hide();
        var targetHandle = new IntPtr(_viewModel.CurrentProfile.Window.WindowHandle);
        var geometry = _geometryProvider.GetClientRect(targetHandle);
        if (!geometry.Succeeded)
        {
            System.Windows.MessageBox.Show(
                "Выбранное окно игры сейчас недоступно. Разверните его и обновите список окон.",
                "Редактор управления",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            _controlEditorWindow = new ControlEditorOverlayWindow(_viewModel, _geometryProvider, targetHandle);
            _controlEditorWindow.Closed += OnControlEditorClosed;
            _controlEditorWindow.Show();
            Hide();
            _controlEditorWindow.Activate();
        }
        catch (Exception ex)
        {
            if (_controlEditorWindow is not null)
            {
                _controlEditorWindow.Closed -= OnControlEditorClosed;
                _controlEditorWindow = null;
            }

            Show();
            Activate();
            System.Windows.MessageBox.Show(
                $"Не удалось открыть редактор: {ex.Message}",
                "Редактор управления",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnControlEditorClosed(object? sender, EventArgs e)
    {
        if (_controlEditorWindow is not null)
        {
            _controlEditorWindow.Closed -= OnControlEditorClosed;
            _controlEditorWindow = null;
        }

        if (_isClosing)
        {
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing) return;
        e.Cancel = true;
        _isClosing = true;
        try
        {
            await _mappingEngine.StopAsync();
            if (_controlEditorWindow is not null)
            {
                _controlEditorWindow.Closed -= OnControlEditorClosed;
                _controlEditorWindow.Close();
                _controlEditorWindow = null;
            }
            _overlayWindow?.Close();
            _overlayWindow = null;
            _ = Dispatcher.BeginInvoke(Close);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Ошибка завершения", MessageBoxButton.OK, MessageBoxImage.Error);
            _isClosing = false;
            e.Cancel = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.SelectAreaRequested -= OnSelectAreaRequested;
        _viewModel.PickCenterRequested -= OnPickCenterRequested;
        _viewModel.ToggleOverlayRequested -= OnToggleOverlayRequested;
        _viewModel.ShowOverlayRequested -= OnShowOverlayRequested;
        _viewModel.HideOverlayRequested -= OnHideOverlayRequested;
        _viewModel.EditorRequested -= OnEditorRequested;
        _viewModel.ControlEditorRequested -= OnControlEditorRequested;
        Closing -= OnClosing;
        Closed -= OnClosed;
        System.Windows.Application.Current.Shutdown();
    }
}
