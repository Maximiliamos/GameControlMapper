using System.Windows;
using System.Windows.Input;

namespace GameControlMapper.UI.Views;

public partial class PointCaptureWindow : Window
{
    public PointCaptureWindow()
    {
        InitializeComponent();
    }

    public System.Windows.Point SelectedPoint { get; private set; }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SelectedPoint = e.GetPosition(this);
        DialogResult = true;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
