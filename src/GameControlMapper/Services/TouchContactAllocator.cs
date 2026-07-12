using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public enum TouchLeaseState { Active, ReleasePending, Released, Quarantined }
public sealed class TouchContactLease
{
    internal TouchContactLease(int id,long generation,string owner,long sequence){ContactId=id;SessionGeneration=generation;OwnerId=owner;Sequence=sequence;}
    public int ContactId{get;} public long SessionGeneration{get;} public string OwnerId{get;} public long Sequence{get;}
    public TouchLeaseState State{get;internal set;}=TouchLeaseState.Active;
}
public interface ITouchContactAllocator
{
    TouchContactLease? TryAcquire(long generation,string ownerId);
    bool RequestRelease(TouchContactLease lease);
    void CompleteSuccessfulUp(IEnumerable<int> ids);
    void QuarantineFailedUp(IEnumerable<int> ids);
    void Reset(long backendGeneration);
    IReadOnlyList<TouchContactLease> ActiveLeases { get; }
}
public sealed class TouchContactAllocator : ITouchContactAllocator
{
    private readonly object _gate=new();private readonly int _capacity;private readonly ILogger<TouchContactAllocator> _logger;
    private readonly Dictionary<int,TouchContactLease> _leases=[];private readonly HashSet<int> _quarantine=[];private long _sequence,_backendGeneration;
    public TouchContactAllocator(Models.TouchCapabilities capabilities,ILogger<TouchContactAllocator> logger){_capacity=capabilities.MaxContacts;_logger=logger;}
    public IReadOnlyList<TouchContactLease> ActiveLeases{get{lock(_gate)return _leases.Values.ToArray();}}
    public TouchContactLease? TryAcquire(long generation,string ownerId){lock(_gate){for(var id=0;id<_capacity;id++)if(!_leases.ContainsKey(id)&&!_quarantine.Contains(id)){var l=new TouchContactLease(id,generation,ownerId,++_sequence);_leases[id]=l;return l;}_logger.LogWarning("Touch capacity exhausted for owner {Owner}; capacity {Capacity}",ownerId,_capacity);return null;}}
    public bool RequestRelease(TouchContactLease lease){lock(_gate){if(!_leases.TryGetValue(lease.ContactId,out var current)||!ReferenceEquals(current,lease)||current.SessionGeneration!=lease.SessionGeneration)return false;if(current.State!=TouchLeaseState.Active)return current.State==TouchLeaseState.ReleasePending;current.State=TouchLeaseState.ReleasePending;return true;}}
    public void CompleteSuccessfulUp(IEnumerable<int> ids){lock(_gate)foreach(var id in ids)if(_leases.Remove(id,out var l)){l.State=TouchLeaseState.Released;}}
    public void QuarantineFailedUp(IEnumerable<int> ids){lock(_gate)foreach(var id in ids)if(_leases.Remove(id,out var l)){l.State=TouchLeaseState.Quarantined;_quarantine.Add(id);}}
    public void Reset(long backendGeneration){lock(_gate){if(backendGeneration==_backendGeneration)return;foreach(var l in _leases.Values)l.State=TouchLeaseState.Released;_leases.Clear();_quarantine.Clear();_backendGeneration=backendGeneration;}}
}
