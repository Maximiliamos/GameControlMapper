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
    private OverlayWindow? _overlayWindow;
    private bool _isClosing;

    public MainWindow(MainViewModel viewModel, InputMappingEngine mappingEngine)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _mappingEngine = mappingEngine;
        DataContext = viewModel;
        _viewModel.SelectAreaRequested += OnSelectAreaRequested;
        _viewModel.PickCenterRequested += OnPickCenterRequested;
        _viewModel.ToggleOverlayRequested += OnToggleOverlayRequested;
        _viewModel.EditorRequested += OnEditorRequested;
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

        if (_isClosing)
        {
            return;
        }

        if (_overlayWindow?.IsVisible == true)
        {
            _overlayWindow.Hide();
            return;
        }

        _overlayWindow ??= new OverlayWindow(_viewModel);
        _overlayWindow.Show();
    }

    private void OnEditorRequested(object? sender, EventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        _mappingEngine.Stop();
        _overlayWindow?.Close();
        _overlayWindow = null;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.SelectAreaRequested -= OnSelectAreaRequested;
        _viewModel.PickCenterRequested -= OnPickCenterRequested;
        _viewModel.ToggleOverlayRequested -= OnToggleOverlayRequested;
        _viewModel.EditorRequested -= OnEditorRequested;
        Closing -= OnClosing;
        Closed -= OnClosed;
        System.Windows.Application.Current.Shutdown();
    }
}
