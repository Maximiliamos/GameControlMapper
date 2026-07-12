using System.Collections.ObjectModel;
using GameControlMapper.Services;
namespace GameControlMapper.ViewModels;
public sealed record TouchOwnerDisplay(int ContactId,string OwnerId,string OwnerType,string? BindingName,TouchLeaseState State);
public sealed class TouchDebugViewModel : ObservableObject
{
    private readonly ITouchContactAllocator _allocator;private double _injectedFps;
    public TouchDebugViewModel(ContactManager contacts,TouchScheduler scheduler,ITouchContactAllocator allocator){_allocator=allocator;contacts.ContactsChanged+=(_,_)=>UpdateContacts();scheduler.FpsChanged+=(_,_)=>InjectedFps=scheduler.CurrentFps;UpdateContacts();}
    public ObservableCollection<TouchOwnerDisplay> Contacts{get;}=[];
    public double InjectedFps{get=>_injectedFps;private set=>SetProperty(ref _injectedFps,value);}
    private void UpdateContacts(){Contacts.Clear();foreach(var lease in _allocator.ActiveLeases.OrderBy(x=>x.ContactId)){var parts=lease.OwnerId.Split(':',2);var type=parts[0] switch{"camera"=>"Камера","joystick"=>"Джойстик","mouse-area"=>"Область мыши","binding"=>"Привязка",_=>"Динамический контакт"};Contacts.Add(new(lease.ContactId,lease.OwnerId,type,parts.Length==2?parts[1]:null,lease.State));}}
}
