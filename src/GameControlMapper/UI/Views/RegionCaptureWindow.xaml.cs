using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GameControlMapper.UI.Views;

public partial class RegionCaptureWindow : Window
{
    private System.Windows.Point _start;
    private bool _isDragging;

    public RegionCaptureWindow()
    {
        InitializeComponent();
    }

    public System.Windows.Rect SelectedRect { get; private set; }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _isDragging = true;
        SelectionRectangle.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var current = e.GetPosition(this);
        var rect = new System.Windows.Rect(_start, current);
        Canvas.SetLeft(SelectionRectangle, rect.Left);
        Canvas.SetTop(SelectionRectangle, rect.Top);
        SelectionRectangle.Width = rect.Width;
        SelectionRectangle.Height = rect.Height;
    }

    private void Window_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        ReleaseMouseCapture();
        _isDragging = false;
        var current = e.GetPosition(this);
        SelectedRect = new System.Windows.Rect(_start, current);
        DialogResult = SelectedRect.Width >= 4 && SelectedRect.Height >= 4;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
