using System;
using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public class TouchEngine
{
    private readonly ILogger<TouchEngine> _logger;
    private readonly ContactManager _contacts;

    public TouchEngine(ILogger<TouchEngine> logger, ContactManager contacts)
    {
        _logger = logger;
        _contacts = contacts;
    }

    public void StartTouch(int id, double x, double y)
    {
        _contacts.StartContact(id, x, y);
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
}
