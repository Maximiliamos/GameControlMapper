using System;
using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public class TouchEngine
{
    private readonly ILogger<TouchEngine> _logger;
    private readonly ContactManager _contacts;
    private readonly object _gate = new();
    private bool _acceptingContacts = true;
    private long? _acceptedGeneration;
    private readonly ITouchContactAllocator _allocator;
    public ITouchContactAllocator ContactAllocator => _allocator;

    public TouchEngine(ILogger<TouchEngine> logger, ContactManager contacts, ITouchContactAllocator? allocator = null)
    {
        _logger = logger;
        _contacts = contacts;
        _allocator = allocator ?? new TouchContactAllocator(new(contacts.MaxContacts,true,false,true), Microsoft.Extensions.Logging.Abstractions.NullLogger<TouchContactAllocator>.Instance);
    }

    public TouchContactLease? StartTouch(long generation,string owner,double x,double y)
    {
        lock(_gate)
        {
            if(!_acceptingContacts){_logger.LogDebug("StartTouch ignored while touch engine is stopping");return null;}
            if (_acceptedGeneration is { } acceptedGeneration && acceptedGeneration != generation)
            {
                _logger.LogDebug("StartTouch rejected for a non-active target generation");
                return null;
            }
            var lease=_allocator.TryAcquire(generation,owner);if(lease is null)return null;
            _contacts.StartContact(lease.ContactId,x,y);return lease;
        }
    }
    public void MoveTouch(TouchContactLease lease,double x,double y){if(lease.State==TouchLeaseState.Active)MoveTouch(lease.ContactId,x,y);}
    public void EndTouch(TouchContactLease lease){if(!_allocator.RequestRelease(lease))return;var sent=_contacts.WasSuccessfullyStarted(lease.ContactId);EndTouch(lease.ContactId);if(!sent){_contacts.CompleteReleasedContacts([lease.ContactId]);_allocator.CompleteSuccessfulUp([lease.ContactId]);}}

    public void StartTouch(int id, double x, double y)
    {
        lock (_gate)
        {
            if (!_acceptingContacts)
            {
                _logger.LogDebug("StartTouch ignored while touch engine is stopping");
                return;
            }
            _contacts.StartContact(id, x, y);
        }
        _logger.LogTrace("StartTouch: ID={Id}, X={X}, Y={Y}", id, x, y);
    }

    public void MoveTouch(int id, double x, double y)
    {
        if (!_contacts.MoveContact(id, x, y))
        {
            _logger.LogWarning("MoveTouch: Contact {Id} not found", id);
            return;
        }
        _logger.LogTrace("MoveTouch: ID={Id}, X={X}, Y={Y}", id, x, y);
    }

    public void EndTouch(int id)
    {
        if (!_contacts.EndContact(id))
        {
            _logger.LogWarning("EndTouch: Contact {Id} not found", id);
            return;
        }
        _logger.LogTrace("EndTouch: ID={Id}", id);
    }

    public void ReleaseAll()
    {
        _contacts.ReleaseAll();
    }

    public void StartAcceptingContacts(long generation)
    {
        lock (_gate)
        {
            _acceptedGeneration = generation;
            _acceptingContacts = true;
        }
    }

    // Low-level tests and legacy callers that do not model target sessions may opt out
    // of generation admission. Production always supplies an explicit generation.
    public void StartAcceptingContacts()
    {
        lock (_gate)
        {
            _acceptedGeneration = null;
            _acceptingContacts = true;
        }
    }

    public void StopAcceptingContacts()
    {
        lock (_gate)
        {
            _acceptingContacts = false;
            _acceptedGeneration = null;
        }
    }
}
