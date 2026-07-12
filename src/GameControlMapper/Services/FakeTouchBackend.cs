using System;
using System.Collections.Generic;
using GameControlMapper.Models;

namespace GameControlMapper.Services;

/// <summary>
/// Бэкенд для тестов: не вызывает WinAPI, только сохраняет все кадры в память
/// </summary>
public class FakeTouchBackend : ITouchBackend
{
    public TouchCapabilities Capabilities { get; } = new TouchCapabilities(32, true, true, true);

    public List<TouchFrameSnapshot> RecordedFrames { get; } = [];
    public bool IsInitialized { get; private set; }

    public bool Initialize()
    {
        IsInitialized = true;
        return true;
    }

    public bool SendFrame(TouchFrame frame)
    {
        var contacts = frame.GetContacts().ToArray()
            .Select(contact => new TouchContactSnapshot(contact.ContactId, contact.X, contact.Y, contact.State))
            .ToArray();
        RecordedFrames.Add(new TouchFrameSnapshot(frame.FrameId, frame.Timestamp, contacts));
        return true;
    }

    public void Shutdown()
    {
        IsInitialized = false;
        // Clear frames? Let's keep them for tests.
    }

    /// <summary>
    /// Очистить записанные кадры
    /// </summary>
    public void Clear()
    {
        RecordedFrames.Clear();
    }
}

public sealed record TouchFrameSnapshot(int FrameId, DateTime Timestamp, IReadOnlyList<TouchContactSnapshot> Contacts);

public sealed record TouchContactSnapshot(int ContactId, double X, double Y, TouchState State);
