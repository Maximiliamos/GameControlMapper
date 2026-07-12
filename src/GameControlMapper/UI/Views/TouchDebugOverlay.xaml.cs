using System.Windows;
using System.Windows.Interop;
using GameControlMapper.ViewModels;
using GameControlMapper.Win32;

namespace GameControlMapper.UI.Views;

public partial class TouchDebugOverlay : Window
{
    public TouchDebugOverlay(TouchDebugViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW);
    }
}
