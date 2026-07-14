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
    private readonly TouchLifecycleTracker _tracker=new();private readonly GuidedValidationSession _guided=new();private readonly WindowsMonitorInformationProvider _monitorProvider=new();private readonly Dictionary<int,FrameworkElement> _markers=[];private readonly string _commit;
    public MainWindow(){InitializeComponent();_commit=Arg("--commit")??"not supplied";ScenarioList.ItemsSource=_guided.Scenarios;Loaded+=(_,_)=>{var harnessHandle=new WindowInteropHelper(this).Handle;var targetHandle=TargetWindowArg();_guided.SetEnvironment(_monitorProvider.Capture(harnessHandle,targetHandle));UpdateWindowInfo();UpdateUi();var export=Arg("--export-report");if(!string.IsNullOrWhiteSpace(export)){var report=_guided.CreateReport(_tracker,Arg("--version")??"1.0.0-beta.3",_commit,Arg("--application-commit")??_commit,Arg("--application-archive")??"",Arg("--harness-archive")??"",harnessHandle,targetHandle,_monitorProvider);GuidedValidationSession.Export(report,export);}};Closing+=(_,_)=>_tracker.RecordActiveContactErrors("window close");}
    private void OnTouchDown(object s,TouchEventArgs e)=>Handle(e,HarnessTouchState.Down);
    private void OnTouchMove(object s,TouchEventArgs e)=>Handle(e,HarnessTouchState.Move);
    private void OnTouchUp(object s,TouchEventArgs e)=>Handle(e,HarnessTouchState.Up);
    private void Handle(TouchEventArgs e,HarnessTouchState state){var p=e.GetTouchPoint(TouchCanvas).Position;var id=e.TouchDevice.Id;var item=_tracker.Process(id,p.X,p.Y,state);if(state==HarnessTouchState.Up)RemoveMarker(id);else UpdateMarker(id,p,state);AddLog(item);UpdateUi();e.Handled=true;}
    private void UpdateMarker(int id,Point p,HarnessTouchState state){if(!_markers.TryGetValue(id,out var marker)){var panel=new Border{Width=92,Height=92,CornerRadius=new(46),Background=new SolidColorBrush(Color.FromArgb(150,76,201,240)),BorderBrush=Brushes.White,BorderThickness=new(2),Child=new TextBlock{Foreground=Brushes.White,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};marker=panel;_markers[id]=marker;TouchCanvas.Children.Add(marker);}((TextBlock)((Border)marker).Child).Text=$"ID {id}\n{state}\n{p.X:F0},{p.Y:F0}";Canvas.SetLeft(marker,p.X-46);Canvas.SetTop(marker,p.Y-46);}
    private void RemoveMarker(int id){if(_markers.Remove(id,out var marker))TouchCanvas.Children.Remove(marker);}
    private void AddLog(HarnessEvent e){var text=$"{e.Timestamp:HH:mm:ss.fff} ID={e.Id} {e.State} X={e.X:F1} Y={e.Y:F1}"+(e.ProtocolError is null?"":$"  PROTOCOL ERROR: {e.ProtocolError}");EventLog.Items.Insert(0,text);while(EventLog.Items.Count>500)EventLog.Items.RemoveAt(EventLog.Items.Count-1);}
    private void UpdateUi(){ActiveText.Text=$"Active: {_tracker.ActiveContacts.Count}";DownText.Text=$"Down: {_tracker.DownCount}";MoveText.Text=$"Move: {_tracker.MoveCount}";UpText.Text=$"Up: {_tracker.UpCount}";MaxText.Text=$"Максимум одновременно: {_tracker.MaximumConcurrentContacts}; protocol errors: {_tracker.ProtocolErrorCount}";FocusText.Text=$"Keyboard focus: {IsKeyboardFocusWithin}";FocusText.Foreground=IsKeyboardFocusWithin?Brushes.LightGreen:Brushes.Orange;UpdateEvidenceUi();}
    private void OnFocusChanged(object s,KeyboardFocusChangedEventArgs e)=>UpdateUi();
    private void ClearLog(object s,RoutedEventArgs e){_tracker.ClearLog();EventLog.Items.Clear();}
    private void Reset(object s,RoutedEventArgs e){if(!_tracker.TryResetValidationSession(out var error)){MessageBox.Show(error,"Проверка",MessageBoxButton.OK,MessageBoxImage.Warning);return;}_guided.ResetValidationSession();_markers.Clear();TouchCanvas.Children.Clear();EventLog.Items.Clear();UpdateUi();}
    private void CheckActive(object s,RoutedEventArgs e){var ids=_tracker.ActiveContacts.Keys.OrderBy(x=>x).ToArray();if(ids.Length>0)foreach(var item in _tracker.RecordActiveContactErrors("manual check"))AddLog(item);UpdateUi();MessageBox.Show(ids.Length==0?"PASS — активных контактов нет.":$"FAIL — активные ID: {string.Join(", ",ids)}","Проверка контактов");}
    private void Export(object s,RoutedEventArgs e){var d=new SaveFileDialog{Filter="Text report (*.txt)|*.txt",FileName="touch-validation-report.txt"};if(d.ShowDialog()!=true)return;var dpi=VisualTreeHelper.GetDpi(this).PixelsPerInchX;File.WriteAllText(d.FileName,_tracker.Export(_commit,Environment.OSVersion.VersionString,dpi,new WindowInteropHelper(this).Handle,GeometryText()).Text);}
    private void GuidedPass(object s,RoutedEventArgs e)=>SetGuided(ValidationStatus.Passed);
    private void GuidedFail(object s,RoutedEventArgs e)=>SetGuided(ValidationStatus.Failed);
    private void GuidedUnavailable(object s,RoutedEventArgs e)=>SetGuided(ValidationStatus.NotAvailable);
    private void SetGuided(ValidationStatus status){if(ScenarioList.SelectedItem is not ValidationScenario scenario){MessageBox.Show("Выберите сценарий.");return;}if(!_guided.SetStatus(scenario.Id,status,ScenarioComment.Text,_tracker,out var error)){MessageBox.Show(error,"Проверка",MessageBoxButton.OK,MessageBoxImage.Warning);return;}ScenarioList.Items.Refresh();var next=_guided.Scenarios.FirstOrDefault(x=>x.Status==ValidationStatus.NotStarted);if(next is not null)ScenarioList.SelectedItem=next;ScenarioComment.Clear();}
    private void ScenarioSelectionChanged(object s,SelectionChangedEventArgs e){if(ScenarioList.SelectedItem is ValidationScenario scenario)_guided.BeginScenario(scenario.Id,_tracker);UpdateEvidenceUi();}
    private void UpdateEvidenceUi(){if(ScenarioList.SelectedItem is not ValidationScenario scenario){GuidedPassButton.IsEnabled=false;EvidenceText.Text="Выберите сценарий";return;}var canPass=_guided.CanPass(scenario.Id,_tracker,out var reason);GuidedPassButton.IsEnabled=canPass;EvidenceText.Text=GuidedValidationSession.RequiresMachineEvidence(scenario.Id)?$"Автопроверка: {(canPass?"PASS":"ожидание")} — {reason}":"Ручной сценарий: подтвердите результат после проверки";}
    private void ExportGuided(object s,RoutedEventArgs e){var d=new SaveFileDialog{Filter="JSON report (*.json)|*.json",FileName="manual-validation-report.json"};if(d.ShowDialog()!=true)return;var version=Arg("--version")??"1.0.0-beta.3";var report=_guided.CreateReport(_tracker,version,_commit,Arg("--application-commit")??_commit,Arg("--application-archive")??"",Arg("--harness-archive")??"",new WindowInteropHelper(this).Handle,TargetWindowArg(),_monitorProvider);GuidedValidationSession.Export(report,d.FileName);MessageBox.Show($"Созданы:\n{d.FileName}\n{System.IO.Path.ChangeExtension(d.FileName,".txt")}");}
    private static string? Arg(string name){var a=Environment.GetCommandLineArgs();var i=Array.IndexOf(a,name);return i>=0&&i+1<a.Length?a[i+1]:null;}
    private static nint TargetWindowArg(){var value=Arg("--target-window");if(string.IsNullOrWhiteSpace(value))return 0;value=value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?value[2..]:value;return long.TryParse(value,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out var handle)?new nint(handle):0;}
    private void UpdateWindowInfo(){WindowText.Text=$"HWND: 0x{new WindowInteropHelper(this).Handle:X}\nPID: {Environment.ProcessId}\nClient: {GeometryText()}\nDPI: {VisualTreeHelper.GetDpi(this).PixelsPerInchX:F0}";}
    private string GeometryText(){var h=new WindowInteropHelper(this).Handle;if(!GetClientRect(h,out var r))return "unavailable";var p=new POINT();ClientToScreen(h,ref p);return $"origin=({p.X},{p.Y}) size={r.Right-r.Left}x{r.Bottom-r.Top}";}
    [StructLayout(LayoutKind.Sequential)]private struct RECT{public int Left,Top,Right,Bottom;}[StructLayout(LayoutKind.Sequential)]private struct POINT{public int X,Y;}[DllImport("user32.dll")]private static extern bool GetClientRect(nint h,out RECT r);[DllImport("user32.dll")]private static extern bool ClientToScreen(nint h,ref POINT p);
}
