using System.Text;

namespace GameControlMapper.TouchTestHarness;
public enum HarnessTouchState { Down, Move, Up }
public sealed record HarnessContact(int Id,double X,double Y,HarnessTouchState State);
public sealed record HarnessEvent(DateTimeOffset Timestamp,int Id,double X,double Y,HarnessTouchState State,string? ProtocolError);
public sealed record HarnessReport(string Text,bool Passed);

public sealed class TouchLifecycleTracker
{
    private readonly int _logLimit;private readonly Dictionary<int,HarnessContact> _active=[];private readonly HashSet<int> _completed=[];private readonly List<HarnessEvent> _events=[];
    public TouchLifecycleTracker(int logLimit=500)=>_logLimit=Math.Max(1,logLimit);
    public IReadOnlyDictionary<int,HarnessContact> ActiveContacts=>new Dictionary<int,HarnessContact>(_active);public IReadOnlyList<HarnessEvent> Events=>_events.ToArray();
    public int DownCount{get;private set;}public int MoveCount{get;private set;}public int UpCount{get;private set;}public int MaximumConcurrentContacts{get;private set;}public int ProtocolErrorCount=>_events.Count(e=>e.ProtocolError is not null);
    public HarnessEvent Process(int id,double x,double y,HarnessTouchState state,DateTimeOffset? timestamp=null)
    {
        string? error=null;
        switch(state)
        {
            case HarnessTouchState.Down: DownCount++;if(_active.ContainsKey(id))error="Repeated Down without Up";else{_completed.Remove(id);_active[id]=new(id,x,y,state);MaximumConcurrentContacts=Math.Max(MaximumConcurrentContacts,_active.Count);}break;
            case HarnessTouchState.Move: MoveCount++;if(!_active.ContainsKey(id))error="Update before Down";else _active[id]=new(id,x,y,state);break;
            case HarnessTouchState.Up: UpCount++;if(!_active.Remove(id)){error=_completed.Contains(id)?"Second Up":"Up without Down";}else _completed.Add(id);break;
        }
        var item=new HarnessEvent(timestamp??DateTimeOffset.Now,id,x,y,state,error);_events.Add(item);if(_events.Count>_logLimit)_events.RemoveRange(0,_events.Count-_logLimit);return item;
    }
    public void ClearLog()=>_events.Clear();
    public IReadOnlyList<HarnessEvent> RecordActiveContactErrors(string reason)
    {
        var added=new List<HarnessEvent>();foreach(var c in _active.Values.ToArray()){var item=new HarnessEvent(DateTimeOffset.Now,c.Id,c.X,c.Y,c.State,$"Contact remained active during {reason}");_events.Add(item);added.Add(item);}if(_events.Count>_logLimit)_events.RemoveRange(0,_events.Count-_logLimit);return added;
    }
    public void Reset(){var orphaned=_active.Values.ToArray();_active.Clear();_completed.Clear();_events.Clear();DownCount=MoveCount=UpCount=MaximumConcurrentContacts=0;foreach(var c in orphaned)_events.Add(new(DateTimeOffset.Now,c.Id,c.X,c.Y,c.State,"Contact remained active during reset"));}
    public HarnessReport Export(string commitHash,string windowsVersion,double dpi,nint hwnd,string geometry)
    {
        var pass=_active.Count==0&&ProtocolErrorCount==0;var b=new StringBuilder();b.AppendLine($"Touch validation report {DateTimeOffset.Now:O}").AppendLine($"Commit: {commitHash}").AppendLine($"Windows: {windowsVersion}").AppendLine($"DPI: {dpi:F0}").AppendLine($"HWND: 0x{hwnd:X}").AppendLine($"Client: {geometry}").AppendLine($"Down={DownCount} Move={MoveCount} Up={UpCount} MaxConcurrent={MaximumConcurrentContacts}").AppendLine($"ProtocolErrors={ProtocolErrorCount}");foreach(var e in _events)b.AppendLine($"{e.Timestamp:O} ID={e.Id} {e.State} X={e.X:F1} Y={e.Y:F1}{(e.ProtocolError is null?"":$" PROTOCOL ERROR: {e.ProtocolError}")}");b.AppendLine(pass?"RESULT: PASS":"RESULT: FAIL");return new(b.ToString(),pass);
    }
}
