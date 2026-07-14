using System.Text;

namespace GameControlMapper.TouchTestHarness;

public enum HarnessTouchState { Down, Move, Up }
public sealed record HarnessContact(int Id,double X,double Y,HarnessTouchState State);
public sealed record HarnessEvent(long Sequence,DateTimeOffset Timestamp,int Id,double X,double Y,HarnessTouchState State,string? ProtocolError);
public sealed record HarnessReport(string Text,bool Passed);

/// <summary>
/// Keeps a bounded display log separately from the complete validation evidence.
/// Evidence is retained until an explicit validation-session reset.
/// </summary>
public sealed class TouchLifecycleTracker
{
    private readonly int _logLimit;
    private readonly Dictionary<int,HarnessContact> _active=[];
    private readonly HashSet<int> _completed=[];
    private readonly List<HarnessEvent> _displayEvents=[];
    private readonly List<HarnessEvent> _validationEvents=[];
    private long _sequence;

    public TouchLifecycleTracker(int logLimit=500)=>_logLimit=Math.Max(1,logLimit);
    public IReadOnlyDictionary<int,HarnessContact> ActiveContacts=>new Dictionary<int,HarnessContact>(_active);
    public IReadOnlyList<HarnessEvent> Events=>_displayEvents.ToArray();
    public IReadOnlyList<HarnessEvent> ValidationEvents=>_validationEvents.ToArray();
    public int DownCount{get;private set;}
    public int MoveCount{get;private set;}
    public int UpCount{get;private set;}
    public int MaximumConcurrentContacts{get;private set;}
    public int ProtocolErrorCount=>_validationEvents.Count(e=>e.ProtocolError is not null);

    public HarnessEvent Process(int id,double x,double y,HarnessTouchState state,DateTimeOffset? timestamp=null)
    {
        string? error=null;
        switch(state)
        {
            case HarnessTouchState.Down:
                DownCount++;
                if(_active.ContainsKey(id))error="Repeated Down without Up";
                else{_completed.Remove(id);_active[id]=new(id,x,y,state);MaximumConcurrentContacts=Math.Max(MaximumConcurrentContacts,_active.Count);}
                break;
            case HarnessTouchState.Move:
                MoveCount++;
                if(!_active.ContainsKey(id))error="Update before Down";
                else _active[id]=new(id,x,y,state);
                break;
            case HarnessTouchState.Up:
                UpCount++;
                if(!_active.Remove(id))error=_completed.Contains(id)?"Second Up":"Up without Down";
                else _completed.Add(id);
                break;
        }
        var item=new HarnessEvent(Interlocked.Increment(ref _sequence),timestamp??DateTimeOffset.Now,id,x,y,state,error);
        Add(item);
        return item;
    }

    public void ClearLog()=>_displayEvents.Clear();

    public IReadOnlyList<HarnessEvent> RecordActiveContactErrors(string reason)
    {
        var added=new List<HarnessEvent>();
        foreach(var c in _active.Values.ToArray())
        {
            var item=new HarnessEvent(Interlocked.Increment(ref _sequence),DateTimeOffset.Now,c.Id,c.X,c.Y,c.State,$"Contact remained active during {reason}");
            Add(item);added.Add(item);
        }
        return added;
    }

    public bool TryResetValidationSession(out string? error)
    {
        if(_active.Count!=0){error="Cannot reset validation while contacts are active.";return false;}
        error=null;
        _completed.Clear();_displayEvents.Clear();_validationEvents.Clear();
        DownCount=MoveCount=UpCount=MaximumConcurrentContacts=0;_sequence=0;
        return true;
    }

    // Compatibility entry point for callers that intentionally discard a completed session.
    public void Reset()
    {
        if(!TryResetValidationSession(out var error))throw new InvalidOperationException(error);
    }

    public HarnessReport Export(string commitHash,string windowsVersion,double dpi,nint hwnd,string geometry)
    {
        var pass=_active.Count==0&&ProtocolErrorCount==0;var b=new StringBuilder();
        b.AppendLine($"Touch validation report {DateTimeOffset.Now:O}").AppendLine($"Commit: {commitHash}").AppendLine($"Windows: {windowsVersion}").AppendLine($"DPI: {dpi:F0}").AppendLine($"HWND: 0x{hwnd:X}").AppendLine($"Client: {geometry}").AppendLine($"Down={DownCount} Move={MoveCount} Up={UpCount} MaxConcurrent={MaximumConcurrentContacts}").AppendLine($"ProtocolErrors={ProtocolErrorCount}");
        foreach(var e in _validationEvents)b.AppendLine($"{e.Timestamp:O} #{e.Sequence} ID={e.Id} {e.State} X={e.X:F1} Y={e.Y:F1}{(e.ProtocolError is null?"":$" PROTOCOL ERROR: {e.ProtocolError}")}");
        b.AppendLine(pass?"RESULT: PASS":"RESULT: FAIL");return new(b.ToString(),pass);
    }

    private void Add(HarnessEvent item)
    {
        _validationEvents.Add(item);
        _displayEvents.Add(item);
        if(_displayEvents.Count>_logLimit)_displayEvents.RemoveRange(0,_displayEvents.Count-_logLimit);
    }
}
