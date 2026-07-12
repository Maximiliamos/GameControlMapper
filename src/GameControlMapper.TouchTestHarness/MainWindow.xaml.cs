using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace GameControlMapper.TouchTestHarness;
public partial class MainWindow : Window
{
    private readonly TouchLifecycleTracker _tracker=new();private readonly Dictionary<int,FrameworkElement> _markers=[];private readonly string _commit;
    public MainWindow(){InitializeComponent();_commit=Environment.GetCommandLineArgs().SkipWhile(x=>x!="--commit").Skip(1).FirstOrDefault()??"not supplied";Loaded+=(_,_)=>{UpdateWindowInfo();UpdateUi();};Closing+=(_,_)=>_tracker.RecordActiveContactErrors("window close");}
    private void OnTouchDown(object s,TouchEventArgs e)=>Handle(e,HarnessTouchState.Down);
    private void OnTouchMove(object s,TouchEventArgs e)=>Handle(e,HarnessTouchState.Move);
    private void OnTouchUp(object s,TouchEventArgs e)=>Handle(e,HarnessTouchState.Up);
    private void Handle(TouchEventArgs e,HarnessTouchState state){var p=e.GetTouchPoint(TouchCanvas).Position;var id=e.TouchDevice.Id;var item=_tracker.Process(id,p.X,p.Y,state);if(state==HarnessTouchState.Up)RemoveMarker(id);else UpdateMarker(id,p,state);AddLog(item);UpdateUi();e.Handled=true;}
    private void UpdateMarker(int id,Point p,HarnessTouchState state){if(!_markers.TryGetValue(id,out var marker)){var panel=new Border{Width=92,Height=92,CornerRadius=new(46),Background=new SolidColorBrush(Color.FromArgb(150,76,201,240)),BorderBrush=Brushes.White,BorderThickness=new(2),Child=new TextBlock{Foreground=Brushes.White,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};marker=panel;_markers[id]=marker;TouchCanvas.Children.Add(marker);}((TextBlock)((Border)marker).Child).Text=$"ID {id}\n{state}\n{p.X:F0},{p.Y:F0}";Canvas.SetLeft(marker,p.X-46);Canvas.SetTop(marker,p.Y-46);}
    private void RemoveMarker(int id){if(_markers.Remove(id,out var marker))TouchCanvas.Children.Remove(marker);}
    private void AddLog(HarnessEvent e){var text=$"{e.Timestamp:HH:mm:ss.fff} ID={e.Id} {e.State} X={e.X:F1} Y={e.Y:F1}"+(e.ProtocolError is null?"":$"  PROTOCOL ERROR: {e.ProtocolError}");EventLog.Items.Insert(0,text);while(EventLog.Items.Count>500)EventLog.Items.RemoveAt(EventLog.Items.Count-1);}
    private void UpdateUi(){ActiveText.Text=$"Active: {_tracker.ActiveContacts.Count}";DownText.Text=$"Down: {_tracker.DownCount}";MoveText.Text=$"Move: {_tracker.MoveCount}";UpText.Text=$"Up: {_tracker.UpCount}";MaxText.Text=$"Максимум одновременно: {_tracker.MaximumConcurrentContacts}; protocol errors: {_tracker.ProtocolErrorCount}";FocusText.Text=$"Keyboard focus: {IsKeyboardFocusWithin}";FocusText.Foreground=IsKeyboardFocusWithin?Brushes.LightGreen:Brushes.Orange;}
    private void OnFocusChanged(object s,KeyboardFocusChangedEventArgs e)=>UpdateUi();
    private void ClearLog(object s,RoutedEventArgs e){_tracker.ClearLog();EventLog.Items.Clear();}
    private void Reset(object s,RoutedEventArgs e){_tracker.Reset();_markers.Clear();TouchCanvas.Children.Clear();EventLog.Items.Clear();UpdateUi();}
    private void CheckActive(object s,RoutedEventArgs e){var ids=_tracker.ActiveContacts.Keys.OrderBy(x=>x).ToArray();if(ids.Length>0)foreach(var item in _tracker.RecordActiveContactErrors("manual check"))AddLog(item);UpdateUi();MessageBox.Show(ids.Length==0?"PASS — активных контактов нет.":$"FAIL — активные ID: {string.Join(", ",ids)}","Проверка контактов");}
    private void Export(object s,RoutedEventArgs e){var d=new SaveFileDialog{Filter="Text report (*.txt)|*.txt",FileName="touch-validation-report.txt"};if(d.ShowDialog()!=true)return;var dpi=VisualTreeHelper.GetDpi(this).PixelsPerInchX;File.WriteAllText(d.FileName,_tracker.Export(_commit,Environment.OSVersion.VersionString,dpi,new WindowInteropHelper(this).Handle,GeometryText()).Text);}
    private void UpdateWindowInfo(){WindowText.Text=$"HWND: 0x{new WindowInteropHelper(this).Handle:X}\nPID: {Environment.ProcessId}\nClient: {GeometryText()}\nDPI: {VisualTreeHelper.GetDpi(this).PixelsPerInchX:F0}";}
    private string GeometryText(){var h=new WindowInteropHelper(this).Handle;if(!GetClientRect(h,out var r))return "unavailable";var p=new POINT();ClientToScreen(h,ref p);return $"origin=({p.X},{p.Y}) size={r.Right-r.Left}x{r.Bottom-r.Top}";}
    [StructLayout(LayoutKind.Sequential)]private struct RECT{public int Left,Top,Right,Bottom;}[StructLayout(LayoutKind.Sequential)]private struct POINT{public int X,Y;}[DllImport("user32.dll")]private static extern bool GetClientRect(nint h,out RECT r);[DllImport("user32.dll")]private static extern bool ClientToScreen(nint h,ref POINT p);
}
