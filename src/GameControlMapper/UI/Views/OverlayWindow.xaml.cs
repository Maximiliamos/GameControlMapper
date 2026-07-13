using System.Windows;
using System.Windows.Interop;
using GameControlMapper.Services;
using GameControlMapper.Win32;
using GameControlMapper.ViewModels;

namespace GameControlMapper.UI.Views;

public partial class OverlayWindow : Window
{
    private readonly IGameWindowGeometryProvider _geometryProvider;
    private readonly nint _targetWindow;

    public OverlayWindow(
        MainViewModel viewModel,
        IGameWindowGeometryProvider geometryProvider,
        nint targetWindow)
    {
        InitializeComponent();
        DataContext = viewModel;
        _geometryProvider = geometryProvider;
        _targetWindow = targetWindow;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(
            handle,
            NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);

        var geometry = _geometryProvider.GetClientRect(_targetWindow);
        if (!geometry.Succeeded || !NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HWND_TOPMOST,
                geometry.ClientRect.Left,
                geometry.ClientRect.Top,
                geometry.ClientRect.Width,
                geometry.ClientRect.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
        {
            Dispatcher.BeginInvoke(Close);
        }
    }
}
